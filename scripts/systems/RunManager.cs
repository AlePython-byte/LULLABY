using System;
using System.IO;
using Godot;

/// <summary>
/// Administra una partida: elige la semilla de partida y la dificultad objetivo, le pide el nivel al AIDungeonDirector
/// (que genera candidatos, predice su dificultad y elige uno) y se lo pasa a RoomManager.
///
/// La semilla que se muestra y se repite es la SEMILLA DE PARTIDA: con el director, una misma semilla de partida y una
/// misma dificultad dan siempre el mismo nivel, pero ya no el mismo que daría el generador solo con esa semilla.
///
/// Prioridad de la semilla de partida:
///   1. La que dejó una tecla de depuración antes de recargar la escena.
///   2. El argumento de línea de comandos --seed=N (en Godot: godot -- --seed=12345).
///   3. FixedSeed, si UseFixedSeed está activo en el inspector.
///   4. Una semilla aleatoria.
/// La dificultad sale, por este orden, de la tecla de depuración, del argumento --difficulty=Easy|Normal|Hard, de la que
/// eligió el jugador en el menú (GameSettings.Difficulty) o del [Export] Difficulty.
///
/// Versión del generador: GeneratorVersion (DefaultGeneratorVersion, la 2, por defecto), el argumento --generator=N
/// (--generator=1 para la versión 1) o la tecla G. El HUD muestra siempre la versión en uso. Cada versión usa su
/// modelo de dificultad: ModelFile (difficulty_v1.json, 14 características) para la 1 y ModelFileV2 (difficulty_v2.json,
/// 22 características con tipos de enemigo) para la 2; para la N siguiente, res://resources/ai/difficulty_vN.json. Si el
/// modelo de una versión todavía no existe (la 3, con el Huidor y el Excavador, aún no tiene el suyo), se usa el de la
/// versión anterior más reciente y el HUD avisa de que no está entrenado para ella.
///
/// La opción «Variedad extendida (experimental)» del menú (GameSettings.ExtendedVariety) elige la versión más reciente del generador
/// (la 3) si ni el argumento ni la tecla piden otra.
///
/// Teclas de depuración (solo si OS.IsDebugBuild()): N reinicia con una semilla nueva; R repite la actual; T cambia la
/// dificultad (Easy → Normal → Hard) con la misma semilla; G recorre las versiones del generador (1 → 2 → 3 → 1).
/// Al morir, la escena se recarga sin estado pendiente, así que empieza una partida nueva (o la fija).
///
/// Fin de la partida: cuando RoomManager avisa de que se derrotó a The Mother (BossRoomCleared), la partida termina UNA sola
/// vez (RunEnded): se congela a Noah, se bloquean las transiciones y se eliminan los proyectiles enemigos (RoomManager.EndRun);
/// tras VictoryDelay (deja ver el efecto de muerte del jefe) y un fundido a oscuro, VictoryScreen muestra una de las cadenas
/// de victoria (la elige la semilla de partida, sin consumir ningún azar) y el botón «Nueva partida» llama a StartNewRun.
/// StartNewRun no depende de las teclas de depuración: funciona igual en Release y siempre cambia la semilla, aunque la
/// partida arrancara con --seed o UseFixedSeed.
/// </summary>
public partial class RunManager : Node
{
	/// <summary>Versión del generador con la que se juega si nadie pide otra. La v1 sigue disponible (G, --generator=1).</summary>
	public const int DefaultGeneratorVersion = 2;

	private const string NewSeedAction = "debug_new_seed";
	private const string RepeatSeedAction = "debug_repeat_seed";
	private const string NextDifficultyAction = "debug_next_difficulty";
	private const string ToggleGeneratorAction = "debug_toggle_generator";
	private const string SeedArgument = "--seed=";
	private const string DifficultyArgument = "--difficulty=";
	private const string GeneratorArgument = "--generator=";

