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
///   9. Huidor: se aleja del jugador cercano y lo hostiga con embestidas avisadas, cuya dirección se fija al empezar el
///      aviso; llega a golpearlo.
///  10. Huidor acorralado en una esquina (sin embestidas): se desplaza en perpendicular y sale.
///  11. Excavador: bajo tierra una lágrima no le hace daño y fuera sí; dispara mientras está fuera; siempre emerge a
///      MinEmergeDistance (3 m) o más del jugador.
///  12. Sala con un solo Excavador y el jugador quieto a 2 m: emerge (a 3 m o más), se le puede matar y la sala se despeja.
///  13. Sala con un solo Excavador y el jugador pegado a su montículo: tras MaxUndergroundTime emerge en el punto más
///      lejano de la sala (a 3 m o más), se le puede matar y la sala se despeja.
/// El jugador nunca pulsa nada (se queda quieto, salvo cuando la prueba lo mueve) y tiene vida de sobra; en las salas con
/// Excavador, la prueba dispara lágrimas por él. Sale con código 0 si todo pasa.
/// </summary>
public partial class EnemyBehaviorTest : Node
{
	private const string PlayerScene = "res://scenes/player/Player.tscn";
	private const string ProjectileScene = "res://scenes/combat/EnemyProjectile.tscn";
	private const string RoomScene = "res://scenes/rooms/Room.tscn";
	private const string TearScene = "res://scenes/combat/Tear.tscn";

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
		await RunnerFleesAndHarasses();
		await RunnerEscapesCorner();
		await DiggerOnlyHurtWhenOut();
		await DiggerRoomWithPlayerNearby();
		await DiggerRoomWithPlayerOnMound();

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

	private async Task RunnerFleesAndHarasses()
	{
		GD.Print("\n9. Huidor: se aleja y hostiga con embestidas");
		Node3D world = NewWorld(24.0f);
		PlayerController player = NewPlayer(world, new Vector3(0, 0.5f, 0), out List<(int Frame, float Amount)> hits);
		EnemyBase runner = NewEnemy(world, EnemyKind.Runner, new Vector3(2, 0.5f, 0));
		KeepDistanceBehavior distance = runner.Behavior<KeepDistanceBehavior>();
		DashBehavior dash = runner.Behavior<DashBehavior>();
		List<(int Frame, Vector3 Direction)> warnings = new(), dashes = new();
		List<bool> ends = new();
		dash.WarningStarted += direction => warnings.Add((_frame, direction));
		dash.DashStarted += direction => dashes.Add((_frame, direction));
		dash.DashEnded += hitPlayer => ends.Add(hitPlayer);

		float start = Planar(runner, player);
		int fleeFrames = Mathf.RoundToInt((dash.FirstDelay - 0.2f) / _dt);
		await Frames(fleeFrames);
		float fled = Planar(runner, player);
		Check("se aleja del jugador cercano", fled >= distance.PreferredDistance - distance.Tolerance - 0.05f,
			$"{F(start)} → {F(fled)} m en {F(fleeFrames * _dt)} s (huye por debajo de {F(distance.PreferredDistance - distance.Tolerance)} m)");

		await Frames(480);
		int warnFrames = Mathf.RoundToInt(dash.WarnTime / _dt);
		List<int> gaps = dashes.Select(d => d.Frame - warnings.Where(w => w.Frame <= d.Frame).Select(w => w.Frame).DefaultIfEmpty(-999).Max()).ToList();
		bool sameDirection = dashes.All(d => warnings.Where(w => w.Frame <= d.Frame).OrderBy(w => w.Frame).Last().Direction.IsEqualApprox(d.Direction));
		Check("avisa antes de cada embestida", dashes.Count >= 1 && gaps.All(gap => Math.Abs(gap - warnFrames) <= 1),
			$"{dashes.Count} embestidas; aviso→embestida en fotogramas: {string.Join(", ", gaps)} (esperado {warnFrames} ± 1)");
		Check("la dirección se fija al empezar el aviso", dashes.Count >= 1 && sameDirection,
			$"{dashes.Count} embestidas con la misma dirección que su aviso");
		Check("hostiga: llega a golpear al jugador", hits.Count >= 1 && ends.Any(hit => hit),
			$"{hits.Count} golpes con daño; embestidas que acabaron en el jugador: {ends.Count(hit => hit)} de {ends.Count}");
		await Dispose(world);
	}

