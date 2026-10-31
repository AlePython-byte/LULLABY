using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de resistencia: recorre muchas salas seguidas con el nivel real (RoomManager) y en CADA una comprueba que una lágrima
/// hace daño a un enemigo y que un proyectil enemigo hace daño a Noah. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/RoomEnduranceTest.tscn -- [--rooms=14] [--seeds=12345,777,4242]
///                                                                                  [--audio=real|silent|off] [--generator=3] [--fight=1] [--items=1]
///
/// Por cada sala (la de inicio, las de combate con sus enemigos reales y las ya despejadas):
///   1. Congela a los enemigos reales (sin movimiento ni ataques) para que nada más dañe a Noah durante la medición.
///   2. Lágrima: dispara con el código real del jugador contra un enemigo de prueba (y contra el real más cercano, si hay) y
///      comprueba que su vida baja.
///   3. Proyectil: lanza un proyectil enemigo real contra Noah, tras dejar pasar su invulnerabilidad, y comprueba que su vida baja.
///   4. Despeja la sala matando a los enemigos y cruza la puerta a la sala vecina menos visitada (sin entrar en la del jefe, que
///      termina la partida).
/// Si una comprobación falla, vuelca el estado que podría arrastrarse entre salas (capas y máscaras, invulnerabilidad, bloqueo,
/// lágrimas y proyectiles en vuelo...).
///
/// Audio (--audio): "real" activa los reproductores de AudioManager (con el controlador de audio ficticio de --headless),
/// "silent" deja el modo silencioso de --headless y "off" lo apaga (AudioManager.Enabled = false). La prueba no referencia
/// AudioManager por tipo, así que compila también en commits anteriores al audio (línea base). Sale con código 0 si todo pasa.
/// </summary>
public partial class RoomEnduranceTest : Node
{
	private const string ProjectileScenePath = "res://scenes/combat/EnemyProjectile.tscn";
	private const string DummyScenePath = "res://scenes/enemies/BasicEnemy.tscn";

	private readonly List<string> _failures = new();
	private int _probes;
	private string _audioMode = "silent";
	private bool _fight;
	private bool _items;

