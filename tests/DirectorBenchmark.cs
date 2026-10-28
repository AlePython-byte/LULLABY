using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Compara "niveles aleatorios" con "niveles seleccionados por el director de IA" midiendo la dificultad REAL
/// (damage_total_mean) con el simulador y el bot. Modo headless:
///   godot --headless --path . res://tests/DirectorBenchmark.tscn
/// Argumentos (tras "--"): --first-seed=N (300001), --count=N (300), --out=ruta del JSON de resultados.
///
/// Por cada semilla de partida (300001..300300) y cada objetivo (Easy, Normal, Hard):
///   - se generan los 30 candidatos del director y se mide la dificultad real de TODOS con el mismo protocolo que las
///     etiquetas del entrenamiento (media de 5 partidas del bot, semillas de bot derivadas de la semilla efectiva);
///   - error = |dificultad real del nivel elegido - objetivo|, para cuatro estrategias:
///       azar      un nivel cualquiera de los candidatos (media exacta de los 30: es lo que da un nivel aleatorio);
///       director  el nivel que elige AIDungeonDirector (azar entre los 5 con predicción más cercana, tal como juega);
///       mejor     siempre el candidato con la predicción más cercana;
///       oráculo   el candidato con la dificultad real más cercana (cota superior; no es alcanzable sin medir).
///     Como comprobación extra se informa la esperanza exacta del director (media de los 5 mejores).
///
/// Variedad (con el mismo objetivo y las 300 semillas de partida): cuántos niveles distintos produce cada estrategia por
/// estructura completa (celdas, tipos, plantillas, tamaños y puntos de aparición) y por forma (celdas y tipos), y
/// cuántas salas comparten dos niveles cualesquiera (mismo hueco de la cuadrícula con la misma plantilla; sin contar la
/// sala inicial, que es siempre la misma).
///
/// El modelo NO se entrenó con estas semillas: entrenamiento 1..5000, prueba 100001..101000, confirmación 200001..201000
/// (semillas del generador), mientras que aquí cada candidato usa una semilla derivada por hash de la semilla de partida.
/// Se verifica que ninguna semilla efectiva de ningún candidato aparece en los CSV de esos tres conjuntos.
/// Herramienta de desarrollo: usa el simulador y el bot; el juego no depende de esto.
/// </summary>
public partial class DirectorBenchmark : Node
{
	private const string CatalogPath = "res://resources/rooms/templates/Catalog.tres";
	private const float MaxSeconds = 600.0f;
	private const int BotRuns = 5;

	private static readonly string[] SeedFiles =
	{
		"ai-training/datasets/levels_v1.csv",
		"ai-training/datasets/levels_v1_test.csv",
		"ai-training/datasets/levels_v1_confirm.csv",
	};

	private static readonly string[] Strategies = { "random", "director", "best", "oracle" };

	private static readonly Dictionary<string, string> StrategyLabels = new()
	{
		["random"] = "azar",
		["director"] = "director (azar entre top-5)",
		["best"] = "siempre el mejor",
		["oracle"] = "oráculo",
	};

	[Export] public int FirstSeed { get; set; } = 300001;
	[Export] public int GameCount { get; set; } = 300;
	[Export] public int CandidateCount { get; set; } = 30;
	[Export] public int TopCount { get; set; } = 5;

	private string _outPath;

