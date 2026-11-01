using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba las fases de The Mother con el código real del juego (TestLevel, física y salas). Modo headless:
///   godot --headless --path . res://tests/BossPhasesTest.tscn
/// Casos:
///   1. Funciones puras: umbrales de fase (66 % y 33 % de 180), números de cada fase, anillo (cantidad, hueco, nada nace
///      encima del objetivo), órbita (converge al radio y no sale de la arena) y daño mínimo con resistencia.
///   2. Cambio de fase: justo en el umbral, invulnerable 1,2 s (el daño no entra, Kill sí), la frase de la fase, una sola
///      señal por cambio, y un golpe grande que salta a la fase 3 con una única invulnerabilidad.
///   3. Fase 2 y fase 3 en combate: avisos completos antes de cada ataque nuevo, nunca dos ataques a la vez, solo se mueve
///      en Approach, Dash u Orbit, nunca sale de la arena ni se acerca a las puertas, ningún estado se eterniza, el anillo
///      tiene su cantidad, su hueco hacia donde escapar, nada nace encima de Noah ni va más rápido de 5,01 m/s, y en la
///      fase 3 hay dos embestidas por anillo.
///      La fase 3 alterna el anillo con la espiral (MotherSpiral): tres brazos, ~2 s, 5 m/s, aviso de 0,8 s y tres huecos de 75°.
///      Se prueba su geometría, que es esquivable con la velocidad real de Noah desde cualquier punto de la arena (simulando los
///      proyectiles), que dura lo debido, que nada nace sobre Noah ni se sale de los huecos y que The Mother se queda quieta.
///   4. Daño: ningún proyectil ni el contacto del jefe hace más de 1 de daño.
///   5. Reintentar y volver al menú: sin proyectiles ni jefe, aunque se cierre en pleno cambio de fase o en plena espiral.
/// Noah se queda quieto y tiene vida de sobra. Sale con código 0 si todo pasa.
/// </summary>
public partial class BossPhasesTest : Node
{
	private const int MinWarningTicks = 32;   // 0,6 s a 60 pasos por segundo, con el margen de los temporizadores
	private const int MinShotWarningTicks = 38;
	private const float Tolerance = 0.02f;
	private const float BodyRadius = 1.2f;

	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== BossPhasesTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckPhaseThresholds();
			CheckRing();
			CheckSpiral();
			CheckOrbit();
			CheckHitDamage();
			await CheckPhaseChange();
			await CheckSkippedPhase();
			await CheckCombat(2, 70.0f);
			await CheckCombat(3, 130.0f);
			await CheckRetryLeavesNothing();
			await CheckRetryDuringSpiral();
			GD.Print($"BossPhasesTest: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	// --- 1. Funciones puras ----------------------------------------------------------------------------------------------

	private void CheckPhaseThresholds()
	{
		Require(MotherPhases.PhaseFor(180.0f, 180.0f) == 1 && MotherPhases.PhaseFor(119.0f, 180.0f) == 1, "Por encima del 66 % es la fase 1");
		Require(MotherPhases.PhaseFor(118.0f, 180.0f) == 2 && MotherPhases.PhaseFor(60.0f, 180.0f) == 2, "Entre el 66 % y el 33 % es la fase 2");
		Require(MotherPhases.PhaseFor(59.0f, 180.0f) == 3 && MotherPhases.PhaseFor(1.0f, 180.0f) == 3, "Por debajo del 33 % es la fase 3");
		Require(MotherPhases.PhaseFor(10.0f, 0.0f) == 1, "Una vida máxima nula no rompe nada");

		MotherPhases.Settings second = MotherPhases.For(2), third = MotherPhases.For(3);
		Require(second.RingProjectiles >= 8 && second.RingProjectiles <= 12 && third.RingProjectiles > second.RingProjectiles
			&& third.RingProjectiles <= 14, "El anillo de la fase 2 tiene de 8 a 12 proyectiles y el de la 3 es más denso");
		Require(second.DashCount == 1 && third.DashCount == 2, "La fase 3 encadena dos embestidas");
		Require(third.RecoveryTime < second.RecoveryTime && third.OrbitTime < second.OrbitTime && third.ShotInterval < second.ShotInterval,
			"La fase 3 tiene pausas más cortas");
		Require(second.RingGapDegrees >= 40.0f && third.RingGapDegrees >= 40.0f, "Todos los anillos dejan un hueco de 40° o más");
		Require(third.ChainDashWarning >= 0.6f && third.DashWarning >= 0.6f, "Los avisos de embestida duran 0,6 s o más");
		GD.Print("Umbrales y números de fase: OK");
	}

	private void CheckRing()
	{
		foreach (MotherPhases.Settings settings in new[] { MotherPhases.For(2), MotherPhases.For(3) })
		{
			Vector3 center = new Vector3(0.3f, 0.0f, -1.0f).Normalized();
			List<Vector3> directions = MotherRing.Directions(settings.RingProjectiles, center, settings.RingGapDegrees);
			Require(directions.Count == settings.RingProjectiles, "El anillo tiene todos sus proyectiles");
			float closest = 360.0f;
			foreach (Vector3 direction in directions)
			{
				Require(Mathf.IsEqualApprox(direction.Length(), 1.0f) && Mathf.Abs(direction.Y) < 0.0001f, "Cada dirección es unitaria y plana");
				closest = Mathf.Min(closest, Mathf.RadToDeg(direction.AngleTo(center)));
			}

			Require(closest >= settings.RingGapDegrees / 2.0f - 0.01f, $"Ningún proyectil entra en el hueco ({closest:0.0}° del centro)");
			Require(closest <= settings.RingGapDegrees / 2.0f + 0.01f, "Los extremos del anillo son el borde del hueco");
		}

		Vector3 toPlayer = new(0.0f, 0.0f, -5.0f);
		Require(MotherRing.GapCenter(toPlayer, 0.0f).IsEqualApprox(Vector3.Forward), "Sin desvío, el hueco mira a Noah");
		Require(Mathf.IsEqualApprox(Mathf.RadToDeg(MotherRing.GapCenter(toPlayer, 25.0f).AngleTo(Vector3.Forward)), 25.0f), "El desvío gira el hueco 25°");
		Require(MotherRing.GapCenter(Vector3.Zero, 0.0f).IsEqualApprox(Vector3.Forward), "Con Noah encima, el hueco tiene una dirección válida");

		Require(MotherRing.SpawnsOnTarget(Vector3.Zero, Vector3.Right, 2.4f, new Vector3(2.4f, 0.0f, 0.5f), 1.2f), "Nace encima de Noah: se omite");
		Require(!MotherRing.SpawnsOnTarget(Vector3.Zero, Vector3.Right, 2.4f, new Vector3(5.0f, 0.0f, 0.0f), 1.2f), "Lejos de Noah: se dispara");
		Require(!MotherRing.SpawnsOnTarget(Vector3.Zero, Vector3.Left, 2.4f, new Vector3(1.0f, 0.0f, 0.0f), 1.2f), "Noah dentro del anillo no estorba");
		GD.Print("Anillo de proyectiles: OK");
	}

	private void CheckSpiral()
	{
		Require(MotherSpiral.Arms == 3, "La espiral tiene tres brazos");
		Require(Mathf.Abs(MotherSpiral.Interval * (MotherSpiral.Emissions - 1) - MotherSpiral.Duration) < 0.001f
			&& MotherSpiral.Duration >= 1.8f && MotherSpiral.Duration <= 2.2f, "La espiral dura unos 2 s");
		Require(MotherSpiral.ProjectileSpeed == 5.0f && MotherSpiral.Warning >= 0.6f, "Proyectiles a 5 m/s y aviso de 0,6 s o más");
		Require(MotherSpiral.GapDegrees >= 60.0f && Mathf.IsEqualApprox(MotherSpiral.GapDegrees + MotherSpiral.SweepDegrees, 120.0f),
			$"Cada hueco de escape mide {MotherSpiral.GapDegrees}°, al menos 60°");
		Require(MotherSpiral.TotalProjectiles == 3 * MotherSpiral.Emissions, "Cada brazo emite todas sus tandas");
		Require(MotherSpiral.Warning >= MotherPhases.For(3).DashWarning, "El aviso no es más corto que el de la embestida de la fase 3");

		Vector3 center = new Vector3(0.4f, 0.0f, -1.0f).Normalized();
		foreach (bool reverse in new[] { false, true })
		{
			float lowest = 360.0f, highest = -360.0f;
			for (int emission = 0; emission < MotherSpiral.Emissions; emission++)
			{
				List<Vector3> directions = MotherSpiral.Directions(emission, center, reverse);
				Require(directions.Count == MotherSpiral.Arms, "Una dirección por brazo en cada tanda");
				for (int arm = 0; arm < directions.Count; arm++)
				{
					Vector3 direction = directions[arm];
					Require(Mathf.IsEqualApprox(direction.Length(), 1.0f) && Mathf.Abs(direction.Y) < 0.0001f, "Cada dirección es unitaria y plana");
					float offset = MotherSpiral.OffsetDegrees(arm, emission, reverse);
					Require(!MotherSpiral.InGap(offset, 1.0f), $"Un proyectil de la espiral cae en un hueco ({offset:0.0}°)");
					float measured = Mathf.PosMod(Mathf.RadToDeg(center.SignedAngleTo(direction, Vector3.Up)), 360.0f);
					Require(Mathf.Abs(Mathf.PosMod(offset, 360.0f) - measured) < 0.05f, "La dirección coincide con su ángulo");
					if (arm == 0)
					{
						lowest = Mathf.Min(lowest, offset);
						highest = Mathf.Max(highest, offset);
					}
				}
			}

			Require(Mathf.Abs(highest - lowest - MotherSpiral.SweepDegrees) < 0.01f, "Cada brazo barre sus 45°");
		}

		// El primer y el último disparo de un brazo son los extremos del barrido, y el sentido contrario los invierte.
		Require(Mathf.IsEqualApprox(MotherSpiral.OffsetDegrees(0, 0, false), MotherSpiral.OffsetDegrees(0, MotherSpiral.Emissions - 1, true)),
			"Girar al revés invierte el barrido");
		Require(MotherSpiral.InGap(0.0f) && MotherSpiral.InGap(120.0f) && MotherSpiral.InGap(-120.0f) && !MotherSpiral.InGap(60.0f), "Hay un hueco cada 120°");

		// Noah queda fuera del hueco (en un sector), a 55° de su centro, y el desvío cambia de lado.
		Vector3 toPlayer = new(0.0f, 0.0f, -5.0f);
		foreach (int side in new[] { 1, -1 })
		{
			float angle = Mathf.RadToDeg(MotherSpiral.GapCenter(toPlayer, side).AngleTo(Vector3.Forward));
			Require(Mathf.Abs(angle - MotherSpiral.GapOffsetDegrees) < 0.01f && !MotherSpiral.InGap(MotherSpiral.GapOffsetDegrees), "El hueco queda a 55° de Noah");
		}

		Require(MotherSpiral.TimeToFirstHit(10.0f, 2.4f) > MotherSpiral.TimeToFirstHit(4.0f, 2.4f), "Cuanto más lejos, más tiempo tiene Noah");
		GD.Print("Espiral: geometría, huecos y sentidos: OK");
	}

	// Noah (velocidad real) sale de los sectores a tiempo desde cualquier punto de la arena, con el jefe en cualquier lugar permitido.
	// Se simulan los proyectiles uno a uno: Noah reacciona 0,4 s después de empezar el aviso, corre al 85 % de su velocidad en línea
	// recta hasta el centro de algún hueco (a cualquier distancia razonable) y se queda ahí. Es esquivable si algún destino lo logra.
	// Quedarse quieto solo cuenta como destino si Noah está más allá del alcance de los proyectiles; dentro de él exige una ruta real.
	private void CheckSpiralEscape(float noahSpeed, MotherBehavior behavior)
	{
		const float reaction = 0.4f, hit = 0.9f, step = 1.0f / 30.0f;
		EnemyProjectile sample = behavior.ProjectileScene.Instantiate<EnemyProjectile>();
		float lifetime = sample.Lifetime;
		sample.Free();
		Vector2 arena = behavior.ArenaSize;
		float bossX = arena.X / 2.0f - behavior.ArenaMargin, bossZ = arena.Y / 2.0f - behavior.ArenaMargin;
		float walkX = arena.X / 2.0f - 1.0f, walkZ = arena.Y / 2.0f - 1.0f;
		float spawn = behavior.RingSpawnDistance;
		float minimumRadius = spawn + behavior.RingSafeDistance + 0.6f;
		float speed = noahSpeed * 0.85f;
		// Más allá de este alcance (nacimiento + velocidad x vida + radio de impacto) ningún proyectil de la espiral llega a Noah.
		float reach = spawn + MotherSpiral.ProjectileSpeed * lifetime + hit;
		int cases = 0, outOfReach = 0;
		Vector2[] bosses = { new(0.0f, 0.0f), new(-bossX, -bossZ), new(bossX, -bossZ), new(-bossX, bossZ), new(bossX, bossZ) };
		foreach (Vector2 boss in bosses)
		{
			for (float x = -walkX; x <= walkX + 0.01f; x += 3.0f)
			{
				for (float z = -walkZ; z <= walkZ + 0.01f; z += 3.0f)
				{
					Vector2 noah = new(x, z);
					Vector2 offset = noah - boss;
					if (offset.Length() < minimumRadius) continue;
					foreach (int side in new[] { 1, -1 })
					{
						Vector3 gap = MotherSpiral.GapCenter(new Vector3(offset.X, 0.0f, offset.Y), side);
						List<(Vector2 Direction, float Start)> rays = new();
						for (int emission = 0; emission < MotherSpiral.Emissions; emission++)
						{
							foreach (Vector3 direction in MotherSpiral.Directions(emission, gap, side < 0))
							{
								rays.Add((new Vector2(direction.X, direction.Z), MotherSpiral.Warning + emission * MotherSpiral.Interval));
							}
						}

						List<Vector2> targets = new();
						bool beyondReach = offset.Length() > reach;
						if (beyondReach)
						{
							targets.Add(noah);
							outOfReach++;
						}

						for (int k = 0; k < MotherSpiral.Arms; k++)
						{
							Vector3 center = gap.Rotated(Vector3.Up, Mathf.DegToRad(MotherSpiral.ArmSpacingDegrees * k));
							foreach (float radius in new[] { offset.Length(), offset.Length() * 0.75f, offset.Length() * 0.5f, minimumRadius })
							{
								Vector2 target = boss + new Vector2(center.X, center.Z) * Mathf.Max(radius, minimumRadius);
								if (Mathf.Abs(target.X) <= walkX && Mathf.Abs(target.Y) <= walkZ) targets.Add(target);
							}
						}

						targets.Sort((a, b) => noah.DistanceTo(a).CompareTo(noah.DistanceTo(b)));
						bool escaped = false;
						foreach (Vector2 target in targets)
						{
							if (SpiralPathIsSafe(noah, target, boss, rays, speed, reaction, hit, step, spawn, lifetime)) { escaped = true; break; }
						}

						Require(escaped, $"Sin salida de la espiral: Noah en {noah}, jefe en {boss}, lado {side}, a {offset.Length():0.0} m");
						cases++;
					}
				}
			}
		}

		GD.Print($"Espiral esquivable a {noahSpeed:0.0} m/s de Noah desde {cases} posiciones y lados ({outOfReach} fuera de alcance, el resto con ruta de escape): OK");
	}

	private static bool SpiralPathIsSafe(Vector2 from, Vector2 to, Vector2 boss, List<(Vector2 Direction, float Start)> rays, float speed,
		float reaction, float hit, float step, float spawn, float lifetime)
	{
		float travel = from.DistanceTo(to) / Mathf.Max(0.01f, speed);
		float end = MotherSpiral.Warning + MotherSpiral.Duration + lifetime + 0.5f;
		for (float t = 0.0f; t <= end; t += step)
		{
			float moved = Mathf.Clamp((t - reaction) / Mathf.Max(0.01f, travel), 0.0f, 1.0f);
			Vector2 position = from.Lerp(to, moved);
			foreach ((Vector2 direction, float start) in rays)
			{
				float age = t - start;
				if (age < 0.0f || age > lifetime) continue;
				if ((boss + direction * (spawn + MotherSpiral.ProjectileSpeed * age) - position).Length() < hit) return false;
			}
		}

		return true;
	}

	private void CheckOrbit()
	{
		const float delta = 1.0f / 60.0f;
		Vector2 room = new(26.0f, 26.0f);
		foreach (Vector3 start in new[] { Vector3.Zero, new Vector3(10.0f, 0.0f, 10.0f), new Vector3(-3.0f, 0.0f, 1.0f) })
		{
			foreach (int direction in new[] { 1, -1 })
			{
				Vector3 position = start;
				float angle = 0.0f;
				for (int i = 0; i < 600; i++)
				{
					Vector3 velocity = MotherArena.OrbitVelocity(position, 6.0f, 2.5f, direction);
					Require(velocity.Length() <= 2.5f * 1.5f + 0.001f, "La órbita no supera 1,5 veces su velocidad");
					velocity = MotherArena.LimitVelocity(position, velocity, room, 3.0f, delta);
					Vector3 previous = position;
					position += velocity * delta;
					Require(Mathf.Abs(position.X) <= 10.0001f && Mathf.Abs(position.Z) <= 10.0001f, "La órbita no sale de la arena");
					if (i > 300) angle += Mathf.Abs(previous.SignedAngleTo(position, Vector3.Up));
				}

				Require(Mathf.Abs(position.Length() - 6.0f) < 0.6f, $"La órbita converge a su radio (llegó a {position.Length():0.00} m)");
				Require(angle > 0.5f, "La órbita avanza en arco");
			}
		}

		GD.Print("Órbita: converge al radio, avanza en arco y no sale de la arena: OK");
	}

	private void CheckHitDamage()
	{
		Require(PlayerHealth.EffectiveHitDamage(1.0f, 1.0f) == 1.0f, "Sin resistencia, un golpe vale 1");
		Require(PlayerHealth.EffectiveHitDamage(1.0f, 0.5f) == 0.5f && PlayerHealth.EffectiveHitDamage(1.0f, 0.1f) == 0.5f,
			"Con resistencia, un golpe vale 0,5 como mínimo");

		PackedScene projectile = GD.Load<PackedScene>("res://scenes/combat/EnemyProjectile.tscn");
		EnemyProjectile instance = projectile.Instantiate<EnemyProjectile>();
		Require(instance.Damage <= 1.0f && instance.Speed <= 5.01f, "El proyectil del jefe hace como mucho 1 de daño y va a 5 m/s o menos");
		instance.Free();

		PackedScene bossScene = GD.Load<PackedScene>("res://scenes/bosses/TheMother.tscn");
		EnemyBase boss = bossScene.Instantiate<EnemyBase>();
		Require(boss.ContactDamage <= 1.0f && HealthComponent.FindIn(boss).MaxHealth == 180.0f, "El contacto del jefe vale 1 y su vida sigue en 180");
		boss.Free();
		GD.Print("Daño por golpe: OK");
	}

	// --- 2. Cambio de fase -----------------------------------------------------------------------------------------------

	private static async Task<(BossTestHarness.Level Level, RoomController Room, EnemyBase Boss, MotherBehavior Behavior)> OpenBoss(SceneTree tree, Node parent)
	{
		BossTestHarness.Level level = BossTestHarness.Open(parent, BossTestHarness.Seed);
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		return (level, room, room.BossEnemy, room.BossEnemy.Behavior<MotherBehavior>());
	}

	private async Task CheckPhaseChange()
	{
		SceneTree tree = GetTree();
		var (level, room, boss, behavior) = await OpenBoss(tree, this);
		HealthComponent health = boss.Health;
		int changes = 0, lastPhase = 0;
		behavior.PhaseChanged += phase => { changes++; lastPhase = phase; };
		Label3D phrase = boss.GetNode<Label3D>("EntranceText");

		Require(behavior.Phase == 1 && !health.Invulnerable && health.EffectiveMaxHealth == 180.0f, "Empieza en la fase 1, vulnerable y con 180 de vida");
		health.TakeDamage(61.0f);   // 119: todavía fase 1
		Require(behavior.Phase == 1 && changes == 0 && !health.Invulnerable, "A 119 de 180 sigue en la fase 1");
		health.TakeDamage(1.0f);    // 118: fase 2
		Require(behavior.Phase == 2 && changes == 1 && lastPhase == 2, "A 118 de 180 pasa a la fase 2, con una sola señal");
		Require(behavior.State == MotherBehavior.AttackState.PhaseChange && behavior.PhaseChangeActive && health.Invulnerable,
			"El cambio de fase deja al jefe invulnerable");

		await BossTestHarness.Frames(tree, 2);
		Require(phrase.Visible && phrase.Text == MotherTexts.PhaseTwo, "Muestra la frase de la fase 2");
		Require(boss.Visual.Highlighted || boss.Visual.WarningActive, "Parpadea con el color de aviso");

		float before = health.CurrentHealth;
		health.TakeDamage(40.0f);
		Require(health.CurrentHealth == before, "Invulnerable: el daño no entra");
		int ticks = 0;
		Vector3 frozen = boss.Position;
		while (behavior.State == MotherBehavior.AttackState.PhaseChange && ticks < 200)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			ticks++;
			Vector3 step = boss.Position - frozen;
			step.Y = 0.0f;
			Require(step.Length() < 0.01f, "Durante el cambio de fase el jefe está quieto");
		}

		Require(ticks >= 66 && ticks <= 80, $"La invulnerabilidad dura unos 1,2 s ({ticks} pasos)");
		await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		Require(!health.Invulnerable, "Termina la invulnerabilidad");
		health.TakeDamage(5.0f);
		Require(health.CurrentHealth == before - 5.0f && changes == 1, "Después vuelve a recibir daño, sin repetir el cambio de fase");

		// Se puede hacer más daño dentro de la misma fase sin que se repita nada.
		health.TakeDamage(10.0f);
		Require(changes == 1 && behavior.Phase == 2, "Más daño dentro de la fase 2 no la repite");
		await BossTestHarness.Close(tree, level);
		GD.Print($"Cambio de fase: umbral, invulnerabilidad de {ticks} pasos, frase y señal única: OK");
	}

