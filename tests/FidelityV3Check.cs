using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Fidelidad v3 secuencial. Por defecto solo cobertura, sin combates.
/// --mode=coverage|pilot|development --out-dir=ai-training/reports/v3/NOMBRE_NUEVO
/// Piloto: primera semilla de desarrollo, repeticion 0. Desarrollo: 30 x 3.
/// Aceptacion y reserva no son modos disponibles: requieren revisar desarrollo primero.
/// </summary>
public partial class FidelityV3Check : Node
{
	private string _mode = "coverage";
	private string _out;
	private bool _outputCreated;
	private readonly Stopwatch _wall = new();
	private FidelityV3StallMonitor _monitor;
	private TaskCompletionSource<string> _stall;
	private RoomManager _manager;
	private int _realFrame;
	private object _lastReal;

	public override async void _Ready()
	{
		ProcessPhysicsPriority = 1000;
		_wall.Start();
		try
		{
			ParseArguments();
			if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Esta herramienta solo se ejecuta en headless.");
			using FidelityV3Protocol protocol = new();
			if (Engine.PhysicsTicksPerSecond != protocol.PhysicsTicks)
				throw new InvalidDataException("El paso de fisica no coincide con el protocolo.");
			if (Engine.TimeScale != 1.0) throw new InvalidDataException("No se permite cambiar TimeScale.");
			GameplayParametersV3 parameters = GameplayParametersV3.FromScenes();
			DungeonGenerator generator = new(GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres"));
			var frozen = FidelityV3Protocol.Fingerprints();
			var coverage = FidelityV3Protocol.Coverage(protocol, generator, parameters);
			FidelityV3Protocol.WriteNew(Path.Combine(_out, "coverage.json"), new
			{
				ProtocolSha256 = protocol.Hash, Samples = coverage, CombatsExecuted = 0,
				CrossSampleSignatureDuplicates = coverage.SelectMany(s => s.Signatures).GroupBy(s => s).Count(g => g.Count() > 1),
			});
			if (coverage.Any(s => s.Errors.Count > 0)) throw new InvalidDataException("Fallo de cobertura; no ejecutar combates ni seleccionar otras semillas.");
			foreach (var sample in coverage)
				GD.Print($"Cobertura {sample.Name}: " + string.Join(", ", sample.LevelsByKind.Select(kv => $"{kv.Key}={kv.Value}")));
			if (_mode == "coverage")
			{
				WriteStatus("coverage_complete", 0);
				GetTree().Quit();
				return;
			}

			BotSettings settings = new();
			FidelityV3Protocol.WriteNew(Path.Combine(_out, "manifest.json"), new
			{
				SchemaVersion = 1, Mode = _mode, ProtocolSha256 = protocol.Hash, Protocol = protocol.Root,
				WorkerCount = 1, ConcurrentEnvironments = false, PhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
				Godot = Engine.GetVersionInfo()["string"].AsString(), Runtime = System.Environment.Version.ToString(),
				BotPolicyVersion = BotBrainV3.PolicyVersion, BotSettings = settings.ToDictionary(true),
				GameplayParameters = parameters.ToDictionary(), Fingerprints = frozen,
				ExecutionPolicy = new { StaticStallSeconds = 60, PositionToleranceMeters = 0.1,
					StopOnFirstTimeout = true, StopOnFirstSuspectedStall = true, PreservePartialResults = true },
			});
			int completed = 0;
			var range = protocol.Sample("development");
			int count = _mode == "pilot" ? 1 : range.Count;
			int runs = _mode == "pilot" ? 1 : protocol.Runs;
			for (int seed = range.FirstSeed; seed < range.FirstSeed + count; seed++)
			{
				var generated = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
				if (!generated.Success) throw new InvalidDataException($"Generacion fallida: {seed}.");
				SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
				FidelityV3Protocol.WriteNew(Path.Combine(_out, $"level_{seed}.json"), new
				{
					RequestedSeed = seed, generated.EffectiveSeed, Map = FidelityV3Protocol.MapData(level),
				});
				for (int run = 0; run < runs; run++)
				{
					FidelityV3Protocol.AssertFrozen(frozen);
					ulong botSeed = BotBrainV3.SeedFor(generated.EffectiveSeed, run);
					Stopwatch clock = Stopwatch.StartNew();
					LevelSimulatorV3 simulator = new(parameters, level, new BotBrainV3(level, settings, botSeed));
					FidelityV3StallMonitor simMonitor = new(60, 0.1f);
					SimulationFrameV3 lastSim = null;
					simulator.FrameCompleted += frame =>
					{
						if (frame.Frame % protocol.PhysicsTicks != 0) return;
						lastSim = frame;
						if (simMonitor.Observe(frame.Cell, frame.Player,
							frame.Enemies.Select((e, i) => new FidelityV3StallMonitor.Actor((ulong)i, e.Position, e.Health)).ToArray(),
							frame.Frame * parameters.PhysicsDelta))
							throw new InvalidOperationException("Posible atasco estatico en simulador; detenerse y revisar.");
					};
					SimulationResultV3 simulated;
					try { simulated = simulator.Run(protocol.MaxSeconds); }
					catch
					{
						FidelityV3Protocol.WriteNew(Path.Combine(_out, $"stall_sim_{seed}_{run}.json"), Snapshot(lastSim));
						throw;
					}
					double simSeconds = clock.Elapsed.TotalSeconds;
					FidelityV3Protocol.WriteNew(Path.Combine(_out, $"sim_{seed}_{run}.json"), new
					{
						RequestedSeed = seed, generated.EffectiveSeed, Run = run, BotSeed = botSeed.ToString(),
						Result = FidelityV3Protocol.ResultData(simulated),
					});
					if (simulated.TimedOut) throw new InvalidOperationException($"Timeout en simulador {seed}, repeticion {run}; no continuar.");
					AssertCleared(simulated, level, "simulador");
					clock.Restart();
					SimulationResultV3 real = await PlayReal(generated.Map, level, parameters, settings, botSeed, protocol.MaxSeconds);
					double realSeconds = clock.Elapsed.TotalSeconds;
					FidelityV3Protocol.WriteNew(Path.Combine(_out, $"real_{seed}_{run}.json"), new
					{
						RequestedSeed = seed, generated.EffectiveSeed, Run = run, BotSeed = botSeed.ToString(),
						Result = FidelityV3Protocol.ResultData(real),
					});
					FidelityV3Protocol.WriteNew(Path.Combine(_out, $"resources_{seed}_{run}.json"), new
					{
						SimSeconds = simSeconds, RealSeconds = realSeconds, WallSeconds = _wall.Elapsed.TotalSeconds,
						Memory = Memory(),
					});
					if (real.TimedOut) throw new InvalidOperationException($"Timeout real {seed}, repeticion {run}; no continuar.");
					AssertCleared(real, level, "juego real");
					FidelityV3Protocol.AssertFrozen(frozen);
					completed++;
					GD.Print($"Par {completed}/{count * runs}: semilla {seed}, repeticion {run}; " +
						$"dano sim/real {simulated.DamageTotal:0.0}/{real.DamageTotal:0.0}; " +
						$"tiempo sim/real {simulated.TimeTotal:0.0}/{real.TimeTotal:0.0}; pared {simSeconds + realSeconds:0.0} s.");
				}
			}
			WriteStatus(_mode == "pilot" ? "pilot_complete_not_a_quality_gate" : "development_complete_pending_analysis", completed);
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			if (_outputCreated && !File.Exists(Path.Combine(_out, "failure.json")))
				FidelityV3Protocol.WriteNew(Path.Combine(_out, "failure.json"), new
				{
					Status = "aborted_requires_review", Reason = error.Message, WallSeconds = _wall.Elapsed.TotalSeconds,
					LastReal = _lastReal, Memory = Memory(),
				});
			GetTree().Quit(1);
		}
	}

	private void ParseArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--mode=")) _mode = argument[7..];
			else if (argument.StartsWith("--out-dir=")) _out = argument[10..];
			else throw new ArgumentException($"Argumento no soportado: {argument}");
		}
		if (!new[] { "coverage", "pilot", "development" }.Contains(_mode))
			throw new ArgumentException("Solo cobertura, piloto o desarrollo. Aceptacion/reserva requieren revisar desarrollo.");
		if (string.IsNullOrWhiteSpace(_out)) throw new ArgumentException("Falta --out-dir con una carpeta nueva.");
		_out = Path.GetFullPath(_out);
		string allowed = Path.GetFullPath("ai-training/reports/v3") + Path.DirectorySeparatorChar;
		if (!_out.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("La salida debe estar dentro de ai-training/reports/v3/.");
		if (Directory.Exists(_out) || File.Exists(_out)) throw new IOException("La salida ya existe; no sobrescribir informes.");
		Directory.CreateDirectory(_out);
		_outputCreated = true;
	}

