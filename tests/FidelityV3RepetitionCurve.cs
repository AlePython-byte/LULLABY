using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

/// <summary>Curva diagnostica fija de 30 niveles x 10 repeticiones, sin cambiar el combate ni ejecutar aceptacion.</summary>
public partial class FidelityV3RepetitionCurve : FidelityV3Check
{
	private const string PlanPath = "ai-training/protocols/v3_repetition_curve_plan.json";
	private const string AnalyzerPath = "ai-training/validation/repetition_curve_v3.py";
	private string _curveOut;
	private bool _created;
	private int _activeSeed;
	private int _activeRun;
	private string _phase = "preflight";

	public override async void _Ready()
	{
		ProcessPhysicsPriority = 1000;
		Stopwatch wall = Stopwatch.StartNew();
		try
		{
			string[] args = OS.GetCmdlineUserArgs();
			if (args.Length != 1 || !args[0].StartsWith("--out-dir=")) throw new ArgumentException("Solo --out-dir=carpeta_nueva.");
			string path = Path.GetFullPath(args[0][10..]);
			if (!path.StartsWith(Path.GetFullPath("ai-training/reports/v3") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Salida fuera de informes v3.");
			if (Directory.Exists(path) || File.Exists(path)) throw new IOException("La salida ya existe.");
			Directory.CreateDirectory(path);
			_curveOut = path;
			_created = true;
			if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Solo headless.");
			using FidelityV3Protocol protocol = new();
			using JsonDocument plan = JsonDocument.Parse(File.ReadAllBytes(PlanPath));
			JsonElement config = plan.RootElement;
			ValidatePlan(config, protocol);
			if (Engine.PhysicsTicksPerSecond != protocol.PhysicsTicks || Engine.TimeScale != 1)
				throw new InvalidOperationException("Paso de fisica o TimeScale distinto.");
			using ZipArchive original = ZipFile.OpenRead("ai-training/reports/v3/development_20261031_01/raw_results.zip");
			using Stream originalStream = original.GetEntry("manifest.json").Open();
			using JsonDocument originalManifest = JsonDocument.Parse(originalStream);
			var frozen = Fingerprints();
			foreach (var property in originalManifest.RootElement.GetProperty("fingerprints").EnumerateObject())
			{
				if (property.Name == "executed_assembly") continue;
				if (!frozen.TryGetValue(property.Name, out string actual) || actual != property.Value.GetString())
					throw new InvalidOperationException($"Codigo o recurso original modificado: {property.Name}");
			}
			GameplayParametersV3 parameters = GameplayParametersV3.FromScenes();
			DungeonGenerator generator = new(GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres"));
			var coverage = DevelopmentCoverage(generator, parameters);
			FidelityV3Protocol.WriteNew(Path.Combine(path, "coverage.json"), new { Sample = coverage, CombatsExecuted = 0, ReserveUsed = false });
			if (coverage.Errors.Count > 0) throw new InvalidOperationException("Cobertura de desarrollo alterada.");
			BotSettings settings = new();
			FidelityV3Protocol.WriteNew(Path.Combine(path, "manifest.json"), new
			{
				SchemaVersion = 1, Mode = "development_repetition_curve_diagnostic", DiagnosticOnly = true,
				Plan = config, PlanSha256 = FidelityV3Protocol.HashFile(PlanPath), ProtocolSha256 = protocol.Hash,
				WorkerCount = 1, ConcurrentEnvironments = false, PhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
				BotPolicyVersion = BotBrainV3.PolicyVersion, BotSettings = settings.ToDictionary(true),
				GameplayParameters = parameters.ToDictionary(), Fingerprints = frozen,
				OriginalArchiveSha256 = FidelityV3Protocol.HashFile("ai-training/reports/v3/development_20261031_01/raw_results.zip"),
				RealGameMethod = "FidelityV3Check.PlayReal, cuerpo original sin cambios",
			});
			MethodInfo playReal = typeof(FidelityV3Check).GetMethod("PlayReal", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException("PlayReal");
			int completed = 0;
			for (int seed = 600001; seed <= 600030; seed++)
			{
				_activeSeed = seed;
				var generated = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
				if (!generated.Success) throw new InvalidOperationException("Generacion fallida.");
				SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
				FidelityV3Protocol.WriteNew(Path.Combine(path, $"level_{seed}.json"), new
					{ RequestedSeed = seed, generated.EffectiveSeed, Map = FidelityV3Protocol.MapData(level) });
				for (int run = 0; run < 10; run++)
				{
					_activeRun = run;
					AssertFrozen(frozen);
					ulong botSeed = BotBrainV3.SeedFor(generated.EffectiveSeed, run);
					_phase = "sim";
					GD.Print($"CURVE_BEGIN seed={seed} run={run} phase=sim");
					Stopwatch clock = Stopwatch.StartNew();
					LevelSimulatorV3 simulator = new(parameters, level, new BotBrainV3(level, settings, botSeed));
					FidelityV3StallMonitor monitor = new(60, 0.1f);
					simulator.FrameCompleted += frame =>
					{
						if (frame.Frame % protocol.PhysicsTicks == 0 && monitor.Observe(frame.Cell, frame.Player,
							frame.Enemies.Select((e, i) => new FidelityV3StallMonitor.Actor((ulong)i, e.Position, e.Health)).ToArray(),
							frame.Frame * parameters.PhysicsDelta)) throw new InvalidOperationException("Posible atasco en simulador.");
					};
					SimulationResultV3 sim = simulator.Run(protocol.MaxSeconds);
					double simSeconds = clock.Elapsed.TotalSeconds;
					FidelityV3Protocol.WriteNew(Path.Combine(path, $"sim_{seed}_{run}.json"), new
						{ RequestedSeed = seed, generated.EffectiveSeed, Run = run, BotSeed = botSeed.ToString(), Result = FidelityV3Protocol.ResultData(sim) });
					AssertCleared(sim, level);
					_phase = "real";
					GD.Print($"CURVE_BEGIN seed={seed} run={run} phase=real");
					clock.Restart();
					SimulationResultV3 real = await (Task<SimulationResultV3>)playReal.Invoke(this,
						new object[] { generated.Map, level, parameters, settings, botSeed, protocol.MaxSeconds });
					double realSeconds = clock.Elapsed.TotalSeconds;
					FidelityV3Protocol.WriteNew(Path.Combine(path, $"real_{seed}_{run}.json"), new
						{ RequestedSeed = seed, generated.EffectiveSeed, Run = run, BotSeed = botSeed.ToString(), Result = FidelityV3Protocol.ResultData(real) });
					FidelityV3Protocol.WriteNew(Path.Combine(path, $"resources_{seed}_{run}.json"), new
						{ SimSeconds = simSeconds, RealSeconds = realSeconds, WallSeconds = wall.Elapsed.TotalSeconds, Memory = Memory() });
					AssertCleared(real, level);
					AssertFrozen(frozen);
					completed++;
					_phase = "between_pairs";
					GD.Print($"CURVE_END pair={completed}/300 seed={seed} run={run} damage={sim.DamageTotal}/{real.DamageTotal} " +
						$"time={sim.TimeTotal:0.000}/{real.TimeTotal:0.000} wall={simSeconds + realSeconds:0.0}s");
				}
			}
			FidelityV3Protocol.WriteNew(Path.Combine(path, "status.json"), new
			{
				Status = "repetition_curve_complete_pending_analysis", CompletedPairs = completed, WallSeconds = wall.Elapsed.TotalSeconds,
				Memory = Memory(), DataSha256 = Directory.EnumerateFiles(path, "*.json").OrderBy(p => p, StringComparer.Ordinal)
					.ToDictionary(Path.GetFileName, FidelityV3Protocol.HashFile),
			});
			GD.Print($"CURVE_COMPLETE pairs={completed} wall={wall.Elapsed.TotalSeconds:0.0}s");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			if (_created) FidelityV3Protocol.WriteNew(Path.Combine(_curveOut, "failure.json"), new
				{ Reason = error.Message, Seed = _activeSeed, Run = _activeRun, Phase = _phase, WallSeconds = wall.Elapsed.TotalSeconds, Memory = Memory() });
			GetTree().Quit(1);
		}
	}

	private static void ValidatePlan(JsonElement plan, FidelityV3Protocol protocol)
	{
		if (plan.GetProperty("original_protocol_sha256").GetString() != protocol.Hash
			|| plan.GetProperty("first_seed").GetInt32() != 600001 || plan.GetProperty("level_count").GetInt32() != 30
			|| plan.GetProperty("runs_per_level_per_environment").GetInt32() != 10 || plan.GetProperty("worker_count").GetInt32() != 1
			|| plan.GetProperty("generator_version").GetInt32() != 3 || plan.GetProperty("simulator_version").GetInt32() != LevelSimulatorV3.Version
			|| plan.GetProperty("bot_policy_version").GetInt32() != BotBrainV3.PolicyVersion
			|| !plan.GetProperty("run_indices").EnumerateArray().Select(e => e.GetInt32()).SequenceEqual(Enumerable.Range(0, 10))
			|| !plan.GetProperty("mean_prefixes").EnumerateArray().Select(e => e.GetInt32()).SequenceEqual(new[] { 3, 5, 10 }))
			throw new InvalidDataException("Plan incompatible con la curva fija autorizada.");
	}

	private static FidelityV3Protocol.CoverageSample DevelopmentCoverage(DungeonGenerator generator, GameplayParametersV3 parameters)
	{
		FidelityV3Protocol.CoverageSample sample = new() { Name = "development", FirstSeed = 600001, Count = 30 };
		HashSet<int> seen = new();
		foreach (EnemyKinds.Info info in EnemyKinds.List) sample.LevelsByKind[info.Kind.ToString()] = 0;
		for (int seed = 600001; seed <= 600030; seed++)
		{
			var generated = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
			if (!generated.Success) { sample.Errors.Add($"No se genero {seed}."); continue; }
			if (!seen.Add(generated.EffectiveSeed)) sample.Errors.Add("Semilla efectiva repetida.");
			sample.EffectiveSeeds.Add(generated.EffectiveSeed);
			SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
			foreach (EnemyKind kind in level.Rooms.SelectMany(r => r.SpawnStats).Where(s => s != null).Select(s => s.Kind).Distinct())
				sample.LevelsByKind[kind.ToString()]++;
			sample.Signatures.Add(FidelityV3Protocol.HashBytes(JsonSerializer.SerializeToUtf8Bytes(FidelityV3Protocol.MapData(level), FidelityV3Protocol.JsonOptions)));
		}
		if (sample.LevelsByKind.Values.Any(count => count == 0)) sample.Errors.Add("Falta un tipo en desarrollo.");
		return sample;
	}

	private static SortedDictionary<string, string> Fingerprints()
	{
		var hashes = FidelityV3Protocol.Fingerprints();
		hashes[PlanPath] = FidelityV3Protocol.HashFile(PlanPath);
		hashes[AnalyzerPath] = FidelityV3Protocol.HashFile(AnalyzerPath);
		return hashes;
	}

	private static void AssertFrozen(SortedDictionary<string, string> frozen)
	{
		if (!frozen.SequenceEqual(Fingerprints())) throw new InvalidOperationException("Cambio de codigo, recursos o plan durante la curva.");
	}

	private static void AssertCleared(SimulationResultV3 result, SimLevelV3 level)
	{
		if (result.TimedOut || result.Visits.Count != level.Rooms.Count || result.Visits.Any(v => v.ClearTime < v.EnterTime)
			|| !result.Visits.Select(v => v.Cell).ToHashSet().SetEquals(level.Rooms.Select(r => r.Cell)))
			throw new InvalidOperationException("Timeout o sala sin visitar/despejar; detenerse.");
	}

	private static object Memory()
	{
		using Process process = Process.GetCurrentProcess();
		process.Refresh();
		return new { process.WorkingSet64, process.PeakWorkingSet64, process.PrivateMemorySize64 };
	}
}
