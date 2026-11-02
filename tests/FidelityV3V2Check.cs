using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

/// <summary>Lote de fidelidad v2. Preflight fijo de un par; aceptacion y reserva requieren autorizacion externa.</summary>
public partial class FidelityV3V2Check : FidelityV3Check
{
	private string _output;
	private int _seed;
	private int _run;
	private string _phase = "setup";
	private bool _created;
	private bool _integrityVerified;
	private sealed class QualityFailure : Exception { public QualityFailure(string message) : base(message) { } }

	public override async void _Ready()
	{
		ProcessPhysicsPriority = 1000;
		GD.Print("FIDELITY_V2_BEGIN orchestration=2");
		Stopwatch wall = Stopwatch.StartNew();
		try
		{
			if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Solo headless.");
			Dictionary<string, string> arguments = new(StringComparer.Ordinal);
			foreach (string argument in OS.GetCmdlineUserArgs())
			{
				int separator = argument.IndexOf('=');
				if (separator < 0 || !arguments.TryAdd(argument[..separator], argument[(separator + 1)..]))
					throw new ArgumentException("Argumento repetido o sin valor.");
			}
			if (arguments.Keys.Any(k => !new[] { "--mode", "--out-dir", "--first-seed", "--level-count", "--authorization-file" }.Contains(k)))
				throw new ArgumentException("Argumento desconocido.");
			string mode = arguments.GetValueOrDefault("--mode", "preflight");
			using FidelityV3V2Protocol protocol = new();
			var sample = protocol.Sample(mode);
			int first = int.Parse(arguments.GetValueOrDefault("--first-seed", sample.First.ToString()));
			int count = int.Parse(arguments.GetValueOrDefault("--level-count", (mode == "preflight" ? 1 : 5).ToString()));
			int runs = mode == "preflight" ? 1 : protocol.Runs;
			string authorization = arguments.GetValueOrDefault("--authorization-file", "");
			string authorizationHash = mode == "preflight" ? null : FidelityV3V2Protocol.CheckAuthorization(authorization, mode);
			if ((mode == "preflight" && (first != 600001 || count != 1 || authorization != ""))
				|| (mode != "preflight" && (count != 5 || first < sample.First || first > sample.First + sample.Count - count || (first - sample.First) % 5 != 0)))
				throw new ArgumentException("Rango no autorizado para este modo.");
			if (Engine.PhysicsTicksPerSecond != 60 || Engine.TimeScale != 1)
				throw new InvalidDataException("Paso de fisica o TimeScale incompatible.");
			if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")))
				throw new InvalidDataException("La variable DOTNET_TieredCompilation debe estar ausente.");
			_output = Path.GetFullPath(arguments["--out-dir"]);
			if (!_output.StartsWith(Path.GetFullPath("ai-training/reports/v3") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
				|| Directory.Exists(_output) || File.Exists(_output)) throw new IOException("Se requiere una salida nueva dentro de informes v3.");
			var frozen = protocol.Fingerprints(authorization);
			protocol.AssertMeasurementCore(frozen);
			_integrityVerified = true;
			Directory.CreateDirectory(_output);
			_created = true;
			GameplayParametersV3 parameters = GameplayParametersV3.FromScenes();
			BotSettings settings = new();
			DungeonGenerator generator = new(GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres"));
			if (mode == "preflight")
				FidelityV3Protocol.WriteNew(Path.Combine(_output, "coverage.json"), new { Mode = mode, CombatsExecuted = 0, AllSampleCoverageExecuted = false, ReserveUsed = false });
			else
			{
				// Mismas muestras y reglas de cobertura que el original; no se ejecutan combates en esta comprobacion.
				using FidelityV3Protocol original = new();
				var coverage = FidelityV3Protocol.Coverage(original, generator, parameters);
				FidelityV3Protocol.WriteNew(Path.Combine(_output, "coverage.json"), new { Mode = mode, Samples = coverage, CombatsExecuted = 0 });
				if (coverage.Any(c => c.Errors.Count != 0)) throw new QualityFailure("Cobertura insuficiente o semillas efectivas solapadas.");
			}
			FidelityV3Protocol.WriteNew(Path.Combine(_output, "manifest.json"), new
			{
				SchemaVersion = 2, Mode = mode, Protocol = protocol.Root, ProtocolSha256 = FidelityV3V2Protocol.RegisteredHash,
				WorkerCount = 1, ConcurrentEnvironments = false, BatchFirstSeed = first, BatchLevelCount = count, Runs = runs,
				DiagnosticOnly = mode == "preflight", PhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond, TimeScale = Engine.TimeScale,
				Godot = Engine.GetVersionInfo()["string"].AsString(), DotnetRuntime = System.Environment.Version.ToString(),
				BotPolicyVersion = BotBrainV3.PolicyVersion, BotSettings = settings.ToDictionary(true), GameplayParameters = parameters.ToDictionary(),
				Fingerprints = frozen, AuthorizationSha256 = authorizationHash, TieredCompilationEnvironment = "unset",
				RealGameMethod = "FidelityV3Check.PlayReal, cuerpo original sin cambios",
				AcceptanceAuthorizedByPreflight = false,
			});
			MethodInfo playReal = typeof(FidelityV3Check).GetMethod("PlayReal", BindingFlags.NonPublic | BindingFlags.Instance)
				?? throw new MissingMethodException("PlayReal");
			int completed = 0;
			for (int seed = first; seed < first + count; seed++)
			{
				_seed = seed;
				var generated = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
				if (!generated.Success) throw new QualityFailure("Generacion fallida.");
				SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
				FidelityV3Protocol.WriteNew(Path.Combine(_output, $"level_{seed}.json"), new { RequestedSeed = seed, generated.EffectiveSeed, Map = FidelityV3Protocol.MapData(level) });
				for (int run = 0; run < runs; run++)
				{
					_run = run;
					protocol.AssertFrozen(frozen, authorization);
					ulong botSeed = BotBrainV3.SeedFor(generated.EffectiveSeed, run);
					_phase = "sim";
					GD.Print($"V2_PAIR_BEGIN seed={seed} run={run} phase=sim");
					Stopwatch clock = Stopwatch.StartNew();
					LevelSimulatorV3 simulator = new(parameters, level, new BotBrainV3(level, settings, botSeed));
					FidelityV3StallMonitor monitor = new(60, 0.1f);
					simulator.FrameCompleted += frame =>
					{
						if (frame.Frame % 60 == 0 && monitor.Observe(frame.Cell, frame.Player,
							frame.Enemies.Select((e, i) => new FidelityV3StallMonitor.Actor((ulong)i, e.Position, e.Health)).ToArray(), frame.Frame * parameters.PhysicsDelta))
							throw new QualityFailure("Posible atasco estatico en simulador.");
					};
					SimulationResultV3 sim = simulator.Run(protocol.MaxSeconds);
					double simSeconds = clock.Elapsed.TotalSeconds;
					FidelityV3Protocol.WriteNew(Path.Combine(_output, $"sim_{seed}_{run}.json"), new { RequestedSeed = seed, generated.EffectiveSeed, Run = run, BotSeed = botSeed.ToString(), Result = FidelityV3Protocol.ResultData(sim) });
					AssertCleared(sim, level);
					_phase = "real";
					GD.Print($"V2_PAIR_BEGIN seed={seed} run={run} phase=real");
					clock.Restart();
					SimulationResultV3 real = await (Task<SimulationResultV3>)playReal.Invoke(this,
						new object[] { generated.Map, level, parameters, settings, botSeed, protocol.MaxSeconds });
					double realSeconds = clock.Elapsed.TotalSeconds;
					FidelityV3Protocol.WriteNew(Path.Combine(_output, $"real_{seed}_{run}.json"), new { RequestedSeed = seed, generated.EffectiveSeed, Run = run, BotSeed = botSeed.ToString(), Result = FidelityV3Protocol.ResultData(real) });
					AssertCleared(real, level);
					FidelityV3Protocol.WriteNew(Path.Combine(_output, $"resources_{seed}_{run}.json"), new { SimSeconds = simSeconds, RealSeconds = realSeconds, WallSeconds = wall.Elapsed.TotalSeconds, Memory = Memory() });
					protocol.AssertFrozen(frozen, authorization);
					completed++;
					_phase = "between_pairs";
					GD.Print($"V2_PAIR_END seed={seed} run={run} pairs={completed} damage={sim.DamageTotal}/{real.DamageTotal} time={sim.TimeTotal}/{real.TimeTotal}");
				}
			}
			FidelityV3Protocol.WriteNew(Path.Combine(_output, "status.json"), new
			{
				Status = "batch_complete_pending_log_validation", CompletedPairs = completed, WallSeconds = wall.Elapsed.TotalSeconds, Memory = Memory(),
				DataSha256 = Directory.EnumerateFiles(_output, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToDictionary(Path.GetFileName, FidelityV3Protocol.HashFile),
			});
			GD.Print($"V2_BATCH_COMPLETE mode={mode} pairs={completed}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			Exception root = error.GetBaseException();
			bool quality = root is QualityFailure || new[] { "atasco", "timeout", "salas sin", "incompleta" }.Any(s => root.Message.Contains(s, StringComparison.OrdinalIgnoreCase));
			bool integrity = !_integrityVerified || root is InvalidDataException;
			GD.Print(quality ? "V2_QUALITY_FAILURE" : integrity ? "V2_INTEGRITY_FAILURE" : "V2_RUNTIME_FAILURE");
			GD.PushError(error.ToString());
			if (_created) FidelityV3Protocol.WriteNew(Path.Combine(_output, "failure.json"), new { Reason = root.Message, Seed = _seed, Run = _run, Phase = _phase, QualityFailure = quality, IntegrityFailure = integrity });
			GetTree().Quit(1);
		}
	}

	private static void AssertCleared(SimulationResultV3 result, SimLevelV3 level)
	{
		if (result.TimedOut || result.Visits.Count != level.Rooms.Count || result.Visits.Any(v => v.ClearTime < v.EnterTime)
			|| !result.Visits.Select(v => v.Cell).ToHashSet().SetEquals(level.Rooms.Select(r => r.Cell)))
			throw new QualityFailure("Timeout o salas sin visitar/despejar.");
	}

	private static object Memory()
	{
		using Process process = Process.GetCurrentProcess();
		process.Refresh();
		return new { process.WorkingSet64, process.PeakWorkingSet64, process.PrivateMemorySize64 };
	}
}
