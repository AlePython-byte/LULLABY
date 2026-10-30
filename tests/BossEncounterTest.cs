using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el encuentro con The Mother con el código real del juego (TestLevel, física y salas). Modo headless:
///   godot --headless --path . res://tests/BossEncounterTest.tscn
/// Casos:
///   1. Con el override del jefe: la sala del jefe tiene una Mother y cero básicos, aunque la plantilla traiga seis puntos.
///   2. Sin override (composición histórica): seis básicos, ninguna Mother, y despejarla NO termina la partida.
///   3. Una fase completa: cada disparo va precedido de su aviso completo, cada embestida de su aviso completo, nunca hay
///      dos ataques a la vez (solo se mueve en Approach/Dash, solo dispara en Volley), cada salva tiene tres disparos, el
///      jefe no sale de la arena y la embestida no pasa de DashDistance.
///   4. Embestida contra una pared: con el jefe pegado al límite de la arena, no lo supera.
///   5. MotherArena.LimitVelocity (función pura): límites, diagonal, hacia dentro, hacia fuera y delta nulo.
/// El jugador se queda quieto y tiene vida de sobra. Sale con código 0 si todo pasa.
/// </summary>
public partial class BossEncounterTest : Node
{
	// Margen sobre el aviso configurado (0.7 s = 42 pasos, 1.0 s = 60 pasos): tolera el redondeo de los temporizadores.
	private const int MinShotWarningTicks = 38;
	private const int MinDashWarningTicks = 54;
	private const float PositionTolerance = 0.02f;

	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== BossEncounterTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckArenaLimit();
			await CheckCompositionWithMother();
			await CheckHistoricComposition();
			await CheckAttackSequence();
			await CheckDashRespectsWalls();
			GD.Print($"BossEncounterTest: {_checks} comprobaciones. RESULTADO: OK");
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

	private static void CountEnemies(RoomController room, out int mothers, out int basics, out int others)
	{
		mothers = basics = others = 0;
		foreach (Node child in room.GetNode("Enemies").GetChildren())
		{
			if (child.IsQueuedForDeletion()) continue;
			if (child is EnemyBase enemy && enemy.Behavior<MotherBehavior>() != null) mothers++;
			else if (child is BasicEnemy) basics++;
			else others++;
		}
	}

	private async Task CheckCompositionWithMother()
	{
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await BossTestHarness.GoToBoss(GetTree(), level);
		RoomController room = level.Rooms.CurrentRoom;
		Require(room.Data.SpawnPoints.Length == 6 && room.Data.TemplateId == "jefe",
			"La sala del jefe sigue usando la plantilla histórica (seis puntos)");
		await BossTestHarness.WaitUntil(GetTree(), () => room.BossEnemy != null, 300, "la aparición de The Mother");
		// StartDelay de la sala es 1 s (60 pasos): si el override dejara salir a los básicos, ya habrían nacido.
		await BossTestHarness.Frames(GetTree(), 90);

		CountEnemies(room, out int mothers, out int basics, out int others);
		Require(mothers == 1 && basics == 0 && others == 0,
			$"Con override: una Mother y cero básicos (hay {mothers} Mother, {basics} básicos, {others} otros)");
		Require(room.State == RoomState.Combat, "El combate con The Mother está en curso");
		GD.Print("Con override: una Mother y cero básicos: OK");
		await BossTestHarness.Close(GetTree(), level);
	}

	private async Task CheckHistoricComposition()
	{
		RoomTemplate template = GD.Load<RoomTemplate>("res://resources/rooms/templates/jefe.tres");
		Require(template.Size == new Vector2(26.0f, 26.0f) && template.SpawnPoints.Length == 6 && template.SpawnKinds.Length == 0
			&& template.EnemyScene.ResourcePath == "res://scenes/enemies/BasicEnemy.tscn",
			"jefe.tres conserva sus seis básicos");

		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, withMother: false);
		await BossTestHarness.GoToBoss(GetTree(), level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(GetTree(), () => room.GetNode("Enemies").GetChildCount() >= 6, 400, "los seis básicos");
		await BossTestHarness.Frames(GetTree(), 30);

		CountEnemies(room, out int mothers, out int basics, out int others);
		Require(basics == 6 && mothers == 0 && others == 0 && room.BossEnemy == null,
			$"Sin override: seis básicos y ninguna Mother (hay {basics} básicos, {mothers} Mother, {others} otros)");

		foreach (Node child in room.GetNode("Enemies").GetChildren())
		{
			((EnemyBase)child).Kill();
		}

		await BossTestHarness.Frames(GetTree(), 10);
		Require(room.State == RoomState.Cleared, "Los seis básicos muertos despejan la sala");
		Require(!level.Run.RunEnded && !level.Rooms.RunEnded && !level.Victory.IsPresented,
			"Despejar la sala con la composición histórica no termina la partida");
		GD.Print("Sin override: seis básicos y sin victoria: OK");
		await BossTestHarness.Close(GetTree(), level);
	}

