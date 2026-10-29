using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

/// <summary>
/// Exporta un dataset de niveles generados con sus métricas (sin etiquetas). Modo headless:
///   godot --headless --path . res://tests/DatasetExport.tscn
///   godot --headless --path . res://tests/DatasetExport.tscn -- --count=2000 --first-seed=5001 --out=ai-training/datasets/levels_v1_b.csv
///   godot --headless --path . res://tests/DatasetExport.tscn -- --version=2     (generador v2 y LevelMetricsV2 → levels_v2.csv)
///
/// Argumentos (tras "--"): --count=N (5000), --first-seed=N (1), --out=ruta del CSV, --version=N (1). Una ruta relativa se
/// resuelve desde la raíz del proyecto. Junto al CSV se escribe un .meta.json con el mismo nombre base.
/// La versión elige a la vez el generador y el conjunto de características (1: v1 y LevelMetrics; 2: v2 y
/// LevelMetricsV2). Sin --version la salida es la de siempre, byte a byte.
///
/// Formato: CSV en UTF-8 sin BOM, saltos de línea LF, números con cultura invariante (punto decimal).
/// Primera columna: effective_seed (la semilla con la que se reproduce el nivel al primer intento);
/// después, las características de LevelMetrics en su orden. Sin timestamps: dos ejecuciones con los mismos
/// argumentos producen archivos idénticos byte a byte.
/// </summary>
public partial class DatasetExport : Node
{
	private const string CatalogPath = "res://resources/rooms/templates/Catalog.tres";
	private const string DefaultOutput = "res://ai-training/datasets/levels_v1.csv";
	private const string DefaultOutputV2 = "res://ai-training/datasets/levels_v2.csv";
	private const double ConstantThreshold = 1e-9;
	private const double CorrelationThreshold = 0.98;
	private const double HighCorrelationThreshold = 0.90;

	[Export] public int LevelCount { get; set; } = 5000;
	[Export] public int FirstSeed { get; set; } = 1;
	[Export] public int Version { get; set; } = LevelMetrics.Version;