	private async Task CheckSkippedPhase()
	{
		SceneTree tree = GetTree();
		var (level, room, boss, behavior) = await OpenBoss(tree, this);
		int changes = 0, lastPhase = 0;
		behavior.PhaseChanged += phase => { changes++; lastPhase = phase; };
		Label3D phrase = boss.GetNode<Label3D>("EntranceText");

		boss.Health.TakeDamage(130.0f);   // 50 de 180: salta directo a la fase 3
		Require(behavior.Phase == 3 && changes == 1 && lastPhase == 3 && boss.Health.Invulnerable, "Un golpe grande salta a la fase 3 con un solo cambio");
		await BossTestHarness.Frames(tree, 2);
		Require(phrase.Text == MotherTexts.PhaseThree, "Muestra la frase de la fase 3");
		await BossTestHarness.WaitTicks(tree, () => behavior.State != MotherBehavior.AttackState.PhaseChange, 200, "el fin del cambio de fase");
		Require(!boss.Health.Invulnerable, "Termina la invulnerabilidad");
		await BossTestHarness.Frames(tree, 300);
		Require(changes == 1, "No hay un segundo cambio de fase");
		await BossTestHarness.Close(tree, level);
		GD.Print("Salto directo a la fase 3 con una sola invulnerabilidad: OK");
	}

