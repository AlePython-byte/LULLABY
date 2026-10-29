using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Características de la versión 2 (niveles del generador v2, con varios tipos de enemigo). Son las 14 de LevelMetrics
/// (versión 1), en el mismo orden y calculadas por el mismo código, seguidas de las de tipos de enemigo. Mismas reglas:
/// solo datos del mapa, deterministas, y un mapa nulo o vacío da todo a 0.
///
/// CONJUNTO FIJO Y VERSIONADO. El modelo difficulty_v2.json depende del nombre y del ORDEN exactos. Orden de la versión 2:
///
///    1..14  las de LevelMetrics versión 1 (room_count ... adjacent_same_template_ratio).
///   15 shooter_count                   tiradores de todo el nivel.
///   16 exploder_count                  explosivos de todo el nivel.
///   17 flyer_count                     voladores de todo el nivel.
///   18 ranged_enemy_fraction           tiradores / total_enemies (0 si no hay enemigos).
///   19 special_enemy_fraction          enemigos que no son básicos / total_enemies (0 si no hay enemigos).
///   20 mixed_room_count                salas con dos o más tipos de enemigo distintos.
///   21 critical_path_special_enemies   enemigos que no son básicos en el camino inicio → jefe (ambas incluidas).
///   22 kind_variety                    tipos de enemigo distintos en el nivel (0 si no hay enemigos).
///
/// No hay conteo de básicos: sería total_enemies menos los otros tres.
///
/// El tipo de cada enemigo sale de SpawnKinds si tiene la misma longitud que SpawnPoints (generador v2); si no, todos
/// son del tipo de EnemyScene (EnemyKinds.KindOfScene). Una sala sin EnemyScene no tiene enemigos, como en la versión 1.
/// </summary>
public static class LevelMetricsV2
{
	public const int Version = 2;

#pragma warning disable CA1707, IDE1006
	private enum Extra
	{
		shooter_count,
		exploder_count,
		flyer_count,
		ranged_enemy_fraction,
		special_enemy_fraction,
		mixed_room_count,
		critical_path_special_enemies,
		kind_variety,
	}
#pragma warning restore CA1707, IDE1006

	private static readonly DoorSide[] Sides = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };

	public static readonly string[] Names = BuildNames();

	public static double[] Compute(LevelMap map)
	{
		double[] baseValues = LevelMetrics.Compute(map);
		double[] values = new double[Names.Length];
		Array.Copy(baseValues, values, baseValues.Length);
		int offset = baseValues.Length;

		if (map == null || map.Rooms == null || map.Rooms.Count == 0)
		{
			return values;
		}

		// Mismas salas y misma indexación que LevelMetrics: la primera sala de cada celda.
		List<RoomData> rooms = new();
		Dictionary<Vector2I, int> index = new();
		foreach (RoomData room in map.Rooms)
		{
			if (room != null && index.TryAdd(room.Cell, rooms.Count))
			{
				rooms.Add(room);
			}
		}

		int[] parent = CriticalParents(rooms, index, map.StartCell, out int start);
		int[] kindTotals = new int[EnemyKinds.List.Count];
		int[] specialPerRoom = new int[rooms.Count];
		int mixedRooms = 0;
		int totalEnemies = 0;

		for (int i = 0; i < rooms.Count; i++)
		{
			RoomData room = rooms[i];
			if (room.EnemyScene == null)
			{
				continue;
			}

			HashSet<EnemyKind> kindsInRoom = new();
			for (int spawn = 0; spawn < room.SpawnPoints.Length; spawn++)
			{
				EnemyKind kind = KindAt(room, spawn);
				kindTotals[(int)kind]++;
				kindsInRoom.Add(kind);
				totalEnemies++;
				specialPerRoom[i] += kind != EnemyKind.Chaser ? 1 : 0;
			}

			mixedRooms += kindsInRoom.Count >= 2 ? 1 : 0;
		}

		int boss = rooms.FindIndex(room => room.Type == RoomType.Boss);
		double criticalSpecial = 0.0;
		if (boss >= 0 && start >= 0 && (boss == start || parent[boss] >= 0))
		{
			for (int room = boss; room >= 0; room = parent[room])
			{
				criticalSpecial += specialPerRoom[room];
			}
		}

		int variety = 0;
		foreach (int total in kindTotals)
		{
			variety += total > 0 ? 1 : 0;
		}

		values[offset + (int)Extra.shooter_count] = kindTotals[(int)EnemyKind.Shooter];
		values[offset + (int)Extra.exploder_count] = kindTotals[(int)EnemyKind.Exploder];
		values[offset + (int)Extra.flyer_count] = kindTotals[(int)EnemyKind.Flyer];
		values[offset + (int)Extra.ranged_enemy_fraction] = totalEnemies > 0 ? (double)kindTotals[(int)EnemyKind.Shooter] / totalEnemies : 0.0;
		int special = totalEnemies - kindTotals[(int)EnemyKind.Chaser];
		values[offset + (int)Extra.special_enemy_fraction] = totalEnemies > 0 ? (double)special / totalEnemies : 0.0;
		values[offset + (int)Extra.mixed_room_count] = mixedRooms;
		values[offset + (int)Extra.critical_path_special_enemies] = criticalSpecial;
		values[offset + (int)Extra.kind_variety] = variety;
		return values;
	}

	/// <summary>Tipo del enemigo del punto de aparición spawn (la sala debe tener EnemyScene).</summary>
	public static EnemyKind KindAt(RoomData room, int spawn)
	{
		return room.SpawnKinds.Length == room.SpawnPoints.Length
			? (EnemyKind)room.SpawnKinds[spawn]
			: EnemyKinds.KindOfScene(room.EnemyScene);
	}

	private static string[] BuildNames()
	{
		List<string> names = new(LevelMetrics.Names);
		names.AddRange(Enum.GetNames<Extra>());
		return names.ToArray();
	}

	// Padres del árbol de BFS desde el inicio (vecinos en orden N, E, S, O, como LevelMetrics).
	private static int[] CriticalParents(List<RoomData> rooms, Dictionary<Vector2I, int> index, Vector2I startCell, out int start)
	{
		int[] parent = new int[rooms.Count];
		bool[] seen = new bool[rooms.Count];
		Array.Fill(parent, -1);
		start = index.TryGetValue(startCell, out int startIndex) ? startIndex : -1;
		if (start < 0)
		{
			return parent;
		}

		Queue<int> queue = new();
		seen[start] = true;
		queue.Enqueue(start);
		while (queue.Count > 0)
		{
			int current = queue.Dequeue();
			foreach (DoorSide side in Sides)
			{
				if (index.TryGetValue(rooms[current].Cell + side.GridOffset(), out int neighbor) && !seen[neighbor])
				{
					seen[neighbor] = true;
					parent[neighbor] = current;
					queue.Enqueue(neighbor);
				}
			}
		}

		return parent;
	}
}
