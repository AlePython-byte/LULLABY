using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Etiqueta niveles con la dificultad MEDIDA por el bot en el simulador (nunca calculada con las características).
/// Modo headless:
///   godot --headless --path . res://tests/LabelExport.tscn
///   godot --headless --path . res://tests/LabelExport.tscn -- --first-seed=10001 --count=1000 --out=ai-training/datasets/levels_v1_test_labels.csv
///   godot --headless --path . res://tests/LabelExport.tscn -- --version=2     (generador v2 → levels_v2_labels.csv)
/// Argumentos (tras "--"): --count=N (5000), --first-seed=N (1), --runs=N (5), --out=ruta del CSV, --version=N (1:
/// generador v1 y LevelMetrics; 2: generador v2, LevelMetricsV2 y el desglose por composición de sala), --threads=N
/// (hilos de simulación; por defecto todos los del procesador; el resultado no depende de este número).
/// Sin --version la salida es la de siempre, byte a byte.
///
/// Por nivel juega Runs partidas con semillas de bot distintas (BotBrain.SeedFor(semilla efectiva, i)), con vida
/// ilimitada, y guarda media y desviación típica muestral de cada medida. Columnas:
///   effective_seed, damage_total_mean/std, damage_critical_mean/std, time_total_mean/std, time_to_boss_mean/std,
///   y, como columnas auxiliares (no son etiquetas objetivo): death_rate (fracción de partidas en que el daño
///   alcanza la vida máxima del jugador) y timeouts (partidas que agotaron MaxSeconds).
/// Etiqueta principal: damage_total_mean. Etiqueta secundaria: time_total_mean.
/// Formato: UTF-8 sin BOM, LF, cultura invariante, sin timestamps (dos ejecuciones dan archivos idénticos).
/// Las simulaciones van en paralelo; cada una es independiente y se guarda por índice, así que el resultado no
/// depende del número de hilos.
/// </summary>
public partial class LabelExport : Node
{
	private const string CatalogPath = "res://resources/rooms/templates/Catalog.tres";
	private const string DefaultOutput = "res://ai-training/datasets/levels_v1_labels.csv";
	private const string DefaultOutputV2 = "res://ai-training/datasets/levels_v2_labels.csv";
	private const string MainLabel = "damage_total_mean";
	private const string SecondaryLabel = "time_total_mean";

	// Columnas informativas, no son etiquetas objetivo (death_rate casi no varía con la vida de 5 del jugador).
	private static readonly string[] AuxiliaryLabels = { "death_rate", "timeouts" };

	private static readonly string[] LabelNames =
	{
		"damage_total_mean", "damage_total_std", "damage_critical_mean", "damage_critical_std",
		"time_total_mean", "time_total_std", "time_to_boss_mean", "time_to_boss_std", "death_rate", "timeouts",
	};

	private static readonly (string Name, Func<SimulationResult, double> Value)[] Measures =
	{
		("damage_total", r => r.DamageTotal),
		("damage_critical", r => r.DamageCritical),
		("time_total", r => r.TimeTotal),
		("time_to_boss", r => r.TimeToBoss),
	};

	[Export] public int LevelCount { get; set; } = 5000;
	[Export] public int FirstSeed { get; set; } = 1;
	[Export] public int Runs { get; set; } = 5;
	[Export] public float MaxSeconds { get; set; } = 600.0f;
	[Export] public int Version { get; set; } = LevelMetrics.Version;
	[Export] public int Threads { get; set; }