	[Export] public RoomCatalog Catalog { get; set; }
	[Export] public RoomManager Rooms { get; set; }
	[Export] public SeedHud SeedHud { get; set; }
	[Export] public TreasureItemSpawner TreasureItems { get; set; }
	[Export] public VictoryScreen Victory { get; set; }
	[Export] public DefeatScreen Defeat { get; set; }

	// Pausa tras derrotar al jefe (su efecto de muerte dura 0.7 s) y fundido antes de la pantalla de victoria o de derrota.
	[Export] public float VictoryDelay { get; set; } = 0.8f;
	[Export] public float VictoryFadeTime { get; set; } = 0.6f;
	[Export] public float VictoryDim { get; set; } = 0.85f;

	// Los modelos viajan como recursos Json referenciados desde la escena, así el exportador los incluye como dependencia.
	// Si no se asignan, se leen de DifficultyModel.PathFor(versión del generador).
	[Export] public Json ModelFile { get; set; }
	[Export] public Json ModelFileV2 { get; set; }

	[Export] public DifficultyTarget Difficulty { get; set; } = DifficultyTarget.Normal;
	[Export] public int Candidates { get; set; } = 30;
	[Export] public int TopCount { get; set; } = 5;

	[Export] public int GeneratorVersion { get; set; } = DefaultGeneratorVersion;
	[Export] public int MinRooms { get; set; } = 8;
	[Export] public int MaxRooms { get; set; } = 12;
	[Export] public float BranchChance { get; set; } = 0.5f;
	[Export] public int MinBossDistance { get; set; } = 4;
	[Export] public int MaxAttempts { get; set; } = 20;

	[Export] public bool UseFixedSeed { get; set; }
	[Export] public int FixedSeed { get; set; }

	// Sobreviven a la recarga de escena: así las teclas de depuración pasan su estado a la siguiente partida.
	private static int? _pendingSeed;
	private static DifficultyTarget? _pendingDifficulty;
	private static int? _pendingGeneratorVersion;
	private static LevelMap _pendingMap;

	/// <summary>Cadenas de victoria del juego (MotherTexts); la semilla de partida elige una.</summary>
	public static readonly string[] VictoryMessages = { MotherTexts.VictoryDawn, MotherTexts.VictoryFreedom, MotherTexts.VictorySilence };

	/// <summary>Se derrotó a The Mother y la partida terminó. Se emite una sola vez, en el instante de la muerte.</summary>
	public event Action RunCompleted;

	/// <summary>Noah murió y la partida terminó (con DefeatScreen asignada). Se emite una sola vez.</summary>
	public event Action RunFailed;

	private int _seed;
	private DifficultyTarget _difficulty;
	private int _generatorVersion;
	private string _modelSource;
	private PlayerHealth _playerHealth;

	/// <summary>Dificultad objetivo de la partida en curso.</summary>
	public DifficultyTarget CurrentDifficulty => _difficulty;

	/// <summary>Semilla de la partida en curso.</summary>
	public int Seed => _seed;

	public bool RunEnded { get; private set; }

