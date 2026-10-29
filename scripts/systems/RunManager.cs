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
/// La dificultad sale, por este orden, de la tecla de depuración, del argumento --difficulty=Easy|Normal|Hard o del
/// [Export] Difficulty.
///
/// Versión del generador: GeneratorVersion (1 por defecto), el argumento --generator=N o la tecla G. La versión 1 es la
/// del modelo de dificultad; con la versión 2 (salas con varios tipos de enemigo) el HUD avisa de que el modelo no está
/// entrenado para ella, porque no distingue tipos de enemigo.
///
/// Teclas de depuración (solo si OS.IsDebugBuild()): N reinicia con una semilla nueva; R repite la actual; T cambia la
/// dificultad (Easy → Normal → Hard) con la misma semilla; G alterna la versión del generador (1 ↔ 2).
/// Al morir, la escena se recarga sin estado pendiente, así que empieza una partida nueva (o la fija).
/// </summary>
public partial class RunManager : Node
{
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

	// El modelo viaja como recurso Json referenciado desde la escena, así el exportador lo incluye como dependencia.
	// Si no se asigna, se lee de DifficultyModel.DefaultPath.
	[Export] public Json ModelFile { get; set; }

	[Export] public DifficultyTarget Difficulty { get; set; } = DifficultyTarget.Normal;
	[Export] public int Candidates { get; set; } = 30;
	[Export] public int TopCount { get; set; } = 5;

	[Export] public int GeneratorVersion { get; set; } = DungeonGenerator.OriginalVersion;
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

	private int _seed;
	private DifficultyTarget _difficulty;
	private int _generatorVersion;

	public override void _Ready()
	{
		if (OS.IsDebugBuild())
		{
			InputActions.Register(NewSeedAction, Key.N);
			InputActions.Register(RepeatSeedAction, Key.R);
			InputActions.Register(NextDifficultyAction, Key.T);
			InputActions.Register(ToggleGeneratorAction, Key.G);
		}

		_seed = ChooseSeed();
		_difficulty = _pendingDifficulty ?? ArgumentDifficulty() ?? Difficulty;
		_generatorVersion = _pendingGeneratorVersion ?? ArgumentGeneratorVersion() ?? GeneratorVersion;
		_pendingSeed = null;
		_pendingDifficulty = null;
		_pendingGeneratorVersion = null;

		DifficultyModel model;
		try
		{
			model = ModelFile != null ? DifficultyModel.FromJsonResource(ModelFile, ModelFile.ResourcePath) : DifficultyModel.Load();
		}
		catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
		{
			GD.PushError(exception.Message);
			SeedHud?.ShowError(_seed, "modelo de dificultad no válido (ver la salida)");
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
		};

		AIDungeonDirector.Result result = director.Select(_seed, _difficulty);
		if (!result.Success)
		{
			GD.PushError($"RunManager: el director no encontró ningún nivel para la semilla {_seed}.");
			SeedHud?.ShowError(_seed, "sin nivel válido");
			return;
		}

		SeedHud?.ShowRun(_seed, result.Chosen.EffectiveSeed, _difficulty, result.TargetValue, result.Chosen.Predicted,
			_generatorVersion);
		GD.Print($"Generador v{_generatorVersion}." + (_generatorVersion != DungeonGenerator.OriginalVersion
			? " Aviso: el modelo de dificultad no está entrenado para esta versión." : ""));
		GD.Print(result.Describe());
		GD.Print($"Nivel elegido: salas {result.Map.Rooms.Count}, jefe a {LevelValidator.BossDistance(result.Map)} pasos.");
		Rooms.Begin(result.Map);
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

	private void Restart(int seed, DifficultyTarget difficulty, int generatorVersion)
	{
		_pendingSeed = seed;
		_pendingDifficulty = difficulty;
		_pendingGeneratorVersion = generatorVersion;
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}
}
