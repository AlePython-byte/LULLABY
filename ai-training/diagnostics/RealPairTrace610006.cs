using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;

/// <summary>Una partida real 610006:0; observacion separada, sin cambios al metodo original.</summary>
public partial class RealPairTrace610006 : FidelityV3Check
{
	private static readonly FieldInfo ManagerField = typeof(FidelityV3Check).GetField("_manager", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo FrameField = typeof(FidelityV3Check).GetField("_realFrame", BindingFlags.Instance | BindingFlags.NonPublic);
	private StreamWriter _trace;
	private Vector2I? _cell;
	private Vector3 _previous;
	private Vector3 _windowStart;
	private double _path;
	private int _lastSample;
	private string _output;
	private bool _created;
	private readonly Stopwatch _wall = new();
	private object _lastSnapshot;

	public override async void _Ready()
	{
		ProcessPhysicsPriority = 1000;
		_wall.Start();
		try
		{
			string[] args = OS.GetCmdlineUserArgs();
			if (args.Length != 1 || !args[0].StartsWith("--out-dir=")) throw new ArgumentException("Solo --out-dir=carpeta_nueva.");
			_output = Path.GetFullPath(args[0][10..]);
			if (!_output.StartsWith(Path.GetFullPath("ai-training/reports/v3") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
				|| Directory.Exists(_output) || File.Exists(_output)) throw new IOException("Salida nueva dentro de informes v3 requerida.");
			if (DisplayServer.GetName() != "headless" || Engine.PhysicsTicksPerSecond != 60 || Engine.TimeScale != 1)
				throw new InvalidOperationException("Solo headless, fisica 60 y TimeScale 1.");
			if (System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != null
				|| System.Environment.GetEnvironmentVariable("DOTNET_gcConcurrent") != null)
				throw new InvalidOperationException("Variables de compilacion escalonada y GC concurrente deben estar ausentes.");
			using FidelityV3V2Protocol protocol = new();
			var frozen = protocol.Fingerprints("");
			protocol.AssertMeasurementCore(frozen);
			string reference = "ai-training/reports/v3/acceptance_v2_20261101_02/execution/batch_610006_attempt_1";
			using JsonDocument previous = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reference, "manifest.json")));
			foreach (var property in previous.RootElement.GetProperty("fingerprints").EnumerateObject())
			{
				if (property.Name is "executed_assembly" or "authorization_file") continue;
				if (!frozen.TryGetValue(property.Name, out string value) || value != property.Value.GetString())
					throw new InvalidDataException("Fuente o recurso distinto de aceptacion: " + property.Name);
			}
			Directory.CreateDirectory(_output);
			_created = true;
			_trace = new StreamWriter(Path.Combine(_output, "trace.jsonl"), false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
			GameplayParametersV3 parameters = GameplayParametersV3.FromScenes();
			BotSettings settings = new();
			DungeonGenerator generator = new(GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres"));
			using FidelityV3Protocol original = new();
			var coverage = FidelityV3Protocol.Coverage(original, generator, parameters);
			if (coverage.Any(c => c.Errors.Count != 0)) throw new InvalidDataException("Cobertura alterada.");
			var generated = generator.Generate(610006, new DungeonGenerator.Settings { GeneratorVersion = 3 });
			if (!generated.Success) throw new InvalidDataException("Generacion fallida.");
			SimLevelV3 level = SimLevelV3.From(generated.Map, parameters);
			ulong botSeed = BotBrainV3.SeedFor(generated.EffectiveSeed, 0);
			using JsonDocument oldPair = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reference, "real_610006_0.json")));
			if (generated.EffectiveSeed != oldPair.RootElement.GetProperty("effective_seed").GetInt32()
				|| botSeed.ToString() != oldPair.RootElement.GetProperty("bot_seed").GetString())
				throw new InvalidDataException("Semilla efectiva o bot distinto.");
			AssertEqual(parameters.ToDictionary(), previous.RootElement.GetProperty("gameplay_parameters"));
			AssertEqual(settings.ToDictionary(true), previous.RootElement.GetProperty("bot_settings"));
			using JsonDocument oldLevel = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reference, "level_610006.json")));
			AssertEqual(FidelityV3Protocol.MapData(level), oldLevel.RootElement.GetProperty("map"));
			var diagnosticHashes = new[] { "ai-training/diagnostics/RealPairTrace610006.cs", "ai-training/diagnostics/RealPairTrace610006.tscn" }
				.ToDictionary(p => p, FidelityV3Protocol.HashFile);
			FidelityV3Protocol.WriteNew(Path.Combine(_output, "manifest.json"), new
			{
				DiagnosticOnly = true, AcceptanceVerdict = "not_approved", V3Status = "experimental",
				RequestedSeed = 610006, Run = 0, generated.EffectiveSeed, BotSeed = botSeed.ToString(),
				Attempts = 1, WorkerCount = 1, RealGames = 1, SimulatorGames = 0,
				ProtocolSha256 = FidelityV3V2Protocol.RegisteredHash, Fingerprints = frozen, DiagnosticHashes = diagnosticHashes,
				GameplayParameters = parameters.ToDictionary(), BotSettings = settings.ToDictionary(true),
				ReferenceManifestSha256 = FidelityV3Protocol.HashFile(Path.Combine(reference, "manifest.json")),
				ReferencePairSha256 = FidelityV3Protocol.HashFile(Path.Combine(reference, "real_610006_0.json")),
				RealGameMethod = "FidelityV3Check.PlayReal, cuerpo original por reflection, monitor original conservado",
				MaxVirtualSeconds = protocol.MaxSeconds, MaxWallSeconds = 900, TraceEveryPhysicsFrames = 300,
				Limitation = "Proceso nuevo con observador; ensamblado ampliado. No reproduce las 50 partidas previas ni el combate simulado anterior. La traza puede alterar temporizacion; no corrige ni sustituye aceptacion. Cobertura solo de mapas, sin combates de reserva.",
			});
			MethodInfo playReal = typeof(FidelityV3Check).GetMethod("PlayReal", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException("PlayReal");
			GD.Print("DIAGNOSTIC_SINGLE_REAL_BEGIN seed=610006 run=0");
			SimulationResultV3 result = await (System.Threading.Tasks.Task<SimulationResultV3>)playReal.Invoke(this,
				new object[] { generated.Map, level, parameters, settings, botSeed, protocol.MaxSeconds });
			protocol.AssertFrozen(frozen, "");
			if (diagnosticHashes.Any(p => FidelityV3Protocol.HashFile(p.Key) != p.Value)) throw new InvalidDataException("Observador cambiado.");
			FidelityV3Protocol.WriteNew(Path.Combine(_output, "result.json"), new
			{
				RequestedSeed = 610006, Run = 0, generated.EffectiveSeed, BotSeed = botSeed.ToString(),
				Result = FidelityV3Protocol.ResultData(result), WallSeconds = _wall.Elapsed.TotalSeconds,
				LastSnapshot = _lastSnapshot, AcceptanceVerdictUnchanged = "not_approved",
			});
			GD.Print($"DIAGNOSTIC_SINGLE_REAL_END timed_out={result.TimedOut} time={result.TimeTotal} visits={result.Visits.Count}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			if (_created) FidelityV3Protocol.WriteNew(Path.Combine(_output, "failure.json"), new
				{ Reason = error.ToString(), WallSeconds = _wall.Elapsed.TotalSeconds, LastSnapshot = _lastSnapshot, Diagnosis = "no_diagnosticado" });
			GetTree().Quit(1);
		}
		finally { _trace?.Dispose(); _trace = null; }
	}

	private static void AssertEqual(object actual, JsonElement expected)
	{
		var current = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(actual, FidelityV3Protocol.JsonOptions));
		if (!System.Text.Json.Nodes.JsonNode.DeepEquals(current, System.Text.Json.Nodes.JsonNode.Parse(expected.GetRawText())))
			throw new InvalidDataException("Mapa, parametros o bot distintos de aceptacion.");
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);
		if (_wall.Elapsed.TotalSeconds >= 900) { GD.Print("DIAGNOSTIC_WALL_LIMIT"); GetTree().Quit(2); return; }
		RoomManager manager = ManagerField?.GetValue(this) as RoomManager;
		if (_trace == null || manager?.CurrentRoom == null) return;
		int frame = (int)FrameField.GetValue(this);
		RoomController room = manager.CurrentRoom;
		PlayerController player = manager.Player;
		Vector3 position = player.GlobalPosition;
		bool changedRoom = _cell != room.Data.Cell;
		if (changedRoom) { _path = 0; _windowStart = position; _previous = position; }
		_path += position.DistanceTo(_previous);
		_previous = position;
		if (!changedRoom && frame - _lastSample < 300) return;
		double displacement = position.DistanceTo(_windowStart);
		var collisions = Enumerable.Range(0, player.GetSlideCollisionCount()).Select(i =>
		{
			var collision = player.GetSlideCollision(i);
			return new { Collider = (collision.GetCollider() as Node)?.GetPath().ToString(), Normal = V(collision.GetNormal()), Position = V(collision.GetPosition()) };
		}).ToArray();
		_lastSnapshot = new
		{
			Frame = frame, Time = frame / 60.0, WallSeconds = _wall.Elapsed.TotalSeconds,
			Cell = new[] { room.Data.Cell.X, room.Data.Cell.Y }, State = room.State.ToString(), room.Data.TemplateId,
			Player = V(position), Velocity = V(player.Velocity),
			MoveInput = new[] { Input.GetActionStrength(PlayerController.MoveRight) - Input.GetActionStrength(PlayerController.MoveLeft),
				Input.GetActionStrength(PlayerController.MoveDown) - Input.GetActionStrength(PlayerController.MoveUp) },
			Shooting = PlayerShooter.Actions.Select(action => Input.IsActionPressed(action)).ToArray(),
			WindowSeconds = changedRoom ? 0 : (frame - _lastSample) / 60.0,
			PathMeters = _path, DisplacementMeters = displacement,
			Motion = changedRoom ? "room_change" : _path < 0.2 ? "quieto" : displacement < 0.5 && _path > 2 ? "vueltas_u_oscilacion" : "movimiento",
			Collisions = collisions, manager.IsTransitioning,
			Enemies = room.GetNode("Enemies").GetChildren().OfType<EnemyBase>()
				.Where(e => IsInstanceValid(e) && !e.IsQueuedForDeletion() && e.Health.CurrentHealth > 0).Select(e => new
				{
					Id = e.GetInstanceId(), Kind = e.Kind.ToString(), Position = V(e.GlobalPosition), Health = e.Health.CurrentHealth,
					DigPhase = e.Behavior<DigBehavior>()?.Phase.ToString(), Vulnerable = e.Behavior<DigBehavior>()?.Vulnerable,
				}).ToArray(),
			Doors = room.GetNode("Doors").GetChildren().OfType<Door>().Select(d => new
				{ Name = d.Name.ToString(), Position = V(d.GlobalPosition), d.IsOpen, CollisionDisabled = d.GetNode<CollisionShape3D>("CollisionShape3D").Disabled }).ToArray(),
		};
		string json = JsonSerializer.Serialize(_lastSnapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
		_trace.WriteLine(json);
		GD.Print("REAL_TRACE " + json);
		_cell = room.Data.Cell;
		_lastSample = frame;
		_path = 0;
		_windowStart = position;
	}

	private static float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
}
