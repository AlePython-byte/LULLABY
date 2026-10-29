using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

/// <summary>
/// Prueba de estrés del DungeonGenerator. Ejecutar en modo headless:
///   godot --headless --path . res://tests/GeneratorStressTest.tscn
///   godot --headless --path . res://tests/GeneratorStressTest.tscn -- --generator=2   (versión 2 del generador)
/// Genera SeedCount semillas, valida cada nivel, comprueba el determinismo y sale con código 0 si todo pasa.
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
		DungeonGenerator.Settings settings = new();
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
		GD.Print($"TestMap.tres: {(testMapProblems.Count == 0 ? "válido" : string.Join(" | ", testMapProblems))}");

		foreach (string failure in failures.Take(5))
		{
			GD.Print($"Fallo: {failure}");
		}

		return passed == SeedCount && nondeterministic == 0 && replayMismatches == 0 && testMapProblems.Count == 0;
	}
}