	private async Task RunnerEscapesCorner()
	{
		GD.Print("\n10. Huidor acorralado en una esquina (sin embestidas)");
		Node3D world = NewWorld(20.0f);
		// El jugador más cerca que en la prueba del Tirador: el Huidor huye por debajo de 4 m y es más pequeño, así que con el
		// jugador a (6.5, 6.5) ya llegaría a 4 m metiéndose en la esquina y no quedaría atrapado.
		PlayerController player = NewPlayer(world, new Vector3(7.0f, 0.5f, 7.0f), out _);
		Vector3 corner = new(9.2f, 0.5f, 9.2f);
		EnemyBase runner = NewEnemy(world, EnemyKind.Runner, corner);
		runner.Behavior<DashBehavior>().FirstDelay = 1.0e6f;
		KeepDistanceBehavior distance = runner.Behavior<KeepDistanceBehavior>();
		float start = Planar(runner.GlobalPosition, player.GlobalPosition);

		await Frames(300);
		float end = Planar(runner, player);
		float fromCorner = Planar(runner.GlobalPosition, corner);
		Check("detecta el bloqueo y se desplaza en perpendicular", distance.StrafeCount >= 1, $"{distance.StrafeCount} desplazamientos laterales");
		Check("sale de la esquina", fromCorner > 1.5f, $"a {F(fromCorner)} m de la esquina");
		Check("recupera su distancia", end >= distance.PreferredDistance - distance.Tolerance - 0.05f, $"distancia {F(start)} → {F(end)}");
		await Dispose(world);
	}

	private async Task DiggerOnlyHurtWhenOut()
	{
		GD.Print("\n11. Excavador: vulnerable solo fuera de la tierra");
		Node3D world = NewWorld(20.0f);
		PlayerController player = NewPlayer(world, new Vector3(0, 0.5f, 0), out _);
		EnemyBase digger = NewEnemy(world, EnemyKind.Digger, new Vector3(6, 0.5f, 0));
		DigBehavior dig = digger.Behavior<DigBehavior>();
		HealthComponent health = HealthComponent.FindIn(digger);
		List<(float Distance, bool Forced)> surfaced = new();
		int fired = 0;
		dig.Surfaced += (distance, forced) => surfaced.Add((distance, forced));
		dig.Fired += _ => fired++;

		await Until(() => dig.Phase == DigBehavior.DigPhase.Underground, 600);
		await Frames(10);
		float beforeUnder = health.CurrentHealth;
		ShootTear(world, Toward(digger.GlobalPosition, Vector3.Zero, 1.2f), digger.GlobalPosition);
		await Frames(20);
		float afterUnder = health.CurrentHealth;

		await Until(() => dig.Phase == DigBehavior.DigPhase.Out, 600);
		await Frames(5);
		float beforeOut = health.CurrentHealth;
		ShootTear(world, Toward(digger.GlobalPosition, Vector3.Zero, 1.2f), digger.GlobalPosition);
		await Frames(20);
		float afterOut = health.CurrentHealth;

		await Frames(720);
		Check("bajo tierra no recibe daño", afterUnder == beforeUnder, $"vida {F(beforeUnder)} → {F(afterUnder)}");
		Check("fuera sí recibe daño", afterOut < beforeOut, $"vida {F(beforeOut)} → {F(afterOut)}");
		Check("dispara mientras está fuera", fired >= 1, $"{fired} disparos");
		Check("siempre emerge a 3 m o más del jugador", surfaced.Count >= 2 && surfaced.All(s => s.Distance >= dig.MinEmergeDistance),
			$"{surfaced.Count} emergencias a {string.Join(", ", surfaced.Select(s => F(s.Distance)))} m");
		Check("el jugador no se ha movido", Planar(player.GlobalPosition, Vector3.Zero) < 0.01f,
			$"posición del jugador {player.GlobalPosition}");
		await Dispose(world);
	}

	private async Task DiggerRoomWithPlayerNearby()
	{
		GD.Print("\n12. Sala con un solo Excavador y el jugador quieto a 2 m");
		(Node3D world, RoomController room, PlayerController player) = NewDiggerRoom(new Vector3(0, 0.5f, -1));
		bool cleared = false;
		room.RoomCleared += () => cleared = true;
		room.Activate();

		EnemyBase digger = await SpawnedEnemy(room);
		DigBehavior dig = digger.Behavior<DigBehavior>();
		List<(float Distance, bool Forced)> surfaced = new();
		dig.Surfaced += (distance, forced) => surfaced.Add((distance, forced));
		float startDistance = Planar(digger, player);

		bool emerged = await Until(() => surfaced.Count >= 1, 900);
		await KillWithTears(world, player, digger, () => cleared, 1800);

		Check("empieza a 2 m del jugador", Math.Abs(startDistance - 2.0f) < 0.05f, $"{F(startDistance)} m");
		Check("la primera emergencia es normal (no forzada)", emerged && !surfaced[0].Forced,
			$"primera emergencia {(emerged ? (surfaced[0].Forced ? "forzada" : "normal") : "ninguna")}");
		Check("termina emergiendo a 3 m o más", emerged && surfaced.All(s => s.Distance >= dig.MinEmergeDistance),
			$"emergencias a {string.Join(", ", surfaced.Select(s => $"{F(s.Distance)} m{(s.Forced ? " (forzada)" : "")}"))}");
		Check("se le puede matar y la sala se despeja", cleared && room.State == RoomState.Cleared, $"RoomCleared {cleared}, estado {room.State}");
		await Dispose(world);
	}

