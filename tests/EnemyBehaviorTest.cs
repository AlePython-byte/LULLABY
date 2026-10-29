using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Pruebas de comportamiento de los enemigos con el código real del juego (escenas, física y salas). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/EnemyBehaviorTest.tscn
///
/// Casos:
///   1. Tirador: mantiene su distancia preferida, avisa WarnTime antes de cada disparo y sus proyectiles dañan al jugador.
///   2. Tirador acorralado en una esquina: se desplaza en perpendicular y sale.
///   3. Proyectiles: atraviesan a otros enemigos sin dañarlos, mueren en la pared antes de su tiempo de vida y respetan
///      la invulnerabilidad del jugador (dos impactos seguidos = un solo daño).
///   4. Explosivo con el jugador dentro del radio: mecha de FuseTime, explota, daña ExplosionDamage y muere (Died).
///   5. Explosivo con el jugador fuera del radio al explotar: explota y muere sin dañar.
///   6. Explosivo al que matan durante la mecha: no explota ni daña.
///   7. Volador: su trayectoria coincide con la integración del patrón (FlyPatternBehavior.PatternVelocity), oscila y es
///      determinista (dos ejecuciones idénticas).
///   8. Sala con un solo explosivo: al explotar, la sala se despeja y abre sus puertas.
/// El jugador nunca pulsa nada (se queda quieto) y tiene vida de sobra. Sale con código 0 si todo pasa.
/// </summary>
public partial class EnemyBehaviorTest : Node
{
	private const string PlayerScene = "res://scenes/player/Player.tscn";
	private const string ProjectileScene = "res://scenes/combat/EnemyProjectile.tscn";
	private const string RoomScene = "res://scenes/rooms/Room.tscn";

	private readonly List<string> _failures = new();
	private float _dt;
	private int _frame;

	public override void _Ready()
	{
		Run();
	}