	public override async void _Ready()
	{
		Dictionary<string, string> args = ParseArgs();
		int rooms = args.TryGetValue("rooms", out string r) && int.TryParse(r, out int n) ? n : 14;
		int[] seeds = args.TryGetValue("seeds", out string s) ? s.Split(',').Select(int.Parse).ToArray() : new[] { 12345, 777, 4242 };
		_audioMode = args.TryGetValue("audio", out string a) ? a : "silent";
		_fight = args.TryGetValue("fight", out string f) && f == "1";
		_items = args.TryGetValue("items", out string it) && it == "1";
		ApplyAudioMode();
		GD.Print($"=== RoomEnduranceTest: {rooms} salas por semilla, semillas {string.Join(",", seeds)}, audio {_audioMode} ===");
		try
		{
			foreach (int seed in seeds)
			{
				await RunSeed(seed, rooms);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}

		GD.Print($"Sondeos: {_probes}. " + (_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)"));
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private static Dictionary<string, string> ParseArgs()
	{
		Dictionary<string, string> result = new();
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--") && argument.Contains('='))
			{
				string[] parts = argument[2..].Split('=', 2);
				result[parts[0]] = parts[1];
			}
		}

		return result;
	}

	// Sin referenciar AudioManager por tipo: así compila antes de que existiera.
	private void ApplyAudioMode()
	{
		Node audio = GetTree().Root.GetNodeOrNull("AudioManager");
		if (audio == null)
		{
			GD.Print("  (no hay AudioManager: línea base sin audio)");
			return;
		}

		if (_audioMode == "off")
		{
			// Audio apagado: todas las peticiones se ignoran (AudioManager.Enabled = false). Liberar el autoload provoca un cierre brusco.
			audio.GetType().GetProperty("Enabled")?.SetValue(audio, false);
			GD.Print("  AudioManager.Enabled = false (audio apagado)");
		}
		else
		{
			audio.GetType().GetProperty("Silent")?.SetValue(audio, _audioMode != "real");
			GD.Print($"  AudioManager.Silent = {_audioMode != "real"}");
		}
	}

	private async Task RunSeed(int seed, int roomCount)
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, seed, withMother: true, unlimitedHealth: false, treasure: _items);
		level.Health.MaxHealth = 1000.0f; // 1e9 no distingue un punto de daño en un float; 1000 sí
		level.Health.ResetForNewRun();
		await BossTestHarness.Frames(tree, 5);
		GD.Print($"\n--- semilla {seed}: {level.Root.GetNode<Label>("SeedHud/Label").Text.Replace("\n", " | ")} ---");

		Dictionary<Vector2I, int> visits = new();
		List<string> loads = new();
		level.Rooms.RoomLoaded += loaded =>
		{
			loads.Add($"{loaded.Data.Cell}@{Engine.GetPhysicsFrames()}");
			foreach (Area3D trigger in loaded.GetNode("Doors").GetChildren().OfType<Area3D>())
			{
				Area3D captured = trigger;
				captured.BodyEntered += body => loads.Add($"  [{captured.Name} recibe {body.Name} ({body.GetType().Name}) en {body.GlobalPosition:F2}; trigger en {captured.GlobalPosition:F2} @{Engine.GetPhysicsFrames()}]");
			}

			loaded.DoorCrossed += side => loads.Add($"cruce({(DoorSide)side}) nodo {level.Player.GlobalPosition:F2} físico {PhysicsServer3D.BodyGetState(level.Player.GetRid(), PhysicsServer3D.BodyState.Transform).AsTransform3D().Origin:F2} @{Engine.GetPhysicsFrames()}");
		};
		for (int index = 1; index <= roomCount; index++)
		{
			await BossTestHarness.WaitUntil(tree, () => GodotObject.IsInstanceValid(level.Rooms.CurrentRoom) && level.Player.IsPhysicsProcessing(), 300, "sala lista");
			RoomController room = level.Rooms.CurrentRoom;
			visits[room.Data.Cell] = visits.GetValueOrDefault(room.Data.Cell) + 1;

			// Los enemigos reales aparecen tras StartDelay; se esperan, se congelan y se miden.
			bool expectsEnemies = room.State != RoomState.Cleared && room.Data.SpawnPoints.Length > 0;
			Node enemiesRoot = room.GetNode("Enemies");
			if (expectsEnemies)
			{
				await BossTestHarness.WaitUntil(tree, () => enemiesRoot.GetChildCount() > 0, 300, "aparición de los enemigos");
				await BossTestHarness.Frames(tree, 2);
			}

			List<EnemyBase> real = enemiesRoot.GetChildren().OfType<EnemyBase>().ToList();
			int fought = real.Count;
			if (_fight && real.Count > 0)
			{
				// Combate real: los enemigos se mueven y atacan, y Noah los abate con sus lágrimas (el código real del jugador).
				fought = await Fight(level, room);
				real = enemiesRoot.GetChildren().OfType<EnemyBase>().Where(e => !e.IsQueuedForDeletion()).ToList();
			}

			foreach (EnemyBase enemy in real)
			{
				enemy.SetPhysicsProcess(false);
			}

			if (_items && room.Data.Type == RoomType.Treasure)
			{
				int picked = await CollectItems(level, room);
				GD.Print($"      tesoro: {picked} objetos recogidos; inventario {string.Join(",", level.Player.GetNode<PlayerItems>("Items").CollectedItems.Select(i => i.Id))}");
			}

			string label = $"sala {index} {room.Data.Cell} {room.Data.Type} ({(_fight ? $"{fought} abatidos, quedan {real.Count}" : $"{real.Count} enemigos")}, estado {room.State})";
			Vector3 startPosition = level.Player.GlobalPosition;
			bool isBoss = room.Data.Type == RoomType.Boss;
			bool tearOk = isBoss || await ProbeTear(level, room, null);
			EnemyBase nearest = real.Where(e => e.Kind != EnemyKind.Digger).OrderBy(e => e.GlobalPosition.DistanceTo(level.Player.GlobalPosition)).FirstOrDefault();
			bool realOk = nearest == null || await ProbeTear(level, room, nearest);
			Vector3 afterTear = level.Player.GlobalPosition;
			bool shotOk = await ProbeProjectile(level, room);
			_probes += 3;

			GD.Print($"      métricas: objetos {(long)Performance.GetMonitor(Performance.Monitor.ObjectCount)}, nodos {(long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, huérfanos {(long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)}, recursos {(long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)}, cuerpos3D activos {(long)Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects)}, islas {(long)Performance.GetMonitor(Performance.Monitor.Physics3DIslandCount)}, memoria {(long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576} MB");
			if (tearOk && realOk && shotOk)
			{
				GD.Print($"  OK    {label}: lágrima {(nearest != null ? "(ficticio y real)" : "(ficticio)")}, proyectil | Noah {startPosition:F2} → tras lágrimas {afterTear:F2} → tras proyectil {level.Player.GlobalPosition:F2}");
			}
			else
			{
				string what = $"{label}: lágrima→ficticio {Mark(tearOk)}, lágrima→real {(nearest == null ? "-" : Mark(realOk))}, proyectil→Noah {Mark(shotOk)}";
				GD.Print($"  FALLO {what}");
				Dump(level, room);
				_failures.Add($"semilla {seed}, {what}");
				if (_failures.Count >= 3)
				{
					break;
				}
			}

			if (isBoss)
			{
				GD.Print("      (sala del jefe: se mide con The Mother real y termina el recorrido de esta semilla)");
				break;
			}

			// Despejar y cruzar a la sala vecina menos visitada (nunca la del jefe).
			foreach (EnemyBase enemy in room.GetNode("Enemies").GetChildren().OfType<EnemyBase>().ToList())
			{
				HealthComponent.FindIn(enemy).TakeDamage(1.0e6f);
			}

			await BossTestHarness.WaitUntil(tree, () => room.State == RoomState.Cleared, 300, "sala despejada");
			DoorSide next = ChooseDoor(level, room, visits, _items);
			// Como en el juego, Noah sale por el hueco de la puerta (no se queda donde apareció): evita el cruce en cola que el motor
			// entrega un fotograma tarde cuando el cuerpo antiguo solapaba un disparador de la sala nueva.
			level.Rooms.CurrentRoom.EmitSignal(RoomController.SignalName.DoorCrossed, (int)next);
			await Settle(level, room, index, next, loads);
		}

		await BossTestHarness.Close(tree, level);
	}

