using System.Collections.Generic;
using Godot;

/// <summary>
/// Reglas numéricas del juego para el simulador, leídas de las mismas escenas que usa el juego (con sus valores de
/// escena, no los valores por defecto de los scripts), para que no se desincronicen:
///   - TestLevel.tscn: posición inicial del jugador y el RoomManager (ArrivalOffset, FadeDuration, RoomScene).
///   - El Player instanciado en el nivel: velocidad, vida, invulnerabilidad, caja de colisión y el disparador
///     (cadencia, distancia de salida y la escena de la lágrima: velocidad, vida, daño y radio).
///   - Room.tscn (RoomScene): altura y grosor de paredes, ancho de puerta, margen de entrada y StartDelay.
///   - La escena de cada enemigo (EnemyScene o, en el generador v2, la de su tipo): velocidad, daño por contacto,
///     espera entre golpes, retroceso, vida, caja de colisión, si choca con otros enemigos y los números de sus
///     comportamientos (KeepDistance, Shoot con su proyectil, Fuse y FlyPattern).
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
		public EnemyKind Kind { get; init; }
		public float Speed { get; init; }
		public float ContactDamage { get; init; }
		public float HitCooldown { get; init; }
		public float RecoilDistance { get; init; }
		public float RecoilDuration { get; init; }
		public float MaxHealth { get; init; }

		/// <summary>Tamaño de la caja de colisión en el plano (X, Z).</summary>
		public Vector2 Size { get; init; }

		/// <summary>Si choca con otros enemigos (su máscara incluye la capa Enemies; el Volador no).</summary>
		public bool CollidesWithEnemies { get; init; } = true;

		/// <summary>Null si la escena no tiene ese comportamiento.</summary>
		public KeepDistanceStats KeepDistance { get; init; }
		public ShootStats Shoot { get; init; }
		public FuseStats Fuse { get; init; }
		public FlyStats Fly { get; init; }
	}

	public sealed class KeepDistanceStats
	{
		public float PreferredDistance { get; init; }
		public float Tolerance { get; init; }
		public float BlockedFraction { get; init; }
		public float StrafeTime { get; init; }
		public float MaxStrafeTime { get; init; }
	}

	public sealed class ShootStats
	{
		public float FireInterval { get; init; }
		public float WarnTime { get; init; }
		public float FirstShotDelay { get; init; }
		public float SpawnDistance { get; init; }
		public float ProjectileSpeed { get; init; }
		public float ProjectileDamage { get; init; }
		public float ProjectileLifetime { get; init; }
		public float ProjectileRadius { get; init; }
	}

	public sealed class FuseStats
	{
		public float TriggerRadius { get; init; }
		public float FuseTime { get; init; }
		public float FuseSpeedScale { get; init; }
		public float ExplosionRadius { get; init; }
		public float ExplosionDamage { get; init; }
	}

	public sealed class FlyStats
	{
		public float Amplitude { get; init; }
		public float Frequency { get; init; }
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

		EnemyBase enemy = scene.Instantiate<EnemyBase>();
		KeepDistanceStats keepDistance = null;
		ShootStats shoot = null;
		FuseStats fuse = null;
		FlyStats fly = null;
		foreach (Node child in enemy.GetChildren())
		{
			switch (child)
			{
				case KeepDistanceBehavior behavior:
					keepDistance = new KeepDistanceStats
					{
						PreferredDistance = behavior.PreferredDistance,
						Tolerance = behavior.Tolerance,
						BlockedFraction = behavior.BlockedFraction,
						StrafeTime = behavior.StrafeTime,
						MaxStrafeTime = behavior.MaxStrafeTime,
					};
					break;

				case ShootBehavior behavior:
					EnemyProjectile projectile = behavior.ProjectileScene.Instantiate<EnemyProjectile>();
					shoot = new ShootStats
					{
						FireInterval = behavior.FireInterval,
						WarnTime = behavior.WarnTime,
						FirstShotDelay = behavior.FirstShotDelay,
						SpawnDistance = behavior.SpawnDistance,
						ProjectileSpeed = projectile.Speed,
						ProjectileDamage = projectile.Damage,
						ProjectileLifetime = projectile.Lifetime,
						ProjectileRadius = ((SphereShape3D)projectile.GetNode<CollisionShape3D>("CollisionShape3D").Shape).Radius,
					};
					projectile.Free();
					break;

				case FuseBehavior behavior:
					fuse = new FuseStats
					{
						TriggerRadius = behavior.TriggerRadius,
						FuseTime = behavior.FuseTime,
						FuseSpeedScale = behavior.FuseSpeedScale,
						ExplosionRadius = behavior.ExplosionRadius,
						ExplosionDamage = behavior.ExplosionDamage,
					};
					break;

				case FlyPatternBehavior behavior:
					fly = new FlyStats { Amplitude = behavior.Amplitude, Frequency = behavior.Frequency };
					break;
			}
		}

		EnemyStats stats = new()
		{
			ScenePath = scene.ResourcePath,
			Kind = enemy.Kind,
			Speed = enemy.Speed,
			ContactDamage = enemy.ContactDamage,
			HitCooldown = enemy.HitCooldown,
			RecoilDistance = enemy.RecoilDistance,
			RecoilDuration = enemy.RecoilDuration,
			MaxHealth = HealthComponent.FindIn(enemy).MaxHealth,
			Size = BoxSize(enemy),
			CollidesWithEnemies = (enemy.CollisionMask & CollisionLayers.Enemies) != 0,
			KeepDistance = keepDistance,
			Shoot = shoot,
			Fuse = fuse,
			Fly = fly,
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

			// Solo en los tipos nuevos: así los meta.json de niveles con enemigos básicos no cambian.
			if (enemy.Kind == EnemyKind.Chaser)
			{
				continue;
			}

			values[prefix + "kind"] = (int)enemy.Kind;
			values[prefix + "collides_with_enemies"] = enemy.CollidesWithEnemies ? 1 : 0;
			if (enemy.KeepDistance is { } keep)
			{
				values[prefix + "keep.preferred_distance"] = keep.PreferredDistance;
				values[prefix + "keep.tolerance"] = keep.Tolerance;
				values[prefix + "keep.blocked_fraction"] = keep.BlockedFraction;
				values[prefix + "keep.strafe_time"] = keep.StrafeTime;
				values[prefix + "keep.max_strafe_time"] = keep.MaxStrafeTime;
			}

			if (enemy.Shoot is { } shoot)
			{
				values[prefix + "shoot.fire_interval"] = shoot.FireInterval;
				values[prefix + "shoot.warn_time"] = shoot.WarnTime;
				values[prefix + "shoot.first_shot_delay"] = shoot.FirstShotDelay;
				values[prefix + "shoot.spawn_distance"] = shoot.SpawnDistance;
				values[prefix + "shoot.projectile_speed"] = shoot.ProjectileSpeed;
				values[prefix + "shoot.projectile_damage"] = shoot.ProjectileDamage;
				values[prefix + "shoot.projectile_lifetime"] = shoot.ProjectileLifetime;
				values[prefix + "shoot.projectile_radius"] = shoot.ProjectileRadius;
			}

			if (enemy.Fuse is { } fuse)
			{
				values[prefix + "fuse.trigger_radius"] = fuse.TriggerRadius;
				values[prefix + "fuse.fuse_time"] = fuse.FuseTime;
				values[prefix + "fuse.fuse_speed_scale"] = fuse.FuseSpeedScale;
				values[prefix + "fuse.explosion_radius"] = fuse.ExplosionRadius;
				values[prefix + "fuse.explosion_damage"] = fuse.ExplosionDamage;
			}

			if (enemy.Fly is { } fly)
			{
				values[prefix + "fly.amplitude"] = fly.Amplitude;
				values[prefix + "fly.frequency"] = fly.Frequency;
			}
		}

		return values;
	}

	private static Vector2 BoxSize(Node body)
	{
		Vector3 size = ((BoxShape3D)body.GetNode<CollisionShape3D>("CollisionShape3D").Shape).Size;
		return new Vector2(size.X, size.Z);
	}
}
