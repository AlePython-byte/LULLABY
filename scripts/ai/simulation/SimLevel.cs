using System.Collections.Generic;
using Godot;

/// <summary>
/// Copia en datos puros de un LevelMap, lista para simular desde cualquier hilo: salas, puertas (vecinos en la
/// cuadrícula), camino crítico inicio → jefe y la ruta del bot.
///
/// Ruta del bot: recorre todas las salas en profundidad desde el inicio, con los vecinos en orden fijo N, E, S, O,
/// salvo que la rama que lleva al jefe va siempre la última; vuelve sobre sus pasos al acabar cada rama que no
/// lleva al jefe y termina al entrar en la sala del jefe. Es la lista de celdas por las que pasa, en orden.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class SimLevel
{
	public sealed class Room
	{
		public Vector2I Cell { get; init; }
		public RoomType Type { get; init; }
		public string TemplateId { get; init; }
		public Vector2 Size { get; init; }
		public Vector2[] SpawnPoints { get; init; }

		/// <summary>Null si la sala no tiene enemigos.</summary>
		public GameplayParameters.EnemyStats Enemy { get; init; }

		/// <summary>Estadísticas del enemigo de cada punto de aparición (su tipo, con SpawnKinds; si no, Enemy).</summary>
		public GameplayParameters.EnemyStats[] SpawnStats { get; init; }

		public List<DoorSide> Doors { get; } = new();

		public bool StartsCleared => Enemy == null || SpawnPoints.Length == 0;
	}

	private static readonly DoorSide[] Sides = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };

	private readonly Dictionary<Vector2I, Room> _rooms = new();

	public List<Room> Rooms { get; } = new();
	public Vector2I StartCell { get; private set; }
	public Vector2I BossCell { get; private set; }
	public HashSet<Vector2I> CriticalPath { get; } = new();
	public List<Vector2I> Route { get; } = new();

	/// <summary>Tolerancia de alineación para que una lágrima toque a un enemigo: media anchura + radio.</summary>
	public float AimTolerance { get; private set; }

	public Room this[Vector2I cell] => _rooms[cell];

	public bool Contains(Vector2I cell)
	{
		return _rooms.ContainsKey(cell);
	}

	/// <summary>Solo en el hilo principal: lee recursos de Godot.</summary>
	public static SimLevel From(LevelMap map, GameplayParameters parameters)
	{
		SimLevel level = new() { StartCell = map.StartCell };
		float enemyHalf = 0.0f;

		foreach (RoomData data in map.Rooms)
		{
			GameplayParameters.EnemyStats enemy = data.EnemyScene != null ? parameters.EnemyFor(data.EnemyScene) : null;
			GameplayParameters.EnemyStats[] spawnStats = new GameplayParameters.EnemyStats[data.SpawnPoints.Length];
			bool typed = data.SpawnKinds.Length == data.SpawnPoints.Length;
			for (int i = 0; enemy != null && i < spawnStats.Length; i++)
			{
				spawnStats[i] = typed ? parameters.EnemyFor(EnemyKinds.Scene((EnemyKind)data.SpawnKinds[i])) : enemy;
				enemyHalf = Mathf.Max(enemyHalf, Mathf.Min(spawnStats[i].Size.X, spawnStats[i].Size.Y) / 2.0f);
			}

			if (enemy != null)
			{
				enemyHalf = Mathf.Max(enemyHalf, Mathf.Min(enemy.Size.X, enemy.Size.Y) / 2.0f);
			}

			Room room = new()
			{
				Cell = data.Cell,
				Type = data.Type,
				TemplateId = data.TemplateId,
				Size = data.Size,
				SpawnPoints = (Vector2[])data.SpawnPoints.Clone(),
				Enemy = enemy,
				SpawnStats = spawnStats,
			};
			level.Rooms.Add(room);
			level._rooms[room.Cell] = room;

			if (room.Type == RoomType.Boss)
			{
				level.BossCell = room.Cell;
			}
		}

		foreach (Room room in level.Rooms)
		{
			foreach (DoorSide side in Sides)
			{
				if (level._rooms.ContainsKey(room.Cell + side.GridOffset()))
				{
					room.Doors.Add(side);
				}
			}
		}

		level.AimTolerance = enemyHalf + parameters.TearRadius;
		level.BuildCriticalPath();
		level.Route.Add(level.StartCell);
		level.Tour(level.StartCell, level.StartCell);
		return level;
	}

	/// <summary>Primer lado a cruzar para ir de una sala a otra por el camino más corto (BFS).</summary>
	public DoorSide FirstStep(Vector2I from, Vector2I to)
	{
		Dictionary<Vector2I, Vector2I> previous = new() { [from] = from };
		Queue<Vector2I> queue = new();
		queue.Enqueue(from);

		while (queue.Count > 0)
		{
			Vector2I cell = queue.Dequeue();
			if (cell == to)
			{
				break;
			}

			foreach (DoorSide side in Sides)
			{
				Vector2I next = cell + side.GridOffset();
				if (_rooms.ContainsKey(next) && !previous.ContainsKey(next))
				{
					previous[next] = cell;
					queue.Enqueue(next);
				}
			}
		}

		Vector2I step = to;
		while (previous[step] != from)
		{
			step = previous[step];
		}

		Vector2I offset = step - from;
		foreach (DoorSide side in Sides)
		{
			if (side.GridOffset() == offset)
			{
				return side;
			}
		}

		return DoorSide.North;
	}

	private void BuildCriticalPath()
	{
		Dictionary<Vector2I, Vector2I> previous = new() { [StartCell] = StartCell };
		Queue<Vector2I> queue = new();
		queue.Enqueue(StartCell);

		while (queue.Count > 0)
		{
			Vector2I cell = queue.Dequeue();
			foreach (DoorSide side in Sides)
			{
				Vector2I next = cell + side.GridOffset();
				if (_rooms.ContainsKey(next) && !previous.ContainsKey(next))
				{
					previous[next] = cell;
					queue.Enqueue(next);
				}
			}
		}

		if (!previous.ContainsKey(BossCell))
		{
			return;
		}

		for (Vector2I cell = BossCell; ; cell = previous[cell])
		{
			CriticalPath.Add(cell);
			if (cell == StartCell)
			{
				break;
			}
		}
	}

	// Devuelve true al llegar al jefe (la ruta termina ahí).
	private bool Tour(Vector2I cell, Vector2I parent)
	{
		List<Vector2I> children = new();
		Vector2I? criticalChild = null;
		foreach (DoorSide side in Sides)
		{
			Vector2I next = cell + side.GridOffset();
			if (next == parent || !_rooms.ContainsKey(next) || Route.Contains(next))
			{
				continue;
			}

			if (CriticalPath.Contains(next))
			{
				criticalChild = next;
			}
			else
			{
				children.Add(next);
			}
		}

		if (criticalChild.HasValue)
		{
			children.Add(criticalChild.Value);
		}

		foreach (Vector2I child in children)
		{
			Route.Add(child);
			if (child == BossCell || Tour(child, cell))
			{
				return true;
			}

			Route.Add(cell);
		}

		return false;
	}
}