	public override void _Ready()
	{
		Rooms.BossRoomCleared += OnBossRoomCleared;
		if (Victory != null)
		{
			Victory.NewRunRequested += StartNewRun;
		}

		if (Defeat != null)
		{
			Defeat.RetryRequested += StartNewRun;
			Defeat.MenuRequested += ReturnToMenu;
			_playerHealth = Rooms.Player?.GetNodeOrNull<PlayerHealth>("PlayerHealth");
			if (_playerHealth != null)
			{
				_playerHealth.Defeated += OnDefeated;
			}
		}

		if (OS.IsDebugBuild())
		{
			InputActions.Register(NewSeedAction, Key.N);
			InputActions.Register(RepeatSeedAction, Key.R);
			InputActions.Register(NextDifficultyAction, Key.T);
			InputActions.Register(ToggleGeneratorAction, Key.G);
		}

		_seed = ChooseSeed();
		_difficulty = _pendingDifficulty ?? ArgumentDifficulty() ?? GameSettings.Difficulty ?? Difficulty;
		_generatorVersion = _pendingGeneratorVersion ?? ArgumentGeneratorVersion()
			?? (GameSettings.ExtendedVariety ? DungeonGenerator.LatestVersion : GeneratorVersion);
		_pendingSeed = null;
		_pendingDifficulty = null;
		_pendingGeneratorVersion = null;
		Rooms.DecorSeed = _seed;

		if (_pendingMap != null)
		{
			LevelMap fixedMap = _pendingMap;
			_pendingMap = null;
			SeedHud?.ShowFixedLevel($"{fixedMap.Rooms.Count} salas", _generatorVersion);
			TreasureItems?.BeginRun(_seed, fixedMap);
			Rooms.Begin(fixedMap);
			return;
		}

		DifficultyModel model;
		try
		{
			model = LoadModel(_generatorVersion);
		}
		catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
		{
			GD.PushError(exception.Message);
			SeedHud?.ShowError(_seed, "modelo de dificultad no válido (ver la salida)", _generatorVersion);
			return;
		}

		DungeonGenerator.Settings settings = new()
		{
			GeneratorVersion = _generatorVersion,
			MinRooms = MinRooms,
			MaxRooms = MaxRooms,
			BranchChance = BranchChance,
			MinBossDistance = MinBossDistance,
			MaxAttempts = MaxAttempts,
		};
		AIDungeonDirector director = new(new DungeonGenerator(Catalog), model, settings)
		{
			CandidateCount = Candidates,
			TopCount = TopCount,
			PreferEnemyVariety = _generatorVersion == 2,
		};

		AIDungeonDirector.Result result = director.Select(_seed, _difficulty);
		if (!result.Success)
		{
			GD.PushError($"RunManager: el director no encontró ningún nivel para la semilla {_seed}.");
			SeedHud?.ShowError(_seed, "sin nivel válido", _generatorVersion);
			return;
		}

		SeedHud?.ShowRun(_seed, result.Chosen.EffectiveSeed, _difficulty, result.TargetValue, result.Chosen.Predicted,
			_generatorVersion, model.GeneratorVersion);
		GD.Print($"Generador v{_generatorVersion}, modelo de dificultad {_modelSource} con {model.FeatureNames.Count} " +
			$"características (versión {model.FeatureSetVersion})." + (model.GeneratorVersion != _generatorVersion
				? $" Aviso: el modelo no está entrenado para el generador v{_generatorVersion}." : ""));
		GD.Print(result.Describe());
		GD.Print($"Nivel elegido: salas {result.Map.Rooms.Count}, jefe a {LevelValidator.BossDistance(result.Map)} pasos.");
		TreasureItems?.BeginRun(_seed, result.Map);
		Rooms.Begin(result.Map);
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Rooms))
		{
			Rooms.BossRoomCleared -= OnBossRoomCleared;
		}

		if (IsInstanceValid(Victory))
		{
			Victory.NewRunRequested -= StartNewRun;
		}

		if (IsInstanceValid(Defeat))
		{
			Defeat.RetryRequested -= StartNewRun;
			Defeat.MenuRequested -= ReturnToMenu;
		}

		if (IsInstanceValid(_playerHealth))
		{
			_playerHealth.Defeated -= OnDefeated;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!OS.IsDebugBuild())
		{
			return;
		}

		if (@event.IsActionPressed(NewSeedAction))
		{
			Restart(RandomSeed(), _difficulty, _generatorVersion);
		}
		else if (@event.IsActionPressed(RepeatSeedAction))
		{
			Restart(_seed, _difficulty, _generatorVersion);
		}
		else if (@event.IsActionPressed(NextDifficultyAction))
		{
			Restart(_seed, (DifficultyTarget)(((int)_difficulty + 1) % Enum.GetValues<DifficultyTarget>().Length), _generatorVersion);
		}
		else if (@event.IsActionPressed(ToggleGeneratorAction))
		{
			int next = _generatorVersion >= DungeonGenerator.LatestVersion ? DungeonGenerator.OriginalVersion : _generatorVersion + 1;
			Restart(_seed, _difficulty, next);
		}
	}

	/// <summary>
	/// Olvida la semilla, dificultad, versión del generador y nivel pendientes de una recarga que no llegó a ocurrir. El menú
	/// lo llama antes de jugar para que una partida nueva tome una semilla aleatoria y no una pendiente.
	/// </summary>
	public static void DiscardPending()
	{
		_pendingSeed = null;
		_pendingDifficulty = null;
		_pendingGeneratorVersion = null;
		_pendingMap = null;
	}

	/// <summary>Cadena de victoria de una partida: determinista por semilla, sin tocar ningún generador de azar.</summary>
	public static string VictoryMessageFor(int seed)
	{
		return VictoryMessages[(int)((uint)seed % (uint)VictoryMessages.Length)];
	}

	/// <summary>
	/// Nueva partida con otra semilla (siempre distinta de la actual, también con --seed o UseFixedSeed) y la misma
	/// dificultad y versión del generador. Reinicia objetos y salud y recarga la escena. Pública y sin depender de la
	/// depuración: es lo que llama el botón «Nueva partida».
	/// </summary>
	public void StartNewRun()
	{
		GetTree().Paused = false;
		PrepareNewRun();
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	/// <summary>Vuelve al menú principal (también desde la pausa y la derrota). La dificultad elegida se conserva.</summary>
	public void ReturnToMenu()
	{
		GetTree().Paused = false;
		GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, GameSettings.MenuScene);
	}

	/// <summary>
	/// Deja lista la partida siguiente sin recargar la escena: fija su semilla (devuelta) y reinicia inventario, mejoras y
	/// salud de Noah. La escena que se cargue después toma esa semilla antes que --seed o FixedSeed.
	/// </summary>
	public int PrepareNewRun()
	{
		int seed;
		do
		{
			seed = RandomSeed();
		}
		while (seed == _seed);

		_pendingSeed = seed;
		_pendingDifficulty = _difficulty;
		_pendingGeneratorVersion = _generatorVersion;
		Rooms.Player?.GetNodeOrNull<PlayerItems>("Items")?.ResetForNewRun();
		return seed;
	}

	/// <summary>Fija la semilla de partida de la próxima partida que se cargue. Solo en compilaciones de depuración.</summary>
	public static void SetNextSeed(int seed)
	{
		if (!OS.IsDebugBuild())
		{
			GD.PushWarning("RunManager.SetNextSeed solo funciona en compilaciones de depuración.");
			return;
		}

		_pendingSeed = seed;
	}

	/// <summary>
	/// Fija la versión del generador de la próxima partida que se cargue (las pruebas que necesitan la v1 la piden con esto,
	/// porque el defecto es la v2). Solo en compilaciones de depuración.
	/// </summary>
	public static void SetNextGeneratorVersion(int version)
	{
		if (!OS.IsDebugBuild())
		{
			GD.PushWarning("RunManager.SetNextGeneratorVersion solo funciona en compilaciones de depuración.");
			return;
		}

		_pendingGeneratorVersion = version;
	}

	/// <summary>
	/// Fija el nivel exacto de la próxima partida, sin director ni modelo (lo usa la prueba de fidelidad para jugar
	/// niveles del generador v2 tal cual). Solo en compilaciones de depuración.
	/// </summary>
	public static void SetNextMap(LevelMap map)
	{
		if (!OS.IsDebugBuild())
		{
			GD.PushWarning("RunManager.SetNextMap solo funciona en compilaciones de depuración.");
			return;
		}

		_pendingMap = map;
	}

	// El modelo de la versión del generador. Si todavía no existe (por ejemplo, la v3 sin difficulty_v3.json), se usa el de
	// la versión anterior (con sus características) y el HUD avisa, como pasó con la v2 antes de tener su modelo.
	private DifficultyModel LoadModel(int generatorVersion)
	{
		Json file = generatorVersion switch
		{
			1 => ModelFile,
			2 => ModelFileV2,
			_ => null,
		};
		string path = generatorVersion <= DungeonGenerator.OriginalVersion
			? DifficultyModel.DefaultPath
			: $"res://resources/ai/difficulty_v{generatorVersion}.json";
		if (file == null && generatorVersion > DungeonGenerator.OriginalVersion && !Godot.FileAccess.FileExists(path))
		{
			GD.PushWarning($"RunManager: no existe {path}; se usa el modelo de dificultad de la versión {generatorVersion - 1} " +
				$"(no está entrenado para el generador v{generatorVersion}).");
			return LoadModel(generatorVersion - 1);
		}

		DifficultyModel model = file != null ? DifficultyModel.FromJsonResource(file, file.ResourcePath) : DifficultyModel.Load(path);
		_modelSource = file != null ? file.ResourcePath : path;
		if (model.GeneratorVersion != generatorVersion)
		{
			throw new InvalidDataException($"RunManager: el modelo de dificultad es para el generador v{model.GeneratorVersion}, " +
				$"pero la partida usa el v{generatorVersion}.");
		}

		return model;
	}

	private int ChooseSeed()
	{
		if (_pendingSeed.HasValue)
		{
			return _pendingSeed.Value;
		}

		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith(SeedArgument) && int.TryParse(argument[SeedArgument.Length..], out int seed))
			{
				return seed;
			}
		}

		return UseFixedSeed ? FixedSeed : RandomSeed();
	}

	private static DifficultyTarget? ArgumentDifficulty()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith(DifficultyArgument)
				&& Enum.TryParse(argument[DifficultyArgument.Length..], true, out DifficultyTarget target))
			{
				return target;
			}
		}

		return null;
	}

	private static int? ArgumentGeneratorVersion()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith(GeneratorArgument) && int.TryParse(argument[GeneratorArgument.Length..], out int version))
			{
				return version;
			}
		}

		return null;
	}

	private static int RandomSeed()
	{
		RandomNumberGenerator rng = new();
		rng.Randomize();
		return (int)(rng.Randi() & int.MaxValue);
	}

	private void OnBossRoomCleared()
	{
		// Si Noah murió en el mismo instante, su muerte reinicia la escena y no hay victoria.
		if (RunEnded || HealthComponent.FindIn(Rooms.Player).IsDead)
		{
			return;
		}

		RunEnded = true;
		Rooms.EndRun();
		RunCompleted?.Invoke();
		PresentEnd(() => Victory?.Present(VictoryMessageFor(_seed)));
	}

	// Noah murió: la partida termina igual que con la victoria (Noah quieto, sin transiciones ni proyectiles) y, tras el
	// mismo fundido, aparece la derrota con una frase elegida por la semilla.
	private void OnDefeated()
	{
		if (RunEnded)
		{
			return;
		}

		RunEnded = true;
		Rooms.EndRun();
		RunFailed?.Invoke();
		PresentEnd(() => Defeat?.Present(GameText.DefeatMessageFor(_seed)));
	}

	private async void PresentEnd(Action present)
	{
		if (VictoryDelay > 0.0f)
		{
			await ToSignal(GetTree().CreateTimer(VictoryDelay, false), SceneTreeTimer.SignalName.Timeout);
			if (!IsInstanceValid(this) || !IsInsideTree())
			{
				return;
			}
		}

		if (Rooms.Fade != null && VictoryFadeTime > 0.0f)
		{
			await Rooms.Fade.FadeTo(VictoryDim, VictoryFadeTime);
			if (!IsInstanceValid(this) || !IsInsideTree())
			{
				return;
			}
		}

		present();
	}

	private void Restart(int seed, DifficultyTarget difficulty, int generatorVersion)
	{
		_pendingSeed = seed;
		_pendingDifficulty = difficulty;
		_pendingGeneratorVersion = generatorVersion;
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}
}