	public override void _Ready()
	{
		bool ok = Run();
		GD.Print(ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(ok ? 0 : 1);
	}

	private bool Run()
	{
		string root = ProjectSettings.GlobalizePath("res://");
		_outPath = Path.Combine(root, "ai-training/reports/director_benchmark.json");
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--first-seed=") && int.TryParse(argument["--first-seed=".Length..], out int first))
			{
				FirstSeed = first;
			}
			else if (argument.StartsWith("--count=") && int.TryParse(argument["--count=".Length..], out int count))
			{
				GameCount = count;
			}
			else if (argument.StartsWith("--out="))
			{
				_outPath = Path.IsPathRooted(argument["--out=".Length..]) ? argument["--out=".Length..] : Path.Combine(root, argument["--out=".Length..]);
			}
		}

		GameplayParameters parameters = GameplayParameters.FromScenes();
		BotSettings botSettings = new();
		DifficultyModel model = DifficultyModel.Load();
		DungeonGenerator generator = new(GD.Load<RoomCatalog>(CatalogPath));
		AIDungeonDirector director = new(generator, model, new DungeonGenerator.Settings())
		{
			CandidateCount = CandidateCount,
			TopCount = TopCount,
		};
		DifficultyTarget[] targets = Enum.GetValues<DifficultyTarget>();

		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		int[] gameSeeds = Enumerable.Range(FirstSeed, GameCount).ToArray();
		List<AIDungeonDirector.Candidate>[] pools = gameSeeds.Select(seed => director.GenerateCandidates(seed)).ToArray();
		double generating = clock.Elapsed.TotalSeconds;

		// Aplanado: un nivel por candidato de cada partida.
		List<(int Game, int Index)> flat = new();
		for (int game = 0; game < pools.Length; game++)
		{
			for (int index = 0; index < pools[game].Count; index++)
			{
				flat.Add((game, index));
			}
		}

		SimLevel[] levels = flat.Select(item => SimLevel.From(pools[item.Game][item.Index].Map, parameters)).ToArray();
		double[] realFlat = new double[flat.Count];
		Parallel.For(0, flat.Count, i =>
		{
			int effectiveSeed = pools[flat[i].Game][flat[i].Index].EffectiveSeed;
			double sum = 0.0;
			for (int run = 0; run < BotRuns; run++)
			{
				BotBrain bot = new(levels[i], botSettings, BotBrain.SeedFor(effectiveSeed, run));
				sum += new LevelSimulator(parameters, levels[i], bot).Run(MaxSeconds).DamageTotal;
			}

			realFlat[i] = sum / BotRuns;
		});
		double simulating = clock.Elapsed.TotalSeconds - generating;

		double[][] real = pools.Select(pool => new double[pool.Count]).ToArray();
		for (int i = 0; i < flat.Count; i++)
		{
			real[flat[i].Game][flat[i].Index] = realFlat[i];
		}

		bool ok = CheckSeedOverlap(root, pools);
		ReportModelAccuracy(pools, real);

		SortedDictionary<string, object> json = new()
		{
			["first_seed"] = FirstSeed,
			["games"] = GameCount,
			["candidates_per_game"] = CandidateCount,
			["top_count"] = TopCount,
			["bot_runs_per_level"] = BotRuns,
			["simulator_version"] = LevelSimulator.Version,
		};

		GD.Print($"\n=== DirectorBenchmark: {GameCount} semillas de partida ({FirstSeed}..{FirstSeed + GameCount - 1}) × 3 objetivos × {CandidateCount} candidatos ===");
		GD.Print($"Tiempo: {generating:0.0} s generando + {simulating:0.0} s simulando {flat.Count} niveles × {BotRuns} partidas del bot.");

		GD.Print("\nError medio |dificultad real del nivel elegido − objetivo|  (menos es mejor; ± error estándar entre partidas)");
		GD.Print($"{"objetivo",-18}" + string.Concat(Strategies.Select(name => $"{StrategyLabels[name],-30}")));
		Dictionary<DifficultyTarget, AIDungeonDirector.Result[]> choices = new();
		SortedDictionary<string, object> errorJson = new();
		double[][] perGameOverall = Strategies.Select(_ => new double[GameCount]).ToArray();
		double[] expectedOverall = new double[GameCount];

		foreach (DifficultyTarget target in targets)
		{
			AIDungeonDirector.Result[] results = gameSeeds.Select((seed, game) => director.Choose(seed, target, pools[game])).ToArray();
			choices[target] = results;
			double value = model.TargetValue(target);

			double[][] errors = Strategies.Select(_ => new double[GameCount]).ToArray();
			double[] expectedDirector = new double[GameCount];
			for (int game = 0; game < GameCount; game++)
			{
				double[] distance = real[game].Select(r => Math.Abs(r - value)).ToArray();
				AIDungeonDirector.Result result = results[game];
				errors[0][game] = distance.Average();
				errors[1][game] = distance[pools[game].IndexOf(result.Chosen)];
				errors[2][game] = distance[pools[game].IndexOf(result.Best)];
				errors[3][game] = distance.Min();
				expectedDirector[game] = result.Ranking.Take(TopCount).Average(candidate => distance[pools[game].IndexOf(candidate)]);
				for (int s = 0; s < Strategies.Length; s++)
				{
					perGameOverall[s][game] += errors[s][game] / targets.Length;
				}

				expectedOverall[game] += expectedDirector[game] / targets.Length;
			}

			GD.Print($"{target + $" ({value:0.0})",-18}" + string.Concat(errors.Select(column =>
				$"{column.Average():0.000} ± {StandardError(column):0.000}".PadRight(30))));
			errorJson[target.ToString()] = Summarize(errors, expectedDirector, value);
		}

		GD.Print($"{"promedio",-18}" + string.Concat(perGameOverall.Select(column => $"{column.Average():0.000} ± {StandardError(column):0.000}".PadRight(30))));
		errorJson["Overall"] = Summarize(perGameOverall, expectedOverall, double.NaN);

		GD.Print("\nReducción del error frente al azar y parte de la distancia azar→oráculo que se recorre:");
		foreach (string name in errorJson.Keys)
		{
			var summary = (SortedDictionary<string, object>)errorJson[name];
			var reduction = (SortedDictionary<string, object>)summary["reduction_vs_random"];
			var closed = (SortedDictionary<string, object>)summary["gap_closed"];
			GD.Print($"  {(name == "Overall" ? "promedio" : name),-9} director {(double)reduction["director"]:P1} ({(double)closed["director"]:P0} del camino al oráculo); " +
				$"siempre el mejor {(double)reduction["best"]:P1} ({(double)closed["best"]:P0}); oráculo {(double)reduction["oracle"]:P1}; " +
				$"esperanza exacta del director (media de los {TopCount}) {(double)summary["director_expected_error"]:0.000}");
		}

		json["error"] = errorJson;
		json["variety"] = ReportVariety(targets, choices, pools);
		json["seed_overlap_checked_against"] = SeedFiles;

		Directory.CreateDirectory(Path.GetDirectoryName(_outPath));
		File.WriteAllBytes(_outPath, new UTF8Encoding(false).GetBytes(
			JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n"));
		GD.Print($"\nResultados escritos en {Path.GetRelativePath(root, _outPath)}.");
		return ok;
	}

	private static SortedDictionary<string, object> Summarize(double[][] errors, double[] expectedDirector, double targetValue)
	{
		double random = errors[0].Average(), oracle = errors[3].Average();
		SortedDictionary<string, object> mean = new(), reduction = new(), closed = new();
		for (int s = 0; s < Strategies.Length; s++)
		{
			double value = errors[s].Average();
			mean[Strategies[s]] = Round(value);
			if (s > 0)
			{
				reduction[Strategies[s]] = Round(1.0 - value / random);
				closed[Strategies[s]] = Round((random - value) / (random - oracle));
			}
		}

		return new SortedDictionary<string, object>
		{
			["target_value"] = double.IsNaN(targetValue) ? null : Round(targetValue),
			["mean_error"] = mean,
			["standard_error"] = Strategies.Select((name, s) => (name, value: Round(StandardError(errors[s])))).ToDictionary(pair => pair.name, pair => (object)pair.value),
			["reduction_vs_random"] = reduction,
			["gap_closed"] = closed,
			["director_expected_error"] = Round(expectedDirector.Average()),
		};
	}

	private bool CheckSeedOverlap(string root, List<AIDungeonDirector.Candidate>[] pools)
	{
		HashSet<int> used = new();
		foreach (string file in SeedFiles)
		{
			string path = Path.Combine(root, file);
			if (!File.Exists(path))
			{
				GD.Print($"AVISO: falta {file}; no se puede comprobar el solapamiento con ese conjunto.");
				continue;
			}

			foreach (string line in File.ReadLines(path).Skip(1))
			{
				used.Add(int.Parse(line[..line.IndexOf(',')], CultureInfo.InvariantCulture));
			}
		}

		int candidates = pools.Sum(pool => pool.Count);
		int collisions = pools.SelectMany(pool => pool).Count(candidate => used.Contains(candidate.EffectiveSeed));
		int gameSeedCollisions = Enumerable.Range(FirstSeed, GameCount).Count(used.Contains);
		GD.Print($"Solapamiento con entrenamiento/prueba/confirmación ({used.Count} semillas efectivas): " +
			$"{collisions} de {candidates} candidatos; semillas de partida en esos conjuntos: {gameSeedCollisions}.");
		return collisions == 0;
	}

	private static void ReportModelAccuracy(List<AIDungeonDirector.Candidate>[] pools, double[][] real)
	{
		List<double> predicted = pools.SelectMany(pool => pool.Select(candidate => candidate.Predicted)).ToList();
		List<double> measured = real.SelectMany(values => values).ToList();
		double mean = measured.Average();
		double residual = predicted.Zip(measured, (p, r) => (p - r) * (p - r)).Sum();
		double total = measured.Sum(r => (r - mean) * (r - mean));
		GD.Print($"Predicción frente a medida en los {measured.Count} candidatos del benchmark (informativo; no es la evaluación de confirmación): " +
			$"MAE {predicted.Zip(measured, (p, r) => Math.Abs(p - r)).Average():0.00}, R² {1.0 - residual / total:0.000}, " +
			$"Spearman {SampleStatistics.Spearman(predicted, measured):0.000}");
	}

	private SortedDictionary<string, object> ReportVariety(DifficultyTarget[] targets,
		Dictionary<DifficultyTarget, AIDungeonDirector.Result[]> choices, List<AIDungeonDirector.Candidate>[] pools)
	{
		SortedDictionary<string, object> json = new();
		GD.Print($"\n=== Variedad: {GameCount} semillas de partida distintas, mismo objetivo ===");
		GD.Print("Niveles distintos por estructura completa / por forma (celdas y tipos); salas compartidas entre dos niveles cualesquiera");
		GD.Print("(mismo hueco de la cuadrícula y misma plantilla, sin contar la sala inicial): media, máximo y % de pares con 3 o más.");
		GD.Print($"{"objetivo",-10}{"estrategia",-30}{"distintos",11}{"formas",9}{"comparten (media)",20}{"máx",6}{"≥3",8}");

		foreach (DifficultyTarget target in targets)
		{
			AIDungeonDirector.Result[] results = choices[target];
			(string Name, LevelMap[] Maps)[] strategies =
			{
				("candidato sin elegir (índice 0)", pools.Select(pool => pool[0].Map).ToArray()),
				("director (azar entre top-5)", results.Select(result => result.Chosen.Map).ToArray()),
				("siempre el mejor", results.Select(result => result.Best.Map).ToArray()),
			};

			SortedDictionary<string, object> perStrategy = new();
			foreach ((string name, LevelMap[] maps) in strategies)
			{
				int distinct = maps.Select(DungeonGenerator.Describe).Distinct().Count();
				int shapes = maps.Select(Shape).Distinct().Count();
				(double mean, int max, double manyShared) = SharedRooms(maps);
				GD.Print($"{target,-10}{name,-30}{distinct + "/" + maps.Length,11}{shapes,9}{mean,20:0.00}{max,6}{manyShared,8:P0}");
				perStrategy[name] = new SortedDictionary<string, object>
				{
					["distinct_levels"] = distinct, ["distinct_shapes"] = shapes, ["levels"] = maps.Length,
					["shared_rooms_mean"] = Round(mean), ["shared_rooms_max"] = max, ["pairs_with_3_or_more_shared"] = Round(manyShared),
				};
			}

			json[target.ToString()] = perStrategy;
		}

		// Opciones entre las que elige el director dentro de UNA partida: ¿son niveles distintos entre sí?
		List<double> topShared = new(), poolShared = new();
		for (int game = 0; game < pools.Length; game++)
		{
			List<AIDungeonDirector.Candidate> top = choices[DifficultyTarget.Normal][game].Ranking.Take(TopCount).ToList();
			topShared.Add(SharedRooms(top.Select(candidate => candidate.Map).ToArray()).Mean);
			poolShared.Add(SharedRooms(pools[game].Select(candidate => candidate.Map).ToArray()).Mean);
		}

		GD.Print($"\nDentro de una misma partida (objetivo Normal): salas compartidas por pares de candidatos: entre los {TopCount} mejores " +
			$"{topShared.Average():0.00}, entre los {CandidateCount} candidatos {poolShared.Average():0.00}.");
		json["within_game_shared_rooms"] = new SortedDictionary<string, object>
		{
			["top_candidates_normal"] = Round(topShared.Average()), ["all_candidates"] = Round(poolShared.Average()),
		};
		return json;
	}

	// Forma = celdas ocupadas con su tipo de sala, ordenadas.
	private static string Shape(LevelMap map)
	{
		return string.Join(";", map.Rooms.OrderBy(room => room.Cell.X).ThenBy(room => room.Cell.Y)
			.Select(room => $"{room.Cell.X},{room.Cell.Y}:{room.Type}"));
	}

	private static (double Mean, int Max, double ThreeOrMore) SharedRooms(LevelMap[] maps)
	{
		Dictionary<Vector2I, string>[] rooms = maps.Select(map => map.Rooms
			.Where(room => room.Type != RoomType.Start)
			.ToDictionary(room => room.Cell, room => $"{room.Type}|{room.TemplateId}")).ToArray();

		long total = 0;
		int pairs = 0, max = 0, many = 0;
		for (int a = 0; a < rooms.Length; a++)
		{
			for (int b = a + 1; b < rooms.Length; b++)
			{
				int shared = rooms[a].Count(pair => rooms[b].TryGetValue(pair.Key, out string other) && other == pair.Value);
				total += shared;
				max = Math.Max(max, shared);
				many += shared >= 3 ? 1 : 0;
				pairs++;
			}
		}

		return pairs == 0 ? (0.0, 0, 0.0) : ((double)total / pairs, max, (double)many / pairs);
	}

	private static double StandardError(double[] values)
	{
		return SampleStatistics.SampleStd(values) / Math.Sqrt(values.Length);
	}

	private static double Round(double value)
	{
		return Math.Round(value, 6) + 0.0;
	}
}
