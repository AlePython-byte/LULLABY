using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba la derrota con el código real del juego (TestLevel). Modo headless:
///   godot --headless --path . res://tests/DefeatTest.tscn
/// Casos:
///   1. Al morir Noah, la partida termina una sola vez al instante (Noah quieto, puertas bloqueadas), la escena NO se
///      recarga y, tras el fundido, aparece la pantalla de derrota una vez con una de las tres frases del juego.
///   2. Morir en plena pelea con The Mother: no hay victoria aunque el jefe muera después, y no quedan proyectiles.
///   3. Reintentar: semilla nueva, misma dificultad, inventario vacío y salud completa (también en el mismo fotograma).
///   4. Menú: vuelve al menú sin pausa y conserva la dificultad.
///   5. Sin pantalla de derrota asignada (RunManager.Defeat vacío) la escena se reinicia como antes.
/// Convierte el nivel en la escena actual cuando hay recarga o cambio de escena. Sale con código 0 si todo pasa.
/// </summary>
public partial class DefeatTest : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== DefeatTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckMessages();
			await CheckDefeatInStartRoom();
			await CheckDefeatDuringBossFight();
			await CheckRetry();
			await CheckMenu();
			await CheckFallbackReload();
			GD.Print($"DefeatTest: {_checks} comprobaciones. RESULTADO: OK");
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

	private void CheckMessages()
	{
		Require(GameText.DefeatMessages.Length == 3
			&& GameText.DefeatMessages[0] == "El sueño te ha vencido. Descansa en la penumbra."
			&& GameText.DefeatMessages[1] == "Tus lágrimas se secaron antes de encontrar la salida."
			&& GameText.DefeatMessages[2] == "La noche es demasiado pesada para un corazón tan pequeño.", "Las tres frases de derrota del juego");
		Require(GameText.DefeatMessageFor(7) == GameText.DefeatMessageFor(7)
			&& Array.IndexOf(GameText.DefeatMessages, GameText.DefeatMessageFor(-3)) >= 0
			&& Array.IndexOf(GameText.DefeatMessages, GameText.DefeatMessageFor(int.MinValue)) >= 0, "Frase determinista, también con semilla negativa");
		GD.Print("Frases de derrota: OK");
	}

	private async Task CheckDefeatInStartRoom()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		int failed = 0, presented = 0, completed = 0;
		level.Run.RunFailed += () => failed++;
		level.Run.RunCompleted += () => completed++;
		DefeatScreen defeat = level.Root.GetNode<DefeatScreen>("DefeatScreen");
		defeat.Presented += _ => presented++;
		await BossTestHarness.Frames(tree, 3);

		level.Health.TakeDamage(1000.0f);
		Require(failed == 1 && level.Run.RunEnded && level.Rooms.RunEnded, "La partida termina al instante, una vez");
		Require(!level.Player.IsPhysicsProcessing() && level.Player.GetNode<PlayerHealth>("PlayerHealth").IsLocked, "Noah queda quieto");

		RoomController room = level.Rooms.CurrentRoom;
		foreach (DoorSide side in Enum.GetValues<DoorSide>())
		{
			room.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);
		}

		await BossTestHarness.WaitUntil(tree, () => defeat.IsPresented, 300, "la pantalla de derrota");
		await BossTestHarness.Frames(tree, 90);
		Require(GodotObject.IsInstanceValid(level.Root) && level.Root.IsInsideTree() && level.Rooms.CurrentRoom == room,
			"La escena no se recarga ni cambia de sala");
		Require(presented == 1 && failed == 1 && completed == 0 && !level.Victory.IsPresented, "Derrota una sola vez y sin victoria");
		Require(defeat.Message == GameText.DefeatMessageFor(level.Run.Seed) && Array.IndexOf(GameText.DefeatMessages, defeat.Message) >= 0,
			"Una frase de derrota del juego");
		Require(defeat.RetryButton.IsVisibleInTree() && defeat.MenuButton.IsVisibleInTree()
			&& defeat.RetryButton.Text == "Reintentar" && defeat.MenuButton.Text == "Menú", "Botones Reintentar y Menú");
		GD.Print("Derrota en la sala inicial: fin único, sin recarga y con frase: OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task CheckDefeatDuringBossFight()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		int completed = 0;
		level.Run.RunCompleted += () => completed++;
		DefeatScreen defeat = level.Root.GetNode<DefeatScreen>("DefeatScreen");
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		EnemyBase boss = room.BossEnemy;
		MotherBehavior behavior = boss.Behavior<MotherBehavior>();
		await BossTestHarness.WaitTicks(tree, () => behavior.State == MotherBehavior.AttackState.Volley, 3000, "la salva");

		level.Health.TakeDamage(1000.0f);
		Require(level.Run.RunEnded, "La derrota termina la partida durante el combate");
		boss.Kill();
		await BossTestHarness.WaitUntil(tree, () => defeat.IsPresented, 300, "la pantalla de derrota");
		await BossTestHarness.Frames(tree, 60);
		Require(completed == 0 && !level.Victory.IsPresented, "Matar al jefe tras la derrota no da la victoria");
		Require(tree.GetNodesInGroup("enemy_projectiles").Count == 0, "No quedan proyectiles enemigos");
		GD.Print("Derrota en plena pelea con The Mother: sin victoria posterior: OK");
		await BossTestHarness.Close(tree, level);
	}

	private async Task<BossTestHarness.Level> OpenWithItems(DifficultyTarget difficulty)
	{
		GameSettings.Difficulty = difficulty;
		BossTestHarness.Level level = BossTestHarness.OpenAsCurrent(GetTree(), 777, unlimitedHealth: false);
		PlayerItems items = level.Player.GetNode<PlayerItems>("Items");
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		foreach (ItemDefinition item in catalog.Items)
		{
			if (item.Effect == ItemEffect.Damage || item.Effect == ItemEffect.MaxHealth) items.TryCollect(item);
		}

		Require(items.CollectedItems.Count == 2, "Noah tiene dos mejoras antes de morir");
		await BossTestHarness.Frames(GetTree(), 3);
		level.Health.TakeDamage(1000.0f);
		await BossTestHarness.WaitUntil(GetTree(), () => level.Root.GetNode<DefeatScreen>("DefeatScreen").IsPresented, 300, "la pantalla de derrota");
		return level;
	}

	private async Task CheckRetry()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		BossTestHarness.Level old = await OpenWithItems(DifficultyTarget.Easy);
		Node oldRoot = old.Root;
		Require(old.Run.CurrentDifficulty == DifficultyTarget.Easy, "La partida de prueba es Fácil");

		old.Root.GetNode<DefeatScreen>("DefeatScreen").RetryButton.EmitSignal(BaseButton.SignalName.Pressed);
		PlayerItems oldItems = old.Player.GetNode<PlayerItems>("Items");
		Require(oldItems.CollectedItems.Count == 0 && Mathf.IsEqualApprox(old.Health.CurrentHealth, old.Health.MaxHealth)
			&& old.Health.MaxHealthBonus == 0.0f, "Al reintentar, inventario vacío y salud completa");

		Node fresh = await BossTestHarness.WaitForScene(tree, oldRoot, GameSettings.LevelScene);
		RunManager run = fresh.GetNode<RunManager>("RunManager");
		HealthComponent health = HealthComponent.FindIn(fresh.GetNode("Player"));
		Require(run.Seed != 777 && run.CurrentDifficulty == DifficultyTarget.Easy, $"Semilla nueva ({run.Seed}) y misma dificultad");
		Require(fresh.GetNode("Player").GetNode<PlayerItems>("Items").CollectedItems.Count == 0
			&& Mathf.IsEqualApprox(health.CurrentHealth, health.MaxHealth) && health.MaxHealthBonus == 0.0f, "Partida nueva limpia");
		Require(!run.RunEnded && !fresh.GetNode<DefeatScreen>("DefeatScreen").IsPresented && !tree.Paused, "Sin pantalla de derrota ni pausa");
		await BossTestHarness.Restore(tree, testScene, fresh);
		GameSettings.Difficulty = null;
		GD.Print("Reintentar: semilla nueva, misma dificultad, inventario vacío y salud completa: OK");
	}

	private async Task CheckMenu()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		BossTestHarness.Level old = await OpenWithItems(DifficultyTarget.Hard);
		Node oldRoot = old.Root;

		old.Root.GetNode<DefeatScreen>("DefeatScreen").MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
		Node menu = await BossTestHarness.WaitForScene(tree, oldRoot, GameSettings.MenuScene);
		Require(menu is MainMenu && !tree.Paused && GameSettings.Difficulty == DifficultyTarget.Hard, "Menú sin pausa y con la dificultad conservada");
		await BossTestHarness.Restore(tree, testScene, menu);
		GameSettings.Difficulty = null;
		GD.Print("Menú desde la derrota: OK");
	}

	private async Task CheckFallbackReload()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		Node level = GD.Load<PackedScene>(GameSettings.LevelScene).Instantiate();
		RunManager run = level.GetNode<RunManager>("RunManager");
		run.Defeat = null;
		run.UseFixedSeed = true;
		run.FixedSeed = 777;
		level.GetNode<TreasureItemSpawner>("TreasureItemSpawner").Enabled = false;
		tree.Root.AddChild(level);
		tree.CurrentScene = level;
		await BossTestHarness.Frames(tree, 3);

		HealthComponent.FindIn(level.GetNode("Player")).TakeDamage(1000.0f);
		Node fresh = await BossTestHarness.WaitForScene(tree, level, GameSettings.LevelScene);
		Require(fresh.GetNode<DefeatScreen>("DefeatScreen") != null && !fresh.GetNode<RunManager>("RunManager").RunEnded,
			"Sin pantalla de derrota asignada, la escena se reinicia como antes");
		await BossTestHarness.Restore(tree, testScene, fresh);
		GD.Print("Sin pantalla de derrota asignada: recarga de escena como antes: OK");
	}
}