	private async void Run()
	{
		_dt = 1.0f / Engine.PhysicsTicksPerSecond;
		GD.Print("=== EnemyBehaviorTest ===");

		await ShooterKeepsDistanceAndFires();
		await ShooterEscapesCorner();
		await ProjectileLayersAndInvulnerability();
		await ExploderInsideRadius();
		await ExploderOutsideRadius();
		await ExploderKilledDuringFuse();
		await FlyerFollowsPattern();
		await ExploderRoomOpensDoors();

		GD.Print("");
		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	// --- casos ---------------------------------------------------------------------------------------------------

	private async Task ShooterKeepsDistanceAndFires()
	{
		GD.Print("\n1. Tirador: distancia y disparo");
		Node3D world = NewWorld(20.0f);
		PlayerController player = NewPlayer(world, new Vector3(0, 0.5f, 0), out List<(int Frame, float Amount)> hits);
		EnemyBase shooter = NewEnemy(world, EnemyKind.Shooter, new Vector3(2, 0.5f, 0));
		KeepDistanceBehavior distance = shooter.Behavior<KeepDistanceBehavior>();
		ShootBehavior shoot = shooter.Behavior<ShootBehavior>();

		List<int> warnings = new(), shots = new();
		shoot.WarningStarted += () => warnings.Add(_frame);
		shoot.Fired += direction => shots.Add(_frame);

		List<float> distances = new();
		for (int i = 0; i < 360; i++)
		{
			await Frames(1);
			distances.Add(Planar(shooter, player));
		}

		float low = distance.PreferredDistance - distance.Tolerance, high = distance.PreferredDistance + distance.Tolerance;
		List<float> last = distances.Skip(240).ToList();
		Check("se estabiliza en la banda de distancia", last.All(d => d >= low - 0.05f && d <= high + 0.05f),
			$"últimos 2 s entre {F(last.Min())} y {F(last.Max())} (banda {F(low)}–{F(high)}); empezó a {F(distances[0])}");

		int warnFrames = Mathf.RoundToInt(shoot.WarnTime / _dt);
		List<int> gaps = shots.Select(shot => shot - warnings.Where(w => w <= shot).DefaultIfEmpty(-999).Max()).ToList();
		Check("dispara varias veces", shots.Count >= 2, $"{shots.Count} disparos en 6 s");
		Check("avisa antes de cada disparo", gaps.Count > 0 && gaps.All(gap => Math.Abs(gap - warnFrames) <= 1),
			$"aviso→disparo en fotogramas: {string.Join(", ", gaps)} (esperado {warnFrames} ± 1)");
		Check("sus proyectiles dañan al jugador", hits.Count >= 1, $"{hits.Count} impactos con daño");
		await Dispose(world);
	}

	private async Task ShooterEscapesCorner()
	{
		GD.Print("\n2. Tirador acorralado en una esquina");
		Node3D world = NewWorld(20.0f);
		PlayerController player = NewPlayer(world, new Vector3(6.5f, 0.5f, 6.5f), out _);
		Vector3 corner = new(9.2f, 0.5f, 9.2f);
		EnemyBase shooter = NewEnemy(world, EnemyKind.Shooter, corner);
		KeepDistanceBehavior distance = shooter.Behavior<KeepDistanceBehavior>();
		float start = Planar(shooter.GlobalPosition, player.GlobalPosition);

		await Frames(300);
		float end = Planar(shooter, player);
		float fromCorner = Planar(shooter.GlobalPosition, corner);
		Check("detecta el bloqueo y se desplaza en perpendicular", distance.StrafeCount >= 1, $"{distance.StrafeCount} desplazamientos laterales");
		Check("sale de la esquina", fromCorner > 1.5f, $"a {F(fromCorner)} m de la esquina");
		Check("recupera su distancia", end >= distance.PreferredDistance - distance.Tolerance - 0.05f,
			$"distancia {F(start)} → {F(end)}");
		await Dispose(world);
	}

	private async Task ProjectileLayersAndInvulnerability()
	{
		GD.Print("\n3. Proyectiles enemigos");
		Node3D world = NewWorld(20.0f);
		EnemyBase blocker = NewEnemy(world, EnemyKind.Chaser, new Vector3(5, 0.5f, 0));
		EnemyProjectile projectile = Launch(world, new Vector3(2, 0.5f, 0), Vector3.Right);
		(int Frame, bool HitPlayer)? impact = null;
		projectile.Impact += hitPlayer => impact = (_frame, hitPlayer);
		int launched = _frame;

		await Frames(150);
		float wallFrames = (10.0f - 0.2f - 2.0f) / projectile.Speed / _dt;
		Check("no daña a otros enemigos", blocker.Health.CurrentHealth == blocker.Health.MaxHealth,
			$"vida del enemigo en el camino {F(blocker.Health.CurrentHealth)}/{F(blocker.Health.MaxHealth)}");
		Check("muere en la pared antes de su tiempo de vida",
			impact is { HitPlayer: false } && Math.Abs(impact.Value.Frame - launched - wallFrames) <= 3 && !IsInstanceValid(projectile),
			impact == null ? "sin impacto" : $"impacto en el fotograma {impact.Value.Frame - launched} (pared esperada ≈ {F(wallFrames)}; vida {F(projectile.Lifetime / _dt)})");
		await Dispose(world);

		world = NewWorld(20.0f);
		NewPlayer(world, new Vector3(4, 0.5f, 0), out List<(int Frame, float Amount)> hits);
		List<bool> impacts = new();
		foreach (float x in new[] { 1.0f, 0.5f })
		{
			EnemyProjectile shot = Launch(world, new Vector3(x, 0.5f, 0), Vector3.Right);
			shot.Impact += hitPlayer => impacts.Add(hitPlayer);
		}

		await Frames(90);
		Check("respeta la invulnerabilidad del jugador", impacts.Count(hit => hit) == 2 && hits.Count == 1,
			$"{impacts.Count(hit => hit)} impactos en el jugador, {hits.Count} con daño");
		await Dispose(world);
	}

	private async Task ExploderInsideRadius()
	{
		GD.Print("\n4. Explosivo con el jugador dentro del radio");
		var result = await RunExploder(onLit: null);
		Check("enciende la mecha y explota tras FuseTime", result.Lit >= 0 && result.Exploded >= 0 &&
			Math.Abs(result.Exploded - result.Lit - Mathf.RoundToInt(result.FuseTime / _dt)) <= 1,
			$"mecha en {result.Lit}, explosión en {result.Exploded} (esperado +{Mathf.RoundToInt(result.FuseTime / _dt)} ± 1)");
		Check("daña al jugador dentro del radio", result.HitPlayer && Math.Abs(result.Damage - result.ExplosionDamage) < 0.001f,
			$"daño {F(result.Damage)} (esperado {F(result.ExplosionDamage)})");
		Check("muere al explotar (Died)", result.Died && result.Freed, $"Died {result.Died}, liberado {result.Freed}");
	}

	private async Task ExploderOutsideRadius()
	{
		GD.Print("\n5. Explosivo con el jugador fuera del radio al explotar");
		var result = await RunExploder(onLit: (exploder, player) =>
		{
			Vector3 away = (player.GlobalPosition - exploder.GlobalPosition) with { Y = 0 };
			player.GlobalPosition = exploder.GlobalPosition + away.Normalized() * 4.5f;
		});
		Check("explota", result.Exploded >= 0, $"explosión en {result.Exploded}");
		Check("no daña fuera del radio", !result.HitPlayer && result.Damage == 0.0f,
			$"daño {F(result.Damage)}; distancia al explotar {F(result.DistanceAtExplosion)} (radio {F(result.Radius)})");
		Check("muere al explotar (Died)", result.Died, $"Died {result.Died}");
	}

	private async Task ExploderKilledDuringFuse()
	{
		GD.Print("\n6. Explosivo al que matan durante la mecha");
		var result = await RunExploder(onLit: (exploder, player) => exploder.Health.TakeDamage(100.0f));
		Check("no explota", result.Exploded < 0, result.Exploded < 0 ? "sin explosión" : $"explotó en {result.Exploded}");
		Check("no hace daño", result.Damage == 0.0f, $"daño {F(result.Damage)}");
		Check("muere", result.Died, $"Died {result.Died}");
	}

	private async Task FlyerFollowsPattern()
	{
		GD.Print("\n7. Volador: patrón determinista");
		List<(Vector3 Position, float Time)> first = await FlyOnce(out FlyerInfo info);
		List<(Vector3 Position, float Time)> second = await FlyOnce(out _);

		float maxError = 0.0f;
		int signChanges = 0;
		float previousSide = 0.0f;
		for (int i = 0; i + 1 < first.Count; i++)
		{
			Vector3 velocity = FlyPatternBehavior.PatternVelocity(info.Player - first[i].Position, first[i].Time, info.Speed,
				info.Amplitude, info.Frequency, info.Phase);
			Vector3 expected = first[i].Position + velocity * _dt;
			maxError = Math.Max(maxError, expected.DistanceTo(first[i + 1].Position));

			Vector3 forward = ((info.Player - first[i].Position) with { Y = 0 }).Normalized();
			float side = new Vector3(-forward.Z, 0, forward.X).Dot(first[i + 1].Position - first[i].Position);
			if (previousSide != 0.0f && Math.Sign(side) != Math.Sign(previousSide))
			{
				signChanges++;
			}

			previousSide = side == 0.0f ? previousSide : side;
		}

		float repeatDifference = first.Zip(second, (a, b) => a.Position.DistanceTo(b.Position)).Max();
		Check("sigue el patrón esperado", maxError < 0.001f, $"error máximo frente a la integración del patrón {maxError:0.0e+00} m en {first.Count} muestras");
		Check("oscila a los lados", signChanges >= 2, $"{signChanges} cambios de lado en {F(first.Count * _dt)} s (frecuencia {F(info.Frequency)} Hz)");
		Check("es determinista", repeatDifference < 1e-6f, $"diferencia entre dos ejecuciones {repeatDifference:0.0e+00} m");
	}

	private async Task ExploderRoomOpensDoors()
	{
		GD.Print("\n8. Sala con un solo explosivo");
		Node3D world = new();
		AddChild(world);
		RoomData data = new()
		{
			Cell = Vector2I.Zero,
			Type = RoomType.Normal,
			TemplateId = "prueba_explosivo",
			Size = new Vector2(16, 16),
			EnemyScene = EnemyKinds.Scene(EnemyKind.Chaser),
			SpawnPoints = new[] { new Vector2(0, -5) },
			SpawnKinds = new[] { (int)EnemyKind.Exploder },
		};
		RoomController room = GD.Load<PackedScene>(RoomScene).Instantiate<RoomController>();
		room.Setup(data, new[] { DoorSide.North }, false);
		world.AddChild(room);
		NewPlayer(world, new Vector3(0, 0.5f, 4), out List<(int Frame, float Amount)> hits);
		bool cleared = false;
		room.RoomCleared += () => cleared = true;
		room.Activate();

		Door door = room.GetNode<Door>("Doors/DoorNorth");
		await Frames(30);
		RoomState stateAtHalfSecond = room.State;
		bool closedDuringCombat = room.State == RoomState.Combat && !door.IsOpen;
		for (int i = 0; i < 450 && !cleared; i++)
		{
			await Frames(1);
		}

		Check("cierra sus puertas durante el combate", closedDuringCombat,
			$"a los 0.5 s: estado {stateAtHalfSecond}, puerta {(closedDuringCombat ? "cerrada" : "abierta")}");
		Check("se despeja y abre sus puertas al explotar", cleared && room.State == RoomState.Cleared && door.IsOpen,
			$"RoomCleared {cleared}, estado {room.State}, puerta abierta {door.IsOpen}, daño al jugador {F(hits.Sum(h => h.Amount))}");
		await Dispose(world);
	}

	// --- ayudas --------------------------------------------------------------------------------------------------

	private sealed class ExploderResult
	{
		public int Lit = -1, Exploded = -1;
		public bool HitPlayer, Died, Freed;
		public float Damage, FuseTime, ExplosionDamage, Radius, DistanceAtExplosion;
	}

	private sealed class FlyerInfo
	{
		public Vector3 Player;
		public float Speed, Amplitude, Frequency, Phase;
	}

	private async Task<ExploderResult> RunExploder(Action<EnemyBase, PlayerController> onLit)
	{
		Node3D world = NewWorld(20.0f);
		PlayerController player = NewPlayer(world, new Vector3(0, 0.5f, 0), out List<(int Frame, float Amount)> hits);
		EnemyBase exploder = NewEnemy(world, EnemyKind.Exploder, new Vector3(6, 0.5f, 0));
		FuseBehavior fuse = exploder.Behavior<FuseBehavior>();
		ExploderResult result = new() { FuseTime = fuse.FuseTime, ExplosionDamage = fuse.ExplosionDamage, Radius = fuse.ExplosionRadius };

		fuse.FuseLit += () =>
		{
			result.Lit = _frame;
			onLit?.Invoke(exploder, player);
		};
		fuse.Exploded += hitPlayer =>
		{
			result.Exploded = _frame;
			result.HitPlayer = hitPlayer;
			result.DistanceAtExplosion = Planar(exploder, player);
		};
		exploder.Health.Died += () => result.Died = true;

		for (int i = 0; i < 300 && !(result.Died && i > 0 && !IsInstanceValid(exploder)); i++)
		{
			await Frames(1);
		}

		await Frames(60);
		result.Freed = !IsInstanceValid(exploder);
		result.Damage = hits.Sum(hit => hit.Amount);
		await Dispose(world);
		return result;
	}

	private Task<List<(Vector3 Position, float Time)>> FlyOnce(out FlyerInfo info)
	{
		Node3D world = NewWorld(30.0f);
		PlayerController player = NewPlayer(world, new Vector3(0, 0.5f, -12), out _);
		Vector3 spawn = new(0, 0.5f, 10);
		EnemyBase flyer = NewEnemy(world, EnemyKind.Flyer, spawn);
		FlyPatternBehavior fly = flyer.Behavior<FlyPatternBehavior>();
		info = new FlyerInfo
		{
			Player = player.GlobalPosition,
			Speed = flyer.Speed,
			Amplitude = fly.Amplitude,
			Frequency = fly.Frequency,
			Phase = FlyPatternBehavior.PhaseFor(spawn),
		};
		return Sample(world, flyer, fly);
	}

	private async Task<List<(Vector3 Position, float Time)>> Sample(Node3D world, EnemyBase flyer, FlyPatternBehavior fly)
	{
		List<(Vector3 Position, float Time)> samples = new();
		for (int i = 0; i < 90; i++)
		{
			await Frames(1);
			samples.Add((flyer.GlobalPosition, fly.Time));
		}

		await Dispose(world);
		return samples;
	}

	private Node3D NewWorld(float side)
	{
		Node3D world = new();
		AddChild(world);
		RoomLayout layout = RoomLayout.Build(new Vector2(side, side), Array.Empty<DoorSide>(), 3.0f, 1.0f, 3.0f, 1.5f);
		foreach (RoomLayout.Box box in layout.Walls.Append(layout.Floor))
		{
			StaticBody3D body = new() { Position = box.Center, CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
			body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size } });
			world.AddChild(body);
		}