	public override void _Ready()
	{
		bool ok = Run();
		GD.Print(ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(ok ? 0 : 1);
	}

	private bool Run()
	{
		string outPath = ResolveArguments();
		GameplayParameters parameters = GameplayParameters.FromScenes();
		BotSettings botSettings = new();
		DungeonGenerator generator = new(GD.Load<RoomCatalog>(CatalogPath));
		DungeonGenerator.Settings generatorSettings = new() { GeneratorVersion = Version };

		List<int> seeds = new();
		List<SimLevel> levels = new();
		List<double[]> features = new();
		HashSet<int> seen = new();
		int failed = 0;

		for (int seed = FirstSeed; seed < FirstSeed + LevelCount; seed++)
		{
			DungeonGenerator.Result result = generator.Generate(seed, generatorSettings);
			if (!result.Success || !seen.Add(result.EffectiveSeed))
			{
				failed++;
				continue;
			}

			seeds.Add(result.EffectiveSeed);
			levels.Add(SimLevel.From(result.Map, parameters));
			features.Add(LevelFeatureSet.Compute(Version, result.Map));
		}

		SimulationResult[][] runs = new SimulationResult[levels.Count][];
		Stopwatch clock = Stopwatch.StartNew();
		ParallelOptions options = new() { MaxDegreeOfParallelism = Threads > 0 ? Threads : -1 };
		Parallel.For(0, levels.Count, options, i =>
		{
			runs[i] = new SimulationResult[Runs];
			for (int run = 0; run < Runs; run++)
			{
				BotBrain bot = new(levels[i], botSettings, BotBrain.SeedFor(seeds[i], run));
				runs[i][run] = new LevelSimulator(parameters, levels[i], bot).Run(MaxSeconds);
			}
		});
		clock.Stop();

		double[][] labels = runs.Select(level => Labels(level, parameters.PlayerMaxHealth)).ToArray();

		byte[] csv = new UTF8Encoding(false).GetBytes(BuildCsv(seeds, labels));
		Directory.CreateDirectory(Path.GetDirectoryName(outPath));
		File.WriteAllBytes(outPath, csv);
		string metaPath = Path.ChangeExtension(outPath, ".meta.json");
		File.WriteAllBytes(metaPath, new UTF8Encoding(false).GetBytes(
			BuildMeta(levels.Count, parameters, botSettings, generatorSettings)));

		GD.Print($"=== LabelExport: {levels.Count} niveles × {Runs} partidas del bot (semillas {FirstSeed}..{FirstSeed + LevelCount - 1}) ===");
		GD.Print($"CSV:  {outPath}");
		GD.Print($"Meta: {metaPath}");
		GD.Print($"Tamaño del CSV: {csv.Length} bytes; SHA-256: {Convert.ToHexString(SHA256.HashData(csv))}");
		if (failed > 0)
		{
			GD.Print($"AVISO: {failed} semillas sin nivel válido o con semilla efectiva repetida (descartadas).");
		}

		Report(runs, labels, features, clock.Elapsed.TotalSeconds);
		if (Version != LevelMetrics.Version)
		{
			ReportComposition(levels, runs);
		}

		GD.Print($"Memoria del proceso al terminar: {System.Environment.WorkingSet / (1024.0 * 1024.0):0} MiB " +
			$"({(Threads > 0 ? Threads : System.Environment.ProcessorCount)} hilos).");
		return levels.Count > 0 && failed == 0;
	}

	private double[] Labels(SimulationResult[] results, float playerMaxHealth)
	{
		List<double> values = new();
		foreach ((string _, Func<SimulationResult, double> value) in Measures)
		{
			double[] samples = results.Select(value).ToArray();
			values.Add(SampleStatistics.Mean(samples));
			values.Add(SampleStatistics.SampleStd(samples));
		}

		values.Add(results.Count(r => r.WouldDie) / (double)results.Length);
		values.Add(results.Count(r => r.TimedOut));
		return values.ToArray();
	}

	private string ResolveArguments()
	{
		string output = null;
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (TryValue(argument, "--version=", out string version) && int.TryParse(version, out int parsedVersion))
			{
				Version = parsedVersion;
			}
			else if (TryValue(argument, "--threads=", out string threads) && int.TryParse(threads, out int parsedThreads))
			{
				Threads = parsedThreads;
			}
			else if (TryValue(argument, "--count=", out string count) && int.TryParse(count, out int parsedCount))
			{
				LevelCount = parsedCount;
			}
			else if (TryValue(argument, "--first-seed=", out string first) && int.TryParse(first, out int parsedFirst))
			{
				FirstSeed = parsedFirst;
			}
			else if (TryValue(argument, "--runs=", out string runs) && int.TryParse(runs, out int parsedRuns))
			{
				Runs = parsedRuns;
			}
			else if (TryValue(argument, "--out=", out string path))
			{
				output = path;
			}
		}

		output ??= Version == LevelMetrics.Version ? DefaultOutput : DefaultOutputV2;
		if (output.StartsWith("res://") || output.StartsWith("user://"))
		{
			return ProjectSettings.GlobalizePath(output);
		}

		return Path.IsPathRooted(output) ? output : Path.Combine(ProjectSettings.GlobalizePath("res://"), output);
	}