	// Recoge los objetos del tesoro con la recogida real (ItemPickup.TryCollect), como al pisarlos.
	private async Task<int> CollectItems(BossTestHarness.Level level, RoomController room)
	{
		await BossTestHarness.Frames(GetTree(), 5);
		int picked = 0;
		Node items = room.GetNodeOrNull("TreasureItems");
		if (items == null)
		{
			return 0;
		}

		foreach (ItemPickup pickup in items.GetChildren().OfType<ItemPickup>().ToList())
		{
			if (pickup.TryCollect(level.Player))
			{
				picked++;
			}
		}

		await BossTestHarness.Frames(GetTree(), 5);
		return picked;
	}

	// Pelea de verdad: dispara con el código real del jugador al enemigo más cercano cada 0,3 s hasta abatirlos (o 40 s como mucho).
	private async Task<int> Fight(BossTestHarness.Level level, RoomController room)
	{
		SceneTree tree = GetTree();
		Node enemiesRoot = room.GetNode("Enemies");
		MethodInfo fire = typeof(PlayerShooter).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic);
		int total = enemiesRoot.GetChildren().OfType<EnemyBase>().Count();
		for (int frame = 0; frame < 2400; frame++)
		{
			List<EnemyBase> alive = enemiesRoot.GetChildren().OfType<EnemyBase>().Where(e => !e.IsQueuedForDeletion() && !HealthComponent.FindIn(e).IsDead).ToList();
			if (alive.Count == 0)
			{
				break;
			}

			if (frame % 18 == 0)
			{
				EnemyBase target = alive.OrderBy(e => e.GlobalPosition.DistanceTo(level.Player.GlobalPosition)).First();
				Vector3 direction = target.GlobalPosition - level.Player.GlobalPosition;
				direction.Y = 0.0f;
				if (direction.LengthSquared() > 0.01f)
				{
					fire.Invoke(level.Player.GetNode<PlayerShooter>("Shooter"), new object[] { direction.Normalized() });
				}
			}

			await BossTestHarness.Frames(tree, 1);
		}

