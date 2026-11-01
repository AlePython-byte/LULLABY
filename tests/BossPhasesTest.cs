using System;
using System.Collections.Generic;
using System.IO;
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
///   4. Daño: ningún proyectil ni el contacto del jefe hace más de 1 de daño.
///   5. Reintentar y volver al menú: sin proyectiles ni jefe, aunque se cierre en pleno cambio de fase.
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
			CheckOrbit();
			CheckHitDamage();
			await CheckPhaseChange();
			await CheckSkippedPhase();
			await CheckCombat(2, 70.0f);
			await CheckCombat(3, 130.0f);
			await CheckRetryLeavesNothing();
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

		Vector2 arena = behavior.ArenaSize;
		Vector2 limit = new(arena.X / 2.0f - behavior.ArenaMargin, arena.Y / 2.0f - behavior.ArenaMargin);
		float wallClearance = Mathf.Min(arena.X, arena.Y) / 2.0f - 1.0f;

		HashSet<ulong> seen = new();
		MotherBehavior.AttackState previous = behavior.State;
		Vector3 previousPosition = boss.Position;
		int stateTicks = 0, shotWarnTicks = 0, dashWarnTicks = 0, ringWarnTicks = 0;
		int rings = 0, orbits = 0, dashesSinceRing = 0, dashesCounted = 0;
		bool counting = false;

		for (int tick = 0; tick < 5000 && rings < 2; tick++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			MotherBehavior.AttackState state = behavior.State;
			Vector3 position = boss.Position;

			int warnings = (behavior.ShotWarningActive ? 1 : 0) + (behavior.DashWarningActive ? 1 : 0) + (behavior.RingWarningActive ? 1 : 0)
				+ (behavior.PhaseChangeActive ? 1 : 0);
			Require(warnings <= 1, "Dos avisos o ataques a la vez");

			// Avisos completos: cada aviso se mide hasta que se apaga.
			shotWarnTicks = Count(behavior.ShotWarningActive, shotWarnTicks, MinShotWarningTicks, "disparo");
			dashWarnTicks = Count(behavior.DashWarningActive, dashWarnTicks, MinWarningTicks, "embestida");
			ringWarnTicks = Count(behavior.RingWarningActive, ringWarnTicks, MinWarningTicks, "anillo");

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
			List<EnemyProjectile> fresh = new();
			foreach (Node node in room.ProjectilesRoot.GetChildren())
			{
				if (node is EnemyProjectile projectile && seen.Add(projectile.GetInstanceId())) fresh.Add(projectile);
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
		Require(orbits >= 1, $"Fase {phase}: se vio la órbita entre ataques");
		Require(phase < 3 || dashesCounted >= 4, $"Fase 3: embestidas encadenadas ({dashesCounted})");
		GD.Print($"Fase {phase}: {rings} anillos, {orbits} órbitas, avisos completos, dentro de la arena y sin atascos: OK");
		await BossTestHarness.Close(tree, level);
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