	// --- 3. Combate en cada fase -----------------------------------------------------------------------------------------

	private async Task CheckCombat(int phase, float damage)
	{
		SceneTree tree = GetTree();
		var (level, room, boss, behavior) = await OpenBoss(tree, this);
		MotherPhases.Settings settings = MotherPhases.For(phase);
		boss.Health.TakeDamage(damage);
		Require(behavior.Phase == phase, $"Con {damage} de daño está en la fase {phase}");
		if (phase == 3) CheckSpiralEscape(level.Player.Speed, behavior);

		Vector2 arena = behavior.ArenaSize;
		Vector2 limit = new(arena.X / 2.0f - behavior.ArenaMargin, arena.Y / 2.0f - behavior.ArenaMargin);
		float wallClearance = Mathf.Min(arena.X, arena.Y) / 2.0f - 1.0f;

		HashSet<ulong> seen = new();
		MotherBehavior.AttackState previous = behavior.State;
		Vector3 previousPosition = boss.Position;
		int stateTicks = 0, shotWarnTicks = 0, dashWarnTicks = 0, ringWarnTicks = 0, spiralWarnTicks = 0;
		int rings = 0, orbits = 0, dashesSinceRing = 0, dashesCounted = 0, spirals = 0, spiralTicks = 0;
		bool counting = false;
		List<(Vector3 Position, float Speed, float Damage)> spiralShots = new();
		List<MotherBehavior.AttackState> sequence = new();

		for (int tick = 0; tick < 9000 && (rings < 2 || (phase == 3 && spirals < 2)); tick++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			MotherBehavior.AttackState state = behavior.State;
			Vector3 position = boss.Position;

			int warnings = (behavior.ShotWarningActive ? 1 : 0) + (behavior.DashWarningActive ? 1 : 0) + (behavior.RingWarningActive ? 1 : 0)
				+ (behavior.PhaseChangeActive ? 1 : 0) + (behavior.SpiralWarningActive ? 1 : 0) + (behavior.SpiralActive ? 1 : 0);
			Require(warnings <= 1, "Dos avisos o ataques a la vez");

			// Avisos completos: cada aviso se mide hasta que se apaga.
			shotWarnTicks = Count(behavior.ShotWarningActive, shotWarnTicks, MinShotWarningTicks, "disparo");
			dashWarnTicks = Count(behavior.DashWarningActive, dashWarnTicks, MinWarningTicks, "embestida");
			ringWarnTicks = Count(behavior.RingWarningActive, ringWarnTicks, MinWarningTicks, "anillo");
			spiralWarnTicks = Count(behavior.SpiralWarningActive, spiralWarnTicks, MinWarningTicks, "espiral");
			Require(phase == 3 || (!behavior.SpiralWarningActive && !behavior.SpiralActive), "La espiral solo existe en la fase 3");
			spiralTicks = behavior.SpiralActive ? spiralTicks + 1 : spiralTicks;

			Vector3 step = position - previousPosition;
			step.Y = 0.0f;
			if (step.Length() > 0.001f)
			{
				Require(previous == MotherBehavior.AttackState.Approach || previous == MotherBehavior.AttackState.Dash
					|| previous == MotherBehavior.AttackState.Orbit,
					$"The Mother se movió durante {previous} (de {previousPosition} a {position})");
			}

			Require(Mathf.Abs(position.X) <= limit.X + Tolerance && Mathf.Abs(position.Z) <= limit.Y + Tolerance, $"Salió de la arena: {position}");
			// Con su cuerpo (radio 1,2) queda a más de 1 m de las paredes y por tanto de las puertas, que están en ellas.
			Require(Mathf.Abs(position.X) + BodyRadius <= wallClearance && Mathf.Abs(position.Z) + BodyRadius <= wallClearance,
				$"Su cuerpo llegó demasiado cerca de la pared y las puertas: {position}");

			if (state != previous && (state == MotherBehavior.AttackState.RingWarning || state == MotherBehavior.AttackState.SpiralWarning)) sequence.Add(state);
			stateTicks = state == previous ? stateTicks + 1 : 0;
			Require(stateTicks < 1200, $"Atascado en el estado {state} durante {stateTicks} pasos");

			if (state == MotherBehavior.AttackState.Orbit && previous != MotherBehavior.AttackState.Orbit) orbits++;
			if (previous == MotherBehavior.AttackState.PhaseChange && state != MotherBehavior.AttackState.PhaseChange)
			{
				counting = true;
				dashesSinceRing = 0;
			}

			if (counting && state == MotherBehavior.AttackState.Dash && previous != MotherBehavior.AttackState.Dash)
			{
				dashesSinceRing++;
				dashesCounted++;
			}

			bool ringFired = previous == MotherBehavior.AttackState.RingWarning && state == MotherBehavior.AttackState.Recovery;
			bool spiralDone = previous == MotherBehavior.AttackState.Spiral && state == MotherBehavior.AttackState.Recovery;
			List<EnemyProjectile> fresh = new();
			foreach (Node node in room.ProjectilesRoot.GetChildren())
			{
				if (node is EnemyProjectile projectile && seen.Add(projectile.GetInstanceId())) fresh.Add(projectile);
			}

			// Los proyectiles de la espiral se acumulan hasta que termina; el resto de comprobaciones no los ven.
			if (previous == MotherBehavior.AttackState.Spiral || state == MotherBehavior.AttackState.Spiral)
			{
				// Se guardan sus datos al verlos: los que chocan con una pared se liberan antes de que acabe la espiral.
				foreach (EnemyProjectile projectile in fresh) spiralShots.Add((projectile.GlobalPosition, projectile.Speed, projectile.Damage));
				fresh.Clear();
			}

			if (spiralDone)
			{
				spirals++;
				CheckSpiralShots(spiralShots, boss, level.Player, behavior);
				Require(spiralTicks >= 110 && spiralTicks <= 135, $"La espiral dura {spiralTicks} pasos, no unos 2 s");
				if (counting) Require(dashesSinceRing == settings.DashCount, $"Fase {phase}: {dashesSinceRing} embestidas antes de la espiral, no {settings.DashCount}");
				dashesSinceRing = 0;
				spiralShots.Clear();
				spiralTicks = 0;
			}

			if (ringFired)
			{
				rings++;
				CheckRingShot(fresh, boss, level.Player, behavior, settings, phase);
				if (counting) Require(dashesSinceRing == settings.DashCount, $"Fase {phase}: {dashesSinceRing} embestidas antes del anillo, no {settings.DashCount}");
				dashesSinceRing = 0;
			}
			else
			{
				foreach (EnemyProjectile projectile in fresh)
				{
					Require(previous == MotherBehavior.AttackState.Volley || previous == MotherBehavior.AttackState.RingWarning,
						$"Proyectil fuera de la salva o del anillo (estado {previous})");
				}
			}

			previous = state;
			previousPosition = position;
		}

		Require(rings >= 2, $"Fase {phase}: se vieron {rings} anillos");
		Require(phase < 3 || spirals >= 2, $"Fase 3: se vieron {spirals} espirales");
		Require(phase < 3 || (sequence.Count >= 3 && sequence[0] == MotherBehavior.AttackState.RingWarning
			&& sequence.Zip(sequence.Skip(1), (a, b) => a != b).All(different => different)),
			$"Fase 3: el anillo y la espiral deben alternar empezando por el anillo ({string.Join(",", sequence)})");
		Require(phase == 3 || sequence.All(state => state == MotherBehavior.AttackState.RingWarning), "Fase 2: solo anillos");
		Require(orbits >= 1, $"Fase {phase}: se vio la órbita entre ataques");
		Require(phase < 3 || dashesCounted >= 4, $"Fase 3: embestidas encadenadas ({dashesCounted})");
		GD.Print($"Fase {phase}: {rings} anillos, {spirals} espirales, {orbits} órbitas, avisos completos, dentro de la arena y sin atascos: OK");
		await BossTestHarness.Close(tree, level);
	}

