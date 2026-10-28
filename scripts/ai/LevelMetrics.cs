using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Características numéricas de un LevelMap. Se calculan solo con los datos del mapa (sin instanciar escenas)
/// y son deterministas: mismo mapa, mismos valores.
///
/// CONJUNTO FIJO Y VERSIONADO. El modelo de aprendizaje automático depende del nombre y del ORDEN exactos de
/// estas características. No se puede añadir, quitar, renombrar ni reordenar ninguna sin subir Version (y, con
/// ella, el nombre del dataset: levels_v1.csv → levels_v2.csv). Orden de la versión 1:
///
///    1 room_count                    número de salas.
///    2 boss_distance                 pasos de BFS del inicio al jefe; -1 si no hay jefe o no es alcanzable.
///    3 dead_end_count                salas con un único vecino, sin contar el inicio.
///    4 branching_factor              hijos medios por sala interna, con el mapa enraizado en el inicio
///                                    (un grado medio sería casi un duplicado de room_count en un árbol).
///    5 total_enemies                 enemigos de todo el nivel.
///    6 enemies_per_room_mean         media de enemigos por sala.
///    7 enemies_per_room_std          desviación típica (poblacional) de enemigos por sala.
///    8 enemy_density                 enemigos por metro cuadrado de interior (total_enemies / área total).
///    9 room_area_cv                  coeficiente de variación del área de las salas (desviación / media).
///   10 critical_path_enemies         enemigos en las salas del camino inicio → jefe (ambas incluidas).
///   11 critical_path_enemy_ratio     critical_path_enemies / total_enemies (0 si no hay enemigos).
///   12 treasure_distance             pasos de BFS del inicio a la sala de tesoro más cercana.
///                                    -1 significa "sin sala de tesoro" (o no alcanzable), aunque el generador
///                                    actual siempre crea una.
///   13 template_variety              plantillas distintas usadas / número de salas.
///   14 adjacent_same_template_ratio  fracción de pares de salas vecinas que usan la misma plantilla.
///
/// Historial: max_room_enemies se quitó antes de que ningún modelo consumiera la versión 1, porque era constante
/// (siempre la sala del jefe, con 6 enemigos). Con la versión 1 ya en uso, un cambio así exigiría subir Version.
///
/// Los enemigos de una sala son su número de puntos de aparición si tiene EnemyScene, y 0 si no la tiene.
/// Una sala sin TemplateId (escrita a mano) se considera una plantilla propia, distinta de las demás.
/// Un mapa nulo o vacío da todos los valores a 0.
/// </summary>
public static class LevelMetrics
{
	public const int Version = 1;

	// Los miembros siguen el nombre exacto de la característica (snake_case) y su orden de declaración es el orden
	// del conjunto. Names se deriva de aquí para que nombres y posiciones no se puedan desincronizar.
#pragma warning disable CA1707, IDE1006
	private enum Feature
	{
		room_count,
		boss_distance,
		dead_end_count,
		branching_factor,
		total_enemies,
		enemies_per_room_mean,
		enemies_per_room_std,
		enemy_density,
		room_area_cv,
		critical_path_enemies,
		critical_path_enemy_ratio,
		treasure_distance,
		template_variety,
		adjacent_same_template_ratio,
	}
#pragma warning restore CA1707, IDE1006

	public static readonly string[] Names = Enum.GetNames<Feature>();

