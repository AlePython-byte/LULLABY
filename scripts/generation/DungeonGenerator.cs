using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// Generador procedural de niveles por semilla. Solo produce datos (un LevelMap); no toca escenas.
///
/// Algoritmo (crecimiento tipo The Binding of Isaac):
///   1. Elige el número de salas entre MinRooms y MaxRooms.
///   2. Crece en anchura desde la celda inicial (0, 0). Para cada sala de la cola mira sus vecinos en orden
///      fijo N, E, S, O y añade uno con probabilidad BranchChance solo si esa celda tiene exactamente un vecino
///      ocupado. Así el mapa es un árbol: sin bucles ni racimos de salas.
///   3. Si la cola se agota antes de llegar al número, o hay menos de dos salas sin salida (sin contar el
///      inicio), vuelve a crecer con el mismo generador aleatorio (hasta MaxGrowthTries).
///   4. Tipos: el inicio es Start; la sala sin salida más lejana del inicio (pasos de BFS) es Boss; otra sala
///      sin salida, elegida al azar, es Treasure; el resto, Normal. Cada sala recibe una plantilla de su tipo.
///   5. El LevelValidator revisa el resultado (incluida la distancia mínima al jefe). Si no es válido, se
///      reintenta con una semilla derivada (DeriveSeed) hasta MaxAttempts.
///
/// Determinismo: la misma semilla y los mismos ajustes (y el mismo catálogo) dan siempre el mismo LevelMap.
///   - Cada intento usa un RandomNumberGenerator propio con Seed = semilla del intento. No se usa System.Random
///     ni el generador global de Godot.
///   - Todo orden sale de listas y de órdenes fijos (lados N, E, S, O; orden de creación de las salas). Los
///     diccionarios y conjuntos solo se usan para consultas, nunca se recorren para decidir.
///   - El resultado incluye la semilla efectiva: generar con ella reproduce el mismo nivel en el primer intento.
/// </summary>
public sealed class DungeonGenerator
{
	/// <summary>
	/// Versiones del generador. 1: el contenido original (solo enemigos básicos); es la de los datasets y la del modelo de
	/// dificultad, y con ella los niveles y las semillas no cambian. 2: añade las plantillas de RoomCatalog.NormalTemplatesV2,
	/// que mezclan tipos de enemigo.
	/// </summary>
	public const int OriginalVersion = 1;
	public const int LatestVersion = 2;

	public sealed class Settings
	{
		public int GeneratorVersion { get; set; } = OriginalVersion;
		public int MinRooms { get; set; } = 8;
		public int MaxRooms { get; set; } = 12;
		public float BranchChance { get; set; } = 0.5f;
		public int MinBossDistance { get; set; } = 4;
		public int MaxAttempts { get; set; } = 20;
		public int MaxGrowthTries { get; set; } = 100;
	}

	public sealed class Result
	{
		public LevelMap Map { get; init; }
		public int RequestedSeed { get; init; }
		public int EffectiveSeed { get; init; }
		public int Attempts { get; init; }
		public List<string> Problems { get; init; } = new();
		public bool Success => Map != null && Problems.Count == 0;
	}