	private void CheckSpiralShots(List<(Vector3 Position, float Speed, float Damage)> shots, EnemyBase boss, PlayerController player, MotherBehavior behavior)
	{
		Vector3 gap = behavior.SpiralGapDirection;
		int[] perArm = new int[MotherSpiral.Arms];
		foreach (var shot in shots)
		{
			// Brazo al que pertenece cada proyectil: su sector de barrido, medido desde el centro del hueco.
			float angle = Mathf.PosMod(Mathf.RadToDeg(gap.SignedAngleTo(shot.Position with { Y = 0.0f } - boss.GlobalPosition with { Y = 0.0f }, Vector3.Up))
				- MotherSpiral.GapDegrees * 0.5f + 2.0f, 360.0f);
			perArm[Mathf.Clamp((int)(angle / MotherSpiral.ArmSpacingDegrees), 0, MotherSpiral.Arms - 1)]++;
		}

		Vector2 half = behavior.ArenaSize / 2.0f;
		Vector3 at = boss.Position;
		Require(shots.Count <= MotherSpiral.TotalProjectiles && shots.Count >= MotherSpiral.TotalProjectiles - 6,
			$"La espiral tiene {shots.Count} proyectiles, no {MotherSpiral.TotalProjectiles}. Jefe en ({at.X:0.00}, {at.Z:0.00}); vivos por brazo [{string.Join(", ", perArm)}]; "
			+ $"distancia a las paredes: oeste {at.X + half.X:0.00}, este {half.X - at.X:0.00}, norte {at.Z + half.Y:0.00}, sur {half.Y - at.Z:0.00}; "
			+ $"hueco hacia ({gap.X:0.00}, {gap.Z:0.00}), giro {(behavior.SpiralReverse ? "inverso" : "directo")}; "
			+ $"Noah en ({player.GlobalPosition.X:0.00}, {player.GlobalPosition.Z:0.00}) a {(player.GlobalPosition with { Y = 0.0f }).DistanceTo(boss.GlobalPosition with { Y = 0.0f }):0.00} m del jefe; "
			+ $"omitidos previstos por Noah (SpawnsOnTarget) por brazo [{string.Join(", ", SkippedPerArm(boss, player, behavior))}]");
		Vector3 origin = boss.GlobalPosition with { Y = 0.0f };
		Vector3 target = player.GlobalPosition with { Y = 0.0f };
		Vector2 arena = behavior.ArenaSize;
		Require(Mathf.Abs(origin.X) <= arena.X / 2.0f - behavior.ArenaMargin + Tolerance && Mathf.Abs(origin.Z) <= arena.Y / 2.0f - behavior.ArenaMargin + Tolerance,
			"The Mother no está en la arena durante la espiral");
		foreach (var shot in shots)
		{
			Vector3 offset = (shot.Position with { Y = 0.0f }) - origin;
			Require(offset.Length() < behavior.RingSpawnDistance + 0.5f, $"Un proyectil de la espiral nace a {offset.Length():0.00} m del jefe");
			Require(Mathf.Abs(shot.Position.X) <= arena.X / 2.0f && Mathf.Abs(shot.Position.Z) <= arena.Y / 2.0f, "Un proyectil de la espiral nace fuera de la sala");
			float angle = Mathf.RadToDeg(gap.SignedAngleTo(offset, Vector3.Up));
			Require(!MotherSpiral.InGap(angle, 1.5f), $"Un proyectil de la espiral invade un hueco ({angle:0.0}°)");
			Require(((shot.Position with { Y = 0.0f }) - target).Length() >= behavior.RingSafeDistance - 0.3f, "Un proyectil de la espiral nace encima de Noah");
			Require(shot.Speed == MotherSpiral.ProjectileSpeed && shot.Damage <= 1.0f, "Un proyectil de la espiral va más rápido o hace más daño de lo permitido");
		}

		float towardPlayer = Mathf.RadToDeg((target - origin).AngleTo(gap));
		Require(Mathf.Abs(towardPlayer - MotherSpiral.GapOffsetDegrees) < 3.0f, $"El hueco está a {towardPlayer:0.0}° de Noah, no a {MotherSpiral.GapOffsetDegrees:0.0}°");
	}

