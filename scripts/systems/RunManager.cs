using Godot;

/// <summary>
/// Administra una partida: elige la semilla, genera el nivel con el DungeonGenerator y se lo pasa a RoomManager.
///
/// Prioridad de la semilla:
///   1. La que dejó una tecla de depuración antes de recargar la escena.
///   2. El argumento de línea de comandos --seed=N (en Godot: godot -- --seed=12345).
///   3. FixedSeed, si UseFixedSeed está activo en el inspector.
///   4. Una semilla aleatoria.
///
/// Teclas de depuración (solo si OS.IsDebugBuild()): N reinicia con una semilla nueva; R repite la actual.
/// Al morir, la escena se recarga sin semilla pendiente, así que empieza una partida nueva (o la fija).
/// </summary>
public partial class RunManager : Node
{
	private const string NewSeedAction = "debug_new_seed";
	private const string RepeatSeedAction = "debug_repeat_seed";
	private const string SeedArgument = "--seed=";

	[Export] public RoomCatalog Catalog { get; set; }
	[Export] public RoomManager Rooms { get; set; }
	[Export] public SeedHud SeedHud { get; set; }

	[Export] public int MinRooms { get; set; } = 8;
	[Export] public int MaxRooms { get; set; } = 12;
	[Export] public float BranchChance { get; set; } = 0.5f;
	[Export] public int MinBossDistance { get; set; } = 4;
	[Export] public int MaxAttempts { get; set; } = 20;

	[Export] public bool UseFixedSeed { get; set; }
	[Export] public int FixedSeed { get; set; }

	// Sobrevive a la recarga de escena: así las teclas de depuración pasan la semilla a la siguiente partida.
	private static int? _pendingSeed;

	private int _seed;

	public override void _Ready()
	{
		if (OS.IsDebugBuild())
		{
			InputActions.Register(NewSeedAction, Key.N);
			InputActions.Register(RepeatSeedAction, Key.R);
		}

		_seed = ChooseSeed();
		_pendingSeed = null;

		DungeonGenerator.Settings settings = new()
		{
			MinRooms = MinRooms,
			MaxRooms = MaxRooms,
			BranchChance = BranchChance,
			MinBossDistance = MinBossDistance,
			MaxAttempts = MaxAttempts,
		};
		DungeonGenerator.Result result = new DungeonGenerator(Catalog).Generate(_seed, settings);
		SeedHud?.ShowSeed(result.RequestedSeed, result.EffectiveSeed, result.Success);

		if (!result.Success)
		{
			GD.PushError($"RunManager: no hay nivel para la semilla {_seed}.");
			return;
		}

		GD.Print($"Semilla: {_seed} (efectiva {result.EffectiveSeed}, intentos {result.Attempts}, " +
			$"salas {result.Map.Rooms.Count}, jefe a {LevelValidator.BossDistance(result.Map)} pasos)");
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
			Restart(RandomSeed());
		}
		else if (@event.IsActionPressed(RepeatSeedAction))
		{
			Restart(_seed);
		}
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

	private static int RandomSeed()
	{
		RandomNumberGenerator rng = new();
		rng.Randomize();
		return (int)(rng.Randi() & int.MaxValue);
	}

	private void Restart(int seed)
	{
		_pendingSeed = seed;
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}
}
