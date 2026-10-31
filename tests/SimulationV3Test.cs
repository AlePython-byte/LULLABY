using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Pruebas breves de simulacion v3; no ejecuta muestras de fidelidad ni exporta datos.</summary>
public partial class SimulationV3Test : Node
{
	private int _checks;
	private GameplayParametersV3 _parameters;
	private static BotAction Idle => new() { Shoot = BotBrainV3.ShootNone };

	public override void _Ready()
	{
		try
		{
			_parameters = GameplayParametersV3.FromScenes();
			Parameters();
			Runner();
			Digger();
			BotPerception();
			Recorder();
			HistoricalEquivalence();
			Smoke();
			GD.Print($"SimulationV3Test: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private static LevelMap Encounter(EnemyKind kind, Vector2 spawn, float size = 20.0f)
	{
		return new LevelMap { Rooms = new Godot.Collections.Array<RoomData> { new()
		{
			Type = RoomType.Boss, Size = new Vector2(size, size), EnemyScene = EnemyKinds.Scene(kind),
			SpawnPoints = new[] { spawn }, SpawnKinds = new[] { (int)kind },
		} } };
	}

	private (SimulationResultV3 Result, List<SimulationFrameV3> Frames) Trace(LevelMap map,
		Func<BotObservationV3, BotAction> control, float seconds)
	{
		SimLevelV3 level = SimLevelV3.From(map, _parameters);
		LevelSimulatorV3 simulator = new(_parameters, level, control);
		List<SimulationFrameV3> frames = new();
		simulator.FrameCompleted += frames.Add;
		return (simulator.Run(seconds), frames);
	}

	private void Parameters()
	{
		foreach (EnemyKinds.Info info in EnemyKinds.List)
		{
			EnemyBase real = EnemyKinds.Scene(info.Kind).Instantiate<EnemyBase>();
			try
			{
				GameplayParametersV3.EnemyStats stats = _parameters.EnemyFor(EnemyKinds.Scene(info.Kind));
				Check(stats.Kind == real.Kind && stats.Speed == real.Speed && stats.MaxHealth == HealthComponent.FindIn(real).MaxHealth,
					$"Parametros de escena: {info.Kind}");
				Vector3 size = ((BoxShape3D)real.GetNode<CollisionShape3D>("CollisionShape3D").Shape).Size;
				Check(stats.Size == new Vector2(size.X, size.Z) && stats.ContactDamage == real.ContactDamage
					&& stats.RecoilDistance == real.RecoilDistance && stats.RecoilDuration == real.RecoilDuration,
					$"Colision, contacto y retroceso: {info.Kind}");
				if (stats.Dash != null) CompareExports(stats.Dash, real.GetNode<DashBehavior>("Dash"));
				if (stats.Dig != null)
				{
					DigBehavior dig = real.GetNode<DigBehavior>("Dig");
					CompareExports(stats.Dig, dig);
					EnemyProjectile projectile = dig.ProjectileScene.Instantiate<EnemyProjectile>();
					Check(stats.Dig.Projectile.ProjectileSpeed == (dig.ProjectileSpeed > 0 ? dig.ProjectileSpeed : projectile.Speed)
						&& stats.Dig.Projectile.ProjectileDamage == projectile.Damage
						&& stats.Dig.Projectile.ProjectileLifetime == projectile.Lifetime, "Proyectil real del excavador, con override");
					projectile.Free();
				}
			}
			finally { real.Free(); }
		}
		SortedDictionary<string, double> meta = _parameters.ToDictionary();
		Check(meta.ContainsKey("enemy[res://scenes/enemies/RunnerEnemy.tscn].dash.dash_speed")
			&& meta.ContainsKey("enemy[res://scenes/enemies/DiggerEnemy.tscn].dig.max_underground_time")
			&& meta["player_size_x"] == 1 && meta["player_size_z"] == 1, "Metadatos completos y colision historica");
		GD.Print("Parametros: seis escenas, Dash, Dig y proyectil verificados.");
	}

	private void CompareExports(object stats, object behavior)
	{
		foreach (var property in stats.GetType().GetProperties().Where(p => p.PropertyType == typeof(float)))
			Check(Equals(property.GetValue(stats), behavior.GetType().GetProperty(property.Name)?.GetValue(behavior)),
				$"Valor de escena {stats.GetType().Name}.{property.Name}");
	}

	private void Runner()
	{
		LevelMap map = Encounter(EnemyKind.Runner, new Vector2(5, 0));
		var idle = Trace(map, _ => Idle, 15);
		var frames = idle.Frames.Where(f => f.Enemies.Length > 0).ToList();
		var warning = frames.First(f => f.Enemies[0].DashState == DashBehavior.DashState.Warning);
		var dash = frames.First(f => f.Enemies[0].DashState == DashBehavior.DashState.Dashing);
		GameplayParametersV3.DashStats stats = _parameters.EnemyFor(EnemyKinds.Scene(EnemyKind.Runner)).Dash;
		Check(Math.Abs((dash.Frame - warning.Frame) * _parameters.PhysicsDelta - stats.WarnTime) <= _parameters.PhysicsDelta * 1.1f,
			"Aviso completo antes de mover la embestida");
		Check(frames.Where(f => f.Frame >= warning.Frame && f.Frame < dash.Frame).All(f => f.Enemies[0].Position == warning.Enemies[0].Position),
			"KeepDistance no desplaza al huidor durante el aviso");
		Check(idle.Result.DamageBySource[(int)DamageSource.Contact] > 0, "La embestida alcanza al jugador quieto");
		Check(frames.Any(f => f.Enemies[0].RecoilFrames > 0), "El contacto efectivo inicia retroceso");
		bool recoilPaused = false;
		for (int i = 1; i < frames.Count; i++)
		{
			var before = frames[i - 1].Enemies[0];
			var after = frames[i].Enemies[0];
			if (before.RecoilFrames > 0)
			{
				Check(after.RecoilFrames == before.RecoilFrames - 1 && after.DashTimer == before.DashTimer,
					"Retroceso excluye temporizadores de comportamientos");
				recoilPaused = true;
			}
		}
		Check(recoilPaused, "Cobertura del retroceso del huidor");

		var moving = Trace(map, observation => observation.DashWarning?.Any(v => v) == true
			? new BotAction { Move = Vector2.Down, Shoot = -1 } : Idle, 8);
		var firstWarning = moving.Frames.First(f => f.Enemies.Any(e => e.DashState == DashBehavior.DashState.Warning));
		var firstDash = moving.Frames.First(f => f.Enemies.Any(e => e.DashState == DashBehavior.DashState.Dashing));
		Check(firstDash.Enemies[0].DashDirection == firstWarning.Enemies[0].DashDirection
			&& Math.Abs(firstDash.Player.Y) > 0.5f, "Direccion fijada al aviso aunque el jugador se mueva");

		// Sala pequena: la carga termina en pared antes de agotar DashTime.
		var blocked = Trace(Encounter(EnemyKind.Runner, new Vector2(3, 0), 8), _ => new BotAction { Move = Vector2.Left, Shoot = -1 }, 12);
		Check(blocked.Frames.All(f => f.Enemies.All(e => Math.Abs(e.Position.X) <= 4 && Math.Abs(e.Position.Y) <= 4)),
			"El huidor respeta paredes");
		GD.Print("Huidor: aviso, direccion fija, contacto, retroceso y paredes verificados.");
	}

	private void Digger()
	{
		LevelMap map = Encounter(EnemyKind.Digger, new Vector2(5, 0));
		var trace = Trace(map, _ => Idle, 16);
		var frames = trace.Frames.Where(f => f.Enemies.Length > 0).ToList();
		Check(Enum.GetValues<DigBehavior.DigPhase>().All(phase => frames.Any(f => f.Enemies[0].DigPhase == phase)), "Cuatro fases del excavador");
		Check(frames.All(f => f.Enemies[0].Vulnerable == (f.Enemies[0].DigPhase != DigBehavior.DigPhase.Underground)),
			"Vulnerable hundiendose y emergiendo, invulnerable solo bajo tierra");
		Check(frames.Any(f => f.Enemies[0].DigWarned && !f.Enemies[0].DigFired) && frames.Any(f => f.ProjectileCount > 0),
			"Aviso y proyectil real del excavador");
		var firstOut = frames[0];
		var firstBuried = frames.First(f => !f.Enemies[0].Vulnerable);
		Check(frames.Where(f => f.Frame < firstBuried.Frame).All(f => f.Enemies[0].Position == firstOut.Enemies[0].Position),
			"Excavador inmovil fuera y hundiendose");
		Check(frames.Any(f => !f.Enemies[0].Vulnerable && Math.Abs(f.Enemies[0].Position.Y) > 0.1f), "Orbita en XZ bajo tierra");
		GameplayParametersV3.DigStats dig = _parameters.EnemyFor(EnemyKinds.Scene(EnemyKind.Digger)).Dig;
		var normalEmerge = frames.First(f => f.Enemies[0].DigPhase == DigBehavior.DigPhase.Emerging);
		Check(!normalEmerge.Enemies[0].ForcedEmerge && normalEmerge.Enemies[0].Position.DistanceTo(normalEmerge.Player) >= dig.MinEmergeDistance,
			"Emergencia normal a distancia minima");
		Check((normalEmerge.Frame - firstBuried.Frame) * _parameters.PhysicsDelta >= dig.UndergroundTime,
			"Tiempo minimo bajo tierra");

		var forced = Trace(Encounter(EnemyKind.Digger, new Vector2(1.5f, 0)), observation =>
		{
			if (observation.Vulnerable?.FirstOrDefault() == false && observation.Enemies.Count > 0)
				return new BotAction { Move = observation.Enemies[0] - observation.Player, Shoot = -1 };
			return Idle;
		}, 13);
		var forcedFrame = forced.Frames.First(f => f.Enemies.Any(e => e.ForcedEmerge));
		Check(forcedFrame.Enemies[0].DigPhase == DigBehavior.DigPhase.Emerging && forcedFrame.Enemies[0].Vulnerable,
			"Emergencia forzada restaura vulnerabilidad inmediatamente");
		Vector2 half = new(10 - dig.WallMargin, 10 - dig.WallMargin);
		Vector2[] corners = { -half, new(half.X, -half.Y), half, new(-half.X, half.Y) };
		Vector2 expected = corners.OrderByDescending(c => c.DistanceSquaredTo(forcedFrame.Player)).First();
		Check(forcedFrame.Enemies[0].Position == expected, "Emergencia forzada en esquina mas lejana con margen real");
		int buriedAt = forced.Frames.First(f => f.Enemies.Any(e => !e.Vulnerable)).Frame;
		Check((forcedFrame.Frame - buriedAt) * _parameters.PhysicsDelta >= dig.MaxUndergroundTime, "Espera MaxUndergroundTime antes de forzar");

		var shots = Trace(Encounter(EnemyKind.Digger, new Vector2(1.5f, 0)), observation =>
			observation.Enemies.Count > 0 && observation.Vulnerable?.FirstOrDefault() == false
				? new BotAction { Shoot = BotBrainV3.ShootRight } : Idle, 5);
		Check(shots.Frames.Where(f => f.Enemies.Any(e => !e.Vulnerable)).All(f => f.Enemies[0].Health == 3),
			"Las lagrimas no danan al excavador enterrado");
		var exposed = Trace(Encounter(EnemyKind.Digger, new Vector2(1.5f, 0)), _ => new BotAction { Shoot = BotBrainV3.ShootRight }, 5);
		Check(!exposed.Result.TimedOut && exposed.Frames.Last().Enemies.Length == 0, "Excavador expuesto recibe lagrimas y despeja la sala");
		GD.Print("Excavador: fases, vulnerabilidad, proyectil, orbita y emergencia forzada verificados.");
	}

	private void BotPerception()
	{
		SimLevelV3 level = SimLevelV3.From(Encounter(EnemyKind.Digger, new Vector2(5, 0)), _parameters);
		BotSettings settings = new() { ReactionFrames = 6, ReactionJitter = 0, AimNoise = 0 };
		BotBrainV3 bot = new(level, settings, 42);
		BotObservationV3 Observation(bool vulnerable, bool warning = false) => new()
		{
			Cell = Vector2I.Zero, State = RoomState.Combat, RoomSize = new Vector2(20, 20), Player = Vector2.Zero,
			Enemies = new[] { new Vector2(5, 0) }, EnemyKinds = new[] { EnemyKind.Digger },
			Vulnerable = new[] { vulnerable }, DashWarning = new[] { warning },
		};
		for (int i = 0; i < 12; i++) Check(bot.Decide(Observation(false)).Shoot == -1, "No dispara al monticulo");
		for (int i = 0; i < 6; i++) Check(bot.Decide(Observation(true)).Shoot == -1, "Emergencia percibida con retraso");
		Check(bot.Decide(Observation(true)).Shoot == BotBrainV3.ShootRight, "Ataca al emerger tras el retraso");
		for (int i = 0; i < 6; i++) Check(bot.Decide(Observation(false)).Shoot == BotBrainV3.ShootRight, "Enterramiento tambien retrasado");
		Check(bot.Decide(Observation(false)).Shoot == -1, "Deja de disparar cuando percibe el enterramiento");

		BotBrainV3 control = new(level, settings, 42), warned = new(level, settings, 42);
		for (int i = 0; i < 12; i++) { control.Decide(Observation(true)); warned.Decide(Observation(true)); }
		for (int i = 0; i < 6; i++) Check(control.Decide(Observation(true)).Move == warned.Decide(Observation(true, true)).Move,
			"No anticipa el aviso");
		Check(control.Decide(Observation(true)).Move != warned.Decide(Observation(true, true)).Move, "Esquiva aviso tras retraso");
	}

	private void Recorder()
	{
		SimLevelV3 level = SimLevelV3.From(Encounter(EnemyKind.Runner, new Vector2(5, 0)), _parameters);
		RunRecorderV3 recorder = new(level, 5);
		recorder.EnterRoom(Vector2I.Zero, 0, false);
		recorder.Damage(1, DamageSource.Contact, EnemyKind.Runner);
		recorder.Damage(1, DamageSource.Projectile, EnemyKind.Digger);
		Check(recorder.Result.Visits[0].ContactByKind.Length == 6
			&& recorder.Result.Visits[0].ContactByKind[(int)EnemyKind.Runner] == 1
			&& recorder.Result.HitsBySource[(int)DamageSource.Projectile] == 1, "Registro admite seis tipos y fuentes separadas");
	}

	private void HistoricalEquivalence()
	{
		GameplayParameters historical = GameplayParameters.FromScenes();
		foreach (EnemyKind kind in new[] { EnemyKind.Chaser, EnemyKind.Shooter, EnemyKind.Exploder, EnemyKind.Flyer })
		{
			LevelMap map = Encounter(kind, new Vector2(5, 0));
			SimLevel oldLevel = SimLevel.From(map, historical);
			SimLevelV3 newLevel = SimLevelV3.From(map, _parameters);
			SimulationResult oldResult = new LevelSimulator(historical, oldLevel, new BotBrain(oldLevel, new BotSettings(), 42)).Run(60);
			SimulationResultV3 newResult = new LevelSimulatorV3(_parameters, newLevel, new BotBrainV3(newLevel, new BotSettings(), 42)).Run(60);
			Check(oldResult.DamageTotal == newResult.DamageTotal && oldResult.TimeTotal == newResult.TimeTotal
				&& oldResult.TimedOut == newResult.TimedOut, $"Equivalencia de caso historico aislado: {kind}");
		}
	}

	private void Smoke()
	{
		DungeonGenerator generator = new(GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres"));
		foreach (int seed in new[] { 710001, 710002, 710003 })
		{
			var generated = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
			Check(generated.Success, "Generacion v3 breve");
			SimLevelV3 level = SimLevelV3.From(generated.Map, _parameters);
			SimulationResultV3 Run() => new LevelSimulatorV3(_parameters, level,
				new BotBrainV3(level, new BotSettings(), BotBrainV3.SeedFor(generated.EffectiveSeed, 0))).Run(600);
			var first = Run();
			var second = Run();
			Check(!first.TimedOut, $"Bot completa semilla de humo {seed}");
			Check(first.DamageTotal == second.DamageTotal && first.TimeTotal == second.TimeTotal
				&& first.Visits.Count == second.Visits.Count, "Simulacion determinista");
		}
		GD.Print("Humo: tres niveles v3 completados dos veces; no constituyen validacion de fidelidad.");
	}
}
