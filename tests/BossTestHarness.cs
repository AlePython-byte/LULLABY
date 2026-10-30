using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Utilidades de las pruebas del jefe: abrir TestLevel con una semilla fija, llegar a la sala del jefe cruzando puertas y
/// esperar condiciones. No usa ninguna API exclusiva de depuración (SetNextSeed, SetNextMap, DebugGoToBoss), así que lo
/// que comprueba no depende del tipo de compilación.
/// </summary>
public static class BossTestHarness
{
	public const string LevelPath = "res://scenes/levels/TestLevel.tscn";

	/// <summary>Semilla de partida de las pruebas: genera un nivel con sala del jefe (generador v2, el de por defecto).</summary>
	public const int Seed = 12345;

	public sealed class Level
	{
		public Node Root;
		public RoomManager Rooms;
		public RunManager Run;
		public VictoryScreen Victory;
		public PlayerController Player;
		public HealthComponent Health;
	}

	/// <summary>
	/// Instancia TestLevel bajo `parent`. withMother = false conserva la composición histórica del jefe (seis básicos).
	/// La pausa y el fundido de la victoria se acortan para no alargar la prueba; las recompensas del tesoro se apagan.
	/// </summary>
	public static Level Open(Node parent, int seed, bool withMother = true, bool unlimitedHealth = true)
	{
		Node root = GD.Load<PackedScene>(LevelPath).Instantiate();
		root.GetNode<TreasureItemSpawner>("TreasureItemSpawner").Enabled = false;

		Level level = new()
		{
			Root = root,
			Rooms = root.GetNode<RoomManager>("RoomManager"),
			Run = root.GetNode<RunManager>("RunManager"),
			Victory = root.GetNode<VictoryScreen>("VictoryScreen"),
			Player = root.GetNode<PlayerController>("Player"),
		};
		level.Health = HealthComponent.FindIn(level.Player);

		level.Run.UseFixedSeed = true;
		level.Run.FixedSeed = seed;
		level.Run.VictoryDelay = 0.05f;
		level.Run.VictoryFadeTime = 0.05f;
		level.Rooms.FadeDuration = 0.0f;
		if (!withMother)
		{
			level.Rooms.BossScene = null;
		}

		if (unlimitedHealth)
		{
			level.Health.MaxHealth = 1.0e9f;
		}

		parent.AddChild(root);
		return level;
	}

	/// <summary>
	/// Como Open, pero bajo la raíz y como escena ACTUAL: ReloadCurrentScene y ChangeSceneToFile reemplazan a la escena
	/// actual, y así reemplazan al nivel y no a la prueba.
	/// </summary>
	public static Level OpenAsCurrent(SceneTree tree, int seed, bool withMother = true, bool unlimitedHealth = true)
	{
		Level level = Open(tree.Root, seed, withMother, unlimitedHealth);
		tree.CurrentScene = level.Root;
		return level;
	}

	/// <summary>Espera a que la escena actual sea otra (la de scenePath) tras una recarga o un cambio de escena.</summary>
	public static async Task<Node> WaitForScene(SceneTree tree, Node previous, string scenePath)
	{
		await WaitUntil(tree, () => tree.CurrentScene != null && tree.CurrentScene != previous
			&& tree.CurrentScene.SceneFilePath == scenePath && !GodotObject.IsInstanceValid(previous), 300, $"la escena {scenePath}");
		await Frames(tree, 5);
		return tree.CurrentScene;
	}

	/// <summary>Libera una escena creada por la prueba y devuelve el papel de escena actual a la propia prueba.</summary>
	public static async Task Restore(SceneTree tree, Node testScene, Node created)
	{
		if (GodotObject.IsInstanceValid(created))
		{
			created.QueueFree();
			await tree.ToSignal(created, Node.SignalName.TreeExited);
		}

		tree.Paused = false;
		tree.CurrentScene = testScene;
		await Frames(tree, 1);
	}

