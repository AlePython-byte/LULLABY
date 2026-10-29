using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

/// <summary>
/// Prueba de estrés del DungeonGenerator. Ejecutar en modo headless:
///   godot --headless --path . res://tests/GeneratorStressTest.tscn
///   godot --headless --path . res://tests/GeneratorStressTest.tscn -- --generator=2   (versión 2 del generador)
/// Genera SeedCount semillas, valida cada nivel, comprueba el determinismo y sale con código 0 si todo pasa. Sin
/// --generator usa la versión 1 (pedida explícitamente: no depende del defecto de RunManager). Con --generator=2 añade un
/// bloque de variedad de enemigos (VarietyReport): niveles según los tipos de enemigo distintos que tienen, plantillas
/// mixtas por nivel, enemigos de cada tipo en total y porcentaje de salas con enemigos que son solo de básicos.
/// </summary>
public partial class GeneratorStressTest : Node
{
	private const string CatalogPath = "res://resources/rooms/templates/Catalog.tres";
	private const string TestMapPath = "res://resources/rooms/TestMap.tres";

	[Export] public int SeedCount { get; set; } = 1000;
	[Export] public int FirstSeed { get; set; } = 1;

	public override void _Ready()
	{
		bool ok = Run();
		GD.Print(ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(ok ? 0 : 1);
	}

	private bool Run()
	{
		RoomCatalog catalog = GD.Load<RoomCatalog>(CatalogPath);
		DungeonGenerator generator = new(catalog);
		DungeonGenerator.Settings settings = new() { GeneratorVersion = DungeonGenerator.OriginalVersion };
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--generator=") && int.TryParse(argument["--generator=".Length..], out int version))
			{
				settings.GeneratorVersion = version;
				GD.Print($"Versión del generador: {version}");
			}
		}

		int passed = 0;
		int firstTry = 0;
		int maxAttempts = 0;
		int nondeterministic = 0;
		int replayMismatches = 0;
		int withTreasure = 0;
		long bossDistanceSum = 0;
		int bossDistanceMin = int.MaxValue;
		int bossDistanceMax = 0;
		List<string> failures = new();
		SortedDictionary<int, int> roomCounts = new();
		SortedDictionary<int, int> bossDistances = new();
		SortedDictionary<string, int> templateUse = new();
		HashSet<string> distinctLevels = new();
		VarietyReport variety = new(catalog);

		Stopwatch clock = Stopwatch.StartNew();

		for (int seed = FirstSeed; seed < FirstSeed + SeedCount; seed++)
		{
			DungeonGenerator.Result result = generator.Generate(seed, settings);
			List<string> problems = result.Success
				? LevelValidator.Validate(result.Map, settings.MinBossDistance)
				: result.Problems;

			if (!result.Success || problems.Count > 0)
			{
				failures.Add($"semilla {seed}: {string.Join(" | ", problems)}");
				continue;
			}

			passed++;
			firstTry += result.Attempts == 1 ? 1 : 0;
			maxAttempts = Mathf.Max(maxAttempts, result.Attempts);

			string description = DungeonGenerator.Describe(result.Map);
			distinctLevels.Add(description);

			// Determinismo: misma semilla otra vez, y la semilla efectiva directamente.
			if (DungeonGenerator.Describe(generator.Generate(seed, settings).Map) != description)
			{
				nondeterministic++;
			}

			DungeonGenerator.Result replay = generator.Generate(result.EffectiveSeed, settings);
			if (!replay.Success || replay.Attempts != 1 || DungeonGenerator.Describe(replay.Map) != description)
			{
				replayMismatches++;
			}

			variety.Add(result.Map);

			int rooms = result.Map.Rooms.Count;
			roomCounts[rooms] = roomCounts.GetValueOrDefault(rooms) + 1;

			int bossDistance = LevelValidator.BossDistance(result.Map);
			bossDistances[bossDistance] = bossDistances.GetValueOrDefault(bossDistance) + 1;
			bossDistanceSum += bossDistance;
			bossDistanceMin = Mathf.Min(bossDistanceMin, bossDistance);
			bossDistanceMax = Mathf.Max(bossDistanceMax, bossDistance);

			foreach (RoomData room in result.Map.Rooms)
			{
				templateUse[room.TemplateId] = templateUse.GetValueOrDefault(room.TemplateId) + 1;
				withTreasure += room.Type == RoomType.Treasure ? 1 : 0;
			}
		}

		clock.Stop();

		List<string> testMapProblems = LevelValidator.Validate(GD.Load<LevelMap>(TestMapPath));

		GD.Print($"=== DungeonGenerator: {SeedCount} semillas ({FirstSeed}..{FirstSeed + SeedCount - 1}) en {clock.ElapsedMilliseconds} ms ===");
		GD.Print($"Válidos: {passed}/{SeedCount}  (al primer intento: {firstTry}, máximo de intentos usados: {maxAttempts})");
		GD.Print($"Niveles distintos: {distinctLevels.Count}");
		GD.Print($"Determinismo (misma semilla dos veces): {(nondeterministic == 0 ? "OK" : $"{nondeterministic} diferencias")}");
		GD.Print($"Semilla efectiva reproduce el nivel al primer intento: {(replayMismatches == 0 ? "OK" : $"{replayMismatches} diferencias")}");
		GD.Print("Número de salas: " + string.Join("  ", roomCounts.Select(pair => $"{pair.Key}: {pair.Value}")));
		GD.Print($"Distancia al jefe (pasos): media {(passed > 0 ? (double)bossDistanceSum / passed : 0):0.00}, " +
			$"mín {bossDistanceMin}, máx {bossDistanceMax}");
		GD.Print("Distribución de distancia al jefe: " + string.Join("  ", bossDistances.Select(pair => $"{pair.Key}: {pair.Value}")));
		GD.Print($"Niveles con sala de tesoro: {withTreasure}/{passed}");
		GD.Print("Uso de plantillas: " + string.Join("  ", templateUse.Select(pair => $"{pair.Key}: {pair.Value}")));
		if (settings.GeneratorVersion >= 2)
		{
			variety.Print();
		}

		GD.Print($"TestMap.tres: {(testMapProblems.Count == 0 ? "válido" : string.Join(" | ", testMapProblems))}");

		foreach (string failure in failures.Take(5))
		{
			GD.Print($"Fallo: {failure}");
		}

		return passed == SeedCount && nondeterministic == 0 && replayMismatches == 0 && testMapProblems.Count == 0;
	}