	private static readonly DoorSide[] Sides = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };

	public static double[] Compute(LevelMap map)
	{
		double[] values = new double[Names.Length];
		if (map == null || map.Rooms == null || map.Rooms.Count == 0)
		{
			return values;
		}

		List<RoomData> rooms = new();
		Dictionary<Vector2I, int> index = new();
		foreach (RoomData room in map.Rooms)
		{
			if (room != null && index.TryAdd(room.Cell, rooms.Count))
			{
				rooms.Add(room);
			}
		}

		int count = rooms.Count;
		List<int>[] neighbors = new List<int>[count];
		for (int i = 0; i < count; i++)
		{
			neighbors[i] = new List<int>();
			foreach (DoorSide side in Sides)
			{
				if (index.TryGetValue(rooms[i].Cell + side.GridOffset(), out int neighbor))
				{
					neighbors[i].Add(neighbor);
				}
			}
		}

		int start = index.TryGetValue(map.StartCell, out int startIndex) ? startIndex : -1;
		int[] distance = new int[count];
		int[] parent = new int[count];
		Array.Fill(distance, -1);
		Array.Fill(parent, -1);

		if (start >= 0)
		{
			Queue<int> queue = new();
			distance[start] = 0;
			queue.Enqueue(start);
			while (queue.Count > 0)
			{
				int current = queue.Dequeue();
				foreach (int neighbor in neighbors[current])
				{
					if (distance[neighbor] < 0)
					{
						distance[neighbor] = distance[current] + 1;
						parent[neighbor] = current;
						queue.Enqueue(neighbor);
					}
				}
			}
		}

		int[] enemies = new int[count];
		double[] areas = new double[count];
		double[] enemyValues = new double[count];
		double totalEnemies = 0.0;
		double totalArea = 0.0;
		for (int i = 0; i < count; i++)
		{
			enemies[i] = rooms[i].EnemyScene != null ? rooms[i].SpawnPoints.Length : 0;
			enemyValues[i] = enemies[i];
			areas[i] = rooms[i].Size.X * rooms[i].Size.Y;
			totalEnemies += enemies[i];
			totalArea += areas[i];
		}

		int[] childCount = new int[count];
		int totalChildren = 0;
		for (int i = 0; i < count; i++)
		{
			if (parent[i] >= 0)
			{
				childCount[parent[i]]++;
				totalChildren++;
			}
		}

		int internalRooms = 0;
		int deadEnds = 0;
		for (int i = 0; i < count; i++)
		{
			internalRooms += childCount[i] > 0 ? 1 : 0;
			deadEnds += i != start && neighbors[i].Count == 1 ? 1 : 0;
		}

		int boss = rooms.FindIndex(room => room.Type == RoomType.Boss);
		double criticalEnemies = 0.0;
		if (boss >= 0 && distance[boss] >= 0)
		{
			for (int room = boss; room >= 0; room = parent[room])
			{
				criticalEnemies += enemies[room];
			}
		}

		int treasureDistance = -1;
		for (int i = 0; i < count; i++)
		{
			if (rooms[i].Type == RoomType.Treasure && distance[i] >= 0
				&& (treasureDistance < 0 || distance[i] < treasureDistance))
			{
				treasureDistance = distance[i];
			}
		}

		HashSet<string> templates = new();
		int adjacentPairs = 0;
		int sameTemplatePairs = 0;
		for (int i = 0; i < count; i++)
		{
			templates.Add(TemplateKey(rooms[i]));

			// Este y Sur bastan para contar cada par de vecinos una sola vez.
			foreach (DoorSide side in new[] { DoorSide.East, DoorSide.South })
			{
				if (index.TryGetValue(rooms[i].Cell + side.GridOffset(), out int neighbor))
				{
					adjacentPairs++;
					sameTemplatePairs += TemplateKey(rooms[i]) == TemplateKey(rooms[neighbor]) ? 1 : 0;
				}
			}
		}

		double areaMean = Mean(areas);
		values[(int)Feature.room_count] = count;
		values[(int)Feature.boss_distance] = boss >= 0 ? distance[boss] : -1;
		values[(int)Feature.dead_end_count] = deadEnds;
		values[(int)Feature.branching_factor] = internalRooms > 0 ? (double)totalChildren / internalRooms : 0.0;
		values[(int)Feature.total_enemies] = totalEnemies;
		values[(int)Feature.enemies_per_room_mean] = Mean(enemyValues);
		values[(int)Feature.enemies_per_room_std] = StandardDeviation(enemyValues);
		values[(int)Feature.enemy_density] = totalArea > 0.0 ? totalEnemies / totalArea : 0.0;
		values[(int)Feature.room_area_cv] = areaMean > 0.0 ? StandardDeviation(areas) / areaMean : 0.0;
		values[(int)Feature.critical_path_enemies] = criticalEnemies;
		values[(int)Feature.critical_path_enemy_ratio] = totalEnemies > 0.0 ? criticalEnemies / totalEnemies : 0.0;
		values[(int)Feature.treasure_distance] = treasureDistance;
		values[(int)Feature.template_variety] = (double)templates.Count / count;
		values[(int)Feature.adjacent_same_template_ratio] = adjacentPairs > 0 ? (double)sameTemplatePairs / adjacentPairs : 0.0;
		return values;
	}

	private static string TemplateKey(RoomData room)
	{
		return string.IsNullOrEmpty(room.TemplateId) ? $"sala:{room.Cell}" : room.TemplateId;
	}

	private static double Mean(double[] values)
	{
		double sum = 0.0;
		foreach (double value in values)
		{
			sum += value;
		}

		return sum / values.Length;
	}

	private static double StandardDeviation(double[] values)
	{
		double mean = Mean(values);
		double sum = 0.0;
		foreach (double value in values)
		{
			sum += (value - mean) * (value - mean);
		}

		return Math.Sqrt(sum / values.Length);
	}
}
