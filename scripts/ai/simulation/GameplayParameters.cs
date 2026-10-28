using System.Collections.Generic;
using Godot;

/// <summary>
/// Reglas numéricas del juego para el simulador, leídas de las mismas escenas que usa el juego (con sus valores de
/// escena, no los valores por defecto de los scripts), para que no se desincronicen:
///   - TestLevel.tscn: posición inicial del jugador y el RoomManager (ArrivalOffset, FadeDuration, RoomScene).
///   - El Player instanciado en el nivel: velocidad, vida, invulnerabilidad, caja de colisión y el disparador
///     (cadencia, distancia de salida y la escena de la lágrima: velocidad, vida, daño y radio).
///   - Room.tscn (RoomScene): altura y grosor de paredes, ancho de puerta, margen de entrada y StartDelay.
///   - La EnemyScene de cada sala: velocidad, daño por contacto, espera entre golpes, vida y caja de colisión.
///   - El paso de física: 1 / Engine.PhysicsTicksPerSecond.
/// Se crea en el hilo principal (instancia escenas sin añadirlas al árbol y las libera); después es solo datos y
/// se puede leer desde varios hilos.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class GameplayParameters
{
	public const string LevelScenePath = "res://scenes/levels/TestLevel.tscn";

	public sealed class EnemyStats
	{
		public string ScenePath { get; init; }
		public float Speed { get; init; }
		public float ContactDamage { get; init; }
		public float HitCooldown { get; init; }
		public float RecoilDistance { get; init; }
		public float RecoilDuration { get; init; }
		public float MaxHealth { get; init; }

		/// <summary>Tamaño de la caja de colisión en el plano (X, Z).</summary>
		public Vector2 Size { get; init; }
	}

	public float PhysicsDelta { get; private set; }

	public Vector2 PlayerStart { get; private set; }
	public float PlayerSpeed { get; private set; }
	public Vector2 PlayerSize { get; private set; }
	public float PlayerMaxHealth { get; private set; }
	public float PlayerInvulnerability { get; private set; }

	public float ShotsPerSecond { get; private set; }
	public float TearSpawnDistance { get; private set; }
	public float TearSpeed { get; private set; }
	public float TearLifetime { get; private set; }
	public float TearDamage { get; private set; }
	public float TearRadius { get; private set; }

	public float WallHeight { get; private set; }
	public float WallThickness { get; private set; }
	public float DoorWidth { get; private set; }
	public float EntryMargin { get; private set; }
	public float StartDelay { get; private set; }

	public float ArrivalOffset { get; private set; }
	public float FadeDuration { get; private set; }

	private readonly Dictionary<string, EnemyStats> _enemies = new();

	public static GameplayParameters FromScenes(string levelScenePath = LevelScenePath)
	{
		GameplayParameters parameters = new() { PhysicsDelta = (float)(1.0 / Engine.PhysicsTicksPerSecond) };

		Node level = GD.Load<PackedScene>(levelScenePath).Instantiate();
		try
		{
			RoomManager manager = level.GetNode<RoomManager>("RoomManager");
			parameters.ArrivalOffset = manager.ArrivalOffset;
			parameters.FadeDuration = manager.FadeDuration;

			PlayerController player = level.GetNode<PlayerController>("Player");
			parameters.PlayerStart = new Vector2(player.Position.X, player.Position.Z);
			parameters.PlayerSpeed = player.Speed;
			parameters.PlayerSize = BoxSize(player);
			parameters.PlayerMaxHealth = HealthComponent.FindIn(player).MaxHealth;
			parameters.PlayerInvulnerability = player.GetNode<PlayerHealth>("PlayerHealth").InvulnerabilityTime;

			PlayerShooter shooter = player.GetNode<PlayerShooter>("Shooter");
			parameters.ShotsPerSecond = shooter.ShotsPerSecond;
			parameters.TearSpawnDistance = shooter.SpawnDistance;

			Tear tear = shooter.TearScene.Instantiate<Tear>();
			parameters.TearSpeed = tear.Speed;
			parameters.TearLifetime = tear.Lifetime;
			parameters.TearDamage = tear.Damage;
			parameters.TearRadius = ((SphereShape3D)tear.GetNode<CollisionShape3D>("CollisionShape3D").Shape).Radius;
			tear.Free();

			RoomController room = manager.RoomScene.Instantiate<RoomController>();
			parameters.WallHeight = room.WallHeight;
			parameters.WallThickness = room.WallThickness;
			parameters.DoorWidth = room.DoorWidth;
			parameters.EntryMargin = room.EntryMargin;
			parameters.StartDelay = room.StartDelay;
			room.Free();
		}
		finally
		{
			level.Free();
		}

		return parameters;
	}

	/// <summary>Estadísticas de un tipo de enemigo, leídas de su escena (solo en el hilo principal).</summary>
	public EnemyStats EnemyFor(PackedScene scene)
	{
		if (_enemies.TryGetValue(scene.ResourcePath, out EnemyStats cached))
		{
			return cached;
		}

		BasicEnemy enemy = scene.Instantiate<BasicEnemy>();
		EnemyStats stats = new()
		{
			ScenePath = scene.ResourcePath,
			Speed = enemy.Speed,
			ContactDamage = enemy.ContactDamage,
			HitCooldown = enemy.HitCooldown,
			RecoilDistance = enemy.RecoilDistance,
			RecoilDuration = enemy.RecoilDuration,
			MaxHealth = HealthComponent.FindIn(enemy).MaxHealth,
			Size = BoxSize(enemy),
		};
		enemy.Free();

		_enemies[scene.ResourcePath] = stats;
		return stats;
	}

	/// <summary>Todos los valores, con nombre y en orden alfabético (para el meta.json).</summary>
	public SortedDictionary<string, double> ToDictionary()
	{
		SortedDictionary<string, double> values = new()
		{
			["physics_delta"] = PhysicsDelta,
			["player_start_x"] = PlayerStart.X,
			["player_start_z"] = PlayerStart.Y,
			["player_speed"] = PlayerSpeed,
			["player_size_x"] = PlayerSize.X,
			["player_size_z"] = PlayerSize.Y,
			["player_max_health"] = PlayerMaxHealth,
			["player_invulnerability"] = PlayerInvulnerability,
			["shots_per_second"] = ShotsPerSecond,
			["tear_spawn_distance"] = TearSpawnDistance,
			["tear_speed"] = TearSpeed,
			["tear_lifetime"] = TearLifetime,
			["tear_damage"] = TearDamage,
			["tear_radius"] = TearRadius,
			["wall_height"] = WallHeight,
			["wall_thickness"] = WallThickness,
			["door_width"] = DoorWidth,
			["entry_margin"] = EntryMargin,
			["start_delay"] = StartDelay,
			["arrival_offset"] = ArrivalOffset,
			["fade_duration"] = FadeDuration,
		};

		foreach (EnemyStats enemy in _enemies.Values)
		{
			string prefix = $"enemy[{enemy.ScenePath}].";
			values[prefix + "speed"] = enemy.Speed;
			values[prefix + "contact_damage"] = enemy.ContactDamage;
			values[prefix + "hit_cooldown"] = enemy.HitCooldown;
			values[prefix + "recoil_distance"] = enemy.RecoilDistance;
			values[prefix + "recoil_duration"] = enemy.RecoilDuration;
			values[prefix + "max_health"] = enemy.MaxHealth;
			values[prefix + "size_x"] = enemy.Size.X;
			values[prefix + "size_z"] = enemy.Size.Y;
		}

		return values;
	}

	private static Vector2 BoxSize(Node body)
	{
		Vector3 size = ((BoxShape3D)body.GetNode<CollisionShape3D>("CollisionShape3D").Shape).Size;
		return new Vector2(size.X, size.Z);
	}
}
