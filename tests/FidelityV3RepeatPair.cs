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

/// <summary>
/// Diagnostico limitado a 600019, repeticion 1, tres veces, en un proceso.
/// Reutiliza sin modificar el metodo privado de juego real del arnes congelado.
/// No reproduce las 55 partidas anteriores ni evalua aceptacion.
/// </summary>
public partial class FidelityV3RepeatPair : FidelityV3Check
{
	private const int RequestedSeed = 600019;
	private const int BotRun = 1;
	private string _diagnosticOut;
	private bool _created;

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
			_diagnosticOut = path;
			_created = true;
			if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Solo headless.");
			using FidelityV3Protocol protocol = new();
			if (Engine.PhysicsTicksPerSecond != protocol.PhysicsTicks || Engine.TimeScale != 1)
				throw new InvalidOperationException("Paso de fisica o TimeScale distinto.");
			using ZipArchive archive = ZipFile.OpenRead("ai-training/reports/v3/development_20261031_01/raw_results.zip");
			using JsonDocument originalManifest = ReadJson(archive, "manifest.json");
			using JsonDocument originalPair = ReadJson(archive, "real_600019_1.json");
			var frozen = FidelityV3Protocol.Fingerprints();
			foreach (var property in originalManifest.RootElement.GetProperty("fingerprints").EnumerateObject())
			{
				string name = property.Name;
				if (name == "executed_assembly") continue; // El ensamblado incorpora esta escena diagnostica nueva.
				if (!frozen.TryGetValue(name, out string actual) || actual != property.Value.GetString())
					throw new InvalidOperationException($"Codigo o recurso original modificado: {name}");
			}
			GameplayParametersV3 parameters = GameplayParametersV3.FromScenes();
			DungeonGenerator generator = new(GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres"));
			// Mismo calentamiento de recursos que antes del primer combate del arnes original.
			var coverage = FidelityV3Protocol.Coverage(protocol, generator, parameters);
			if (coverage.Any(s => s.Errors.Count > 0)) throw new InvalidOperationException("Cobertura alterada.");
			var generated = generator.Generate(RequestedSeed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
			if (!generated.Success || generated.EffectiveSeed != originalPair.RootElement.GetProperty("effective_seed").GetInt32())
				throw new InvalidOperationException("Semilla efectiva distinta.");
			ulong botSeed = BotBrainV3.SeedFor(generated.EffectiveSeed, BotRun);
			if (botSeed.ToString() != originalPair.RootElement.GetProperty("bot_seed").GetString())
				throw new InvalidOperationException("Semilla del bot distinta.");
			SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
			BotSettings settings = new();
			MethodInfo playReal = typeof(FidelityV3Check).GetMethod("PlayReal", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException("No existe PlayReal en el arnes original.");
			FidelityV3Protocol.WriteNew(Path.Combine(path, "manifest.json"), new
			{
				DiagnosticOnly = true, RequestedSeed, generated.EffectiveSeed, BotRun, Attempts = 3, BotSeed = botSeed.ToString(),
				WorkerCount = 1, SameProcess = true, PriorGameplayPairs = 0, ProtocolSha256 = protocol.Hash,
				OriginalArchiveSha256 = FidelityV3Protocol.HashFile("ai-training/reports/v3/development_20261031_01/raw_results.zip"),
				Fingerprints = frozen, GameplayParameters = parameters.ToDictionary(), BotSettings = settings.ToDictionary(true),
				RealGameMethod = "FidelityV3Check.PlayReal (invocacion diagnostica por reflection, cuerpo sin cambios)",
				Limitation = "No recrea las 55 partidas previas ni fuerza GC, retencion de recursos o cambios del bot.",
			});
			for (int attempt = 0; attempt < 3; attempt++)
			{
				FidelityV3Protocol.AssertFrozen(frozen);
				Stopwatch clock = Stopwatch.StartNew();
				LevelSimulatorV3 simulator = new(parameters, level, new BotBrainV3(level, settings, botSeed));
				FidelityV3StallMonitor monitor = new(60, 0.1f);
				simulator.FrameCompleted += f =>
				{
					if (f.Frame % protocol.PhysicsTicks == 0 && monitor.Observe(f.Cell, f.Player,
						f.Enemies.Select((e, i) => new FidelityV3StallMonitor.Actor((ulong)i, e.Position, e.Health)).ToArray(),
						f.Frame * parameters.PhysicsDelta)) throw new InvalidOperationException("Posible atasco en simulador.");
				};
				SimulationResultV3 sim = simulator.Run(protocol.MaxSeconds);
				if (sim.TimedOut) throw new InvalidOperationException("Timeout en simulador; detenerse.");
				var task = (Task<SimulationResultV3>)playReal.Invoke(this, new object[]
					{ generated.Map, level, parameters, settings, botSeed, protocol.MaxSeconds });
				SimulationResultV3 real = await task;
				FidelityV3Protocol.WriteNew(Path.Combine(path, $"attempt_{attempt}.json"), new
				{
					Attempt = attempt, RequestedSeed, generated.EffectiveSeed, Run = BotRun, BotSeed = botSeed.ToString(),
					Sim = FidelityV3Protocol.ResultData(sim), Real = FidelityV3Protocol.ResultData(real),
					WallSeconds = clock.Elapsed.TotalSeconds,
				});
				if (real.TimedOut || real.Visits.Count != level.Rooms.Count || real.Visits.Any(v => v.ClearTime < v.EnterTime))
					throw new InvalidOperationException("Timeout o sala sin despejar; detenerse.");
				FidelityV3Protocol.AssertFrozen(frozen);
				GD.Print($"Repeticion diagnostica {attempt + 1}/3 de 600019:1: dano sim/real {sim.DamageTotal}/{real.DamageTotal}, " +
					$"tiempo sim/real {sim.TimeTotal:0.000}/{real.TimeTotal:0.000}; {clock.Elapsed.TotalSeconds:0.0} s de pared.");
			}
			using Process process = Process.GetCurrentProcess();
			FidelityV3Protocol.WriteNew(Path.Combine(path, "status.json"), new
			{
				Status = "three_diagnostic_attempts_complete", WallSeconds = wall.Elapsed.TotalSeconds, process.PeakWorkingSet64,
				DataSha256 = Directory.EnumerateFiles(path, "*.json").OrderBy(p => p, StringComparer.Ordinal)
					.ToDictionary(Path.GetFileName, FidelityV3Protocol.HashFile),
			});
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			if (_created) FidelityV3Protocol.WriteNew(Path.Combine(_diagnosticOut, "failure.json"), new
				{ Reason = error.Message, WallSeconds = wall.Elapsed.TotalSeconds });
			GetTree().Quit(1);
		}
	}

	private static JsonDocument ReadJson(ZipArchive archive, string name)
	{
		using Stream stream = archive.GetEntry(name)?.Open() ?? throw new InvalidDataException(name);
		return JsonDocument.Parse(stream);
	}
}