	private async Task CheckAttackSequence()
	{
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		SceneTree tree = GetTree();
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		EnemyBase boss = room.BossEnemy;
		MotherBehavior behavior = boss.Behavior<MotherBehavior>();
		Vector2 arena = behavior.ArenaSize;
		Vector2 limit = new(arena.X / 2.0f - behavior.ArenaMargin, arena.Y / 2.0f - behavior.ArenaMargin);

		HashSet<ulong> seen = new();
		MotherBehavior.AttackState previous = behavior.State;
		Vector3 previousPosition = boss.Position;
		int shotWarningTicks = 0, dashWarningTicks = 0, shotsInVolley = 0, volleys = 0, dashes = 0, totalShots = 0;
		bool dashMoved = false;
		Vector3 dashStart = Vector3.Zero;

		for (int tick = 0; tick < 4000 && !(volleys >= 2 && dashes >= 1); tick++)
		{
			// Este punto es el inicio del paso de física: el estado es el de después del paso anterior.
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			MotherBehavior.AttackState state = behavior.State;
			Vector3 position = boss.Position;

			Require(!(behavior.ShotWarningActive && behavior.DashWarningActive), "Dos avisos de ataque a la vez");
			if (behavior.ShotWarningActive) shotWarningTicks++;
			if (behavior.DashWarningActive) dashWarningTicks++;

			foreach (Node projectile in room.ProjectilesRoot.GetChildren())
			{
				if (!seen.Add(projectile.GetInstanceId())) continue;
				totalShots++;
				shotsInVolley++;
				Require(previous == MotherBehavior.AttackState.Volley
					&& (state == MotherBehavior.AttackState.Volley || state == MotherBehavior.AttackState.BeforeDash),
					$"Disparo fuera de la salva (estado {previous} → {state})");
				Require(shotWarningTicks >= MinShotWarningTicks,
					$"Disparo {totalShots} sin el aviso completo ({shotWarningTicks} pasos de aviso)");
				shotWarningTicks = 0;
			}

			// Solo cuenta el plano: en el primer paso la física asienta el cuerpo sobre el suelo y varía la Y una diezmilésima.
			Vector3 step = position - previousPosition;
			step.Y = 0.0f;
			bool moved = step.Length() > 0.001f;
			if (moved)
			{
				Require(previous == MotherBehavior.AttackState.Approach || previous == MotherBehavior.AttackState.Dash,
					$"The Mother se movió durante {previous} (paso {tick}, de {previousPosition} a {position}, estado actual {state}): solo puede moverse al acercarse o embestir");
			}

			if (moved && previous == MotherBehavior.AttackState.Dash)
			{
				if (!dashMoved)
				{
					dashMoved = true;
					dashStart = previousPosition;
					Require(dashWarningTicks >= MinDashWarningTicks,
						$"Embestida sin el aviso completo ({dashWarningTicks} pasos de aviso)");
				}

				Require(position.DistanceTo(dashStart) <= behavior.DashDistance + 0.1f,
					$"La embestida superó DashDistance ({position.DistanceTo(dashStart):0.00} m)");
			}

			Require(Mathf.Abs(position.X) <= limit.X + PositionTolerance && Mathf.Abs(position.Z) <= limit.Y + PositionTolerance,
				$"The Mother salió de la arena: {position}");

			if (previous == MotherBehavior.AttackState.Volley && state != MotherBehavior.AttackState.Volley)
			{
				Require(shotsInVolley == behavior.VolleyShots, $"Una salva tuvo {shotsInVolley} disparos, no {behavior.VolleyShots}");
				volleys++;
				shotsInVolley = 0;
				shotWarningTicks = 0;
			}

			if (previous == MotherBehavior.AttackState.Dash && state != MotherBehavior.AttackState.Dash)
			{
				if (dashMoved) dashes++;
				dashMoved = false;
				dashWarningTicks = 0;
			}

			previous = state;
			previousPosition = position;
		}

		Require(volleys >= 2 && dashes >= 1, $"No se observó un ciclo completo ({volleys} salvas, {dashes} embestidas)");
		GD.Print($"Ciclo de ataques: {volleys} salvas ({totalShots} disparos) y {dashes} embestida(s) con avisos completos, sin solaparse: OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task CheckDashRespectsWalls()
	{
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		SceneTree tree = GetTree();
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		EnemyBase boss = room.BossEnemy;
		MotherBehavior behavior = boss.Behavior<MotherBehavior>();
		float limitX = behavior.ArenaSize.X / 2.0f - behavior.ArenaMargin;

		// Jefe a un metro del límite de la arena y Noah más allá, a la altura de la pared: la embestida apunta a la pared y,
		// sin límite, avanzaría hasta casi tocar a Noah (más de 0.5 m por encima del límite).
		await BossTestHarness.WaitTicks(tree, () => behavior.State == MotherBehavior.AttackState.BeforeDash, 3000, "el estado BeforeDash");
		boss.Position = new Vector3(limitX - 1.0f, boss.Position.Y, 0.0f);
		level.Player.GlobalPosition = new Vector3(limitX + 2.2f, level.Player.GlobalPosition.Y, 0.0f);
		level.Player.Velocity = Vector3.Zero;

		bool sawDash = false;
		float maxX = boss.Position.X;
		for (int tick = 0; tick < 1200; tick++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			maxX = Mathf.Max(maxX, boss.Position.X);
			Require(boss.Position.X <= limitX + PositionTolerance, $"La embestida atravesó el límite de la pared: x = {boss.Position.X:0.000}");
			sawDash |= behavior.State == MotherBehavior.AttackState.Dash;
			if (sawDash && behavior.State == MotherBehavior.AttackState.Recovery) break;
		}

		Require(sawDash && behavior.State == MotherBehavior.AttackState.Recovery, "La embestida contra la pared terminó");
		Require(maxX >= limitX - 0.05f, $"La embestida llegó hasta el límite de la arena (máximo x = {maxX:0.000})");
		GD.Print($"Embestida contra la pared: llegó a x = {maxX:0.000} y no pasó del límite {limitX:0.0}: OK");
		await BossTestHarness.Close(tree, level);
	}

	private void CheckArenaLimit()
	{
		const float delta = 1.0f / 60.0f;
		Vector2 room = new(26.0f, 26.0f);

		Vector3 velocity = MotherArena.LimitVelocity(new Vector3(9.95f, 0.0f, 0.0f), new Vector3(8.0f, 0.0f, 0.0f), room, 3.0f, delta);
		Require(9.95f + velocity.X * delta <= 10.0f + 0.0001f, "Pegado a la pared no avanza más allá del límite");

		velocity = MotherArena.LimitVelocity(new Vector3(9.0f, 0.0f, 0.0f), new Vector3(8.0f, 0.0f, 0.0f), room, 3.0f, 1.0f);
		Require(Mathf.IsEqualApprox(9.0f + velocity.X, 10.0f), "Con un paso grande se queda exactamente en el límite");

		Vector3 diagonal = new(6.0f, 0.0f, 8.0f);
		velocity = MotherArena.LimitVelocity(new Vector3(9.5f, 0.0f, 0.0f), diagonal, room, 3.0f, 1.0f);
		Require(velocity.Length() < diagonal.Length() && velocity.Normalized().IsEqualApprox(diagonal.Normalized()),
			"En diagonal conserva la dirección de la trayectoria");

		Vector3 inward = new(-8.0f, 0.0f, 0.0f);
		Require(MotherArena.LimitVelocity(new Vector3(9.9f, 0.0f, 0.0f), inward, room, 3.0f, delta).IsEqualApprox(inward),
			"Hacia dentro de la arena no se limita");
		Require(MotherArena.LimitVelocity(new Vector3(10.5f, 0.0f, 0.0f), new Vector3(8.0f, 0.0f, 0.0f), room, 3.0f, delta)
			.IsZeroApprox(), "Fuera del límite y hacia fuera se detiene");
		Require(MotherArena.LimitVelocity(Vector3.Zero, new Vector3(8.0f, 0.0f, 0.0f), room, 3.0f, 0.0f) == Vector3.Zero,
			"Con delta nulo no hay movimiento");

		Vector2 wide = new(28.0f, 16.0f);
		velocity = MotherArena.LimitVelocity(new Vector3(0.0f, 0.0f, 4.9f), new Vector3(0.0f, 0.0f, 8.0f), wide, 3.0f, 1.0f);
		Require(Mathf.IsEqualApprox(4.9f + velocity.Z, 5.0f), "En una sala ancha el límite en Z es la mitad del fondo menos el margen");
		GD.Print("MotherArena.LimitVelocity: límites, diagonal, hacia dentro y hacia fuera: OK");
	}
}