	private async Task<SimulationResultV3> PlayReal(LevelMap map, SimLevelV3 level, GameplayParametersV3 parameters,
		BotSettings settings, ulong seed, float maxSeconds)
	{
		RunManager.SetNextMap(map);
		Node realLevel = GD.Load<PackedScene>(GameplayParametersV3.LevelScenePath).Instantiate();
		RoomManager manager = realLevel.GetNode<RoomManager>("RoomManager");
		manager.BossScene = null;
		manager.DecorEnabled = false;
		realLevel.GetNode<TreasureItemSpawner>("TreasureItemSpawner").Enabled = false;
		HealthComponent.FindIn(realLevel.GetNode("Player")).MaxHealth = 1.0e9f;
		BotDriverV3 driver = new();
		try
		{
			AddChild(realLevel);
			if (manager.CurrentRoom == null) throw new InvalidOperationException("No se pudo cargar la sala real; detenerse.");
			AddChild(driver);
			TaskCompletionSource<SimulationResultV3> finished = new();
			driver.Finished += result => finished.TrySetResult(result);
			_manager = manager;
			_monitor = new FidelityV3StallMonitor(60, 0.1f);
			_stall = new TaskCompletionSource<string>();
			_realFrame = 0;
			driver.Begin(manager, level, new BotBrainV3(level, settings, seed), parameters.PlayerMaxHealth, maxSeconds);
			Task winner = await Task.WhenAny(finished.Task, _stall.Task);
			if (winner == _stall.Task) throw new InvalidOperationException(await _stall.Task);
			return await finished.Task;
		}
		finally
		{
			_monitor = null;
			_manager = null;
			if (IsInstanceValid(driver)) driver.QueueFree();
			if (IsInstanceValid(realLevel)) realLevel.QueueFree();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_monitor == null || _manager?.CurrentRoom == null) return;
		_realFrame++;
		if (_realFrame % Engine.PhysicsTicksPerSecond != 0) return;
		RoomController room = _manager.CurrentRoom;
		EnemyBase[] enemies = room.GetNode("Enemies").GetChildren().OfType<EnemyBase>()
			.Where(e => IsInstanceValid(e) && !e.IsQueuedForDeletion()).ToArray();
		Vector3 p = _manager.Player.GlobalPosition;
		var actors = enemies.Select(e => new FidelityV3StallMonitor.Actor(e.GetInstanceId(),
			new Vector2(e.GlobalPosition.X, e.GlobalPosition.Z), e.Health.CurrentHealth)).ToArray();
		_lastReal = new
		{
			Cell = new[] { room.Data.Cell.X, room.Data.Cell.Y }, State = room.State.ToString(),
			Player = new[] { p.X, p.Z }, Frame = _realFrame,
			Enemies = enemies.Select(e => new
			{
				Kind = e.Kind.ToString(), Position = new[] { e.GlobalPosition.X, e.GlobalPosition.Z },
				Health = e.Health.CurrentHealth, DashState = e.Behavior<DashBehavior>()?.State.ToString(),
				DigPhase = e.Behavior<DigBehavior>()?.Phase.ToString(),
			}).ToArray(),
		};
		if (_monitor.Observe(room.Data.Cell, new Vector2(p.X, p.Z), actors, _realFrame * delta))
			_stall.TrySetResult("Posible atasco estatico real durante 60 s; detenerse y revisar.");
	}

