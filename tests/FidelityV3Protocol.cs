using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

/// <summary>Protocolo y procedencia de la fidelidad v3; no modifica los criterios registrados.</summary>
public sealed class FidelityV3Protocol : IDisposable
{
	public const string ProtocolPath = "ai-training/protocols/v3_fidelity.json";
	public const string RegisteredHash = "91d0239fad57636476455b66eb06d49a1326b731d6777b2a166fa36aafb1e220";
	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true,
	};
	private readonly JsonDocument _document;
	public JsonElement Root => _document.RootElement;
	public int Runs => Root.GetProperty("runs_per_level_per_environment").GetInt32();
	public float MaxSeconds => Root.GetProperty("max_seconds_per_run").GetSingle();
	public int PhysicsTicks => Root.GetProperty("physics_ticks_per_second").GetInt32();
	public string Hash { get; }

	public FidelityV3Protocol()
	{
		byte[] bytes = File.ReadAllBytes(ProtocolPath);
		Hash = HashBytes(bytes);
		if (Hash != RegisteredHash) throw new InvalidDataException("El protocolo difiere del registrado; detenerse antes de combates.");
		_document = JsonDocument.Parse(bytes);
		if (Root.GetProperty("generator_version").GetInt32() != 3
			|| Root.GetProperty("simulator_version").GetInt32() != LevelSimulatorV3.Version)
			throw new InvalidDataException("Versiones incompatibles con el protocolo.");
	}

	public (int FirstSeed, int Count) Sample(string name)
	{
		JsonElement sample = Root.GetProperty("samples").GetProperty(name);
		return (sample.GetProperty("first_seed").GetInt32(), sample.GetProperty("count").GetInt32());
	}

	public void Dispose() => _document.Dispose();
	public static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	public static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));

	public static void WriteNew(string path, object payload)
	{
		string json = JsonSerializer.Serialize(payload, JsonOptions).Replace("\r\n", "\n");
		byte[] bytes = new UTF8Encoding(false).GetBytes(json + "\n");
		using FileStream stream = new(path, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.Read);
		stream.Write(bytes);
		stream.Flush(true);
	}

	public static string[] Categories(GameplayParametersV3.EnemyStats[] stats)
	{
		if (stats.Length == 0 || stats.All(s => s == null)) return Array.Empty<string>();
		HashSet<EnemyKind> kinds = stats.Where(s => s != null).Select(s => s.Kind).ToHashSet();
		List<string> result = new();
		if (kinds.SetEquals(new[] { EnemyKind.Chaser })) result.Add("basic_only");
		foreach (var (kind, category) in new[] {
			(EnemyKind.Shooter, "with_shooter"), (EnemyKind.Exploder, "with_exploder"),
			(EnemyKind.Flyer, "with_flyer"), (EnemyKind.Runner, "with_runner"), (EnemyKind.Digger, "with_digger") })
			if (kinds.Contains(kind)) result.Add(category);
		return result.ToArray();
	}

	public static object MapData(SimLevelV3 level)
	{
		return new
		{
			Start = new[] { level.StartCell.X, level.StartCell.Y }, Boss = new[] { level.BossCell.X, level.BossCell.Y },
			Rooms = level.Rooms.OrderBy(r => r.Cell.X).ThenBy(r => r.Cell.Y).Select(r => new
			{
				Cell = new[] { r.Cell.X, r.Cell.Y }, Type = r.Type.ToString(), r.TemplateId,
				Size = new[] { r.Size.X, r.Size.Y },
				Spawns = r.SpawnPoints.Select((p, i) => new { Position = new[] { p.X, p.Y }, Kind = r.SpawnStats[i]?.Kind.ToString() }).ToArray(),
				Categories = Categories(r.SpawnStats),
			}).ToArray(),
		};
	}

	public static object ResultData(SimulationResultV3 result)
	{
		return new
		{
			result.DamageTotal, result.DamageCritical, result.TimeTotal, result.TimeToBoss, result.TimedOut, result.WouldDie,
			result.DamageBySource, result.HitsBySource,
			Visits = result.Visits.Select(v => new
			{
				Cell = new[] { v.Cell.X, v.Cell.Y }, v.EnterTime, v.ClearTime, v.Damage, v.DamageBySource, v.ContactByKind,
			}).ToArray(),
		};
	}

	/// <summary>Codigo, escenas y recursos que pueden cambiar el experimento, mas la asamblea ejecutada.</summary>
	public static SortedDictionary<string, string> Fingerprints()
	{
		SortedDictionary<string, string> hashes = new(StringComparer.Ordinal);
		foreach (string folder in new[] { "scripts", "scenes", "resources" })
			foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
				.Where(p => new[] { ".cs", ".tscn", ".tres", ".json" }.Contains(Path.GetExtension(p))))
				hashes[path.Replace('\\', '/')] = HashFile(path);
		foreach (string path in Directory.EnumerateFiles("tests", "FidelityV3*.cs"))
			hashes[path.Replace('\\', '/')] = HashFile(path);
		foreach (string path in new[] { "project.godot", "tests/SampleStatistics.cs", ProtocolPath,
			"tests/HistoricalIntegrityTest.cs", "ai-training/validation/v3_fidelity.py" })
			if (File.Exists(path)) hashes[path] = HashFile(path);
		var assembly = typeof(FidelityV3Protocol).Assembly;
		string assemblyPath = assembly.Location;
		// Godot puede cargar el ensamblado desde memoria y dejar Location vacio.
		if (string.IsNullOrEmpty(assemblyPath)) assemblyPath = ProjectSettings.GlobalizePath(
			$"res://.godot/mono/temp/bin/{(OS.IsDebugBuild() ? "Debug" : "Release")}/{assembly.GetName().Name}.dll");
		using (var stream = File.OpenRead(assemblyPath))
		using (PEReader reader = new(stream))
		{
			var metadata = reader.GetMetadataReader();
			if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.ManifestModule.ModuleVersionId)
				throw new InvalidDataException("El ensamblado cargado difiere del compilado en disco.");
		}
		hashes["executed_assembly"] = HashFile(assemblyPath);
		return hashes;
	}

	public static void AssertFrozen(SortedDictionary<string, string> expected)
	{
		if (!expected.SequenceEqual(Fingerprints())) throw new InvalidDataException("Cambio de codigo, escena, recurso o ensamblado durante el experimento.");
	}

	public sealed class CoverageSample
	{
		public string Name { get; init; }
		public int FirstSeed { get; init; }
		public int Count { get; init; }
		public SortedDictionary<string, int> LevelsByKind { get; } = new();
		public List<int> EffectiveSeeds { get; } = new();
		public List<string> Signatures { get; } = new();
		public List<string> Errors { get; } = new();
	}

	public static List<CoverageSample> Coverage(FidelityV3Protocol protocol, DungeonGenerator generator, GameplayParametersV3 parameters)
	{
		HashSet<int> allSeeds = new();
		List<CoverageSample> samples = new();
		foreach (string name in new[] { "development", "acceptance", "reserve" })
		{
			var range = protocol.Sample(name);
			CoverageSample sample = new() { Name = name, FirstSeed = range.FirstSeed, Count = range.Count };
			foreach (EnemyKinds.Info info in EnemyKinds.List) sample.LevelsByKind[info.Kind.ToString()] = 0;
			for (int seed = range.FirstSeed; seed < range.FirstSeed + range.Count; seed++)
			{
				var generated = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
				if (!generated.Success) { sample.Errors.Add($"No se genero semilla {seed}."); continue; }
				if (!allSeeds.Add(generated.EffectiveSeed)) sample.Errors.Add($"Semilla efectiva repetida: {generated.EffectiveSeed}.");
				sample.EffectiveSeeds.Add(generated.EffectiveSeed);
				SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
				foreach (EnemyKind kind in level.Rooms.SelectMany(r => r.SpawnStats).Where(s => s != null).Select(s => s.Kind).Distinct())
					sample.LevelsByKind[kind.ToString()]++;
				sample.Signatures.Add(HashBytes(JsonSerializer.SerializeToUtf8Bytes(MapData(level), JsonOptions)));
			}
			if (sample.LevelsByKind.Values.Any(count => count == 0)) sample.Errors.Add("No estan presentes los seis tipos.");
			if (name == "acceptance" && (sample.LevelsByKind["Runner"] < 10 || sample.LevelsByKind["Digger"] < 10))
				sample.Errors.Add("Aceptacion requiere 10 niveles con Runner y 10 con Digger.");
			samples.Add(sample);
		}
		return samples;
	}
}
