using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba la pausa con el código real del juego (TestLevel). Modo headless:
///   godot --headless --path . res://tests/PauseTest.tscn
/// Casos:
///   1. Pausa durante el aviso de un disparo y de una embestida de The Mother: el jefe y su aviso quedan congelados, no
///      nace ningún proyectil ni empieza la carga mientras dura la pausa, y al reanudar el aviso continúa donde estaba
///      (los pasos de aviso antes y después de la pausa suman el aviso completo).
///   2. El retraso de la sala (StartDelay) también se detiene: con la pausa no nacen los seis básicos.
///   3. Esc y P abren y cierran la pausa; Esc dentro de Controles solo cierra el panel.
///   4. Esc/P se ignoran durante un cambio de sala, la victoria y la derrota.
///   5. Reiniciar partida (semilla nueva, misma dificultad) y Volver al menú despausan antes de cambiar de escena.
/// Convierte el nivel en la escena actual cuando hay recarga o cambio de escena. Sale con código 0 si todo pasa.
/// </summary>
public partial class PauseTest : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== PauseTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			await CheckBossAttackPause("aviso de disparo", WarningKind.Shot, 42);
			await CheckBossAttackPause("aviso de embestida", WarningKind.Dash, 60);
			await CheckRoomDelayPause();
			await CheckKeys();
			await CheckIgnoredWhenNotPlayable();
			await CheckRestartAndMenu();
			GD.Print($"PauseTest: {_checks} comprobaciones. RESULTADO: OK");
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

	private enum WarningKind { Shot, Dash }

	private static async Task<BossTestHarness.Level> OpenAtBoss(Node parent, SceneTree tree)
	{
		BossTestHarness.Level level = BossTestHarness.Open(parent, BossTestHarness.Seed);
		await BossTestHarness.GoToBoss(tree, level);
		await BossTestHarness.WaitUntil(tree, () => level.Rooms.CurrentRoom.BossEnemy != null, 300, "la aparición de The Mother");
		return level;
	}

	private async Task SendKey(Key key)
	{
		SceneTree tree = GetTree();
		Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
		await BossTestHarness.Frames(tree, 2);
		Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
		await BossTestHarness.Frames(tree, 2);
	}

	private async Task CheckBossAttackPause(string name, WarningKind kind, int expectedWarningTicks)
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = await OpenAtBoss(this, tree);
		RoomController room = level.Rooms.CurrentRoom;
		EnemyBase boss = room.BossEnemy;
		MotherBehavior behavior = boss.Behavior<MotherBehavior>();
		PauseMenu pause = level.Root.GetNode<PauseMenu>("PauseMenu");
		Func<bool> warning = kind == WarningKind.Shot ? () => behavior.ShotWarningActive : () => behavior.DashWarningActive;

		await BossTestHarness.WaitTicks(tree, warning, 4000, name);
		int before = 1;
		for (int i = 0; i < 10; i++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			if (warning()) before++;
		}

		Require(before >= 10 && warning(), $"[{name}] El aviso está en curso al pausar ({before} pasos)");
		Vector3 position = boss.Position;
		MotherBehavior.AttackState state = behavior.State;
		int projectiles = room.ProjectilesRoot.GetChildCount();
		Require(pause.TryPause() && tree.Paused, $"[{name}] Se puede pausar durante el ataque");

		for (int i = 0; i < 150; i++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			Require(behavior.State == state && warning() && boss.Position.IsEqualApprox(position)
				&& room.ProjectilesRoot.GetChildCount() == projectiles,
				$"[{name}] El jefe sigue congelado en el paso {i + 1} de la pausa");
		}

		pause.Resume();
		Require(!tree.Paused, $"[{name}] Reanudar despausa");
		int after = 0;
		bool fired = false;
		for (int i = 0; i < 200 && !fired; i++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			if (warning()) after++;
			fired = kind == WarningKind.Shot
				? room.ProjectilesRoot.GetChildCount() > projectiles
				: new Vector3(boss.Position.X - position.X, 0.0f, boss.Position.Z - position.Z).Length() > 0.001f;
		}

		Require(fired, $"[{name}] Al reanudar, el ataque termina su aviso y se ejecuta");
		int total = before + after;
		Require(total >= expectedWarningTicks - 4 && total <= expectedWarningTicks + 4,
			$"[{name}] El aviso continúa donde estaba: {before} + {after} = {total} pasos (esperado ~{expectedWarningTicks})");
		GD.Print($"Pausa en el {name} del jefe: congelado, y el aviso suma {total} pasos (esperado ~{expectedWarningTicks}): OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task CheckRoomDelayPause()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, withMother: false);
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		PauseMenu pause = level.Root.GetNode<PauseMenu>("PauseMenu");
		Node enemies = room.GetNode("Enemies");

		await BossTestHarness.WaitTicks(tree, () => room.State == RoomState.Combat, 300, "el inicio del combate");
		Require(enemies.GetChildCount() == 0 && pause.TryPause(), "Pausa con el retraso de la sala en marcha");
		for (int i = 0; i < 150; i++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		}

		Require(enemies.GetChildCount() == 0, "Con la pausa, el retraso de la sala (1 s) no avanza y no nacen enemigos");
		pause.Resume();
		await BossTestHarness.WaitUntil(tree, () => enemies.GetChildCount() >= 6, 400, "los seis básicos tras reanudar");
		GD.Print("Pausa durante el retraso de la sala: los enemigos nacen al reanudar: OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task CheckKeys()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		PauseMenu pause = level.Root.GetNode<PauseMenu>("PauseMenu");
		await BossTestHarness.Frames(tree, 3);

		Require(InputMap.HasAction(PauseMenu.PauseAction), "La acción de pausa existe");
		Require(InputMap.ActionHasEvent(PauseMenu.PauseAction, new InputEventKey { PhysicalKeycode = Key.Escape })
			&& InputMap.ActionHasEvent(PauseMenu.PauseAction, new InputEventKey { PhysicalKeycode = Key.P }), "Esc y P abren la pausa");

		foreach (Key key in new[] { Key.Escape, Key.P })
		{
			await SendKey(key);
			Require(pause.IsOpen && tree.Paused, $"{key} abre la pausa y congela el juego");
			await SendKey(key);
			Require(!pause.IsOpen && !tree.Paused, $"{key} la cierra");
		}

		pause.TryPause();
		pause.Controls.Open();
		Require(pause.Controls.Visible, "Controles se abre dentro de la pausa");
		await SendKey(Key.Escape);
		Require(!pause.Controls.Visible && pause.IsOpen && tree.Paused, "Esc dentro de Controles solo cierra el panel");
		await SendKey(Key.Escape);
		Require(!pause.IsOpen && !tree.Paused, "Esc otra vez cierra la pausa");
		GD.Print("Esc y P abren y cierran la pausa; Controles dentro de la pausa: OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task CheckIgnoredWhenNotPlayable()
	{
		SceneTree tree = GetTree();

		// Durante un cambio de sala.
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		PauseMenu pause = level.Root.GetNode<PauseMenu>("PauseMenu");
		await BossTestHarness.Frames(tree, 3);
		DoorSide first = BossTestHarness.PathToBoss(level.Rooms.Map)[0];
		level.Rooms.CurrentRoom.EmitSignal(RoomController.SignalName.DoorCrossed, (int)first);
		Require(level.Rooms.IsTransitioning && !pause.CanPause && !pause.TryPause(), "No se pausa durante un cambio de sala");
		await BossTestHarness.WaitUntil(tree, () => !level.Rooms.IsTransitioning, 240, "el fin de la transición");
		await BossTestHarness.Close(tree, level);

		// Durante la victoria.
		level = await OpenAtBoss(this, tree);
		pause = level.Root.GetNode<PauseMenu>("PauseMenu");
		level.Rooms.CurrentRoom.BossEnemy.Kill();
		Require(level.Run.RunEnded && !pause.CanPause, "No se pausa con la partida ganada");
		await SendKey(Key.Escape);
		await SendKey(Key.P);
		Require(!pause.IsOpen && !tree.Paused, "Esc y P se ignoran durante la victoria");
		await BossTestHarness.Close(tree, level);

		// Durante la derrota.
		level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		pause = level.Root.GetNode<PauseMenu>("PauseMenu");
		await BossTestHarness.Frames(tree, 3);
		level.Health.TakeDamage(1000.0f);
		Require(level.Run.RunEnded && !pause.CanPause, "No se pausa con la partida perdida");
		await SendKey(Key.Escape);
		await SendKey(Key.P);
		Require(!pause.IsOpen && !tree.Paused, "Esc y P se ignoran durante la derrota");
		GD.Print("Esc/P ignorados durante el cambio de sala, la victoria y la derrota: OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task CheckRestartAndMenu()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		GameSettings.Difficulty = DifficultyTarget.Hard;

		BossTestHarness.Level level = BossTestHarness.OpenAsCurrent(tree, 777);
		Node oldRoot = level.Root;
		Require(level.Run.CurrentDifficulty == DifficultyTarget.Hard, "La partida de prueba es Difícil");
		await BossTestHarness.Frames(tree, 3);
		Require(level.Root.GetNode<PauseMenu>("PauseMenu").TryPause() && tree.Paused, "Pausa abierta antes de reiniciar");
		level.Root.GetNode<PauseMenu>("PauseMenu").RestartButton.EmitSignal(BaseButton.SignalName.Pressed);
		Require(!tree.Paused, "Reiniciar despausa antes de recargar");

		Node fresh = await BossTestHarness.WaitForScene(tree, oldRoot, GameSettings.LevelScene);
		RunManager run = fresh.GetNode<RunManager>("RunManager");
		Require(run.Seed != 777 && run.CurrentDifficulty == DifficultyTarget.Hard, $"Semilla nueva ({run.Seed}) y misma dificultad");
		Require(!tree.Paused && !fresh.GetNode<PauseMenu>("PauseMenu").IsOpen, "La partida reiniciada empieza sin pausa");
		await BossTestHarness.Restore(tree, testScene, fresh);

		level = BossTestHarness.OpenAsCurrent(tree, 777);
		oldRoot = level.Root;
		await BossTestHarness.Frames(tree, 3);
		level.Root.GetNode<PauseMenu>("PauseMenu").TryPause();
		level.Root.GetNode<PauseMenu>("PauseMenu").MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
		Require(!tree.Paused, "Volver al menú despausa antes de cambiar de escena");
		Node menu = await BossTestHarness.WaitForScene(tree, oldRoot, GameSettings.MenuScene);
		Require(menu is MainMenu && !tree.Paused && GameSettings.Difficulty == DifficultyTarget.Hard,
			"El menú aparece sin pausa y conserva la dificultad elegida");
		await BossTestHarness.Restore(tree, testScene, menu);
		GameSettings.Difficulty = null;
		GD.Print("Reiniciar y Volver al menú desde la pausa: despausan, semilla nueva y misma dificultad: OK");
	}
}