		return world;
	}

	private PlayerController NewPlayer(Node3D world, Vector3 position, out List<(int Frame, float Amount)> hits)
	{
		PlayerController player = GD.Load<PackedScene>(PlayerScene).Instantiate<PlayerController>();
		HealthComponent health = HealthComponent.FindIn(player);
		health.MaxHealth = 1.0e6f;
		List<(int Frame, float Amount)> received = new();
		health.Damaged += (amount, current) => received.Add((_frame, amount));
		player.Position = position;
		world.AddChild(player);
		hits = received;
		return player;
	}

	private static EnemyBase NewEnemy(Node3D world, EnemyKind kind, Vector3 position)
	{
		EnemyBase enemy = EnemyKinds.Scene(kind).Instantiate<EnemyBase>();
		enemy.Position = position;
		world.AddChild(enemy);
		return enemy;
	}

	private static EnemyProjectile Launch(Node3D world, Vector3 position, Vector3 direction)
	{
		EnemyProjectile projectile = GD.Load<PackedScene>(ProjectileScene).Instantiate<EnemyProjectile>();
		world.AddChild(projectile);
		projectile.GlobalPosition = position;
		projectile.Launch(direction);
		return projectile;
	}

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_frame++;
		}
	}

	private async Task Dispose(Node3D world)
	{
		world.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  [{(ok ? "OK" : "FALLO")}] {name}: {detail}");
		if (!ok)
		{
			_failures.Add(name);
		}
	}

	private static float Planar(Node3D a, Node3D b)
	{
		return Planar(a.GlobalPosition, b.GlobalPosition);
	}

	private static float Planar(Vector3 a, Vector3 b)
	{
		return new Vector2(a.X - b.X, a.Z - b.Z).Length();
	}

	private static string F(double value)
	{
		return value.ToString("0.00", CultureInfo.InvariantCulture);
	}
}