	/// <summary>Libera el nivel y espera a que salga del árbol: dos niveles a la vez confundirían al grupo "player".</summary>
	public static async Task Close(SceneTree tree, Level level)
	{
		if (!GodotObject.IsInstanceValid(level.Root))
		{
			return;
		}

		level.Root.QueueFree();
		await tree.ToSignal(level.Root, Node.SignalName.TreeExited);
		await Frames(tree, 1);
	}

	public static async Task Frames(SceneTree tree, int count)
	{
		for (int i = 0; i < count; i++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}
	}

	public static async Task WaitUntil(SceneTree tree, Func<bool> condition, int maxFrames, string what)
	{
		for (int i = 0; i < maxFrames; i++)
		{
			if (condition())
			{
				return;
			}

			await Frames(tree, 1);
		}

		throw new InvalidDataException("Tiempo agotado esperando: " + what);
	}

	/// <summary>
	/// Igual que WaitUntil, pero comprueba al inicio de CADA paso de física (la señal physics_frame se emite antes de que
	/// los nodos procesen el paso), así no se escapa un estado que dure un solo paso.
	/// </summary>
	public static async Task WaitTicks(SceneTree tree, Func<bool> condition, int maxTicks, string what)
	{
		for (int i = 0; i < maxTicks; i++)
		{
			if (condition())
			{
				return;
			}

			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		}

		throw new InvalidDataException("Tiempo agotado esperando: " + what);
	}

	/// <summary>Cruza una puerta emitiendo la señal de la sala (sin pelear) y espera a que termine la transición.</summary>
	public static async Task Cross(SceneTree tree, RoomManager rooms, DoorSide side)
	{
		Vector2I expected = rooms.CurrentRoom.Data.Cell + side.GridOffset();
		rooms.CurrentRoom.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);
		await WaitUntil(tree, () => GodotObject.IsInstanceValid(rooms.CurrentRoom) && rooms.CurrentRoom.Data.Cell == expected
			&& rooms.Player.IsPhysicsProcessing(), 240, $"transición a la sala {expected}");
		await Frames(tree, 2);
	}

	/// <summary>Recorre el camino más corto de la sala inicial a la del jefe.</summary>
	public static async Task GoToBoss(SceneTree tree, Level level)
	{
		foreach (DoorSide side in PathToBoss(level.Rooms.Map))
		{
			await Cross(tree, level.Rooms, side);
		}

		if (level.Rooms.CurrentRoom.Data.Type != RoomType.Boss)
		{
			throw new InvalidDataException("No se llegó a la sala del jefe.");
		}
	}

	public static List<DoorSide> PathToBoss(LevelMap map)
	{
		Dictionary<Vector2I, RoomData> rooms = new();
		foreach (RoomData room in map.Rooms)
		{
			rooms[room.Cell] = room;
		}

		Dictionary<Vector2I, (Vector2I From, DoorSide Side)> came = new();
		Queue<Vector2I> queue = new();
		queue.Enqueue(map.StartCell);
		came[map.StartCell] = (map.StartCell, DoorSide.North);
		Vector2I? boss = null;

		while (queue.Count > 0 && boss == null)
		{
			Vector2I cell = queue.Dequeue();
			if (rooms[cell].Type == RoomType.Boss)
			{
				boss = cell;
				break;
			}

			foreach (DoorSide side in Enum.GetValues<DoorSide>())
			{
				Vector2I next = cell + side.GridOffset();
				if (rooms.ContainsKey(next) && !came.ContainsKey(next))
				{
					came[next] = (cell, side);
					queue.Enqueue(next);
				}
			}
		}

		if (boss == null)
		{
			throw new InvalidDataException("El mapa no tiene sala del jefe alcanzable.");
		}

		List<DoorSide> path = new();
		for (Vector2I cell = boss.Value; cell != map.StartCell; cell = came[cell].From)
		{
			path.Add(came[cell].Side);
		}

		path.Reverse();
		return path;
	}
}