	/// <summary>
	/// Variedad de enemigos de los niveles de la versión 2. Para cada nivel válido cuenta los tipos de enemigo distintos, las
	/// salas con una plantilla mixta (RoomCatalog.NormalTemplatesV2) y los enemigos de cada tipo; y para las salas con
	/// enemigos (EnemyScene y puntos de aparición), cuántas son solo de básicos, con y sin la sala del jefe (siempre de
	/// básicos).
	/// </summary>
	private sealed class VarietyReport
	{
		private readonly HashSet<string> _mixedTemplates = new();
		private readonly int[] _levelsByKinds = new int[4]; // 0, 1, 2 y 3 o más tipos distintos
		private readonly SortedDictionary<int, int> _levelsByMixedTemplates = new();
		private readonly long[] _enemiesByKind = new long[EnemyKinds.List.Count];
		private int _roomsWithEnemies;
		private int _roomsOnlyBasic;
		private int _normalRoomsWithEnemies;
		private int _normalRoomsOnlyBasic;
		private int _levels;

		public VarietyReport(RoomCatalog catalog)
		{
			foreach (RoomTemplate template in catalog.NormalTemplatesV2)
			{
				_mixedTemplates.Add(template.Id);
			}
		}

		public void Add(LevelMap map)
		{
			_levels++;
			HashSet<EnemyKind> kinds = new();
			int mixedTemplates = 0;

			foreach (RoomData room in map.Rooms)
			{
				mixedTemplates += _mixedTemplates.Contains(room.TemplateId) ? 1 : 0;
				if (room.EnemyScene == null || room.SpawnPoints.Length == 0)
				{
					continue;
				}

				HashSet<EnemyKind> roomKinds = new();
				for (int spawn = 0; spawn < room.SpawnPoints.Length; spawn++)
				{
					EnemyKind kind = LevelMetricsV2.KindAt(room, spawn);
					roomKinds.Add(kind);
					kinds.Add(kind);
					_enemiesByKind[(int)kind]++;
				}

				bool onlyBasic = roomKinds.Count == 1 && roomKinds.Contains(EnemyKind.Chaser);
				_roomsWithEnemies++;
				_roomsOnlyBasic += onlyBasic ? 1 : 0;
				if (room.Type == RoomType.Normal)
				{
					_normalRoomsWithEnemies++;
					_normalRoomsOnlyBasic += onlyBasic ? 1 : 0;
				}
			}

			_levelsByKinds[System.Math.Min(kinds.Count, 3)]++;
			_levelsByMixedTemplates[mixedTemplates] = _levelsByMixedTemplates.GetValueOrDefault(mixedTemplates) + 1;
		}

		public void Print()
		{
			GD.Print("");
			GD.Print($"=== Variedad de enemigos (generador v2, {_levels} niveles) ===");
			GD.Print("Niveles según los tipos de enemigo distintos que tienen: " +
				$"0: {Share(_levelsByKinds[0])}  1: {Share(_levelsByKinds[1])}  2: {Share(_levelsByKinds[2])}  3 o más: {Share(_levelsByKinds[3])}");
			GD.Print("Plantillas mixtas por nivel (niveles con n): " +
				string.Join("  ", _levelsByMixedTemplates.Select(pair => $"{pair.Key}: {Share(pair.Value)}")));

			long totalEnemies = _enemiesByKind.Sum();
			GD.Print($"Enemigos en total: {totalEnemies} ({(double)totalEnemies / System.Math.Max(_levels, 1):0.0} por nivel)");
			foreach (EnemyKinds.Info info in EnemyKinds.List)
			{
				long count = _enemiesByKind[(int)info.Kind];
				GD.Print($"  {info.Name,-10}{count,9}  {(double)count / System.Math.Max(totalEnemies, 1):P1}  ({(double)count / System.Math.Max(_levels, 1):0.00} por nivel)");
			}

			GD.Print($"Salas con enemigos: {_roomsWithEnemies}; solo de básicos: {_roomsOnlyBasic} " +
				$"({(double)_roomsOnlyBasic / System.Math.Max(_roomsWithEnemies, 1):P1})");
			GD.Print($"Salas normales con enemigos (sin la sala del jefe): {_normalRoomsWithEnemies}; solo de básicos: {_normalRoomsOnlyBasic} " +
				$"({(double)_normalRoomsOnlyBasic / System.Math.Max(_normalRoomsWithEnemies, 1):P1}); con algún tipo especial: " +
				$"{_normalRoomsWithEnemies - _normalRoomsOnlyBasic} ({(double)(_normalRoomsWithEnemies - _normalRoomsOnlyBasic) / System.Math.Max(_normalRoomsWithEnemies, 1):P1})");
		}

		private string Share(int levels)
		{
			return $"{levels} ({(double)levels / System.Math.Max(_levels, 1):P1})";
		}
	}
}