	private async Task DiggerRoomWithPlayerOnMound()
	{
		GD.Print("\n13. Sala con un solo Excavador y el jugador pegado a su montículo");
		(Node3D world, RoomController room, PlayerController player) = NewDiggerRoom(new Vector3(0, 0.5f, 3));
		bool cleared = false;
		room.RoomCleared += () => cleared = true;
		room.Activate();

		EnemyBase digger = await SpawnedEnemy(room);
		DigBehavior dig = digger.Behavior<DigBehavior>();
		List<(float Distance, bool Forced)> surfaced = new();
		dig.Surfaced += (distance, forced) => surfaced.Add((distance, forced));

		await Until(() => dig.Phase == DigBehavior.DigPhase.Underground, 600);
		int glued = 0;
		for (int i = 0; i < 900 && surfaced.Count == 0; i++)
		{
			player.GlobalPosition = new Vector3(digger.GlobalPosition.X, 0.5f, digger.GlobalPosition.Z);
			glued++;
			await Frames(1);
		}

		await KillWithTears(world, player, digger, () => cleared, 1800);
		Check("emerge forzado tras el tiempo máximo bajo tierra", surfaced.Count >= 1 && surfaced[0].Forced,
			$"pegado {F(glued * _dt)} s (máximo {F(dig.MaxUndergroundTime)} s); primera emergencia {(surfaced.Count > 0 ? (surfaced[0].Forced ? "forzada" : "normal") : "ninguna")}");
		Check("emerge a 3 m o más", surfaced.Count >= 1 && surfaced.All(s => s.Distance >= dig.MinEmergeDistance),
			$"emergencias a {string.Join(", ", surfaced.Select(s => F(s.Distance)))} m");
		Check("se le puede matar y la sala se despeja", cleared && room.State == RoomState.Cleared, $"RoomCleared {cleared}, estado {room.State}");
		await Dispose(world);
	}

	// --- ayudas --------------------------------------------------------------------------------------------------

	// Sala real de 16 x 16 con un solo Excavador en (0, -3) y el jugador en playerPosition (que debe estar en la sala).
	private (Node3D World, RoomController Room, PlayerController Player) NewDiggerRoom(Vector3 playerPosition)
	{
		Node3D world = new();
		AddChild(world);
		RoomData data = new()
		{
			Cell = Vector2I.Zero,
			Type = RoomType.Normal,
			TemplateId = "prueba_excavador",
			Size = new Vector2(16, 16),
			EnemyScene = EnemyKinds.Scene(EnemyKind.Chaser),
			SpawnPoints = new[] { new Vector2(0, -3) },
			SpawnKinds = new[] { (int)EnemyKind.Digger },
		};
		RoomController room = GD.Load<PackedScene>(RoomScene).Instantiate<RoomController>();
		room.Setup(data, new[] { DoorSide.North }, false);
		world.AddChild(room);
		PlayerController player = NewPlayer(world, playerPosition, out _);
		return (world, room, player);
	}

	private async Task<EnemyBase> SpawnedEnemy(RoomController room)
	{
		Node enemies = room.GetNode("Enemies");
		await Until(() => enemies.GetChildCount() > 0, 300);
		return enemies.GetChildCount() > 0 ? enemies.GetChild<EnemyBase>(0) : null;
	}

	// Dispara una lágrima del jugador hacia el enemigo cada 20 fotogramas hasta que `done` o se agote el tiempo.
	private async Task KillWithTears(Node3D world, PlayerController player, EnemyBase enemy, Func<bool> done, int maxFrames)
	{
		for (int i = 0; i < maxFrames && !done(); i++)
		{
			if (i % 20 == 0 && IsInstanceValid(enemy))
			{
				ShootTear(world, player.GlobalPosition, enemy.GlobalPosition);
			}

			await Frames(1);
		}
	}

	private static void ShootTear(Node3D world, Vector3 from, Vector3 target)
	{
		Vector3 direction = target - from;
		direction.Y = 0.0f;
		Tear tear = GD.Load<PackedScene>(TearScene).Instantiate<Tear>();
		world.AddChild(tear);
		tear.GlobalPosition = new Vector3(from.X, 0.5f, from.Z) + direction.Normalized() * 0.6f;
		tear.Launch(direction.Normalized());
	}

	// Un punto a `distance` metros de `from` en dirección a `toward` (en el plano).
	private static Vector3 Toward(Vector3 from, Vector3 toward, float distance)
	{
		Vector3 direction = toward - from;
		direction.Y = 0.0f;
		return from + direction.Normalized() * distance;
	}

	private async Task<bool> Until(Func<bool> condition, int maxFrames)
	{
		for (int i = 0; i < maxFrames; i++)
		{
			if (condition())
			{
				return true;
			}

			await Frames(1);
		}

		return condition();
	}

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