	public override void _Ready()
	{
		bool ok = Run();
		GD.Print(ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(ok ? 0 : 1);
	}

	private bool Run()
	{
		string outPath = ResolveArguments();
		if (!ToolVersionGuard.Check("DatasetExport", Version))
		{
			return false;
		}

		RoomCatalog catalog = GD.Load<RoomCatalog>(CatalogPath);
		DungeonGenerator generator = new(catalog);
		DungeonGenerator.Settings settings = new() { GeneratorVersion = Version };

		string[] names = LevelFeatureSet.Names(Version);
		List<int> seeds = new();
		List<double[]> rows = new();
		HashSet<int> seen = new();
		int failed = 0;
		int duplicated = 0;

		for (int seed = FirstSeed; seed < FirstSeed + LevelCount; seed++)
		{
			DungeonGenerator.Result result = generator.Generate(seed, settings);
			if (!result.Success)
			{
				failed++;
				continue;
			}

			if (!seen.Add(result.EffectiveSeed))
			{
				duplicated++;
				continue;
			}

			seeds.Add(result.EffectiveSeed);
			rows.Add(LevelFeatureSet.Compute(Version, result.Map));
		}

		string csv = BuildCsv(names, seeds, rows);
		byte[] csvBytes = new UTF8Encoding(false).GetBytes(csv);
		Directory.CreateDirectory(Path.GetDirectoryName(outPath));
		File.WriteAllBytes(outPath, csvBytes);

		string metaPath = Path.ChangeExtension(outPath, ".meta.json");
		File.WriteAllBytes(metaPath, new UTF8Encoding(false).GetBytes(BuildMeta(names, rows.Count, settings)));

		GD.Print($"=== DatasetExport: {rows.Count} niveles (semillas {FirstSeed}..{FirstSeed + LevelCount - 1}) ===");
		GD.Print($"CSV:  {outPath}");
		GD.Print($"Meta: {metaPath}");
		GD.Print($"Tamaño del CSV: {csvBytes.Length} bytes ({csvBytes.Length / 1024.0:0.0} KiB)");
		GD.Print($"SHA-256 del CSV: {Convert.ToHexString(SHA256.HashData(csvBytes))}");

		if (failed > 0 || duplicated > 0)
		{
			GD.Print($"AVISO: {failed} semillas sin nivel válido y {duplicated} semillas efectivas repetidas (descartadas).");
		}

		PrintSummary(names, rows);
		return rows.Count > 0 && failed == 0 && duplicated == 0;
	}

	private string ResolveArguments()
	{
		string output = null;
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if ((TryValue(argument, "--version=", out string version) || TryValue(argument, "--generator=", out version))
				&& int.TryParse(version, out int parsedVersion))
			{
				Version = parsedVersion;
			}
			else if (TryValue(argument, "--count=", out string count) && int.TryParse(count, out int parsedCount))
			{
				LevelCount = parsedCount;
			}
			else if (TryValue(argument, "--first-seed=", out string first) && int.TryParse(first, out int parsedFirst))
			{
				FirstSeed = parsedFirst;
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

	private static string BuildCsv(string[] names, List<int> seeds, List<double[]> rows)
	{
		StringBuilder csv = new();
		csv.Append("effective_seed,").Append(string.Join(",", names)).Append('\n');

		for (int i = 0; i < rows.Count; i++)
		{
			csv.Append(seeds[i].ToString(CultureInfo.InvariantCulture));
			foreach (double value in rows[i])
			{
				// Redondeo a 6 decimales; "+ 0.0" convierte un -0 en 0.
				csv.Append(',').Append((Math.Round(value, 6) + 0.0).ToString("0.######", CultureInfo.InvariantCulture));
			}

			csv.Append('\n');
		}

		return csv.ToString();
	}

	private string BuildMeta(string[] names, int levelCount, DungeonGenerator.Settings settings)
	{
		string json = Version == LevelMetrics.Version ? MetaV1(names, levelCount, settings) : MetaV2(names, levelCount, settings);
		return json.Replace("\r\n", "\n") + "\n";
	}

	// Igual que antes de existir la versión 2 (el meta.json de v1 no cambia).
	private string MetaV1(string[] names, int levelCount, DungeonGenerator.Settings settings)
	{
		var meta = new
		{
			metrics_version = LevelMetrics.Version,
			feature_names = names,
			level_count = levelCount,
			first_seed = FirstSeed,
			generator = new
			{
				MinRooms = settings.MinRooms,
				MaxRooms = settings.MaxRooms,
				MinBossDistance = settings.MinBossDistance,
				BranchChance = settings.BranchChance,
				MaxAttempts = settings.MaxAttempts,
			},
		};

		return JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
	}

	private string MetaV2(string[] names, int levelCount, DungeonGenerator.Settings settings)
	{
		var meta = new
		{
			metrics_version = Version,
			feature_names = names,
			level_count = levelCount,
			first_seed = FirstSeed,
			generator = new
			{
				GeneratorVersion = settings.GeneratorVersion,
				MinRooms = settings.MinRooms,
				MaxRooms = settings.MaxRooms,
				MinBossDistance = settings.MinBossDistance,
				BranchChance = settings.BranchChance,
				MaxAttempts = settings.MaxAttempts,
			},
		};

		return JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
	}

	private static void PrintSummary(string[] names, List<double[]> rows)
	{
		int count = rows.Count;
		double[][] columns = new double[names.Length][];
		double[] stds = new double[names.Length];

		GD.Print("");
		GD.Print($"{"característica",-30}{"mín",12}{"máx",12}{"media",12}{"desv.",12}");
		for (int f = 0; f < names.Length; f++)
		{
			columns[f] = new double[count];
			for (int r = 0; r < count; r++)
			{
				columns[f][r] = rows[r][f];
			}

			double min = double.MaxValue;
			double max = double.MinValue;
			double sum = 0.0;
			foreach (double value in columns[f])
			{
				min = Math.Min(min, value);
				max = Math.Max(max, value);
				sum += value;
			}

			double mean = sum / count;
			double variance = 0.0;
			foreach (double value in columns[f])
			{
				variance += (value - mean) * (value - mean);
			}

			stds[f] = Math.Sqrt(variance / count);
			GD.Print($"{names[f],-30}{Format(min),12}{Format(max),12}{Format(mean),12}{Format(stds[f]),12}");
		}

		GD.Print("");
		int warnings = 0;
		for (int f = 0; f < names.Length; f++)
		{
			if (stds[f] < ConstantThreshold)
			{
				warnings++;
				GD.Print($"AVISO: '{names[f]}' es constante ({Format(columns[f][0])}); no aporta información.");
			}
		}

		for (int a = 0; a < names.Length; a++)
		{
			for (int b = a + 1; b < names.Length; b++)
			{
				if (stds[a] < ConstantThreshold || stds[b] < ConstantThreshold)
				{
					continue;
				}

				double correlation = Correlation(columns[a], columns[b], stds[a], stds[b]);
				if (Math.Abs(correlation) >= CorrelationThreshold)
				{
					warnings++;
					GD.Print($"AVISO: '{names[a]}' y '{names[b]}' están casi duplicadas (correlación {Format(correlation)}).");
				}
				else if (Math.Abs(correlation) >= HighCorrelationThreshold)
				{
					GD.Print($"INFO: '{names[a]}' y '{names[b]}' tienen correlación alta ({Format(correlation)}).");
				}
			}
		}

		GD.Print(warnings == 0 ? "Sin características constantes ni casi duplicadas." : $"{warnings} aviso(s) de características.");
	}

	private static double Correlation(double[] x, double[] y, double stdX, double stdY)
	{
		double meanX = 0.0;
		double meanY = 0.0;
		for (int i = 0; i < x.Length; i++)
		{
			meanX += x[i];
			meanY += y[i];
		}

		meanX /= x.Length;
		meanY /= y.Length;

		double covariance = 0.0;
		for (int i = 0; i < x.Length; i++)
		{
			covariance += (x[i] - meanX) * (y[i] - meanY);
		}

		return covariance / x.Length / (stdX * stdY);
	}

	private static string Format(double value)
	{
		return value.ToString("G5", CultureInfo.InvariantCulture);
	}
}