	private static bool TryValue(string argument, string prefix, out string value)
	{
		value = argument.StartsWith(prefix) ? argument[prefix.Length..] : null;
		return value != null;
	}

	private static string BuildCsv(List<int> seeds, double[][] labels)
	{
		StringBuilder csv = new();
		csv.Append("effective_seed,").Append(string.Join(",", LabelNames)).Append('\n');
		for (int i = 0; i < seeds.Count; i++)
		{
			csv.Append(seeds[i].ToString(CultureInfo.InvariantCulture));
			foreach (double value in labels[i])
			{
				csv.Append(',').Append(Number(value));
			}

			csv.Append('\n');
		}

		return csv.ToString();
	}

	// Redondeo a 6 decimales; "+ 0.0" convierte un -0 en 0.
	private static string Number(double value)
	{
		return (Math.Round(value, 6) + 0.0).ToString("0.######", CultureInfo.InvariantCulture);
	}

	private string BuildMeta(int levelCount, GameplayParameters parameters, BotSettings bot,
		DungeonGenerator.Settings generator)
	{
		if (Version != LevelMetrics.Version)
		{
			return BuildMetaV2(levelCount, parameters, bot, generator);
		}

		var meta = new
		{
			simulator_version = LevelSimulator.VersionFor(Version),
			metrics_version = LevelMetrics.Version,
			label_names = LabelNames,
			main_label = MainLabel,
			secondary_label = SecondaryLabel,
			auxiliary_labels = AuxiliaryLabels,
			join_key = "effective_seed",
			level_count = levelCount,
			first_seed = FirstSeed,
			runs_per_level = Runs,
			max_seconds = MaxSeconds,
			bot = bot.ToDictionary(),
			gameplay = parameters.ToDictionary(),
			generator = new
			{
				MinRooms = generator.MinRooms,
				MaxRooms = generator.MaxRooms,
				MinBossDistance = generator.MinBossDistance,
				BranchChance = generator.BranchChance,
				MaxAttempts = generator.MaxAttempts,
			},
		};

		string json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
		return json.Replace("\r\n", "\n") + "\n";
	}

	private string BuildMetaV2(int levelCount, GameplayParameters parameters, BotSettings bot,
		DungeonGenerator.Settings generator)
	{
		var meta = new
		{
			simulator_version = LevelSimulator.VersionFor(Version),
			metrics_version = Version,
			label_names = LabelNames,
			main_label = MainLabel,
			secondary_label = SecondaryLabel,
			auxiliary_labels = AuxiliaryLabels,
			join_key = "effective_seed",
			level_count = levelCount,
			first_seed = FirstSeed,
			runs_per_level = Runs,
			max_seconds = MaxSeconds,
			bot = bot.ToDictionary(typedSkills: true),
			gameplay = parameters.ToDictionary(),
			generator = new
			{
				GeneratorVersion = generator.GeneratorVersion,
				MinRooms = generator.MinRooms,
				MaxRooms = generator.MaxRooms,
				MinBossDistance = generator.MinBossDistance,
				BranchChance = generator.BranchChance,
				MaxAttempts = generator.MaxAttempts,
			},
		};

		string json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
		return json.Replace("\r\n", "\n") + "\n";
	}