	private static readonly Vector2I StartCell = Vector2I.Zero;
	private static readonly DoorSide[] Sides = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };

	private readonly RoomCatalog _catalog;

	public DungeonGenerator(RoomCatalog catalog)
	{
		_catalog = catalog;
	}

	public static int DeriveSeed(int seed, int attempt)
	{
		return unchecked(seed + attempt * 7919);
	}

	public Result Generate(int seed, Settings settings)
	{
		List<string> problems = new();

		for (int attempt = 0; attempt < settings.MaxAttempts; attempt++)
		{
			int effectiveSeed = DeriveSeed(seed, attempt);
			LevelMap map = GenerateOnce(effectiveSeed, settings, out problems);

			if (problems.Count == 0)
			{
				return new Result { Map = map, RequestedSeed = seed, EffectiveSeed = effectiveSeed, Attempts = attempt + 1 };
			}
		}

		GD.PushError($"DungeonGenerator: no se pudo generar un nivel válido con la semilla {seed} tras " +
			$"{settings.MaxAttempts} intentos. Último intento: {string.Join(" | ", problems)}");

		return new Result
		{
			Map = null,
			RequestedSeed = seed,
			EffectiveSeed = DeriveSeed(seed, settings.MaxAttempts - 1),
			Attempts = settings.MaxAttempts,
			Problems = problems,
		};
	}

	/// <summary>Texto que describe todo el nivel; dos niveles iguales dan el mismo texto.</summary>
	public static string Describe(LevelMap map)
	{
		StringBuilder text = new($"start={map.StartCell}");
		foreach (RoomData room in map.Rooms)
		{
			text.Append($";{room.Cell}|{room.Type}|{room.TemplateId}|{room.Size}|");
			foreach (Vector2 point in room.SpawnPoints)
			{
				text.Append($"{point} ");
			}

			// Solo existe en la versión 2: las descripciones de la versión 1 no cambian.
			if (room.SpawnKinds.Length > 0)
			{
				text.Append($"kinds={string.Join(",", room.SpawnKinds)}");
			}
		}

		return text.ToString();
	}

	private LevelMap GenerateOnce(int seed, Settings settings, out List<string> problems)
	{
		problems = new List<string>();

		if (settings.GeneratorVersion < OriginalVersion || settings.GeneratorVersion > LatestVersion)
		{
			problems.Add($"versión del generador desconocida: {settings.GeneratorVersion} (válidas: {OriginalVersion}..{LatestVersion}).");
			return null;
		}

		foreach (RoomType type in System.Enum.GetValues<RoomType>())
		{
			if (_catalog == null || _catalog.For(type).Count == 0)
			{
				problems.Add($"el catálogo no tiene plantillas de tipo {type}.");
			}
		}

		if (problems.Count > 0)
		{
			return null;
		}

		RandomNumberGenerator rng = new() { Seed = unchecked((ulong)(uint)seed) };
		int target = rng.RandiRange(settings.MinRooms, settings.MaxRooms);

		if (!TryGrowLayout(rng, target, settings, out List<Vector2I> cells, out HashSet<Vector2I> occupied))
		{
			problems.Add($"el crecimiento no llegó a {target} salas con dos salas sin salida tras " +
				$"{settings.MaxGrowthTries} intentos.");
			return null;
		}

		Dictionary<Vector2I, int> distances = Distances(occupied, StartCell);
		List<Vector2I> deadEnds = cells.FindAll(cell => cell != StartCell && NeighborCount(occupied, cell) == 1);

		Vector2I boss = deadEnds[0];
		foreach (Vector2I cell in deadEnds)
		{
			if (distances[cell] > distances[boss])
			{
				boss = cell;
			}
		}

		List<Vector2I> treasureCandidates = deadEnds.FindAll(cell => cell != boss);
		Vector2I treasure = treasureCandidates[rng.RandiRange(0, treasureCandidates.Count - 1)];

		LevelMap map = new() { StartCell = StartCell };
		foreach (Vector2I cell in cells)
		{
			RoomType type = cell == StartCell ? RoomType.Start
				: cell == boss ? RoomType.Boss
				: cell == treasure ? RoomType.Treasure
				: RoomType.Normal;

			Godot.Collections.Array<RoomTemplate> templates = _catalog.For(type, settings.GeneratorVersion);
			RoomTemplate template = templates[rng.RandiRange(0, templates.Count - 1)];

			map.Rooms.Add(new RoomData
			{
				Cell = cell,
				Type = type,
				TemplateId = template.Id,
				Size = template.Size,
				EnemyScene = template.EnemyScene,
				SpawnPoints = (Vector2[])template.SpawnPoints.Clone(),
				SpawnKinds = (int[])template.SpawnKinds.Clone(),
			});
		}

		problems.AddRange(LevelValidator.Validate(map, settings.MinBossDistance));
		return map;
	}

	private static bool TryGrowLayout(RandomNumberGenerator rng, int target, Settings settings,
		out List<Vector2I> cells, out HashSet<Vector2I> occupied)
	{
		cells = new List<Vector2I>();
		occupied = new HashSet<Vector2I>();

		for (int growth = 0; growth < settings.MaxGrowthTries; growth++)
		{
			cells = new List<Vector2I> { StartCell };
			occupied = new HashSet<Vector2I> { StartCell };
			Queue<Vector2I> queue = new();
			queue.Enqueue(StartCell);

			while (queue.Count > 0 && cells.Count < target)
			{
				Vector2I cell = queue.Dequeue();
				foreach (DoorSide side in Sides)
				{
					if (cells.Count >= target)
					{
						break;
					}

					Vector2I next = cell + side.GridOffset();
					if (occupied.Contains(next) || NeighborCount(occupied, next) > 1)
					{
						continue;
					}

					if (rng.Randf() >= settings.BranchChance)
					{
						continue;
					}

					cells.Add(next);
					occupied.Add(next);
					queue.Enqueue(next);
				}
			}

			HashSet<Vector2I> filled = occupied;
			int deadEnds = cells.FindAll(c => c != StartCell && NeighborCount(filled, c) == 1).Count;
			if (cells.Count == target && deadEnds >= 2)
			{
				return true;
			}
		}

		return false;
	}

	private static int NeighborCount(HashSet<Vector2I> occupied, Vector2I cell)
	{
		int count = 0;
		foreach (DoorSide side in Sides)
		{
			if (occupied.Contains(cell + side.GridOffset()))
			{
				count++;
			}
		}

		return count;
	}

	private static Dictionary<Vector2I, int> Distances(HashSet<Vector2I> occupied, Vector2I start)
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
				if (occupied.Contains(next) && !distances.ContainsKey(next))
				{
					distances[next] = distances[cell] + 1;
					queue.Enqueue(next);
				}
			}
		}

		return distances;
	}
}