	// Cuántos disparos de cada brazo omite el comportamiento por nacer a menos de RingSafeDistance de Noah (la misma función que usa él).
	private static int[] SkippedPerArm(EnemyBase boss, PlayerController player, MotherBehavior behavior)
	{
		int[] skipped = new int[MotherSpiral.Arms];
		for (int emission = 0; emission < MotherSpiral.Emissions; emission++)
		{
			for (int arm = 0; arm < MotherSpiral.Arms; arm++)
			{
				Vector3 direction = MotherSpiral.Direction(arm, emission, behavior.SpiralGapDirection, behavior.SpiralReverse);
				if (MotherRing.SpawnsOnTarget(boss.GlobalPosition, direction, behavior.RingSpawnDistance, player.GlobalPosition, behavior.RingSafeDistance)) skipped[arm]++;
			}
		}

		return skipped;
	}

	// Cuenta los pasos que dura un aviso; cuando se apaga comprueba que duró lo debido.
	private int Count(bool active, int ticks, int minimum, string name)
	{
		if (active) return ticks + 1;
		if (ticks > 0) Require(ticks >= minimum, $"Aviso de {name} demasiado corto ({ticks} pasos)");
		return 0;
	}

	private void CheckRingShot(List<EnemyProjectile> shots, EnemyBase boss, PlayerController player, MotherBehavior behavior,
		MotherPhases.Settings settings, int phase)
	{
		Require(shots.Count <= settings.RingProjectiles && shots.Count >= settings.RingProjectiles - 2,
			$"El anillo de la fase {phase} tiene {shots.Count} proyectiles, no {settings.RingProjectiles}");
		Vector3 gap = behavior.RingGapDirection;
		Vector3 origin = boss.GlobalPosition with { Y = 0.0f };
		Vector3 target = player.GlobalPosition with { Y = 0.0f };
		float gapHalf = settings.RingGapDegrees / 2.0f;
		foreach (EnemyProjectile shot in shots)
		{
			Vector3 offset = shot.GlobalPosition with { Y = 0.0f };
			offset -= origin;
			Require(Mathf.Abs(offset.Length() - behavior.RingSpawnDistance) < 0.3f, $"Un proyectil del anillo nace a {offset.Length():0.00} m del jefe");
			Require(Mathf.RadToDeg(offset.AngleTo(gap)) >= gapHalf - 2.0f, "Un proyectil del anillo invade el hueco");
			Require(((shot.GlobalPosition with { Y = 0.0f }) - target).Length() >= behavior.RingSafeDistance - 0.3f, "Un proyectil nace encima de Noah");
			Require(shot.Speed <= 5.01f && shot.Damage <= 1.0f, "Un proyectil del anillo es más rápido o más dañino de lo permitido");
		}

		// En la fase 2 el hueco mira a Noah; en la 3 está girado 25° para que haya que moverse.
		float towardPlayer = Mathf.RadToDeg((target - origin).AngleTo(gap));
		float expected = Mathf.Abs(settings.RingGapOffsetDegrees);
		Require(Mathf.Abs(towardPlayer - expected) < 3.0f, $"El hueco está a {towardPlayer:0.0}° de Noah, no a {expected:0.0}°");
	}