	// Daño y duración medios de la primera visita a cada sala, por composición (todas las partidas de todos los niveles).
	private static void ReportComposition(List<SimLevel> levels, SimulationResult[][] runs)
	{
		var summary = RoomComposition.Summarize(levels.SelectMany((level, i) => runs[i].Select(run => (level, run))));
		GD.Print("");
		GD.Print("Por composición de sala (primera visita; una sala mixta cuenta en cada tipo que contiene):");
		GD.Print($"{"composición",-16}{"salas",9}{"daño medio",12}{"tiempo medio",14}");
		foreach ((string category, (int rooms, double damage, double time)) in summary)
		{
			GD.Print($"{category,-16}{rooms,9}{F(damage),12}{F(time),14}");
		}

		foreach (string category in new[] { RoomComposition.WithShooter, RoomComposition.WithExploder })
		{
			bool measurable = summary.TryGetValue(category, out var row) && row.Damage > 0.0;
			GD.Print(measurable
				? $"Daño medible en '{category}': sí ({F(row.Damage)} por sala)."
				: $"AVISO: el bot no recibe daño medible en '{category}'.");
		}
	}

	private void Report(SimulationResult[][] runs, double[][] labels, List<double[]> features, double seconds)
	{
		int levelCount = runs.Length;
		double simulated = runs.Sum(level => level.Sum(r => (double)r.TimeTotal));
		int timeouts = runs.Sum(level => level.Count(r => r.TimedOut));

		GD.Print("");
		GD.Print($"Tiempo de simulación: {seconds:0.0} s reales para {levelCount * Runs} partidas " +
			$"({simulated / 3600.0:0.0} h de juego, ×{simulated / Math.Max(seconds, 0.001):0} tiempo real, {System.Environment.ProcessorCount} hilos).");
		GD.Print($"Partidas que agotaron el tiempo ({MaxSeconds} s): {timeouts}");

		GD.Print("");
		GD.Print("Distribución de las columnas (media por nivel). Principal: " + MainLabel + ", secundaria: " + SecondaryLabel +
			"; auxiliares: " + string.Join(", ", AuxiliaryLabels) + ".");
		GD.Print($"{"columna",-22}{"mín",9}{"p10",9}{"mediana",9}{"p90",9}{"máx",9}{"media",9}{"desv.",9}");
		for (int l = 0; l < LabelNames.Length; l++)
		{
			double[] column = labels.Select(row => row[l]).ToArray();
			GD.Print($"{LabelNames[l],-22}{F(column.Min()),9}{F(SampleStatistics.Percentile(column, 0.1)),9}" +
				$"{F(SampleStatistics.Percentile(column, 0.5)),9}{F(SampleStatistics.Percentile(column, 0.9)),9}" +
				$"{F(column.Max()),9}{F(column.Average()),9}{F(SampleStatistics.PopulationStd(column)),9}");
		}

		GD.Print("");
		GD.Print($"Ruido entre las {Runs} repeticiones frente a variación entre niveles:");
		GD.Print($"{"medida",-18}{"ruido",9}{"variación",11}{"ruido/var",11}{"ruido media",13}{"fiabilidad",12}");
		GD.Print("  ruido = desviación media dentro de un nivel; variación = desviación de las medias entre niveles;");
		GD.Print($"  ruido media = ruido / √{Runs} (el que queda en la etiqueta promediada); fiabilidad = parte de la");
		GD.Print("  varianza de la etiqueta que se debe al nivel y no al ruido (1 = sin ruido).");
		List<string> warnings = new();
		for (int m = 0; m < Measures.Length; m++)
		{
			double[] means = labels.Select(row => row[m * 2]).ToArray();
			double[] stds = labels.Select(row => row[m * 2 + 1]).ToArray();
			double noise = Math.Sqrt(stds.Average(s => s * s));
			double variation = SampleStatistics.PopulationStd(means);
			double noiseOfMean = noise / Math.Sqrt(Runs);
			double varianceOfMeans = variation * variation;
			double reliability = varianceOfMeans > 0.0
				? Math.Max(0.0, varianceOfMeans - noiseOfMean * noiseOfMean) / varianceOfMeans
				: 0.0;

			GD.Print($"{Measures[m].Name,-18}{F(noise),9}{F(variation),11}{F(variation > 0.0 ? noise / variation : double.NaN),11}" +
				$"{F(noiseOfMean),13}{F(reliability),12}");

			double mean = means.Average();
			if (variation < 1e-9 || (Math.Abs(mean) > 1e-9 && variation / Math.Abs(mean) < 0.05))
			{
				warnings.Add($"'{Measures[m].Name}' casi no varía entre niveles (desviación {F(variation)}, media {F(mean)}).");
			}

			if (noise >= variation)
			{
				warnings.Add($"en '{Measures[m].Name}' el ruido de una partida ({F(noise)}) es mayor o igual que la variación entre niveles ({F(variation)}).");
			}

			if (noiseOfMean >= variation)
			{
				warnings.Add($"en '{Measures[m].Name}' incluso la media de {Runs} partidas tiene más ruido que variación entre niveles.");
			}
		}

		int deathIndex = Array.IndexOf(LabelNames, "death_rate");
		double[] deathRates = labels.Select(row => row[deathIndex]).ToArray();
		var mostCommon = deathRates.GroupBy(d => d).OrderByDescending(group => group.Count()).First();
		GD.Print("");
		GD.Print($"Columnas auxiliares (no son etiquetas objetivo): death_rate: {deathRates.Count(d => d == 0.0)} niveles nunca " +
			$"mortales, {deathRates.Count(d => d == 1.0)} siempre mortales, {deathRates.Count(d => d > 0.0 && d < 1.0)} a veces; " +
			$"media {F(deathRates.Average())}; el valor más común ({F(mostCommon.Key)}) lo tiene el " +
			$"{mostCommon.Count() * 100.0 / levelCount:0.0} % de los niveles.");

		double[] main = labels.Select(row => row[Array.IndexOf(LabelNames, MainLabel)]).ToArray();
		double[] secondary = labels.Select(row => row[Array.IndexOf(LabelNames, SecondaryLabel)]).ToArray();
		GD.Print("");
		GD.Print($"Correlación de cada característica con la etiqueta principal ({MainLabel}) y la secundaria ({SecondaryLabel}):");
		GD.Print($"{"característica",-30}{"principal P",13}{"principal S",13}{"secundaria P",14}{"secundaria S",14}");
		string[] featureNames = LevelFeatureSet.Names(Version);
		for (int f = 0; f < featureNames.Length; f++)
		{
			double[] column = features.Select(row => row[f]).ToArray();
			GD.Print($"{featureNames[f],-30}{F(SampleStatistics.Pearson(column, main)),13}{F(SampleStatistics.Spearman(column, main)),13}" +
				$"{F(SampleStatistics.Pearson(column, secondary)),14}{F(SampleStatistics.Spearman(column, secondary)),14}");
		}
		GD.Print("  (P = Pearson, S = Spearman)");

		GD.Print("");
		foreach (string warning in warnings)
		{
			GD.Print($"AVISO: {warning}");
		}

		GD.Print(warnings.Count == 0 ? "Sin avisos: las etiquetas varían entre niveles más que el ruido." : $"{warnings.Count} aviso(s).");
	}

	private static string F(double value)
	{
		return value.ToString("0.000", CultureInfo.InvariantCulture);
	}
}