		return total - enemiesRoot.GetChildren().OfType<EnemyBase>().Count(e => !e.IsQueuedForDeletion() && !HealthComponent.FindIn(e).IsDead);
	}

	// Lágrima: dispara con el código real del jugador contra un enemigo (el de prueba o uno real) y espera a que su vida baje.
	private async Task<bool> ProbeTear(BossTestHarness.Level level, RoomController room, EnemyBase target)
	{
		SceneTree tree = GetTree();
		EnemyBase dummy = null;
		if (target == null)
		{
			dummy = NewDummy(level, room);
			target = dummy;
		}

		HealthComponent health = HealthComponent.FindIn(target);
		float before = health.CurrentHealth;
		Vector3 direction = target.GlobalPosition - level.Player.GlobalPosition;
		direction.Y = 0.0f;
		typeof(PlayerShooter).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(level.Player.GetNode<PlayerShooter>("Shooter"), new object[] { direction.Normalized() });
		bool hit = true;
		try
		{
			await BossTestHarness.WaitUntil(tree, () => health.CurrentHealth < before || !GodotObject.IsInstanceValid(target), 150, "daño de la lágrima");
		}
		catch (System.IO.InvalidDataException)
		{
			hit = false;
		}

		if (dummy != null && GodotObject.IsInstanceValid(dummy))
		{
			dummy.QueueFree();
		}

		return hit;
	}

	// Proyectil enemigo real: nace a unos metros de Noah, apunta a ella y debe quitarle vida (pasada su invulnerabilidad).
	private async Task<bool> ProbeProjectile(BossTestHarness.Level level, RoomController room)
	{
		SceneTree tree = GetTree();
		await BossTestHarness.Frames(tree, 80); // 1 s de invulnerabilidad tras un golpe anterior
		EnemyBase shooter = NewDummy(level, room);
		Vector3 direction = level.Player.GlobalPosition - shooter.GlobalPosition;
		direction.Y = 0.0f;
		float before = level.Health.CurrentHealth;
		EnemyProjectile projectile = EnemyProjectile.FireFrom(shooter, GD.Load<PackedScene>(ProjectileScenePath), direction.Normalized(), 0.7f);
		bool hit = true;
		try
		{
			await BossTestHarness.WaitUntil(tree, () => level.Health.CurrentHealth < before, 240, "daño del proyectil");
		}
		catch (System.IO.InvalidDataException)
		{
			hit = false;
		}

		if (GodotObject.IsInstanceValid(projectile))
		{
			projectile.QueueFree();
		}

		shooter.QueueFree();
		return hit;
	}

	// Enemigo de prueba, quieto, a 4 m de Noah hacia el centro de la sala.
	private static EnemyBase NewDummy(BossTestHarness.Level level, RoomController room)
	{
		Vector3 toCenter = -level.Player.GlobalPosition;
		toCenter.Y = 0.0f;
		toCenter = toCenter.LengthSquared() < 1.0f ? Vector3.Right : toCenter.Normalized();
		EnemyBase dummy = GD.Load<PackedScene>(DummyScenePath).Instantiate<EnemyBase>();
		room.GetNode("Enemies").AddChild(dummy);
		Vector3 position = level.Player.GlobalPosition + toCenter * 4.0f;
		dummy.GlobalPosition = new Vector3(position.X, 0.5f, position.Z);
		dummy.SetPhysicsProcess(false);
		return dummy;
	}

	// Espera a que las transiciones terminen y la sala actual se mantenga estable (el motor entrega los cruces en cola con un fotograma
	// de retraso, así que a veces una puerta encadena otra: se acepta en qué sala se acabe).
	private async Task Settle(BossTestHarness.Level level, RoomController previous, int index, DoorSide side, List<string> loads)
	{
		SceneTree tree = GetTree();
		RoomController last = null;
		int stable = 0;
		for (int frame = 0; frame < 900; frame++)
		{
			await BossTestHarness.Frames(tree, 1);
			RoomController current = GodotObject.IsInstanceValid(level.Rooms.CurrentRoom) ? level.Rooms.CurrentRoom : null;
			bool quiet = current != null && !level.Rooms.IsTransitioning && level.Player.IsPhysicsProcessing();
			stable = quiet && current == last ? stable + 1 : 0;
			last = current;
			if (stable >= 12 && current != previous)
			{
				return;
			}
		}

		GD.Print($"  cargas de sala (celda@fotograma): {string.Join(" ", loads.TakeLast(12))}");
		throw new System.IO.InvalidDataException($"la transición desde la sala {index} hacia {side} no terminó");
	}

	// Primer paso del camino más corto hacia una sala del tipo dado (nunca atraviesa la sala del jefe).
	private static DoorSide? FirstStepTowards(Dictionary<Vector2I, RoomData> rooms, Vector2I from, RoomType target)
	{
		Dictionary<Vector2I, DoorSide> first = new();
		Queue<Vector2I> queue = new();
		queue.Enqueue(from);
		HashSet<Vector2I> seen = new() { from };
		while (queue.Count > 0)
		{
			Vector2I cell = queue.Dequeue();
			foreach (DoorSide side in Enum.GetValues<DoorSide>())
			{
				Vector2I next = cell + side.GridOffset();
				if (!rooms.TryGetValue(next, out RoomData data) || data.Type == RoomType.Boss || !seen.Add(next))
				{
					continue;
				}

				first[next] = cell == from ? side : first[cell];
				if (data.Type == target)
				{
					return first[next];
				}

				queue.Enqueue(next);
			}
		}

		return null;
	}

	private static Vector3 DoorwayPosition(RoomController room, DoorSide side)
	{
		Vector2 size = room.Data.Size;
		Vector3 direction = side.Direction();
		float half = direction.X != 0.0f ? size.X / 2.0f : size.Y / 2.0f;
		Vector3 position = direction * (half + 0.5f);
		position.Y = 0.5f;
		return position;
	}

	private static DoorSide ChooseDoor(BossTestHarness.Level level, RoomController room, Dictionary<Vector2I, int> visits, bool goToTreasure)
	{
		Dictionary<Vector2I, RoomData> rooms = level.Rooms.Map.Rooms.ToDictionary(r => r.Cell);
		if (goToTreasure && !visits.Keys.Any(cell => rooms[cell].Type == RoomType.Treasure))
		{
			DoorSide? step = FirstStepTowards(rooms, room.Data.Cell, RoomType.Treasure);
			if (step.HasValue)
			{
				return step.Value;
			}
		}

		return Enum.GetValues<DoorSide>()
			.Where(side => rooms.TryGetValue(room.Data.Cell + side.GridOffset(), out RoomData next) && next.Type != RoomType.Boss)
			.OrderBy(side => visits.GetValueOrDefault(room.Data.Cell + side.GridOffset()))
			.ThenBy(side => (int)side)
			.First();
	}

	private void Dump(BossTestHarness.Level level, RoomController room)
	{
		PlayerHealth playerHealth = level.Player.GetNode<PlayerHealth>("PlayerHealth");
		float timer = (float)(typeof(PlayerHealth).GetField("_invulnerableTimer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(playerHealth) ?? -1.0f);
		SceneTree tree = GetTree();
		GD.Print($"      Noah: vida {level.Health.CurrentHealth}, capa {level.Player.CollisionLayer} máscara {level.Player.CollisionMask}, bloqueada {playerHealth.IsLocked}, invulnerabilidad {timer:F2} s, física {level.Player.IsPhysicsProcessing()}, disparo {level.Player.GetNode<PlayerShooter>("Shooter").IsPhysicsProcessing()}");
		GD.Print($"      lágrimas en vuelo {tree.GetNodesInGroup("tears").Count}, proyectiles enemigos en vuelo {tree.GetNodesInGroup("enemy_projectiles").Count}, hijos de Enemies {room.GetNode("Enemies").GetChildCount()}, proyectiles de la sala {room.ProjectilesRoot.GetChildCount()}");
		foreach (EnemyBase enemy in room.GetNode("Enemies").GetChildren().OfType<EnemyBase>().Take(3))
		{
			GD.Print($"      enemigo {enemy.Kind}: capa {enemy.CollisionLayer} máscara {enemy.CollisionMask}, vida {HealthComponent.FindIn(enemy).CurrentHealth}, física {enemy.IsPhysicsProcessing()}");
		}

		GD.Print($"      Physics frames {Engine.GetPhysicsFrames()}, objetos {(long)Performance.GetMonitor(Performance.Monitor.ObjectCount)}, nodos {(long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, huérfanos {(long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)}, cuerpos 3D activos {(long)Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects)}, pares {(long)Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs)}");
		Node audio = tree.Root.GetNodeOrNull("AudioManager");
		if (audio != null)
		{
			GD.Print($"      AudioManager: voces activas {audio.GetType().GetProperty("ActiveVoiceCount")?.GetValue(audio)}, silencioso {audio.GetType().GetProperty("Silent")?.GetValue(audio)}");
		}
	}

	private static string Mark(bool ok) => ok ? "OK" : "FALLO";
}