	private async Task CheckRetryDuringSpiral()
	{
		SceneTree tree = GetTree();
		var (level, room, boss, behavior) = await OpenBoss(tree, this);
		boss.Health.TakeDamage(130.0f);
		await BossTestHarness.WaitTicks(tree, () => behavior.SpiralActive && room.ProjectilesRoot.GetChildCount() > 0, 9000, "la espiral con proyectiles en vuelo");
		Require(behavior.Phase == 3 && room.ProjectilesRoot.GetChildCount() > 0, "En plena espiral de la fase 3");
		await BossTestHarness.Close(tree, level);

		Require(!GodotObject.IsInstanceValid(boss), "El jefe desaparece con el nivel");
		Require(tree.GetNodesInGroup("enemy_projectiles").Count == 0 && tree.GetNodesInGroup("bosses").Count == 0,
			"No quedan proyectiles ni jefes tras cerrar en plena espiral");

		// Un jefe nuevo no hereda nada de la espiral.
		var (level2, room2, boss2, behavior2) = await OpenBoss(tree, this);
		Require(behavior2.Phase == 1 && !behavior2.SpiralActive && !behavior2.SpiralWarningActive && room2.ProjectilesRoot.GetChildCount() == 0
			&& behavior2.State == MotherBehavior.AttackState.Entrance, "Un jefe nuevo empieza sin espiral ni proyectiles");
		await BossTestHarness.Close(tree, level2);
		GD.Print("Cerrar en plena espiral no deja residuos: OK");
	}

