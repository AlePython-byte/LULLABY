using System.Collections.Generic;
using Godot;

/// <summary>
/// Comprueba que un LevelMap se pueda completar. Devuelve la lista de problemas (vacía si es válido).
/// Las puertas se derivan de la adyacencia en la cuadrícula, así que "conectado" significa "vecino en la cuadrícula".
/// </summary>
public static class LevelValidator
{
	private static readonly DoorSide[] Sides = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };

	public static List<string> Validate(LevelMap map, int minBossDistance = 0)
	{
		List<string> problems = new();

		if (map == null || map.Rooms == null || map.Rooms.Count == 0)
		{
			problems.Add("el mapa no tiene salas.");
			return problems;
		}

		Dictionary<Vector2I, RoomData> rooms = new();
		List<RoomData> ordered = new();
		foreach (RoomData room in map.Rooms)
		{
			if (room == null)
			{
				problems.Add("hay una entrada nula en Rooms.");
			}
			else if (!rooms.TryAdd(room.Cell, room))
			{
				problems.Add($"dos salas comparten la celda {room.Cell}.");
			}
			else
			{
				ordered.Add(room);
			}
		}

		CheckTypes(map, rooms, ordered, problems);

		Dictionary<Vector2I, int> distances = rooms.ContainsKey(map.StartCell)
			? Distances(rooms, map.StartCell)
			: new Dictionary<Vector2I, int>();

		foreach (RoomData room in ordered)
		{
			if (ordered.Count > 1 && NeighborCount(rooms, room.Cell) == 0)
			{
				problems.Add($"la sala {room.Cell} está aislada (sin vecinos).");
			}
			else if (!distances.ContainsKey(room.Cell))
			{
				problems.Add($"la sala {room.Cell} no es alcanzable desde el inicio.");
			}

			CheckSpawnPoints(room, problems);
		}

		RoomData boss = ordered.Find(room => room.Type == RoomType.Boss);
		if (boss != null)
		{
			if (!distances.TryGetValue(boss.Cell, out int bossDistance))
			{
				problems.Add($"no hay camino del inicio al jefe ({boss.Cell}).");
			}
			else if (bossDistance < minBossDistance)
			{
				problems.Add($"el jefe está a {bossDistance} pasos del inicio; el mínimo es {minBossDistance}.");
			}
		}

		return problems;
	}

	/// <summary>Pasos de BFS del inicio al jefe, o -1 si no hay jefe o no es alcanzable.</summary>
	public static int BossDistance(LevelMap map)
	{
		Dictionary<Vector2I, RoomData> rooms = new();
		RoomData boss = null;
		foreach (RoomData room in map.Rooms)
		{
			rooms.TryAdd(room.Cell, room);
			if (room.Type == RoomType.Boss)
			{
				boss = room;
			}
		}

		if (boss == null || !rooms.ContainsKey(map.StartCell))
		{
			return -1;
		}

		return Distances(rooms, map.StartCell).TryGetValue(boss.Cell, out int distance) ? distance : -1;
	}

	private static void CheckTypes(LevelMap map, Dictionary<Vector2I, RoomData> rooms, List<RoomData> ordered,
		List<string> problems)
	{
		if (!rooms.TryGetValue(map.StartCell, out RoomData start))
		{
			problems.Add($"la celda inicial {map.StartCell} no tiene sala.");
		}
		else if (start.Type != RoomType.Start)
		{
			problems.Add($"la sala inicial {map.StartCell} es de tipo {start.Type}, no Start.");
		}

		int starts = ordered.FindAll(room => room.Type == RoomType.Start).Count;
		if (starts != 1)
		{
			problems.Add($"hay {starts} salas de tipo Start; debe haber exactamente una.");
		}

		int bosses = ordered.FindAll(room => room.Type == RoomType.Boss).Count;
		if (bosses != 1)
		{
			problems.Add($"hay {bosses} salas de tipo Boss; debe haber exactamente una.");
		}
	}

	private static void CheckSpawnPoints(RoomData room, List<string> problems)
	{
		const float epsilon = 0.001f;
		float limitX = room.Size.X / 2.0f - RoomRules.SpawnWallMargin;
		float limitZ = room.Size.Y / 2.0f - RoomRules.SpawnWallMargin;
		Vector2[] arrivals = RoomRules.ArrivalPoints(room.Size);

		if (room.SpawnKinds.Length > 0 && room.SpawnKinds.Length != room.SpawnPoints.Length)
		{
			problems.Add($"sala {room.Cell} ({room.TemplateId}): SpawnKinds tiene {room.SpawnKinds.Length} tipos para {room.SpawnPoints.Length} puntos de aparición.");
		}

		foreach (int kind in room.SpawnKinds)
		{
			if (!EnemyKinds.IsValid(kind))
			{
				problems.Add($"sala {room.Cell} ({room.TemplateId}): tipo de enemigo desconocido {kind}.");
			}
		}

		foreach (Vector2 point in room.SpawnPoints)
		{
			if (Mathf.Abs(point.X) > limitX + epsilon || Mathf.Abs(point.Y) > limitZ + epsilon)
			{
				problems.Add($"sala {room.Cell} ({room.TemplateId}): el punto de aparición {point} cae fuera del interior.");
			}

			foreach (Vector2 arrival in arrivals)
			{
				float distance = point.DistanceTo(arrival);
				if (distance < RoomRules.MinSpawnDistanceFromArrival - epsilon)
				{
					problems.Add($"sala {room.Cell} ({room.TemplateId}): el punto de aparición {point} está a {distance:0.00} de la llegada {arrival}; el mínimo es {RoomRules.MinSpawnDistanceFromArrival}.");
				}
			}
		}
	}

	private static int NeighborCount(Dictionary<Vector2I, RoomData> rooms, Vector2I cell)
	{
		int count = 0;
		foreach (DoorSide side in Sides)
		{
			if (rooms.ContainsKey(cell + side.GridOffset()))
			{
				count++;
			}
		}

		return count;
	}

	private static Dictionary<Vector2I, int> Distances(Dictionary<Vector2I, RoomData> rooms, Vector2I start)
	{
		Dictionary<Vector2I, int> distances = new() { [start] = 0 };
		Queue<Vector2I> queue = new();
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			Vector2I cell = queue.Dequeue();
			foreach (DoorSide side in Sides)
			{
				Vector2I next = cell + side.GridOffset();
				if (rooms.ContainsKey(next) && !distances.ContainsKey(next))
				{
					distances[next] = distances[cell] + 1;
					queue.Enqueue(next);
				}
			}
		}

		return distances;
	}
}
