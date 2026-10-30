using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el menú principal y cómo pasa la dificultad al nivel. Modo headless:
///   godot --headless --path . res://tests/MenuFlowTest.tscn
/// Casos:
///   1. TestLevel lanzado directo (F6, las pruebas) sigue sin pasar por el menú: dificultad del inspector (Normal).
///   2. El menú muestra los textos del juego, funciona con o sin las imágenes opcionales y recuerda la dificultad.
///   3. La pantalla de Controles muestra solo las teclas reales (WASD, flechas, Esc o P).
///   4. Jugar con cada dificultad carga TestLevel con esa dificultad como objetivo del director y la muestra en el HUD,
///      despausa el juego y descarta una semilla pendiente (la partida del menú toma una aleatoria).
/// Convierte cada escena en la escena actual para que ChangeSceneToFile no libere la prueba. Sale con código 0 si pasa.
/// </summary>
public partial class MenuFlowTest : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== MenuFlowTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			await CheckLevelWithoutMenu();
			await CheckMenuContents();
			await CheckControlsText();
			await CheckPlayWithEachDifficulty();
			GD.Print($"MenuFlowTest: {_checks} comprobaciones. RESULTADO: OK");
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

	private async Task<MainMenu> OpenMenu()
	{
		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		GetTree().Root.AddChild(menu);
		GetTree().CurrentScene = menu;
		await BossTestHarness.Frames(GetTree(), 2);
		return menu;
	}

	private async Task CheckLevelWithoutMenu()
	{
		Require(GameSettings.Difficulty == null, "Sin menú no hay dificultad elegida");
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		Require(level.Run.CurrentDifficulty == level.Run.Difficulty && level.Run.CurrentDifficulty == DifficultyTarget.Normal,
			"TestLevel directo usa la dificultad del inspector (Normal)");
		Require(level.Root.GetNode<Label>("SeedHud/Label").Text.Contains("Dificultad Normal"), "El HUD muestra Normal");
		Require(level.Root.GetNode<PauseMenu>("PauseMenu") != null && level.Root.GetNode<DefeatScreen>("DefeatScreen") != null,
			"TestLevel trae la pausa y la derrota");
		await BossTestHarness.Close(GetTree(), level);
		GD.Print("TestLevel directo (F6): dificultad del inspector, sin pasar por el menú: OK");
	}

	private async Task CheckMenuContents()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		MainMenu menu = await OpenMenu();

		Require(menu.GetNode<Label>("Center/Box/Subtitle").Text == GameText.Subtitle, "Subtítulo del juego");
		Require(menu.PlayButton.Text == GameText.Play && menu.GetNode<Button>("Center/Box/Quit").Text == GameText.Quit
			&& menu.GetNode<Button>("Center/Box/ControlsButton").Text == GameText.Controls, "Jugar, Controles y Salir");
		Require(menu.HasLogo == ResourceLoader.Exists(MainMenu.LogoPath) && menu.HasBackgroundArt == ResourceLoader.Exists(MainMenu.BackgroundPath),
			"Las imágenes se usan solo si existen, y el menú funciona igual sin ellas");
		Require(menu.GetNode<Label>("Center/Box/Title").Visible != menu.HasLogo, "El título en texto se oculta solo si hay logo");

		Require(menu.SelectedDifficulty == DifficultyTarget.Normal && menu.GetNode<Button>("Center/Box/Difficulties/Normal").ButtonPressed,
			"Por defecto, Normal");
		Require(menu.GetNode<Button>("Center/Box/Difficulties/Easy").Text == "Fácil"
			&& menu.GetNode<Button>("Center/Box/Difficulties/Hard").Text == "Difícil", "Fácil / Normal / Difícil");
		menu.Select(DifficultyTarget.Hard);
		Require(GameSettings.Difficulty == DifficultyTarget.Hard && menu.GetNode<Button>("Center/Box/Difficulties/Hard").ButtonPressed
			&& !menu.GetNode<Button>("Center/Box/Difficulties/Normal").ButtonPressed, "Elegir Difícil lo guarda y lo marca");

		await BossTestHarness.Restore(tree, testScene, menu);
		MainMenu again = await OpenMenu();
		Require(again.SelectedDifficulty == DifficultyTarget.Hard, "El menú recuerda la dificultad durante la sesión");
		await BossTestHarness.Restore(tree, testScene, again);
		GameSettings.Difficulty = null;
		GD.Print("Menú: textos, imágenes opcionales y dificultad recordada: OK");
	}

	private async Task CheckControlsText()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		MainMenu menu = await OpenMenu();

		int closed = 0;
		menu.Controls.Closed += () => closed++;
		Require(!menu.Controls.Visible, "Los controles empiezan cerrados");
		menu.Controls.Open();
		string text = menu.Controls.GetNode<Label>("Center/Box/Lines").Text;
		Require(menu.Controls.Visible && text.Contains("W, A, S, D") && text.Contains("flechas") && text.Contains("Esc o P"),
			$"Teclas reales en la pantalla de Controles: {text}");
		Require(!text.ToLowerInvariant().Contains("clic") && !text.ToLowerInvariant().Contains("interactuar"),
			"Sin teclas que el juego no tiene");
		menu.Controls.Close();
		Require(!menu.Controls.Visible && closed == 1, "Volver cierra el panel una vez");

		await BossTestHarness.Restore(tree, testScene, menu);
		GD.Print("Controles: solo las teclas reales: OK");
	}

	private async Task CheckPlayWithEachDifficulty()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;

		foreach (DifficultyTarget target in new[] { DifficultyTarget.Easy, DifficultyTarget.Normal, DifficultyTarget.Hard })
		{
			if (OS.IsDebugBuild())
			{
				RunManager.SetNextSeed(5);
			}

			MainMenu menu = await OpenMenu();
			menu.Select(target);
			tree.Paused = true;
			menu.StartGame();
			Require(!tree.Paused, "Jugar despausa antes de cambiar de escena");

			Node level = await BossTestHarness.WaitForScene(tree, menu, GameSettings.LevelScene);
			RunManager run = level.GetNode<RunManager>("RunManager");
			Require(run.CurrentDifficulty == target, $"El director usa {target} (usa {run.CurrentDifficulty})");
			Require(level.GetNode<Label>("SeedHud/Label").Text.Contains("Dificultad " + GameText.DifficultyName(target)),
				"El HUD muestra la dificultad elegida");
			Require(run.Seed != 5, "Una partida nueva desde el menú no toma la semilla pendiente");
			Require(!tree.Paused, "La partida empieza sin pausa");

			await BossTestHarness.Restore(tree, testScene, level);
			GD.Print($"Jugar con {GameText.DifficultyName(target)}: dificultad del director, HUD, sin pausa y semilla propia: OK");
		}

		GameSettings.Difficulty = null;
	}
}