	private static object Snapshot(SimulationFrameV3 frame) => frame == null ? null : new
	{
		frame.Frame, Cell = new[] { frame.Cell.X, frame.Cell.Y }, Player = new[] { frame.Player.X, frame.Player.Y },
		Enemies = frame.Enemies.Select(e => new
		{
			Kind = e.Kind.ToString(), Position = new[] { e.Position.X, e.Position.Y }, e.Health, e.RecoilFrames,
			DashState = e.DashState.ToString(), DigPhase = e.DigPhase.ToString(), e.DigTime, e.ForcedEmerge,
		}).ToArray(),
	};

	private static object Memory()
	{
		using Process process = Process.GetCurrentProcess();
		process.Refresh();
		return new { process.WorkingSet64, process.PeakWorkingSet64, process.PrivateMemorySize64 };
	}

	private static void AssertCleared(SimulationResultV3 result, SimLevelV3 level, string environment)
	{
		if (result.Visits.Count != level.Rooms.Count || result.Visits.Any(v => v.ClearTime < v.EnterTime)
			|| !result.Visits.Select(v => v.Cell).ToHashSet().SetEquals(level.Rooms.Select(r => r.Cell)))
			throw new InvalidOperationException($"Partida incompleta en {environment}: salas sin visitar o despejar. Detenerse y revisar.");
	}

	private void WriteStatus(string status, int pairs)
	{
		FidelityV3Protocol.WriteNew(Path.Combine(_out, "status.json"), new
		{
			Status = status, CompletedPairs = pairs, WallSeconds = _wall.Elapsed.TotalSeconds, Memory = Memory(),
			DataSha256 = Directory.EnumerateFiles(_out, "*.json").OrderBy(p => p, StringComparer.Ordinal)
				.ToDictionary(p => Path.GetFileName(p), FidelityV3Protocol.HashFile),
		});
		GD.Print($"FidelityV3Check: {status}; {pairs} pares; {_wall.Elapsed.TotalSeconds:0.0} s de pared.");
	}
}