	// --- 5. Reintentar y volver al menú -----------------------------------------------------------------------------------

	private async Task CheckRetryLeavesNothing()
	{
		SceneTree tree = GetTree();
		var (level, room, boss, behavior) = await OpenBoss(tree, this);
		boss.Health.TakeDamage(70.0f);
		await BossTestHarness.WaitTicks(tree, () => room.ProjectilesRoot.GetChildCount() > 0, 3000, "algún proyectil en vuelo");
		Require(behavior.Phase == 2, "El jefe está en la fase 2");

		// Se cierra el nivel (reintentar o volver al menú) en pleno combate; la invulnerabilidad no puede quedarse en ningún sitio.
		HealthComponent health = boss.Health;
		boss.Health.TakeDamage(60.0f);
		Require(behavior.Phase == 3 && health.Invulnerable, "En pleno cambio de fase");
		await BossTestHarness.Close(tree, level);

		Require(!GodotObject.IsInstanceValid(boss), "El jefe desaparece con el nivel");
		Require(tree.GetNodesInGroup("enemy_projectiles").Count == 0 && tree.GetNodesInGroup("bosses").Count == 0,
			"No quedan proyectiles ni jefes");

		// Una partida nueva empieza otra vez en la fase 1 con la vida completa.
		var (level2, room2, boss2, behavior2) = await OpenBoss(tree, this);
		Require(behavior2.Phase == 1 && !boss2.Health.Invulnerable && boss2.Health.CurrentHealth == 180.0f
			&& behavior2.State == MotherBehavior.AttackState.Entrance, "Un jefe nuevo empieza limpio");
		await BossTestHarness.Close(tree, level2);
		GD.Print("Reintentar y volver al menú no dejan residuos: OK");
	}
}
