using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba del mensaje de objeto recogido y de la opción «Variedad extendida (experimental)». Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/ItemMessageVarietyTest.tscn
/// Mensaje: los objetos del suelo no llevan etiqueta flotante; al recoger uno (por la recogida real o por el inventario) aparece su
/// mensaje (nombre, efecto y descripción), dura la duración configurada y se desvanece, el siguiente lo sustituye y la pausa lo
/// detiene. Los textos del juego (GameText.ItemMessages) coinciden con docs/equipo/textos.md (la prueba lo lee; el juego no).
/// Variedad extendida: la casilla del menú existe, está desmarcada, avisa de que es experimental, recuerda su estado, y marcada
/// carga el nivel con el generador v3 y el aviso del HUD (también al reintentar); desmarcada, con el v2 sin aviso; y el argumento o
/// la tecla de depuración mandan sobre ella.
/// Sale con código 0 si pasa.
/// </summary>
public partial class ItemMessageVarietyTest : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== ItemMessageVarietyTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckTextsMatchDocs();
			await CheckMessages();
			await CheckMenuToggle();
			await CheckVarietyInGame();
			GD.Print($"ItemMessageVarietyTest: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
		finally
		{
			GameSettings.ExtendedVariety = false;
			GameSettings.Difficulty = null;
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		GD.Print($"  {(condition ? "OK   " : "FALLO")} {message}");
		if (!condition) throw new InvalidDataException(message);
	}

	private void CheckTextsMatchDocs()
	{
		GD.Print("\n--- Textos ---");
		using Godot.FileAccess file = Godot.FileAccess.Open("res://docs/equipo/textos.md", Godot.FileAccess.ModeFlags.Read);
		Require(file != null, "se puede leer docs/equipo/textos.md (solo en la prueba)");
		string docs = file.GetAsText();
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		foreach (ItemDefinition item in catalog.Items)
		{
			Require(GameText.ItemMessages.TryGetValue(item.Id, out var message), $"{item.Id}: hay mensaje en GameText");
			Require(message.Title.StartsWith(item.DisplayName + ": "), $"{item.Id}: el título empieza por el nombre del objeto");
			string effect = message.Title[(item.DisplayName.Length + 2)..];
			Require(docs.Contains(effect, StringComparison.OrdinalIgnoreCase), $"{item.Id}: el efecto «{effect}» está en textos.md");
			Require(docs.Contains(message.Description), $"{item.Id}: la descripción está en textos.md");
		}
	}

	private async Task CheckMessages()
	{
		GD.Print("\n--- Mensaje al recoger ---");
		SceneTree tree = GetTree();
		PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
		AddChild(player);
		PlayerItems inventory = player.GetNode<PlayerItems>("Items");
		ItemMessageHud hud = GD.Load<PackedScene>("res://scenes/ui/ItemMessageHud.tscn").Instantiate<ItemMessageHud>();
		hud.Inventory = inventory;
		hud.Duration = 0.5f;
		hud.FadeTime = 0.1f;
		AddChild(hud);
		await BossTestHarness.Frames(tree, 2);
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		Require(!hud.IsShowing, "sin recoger nada no hay mensaje");

		PackedScene pickupScene = GD.Load<PackedScene>("res://scenes/items/ItemPickup.tscn");
		foreach (ItemDefinition item in catalog.Items)
		{
			ItemPickup sample = pickupScene.Instantiate<ItemPickup>();
			sample.Definition = item;
			AddChild(sample);
			Require(sample.GetNodeOrNull("NameLabel") == null && sample.FindChildren("*", "Label3D", true, false).Count == 0, $"{item.Id}: el objeto del suelo no lleva etiqueta flotante");
			if (item.Effect == ItemEffect.Heal)
			{
				HealthComponent.FindIn(player).TakeDamage(2.0f);
			}

			Require(sample.TryCollect(player), $"{item.Id}: se recoge");
			(string title, string description) = GameText.ItemMessages[item.Id];
			Require(hud.IsShowing && hud.TitleText == title && hud.DescriptionText == description, $"{item.Id}: el mensaje muestra «{title}»");
			Require(hud.GetNode<Control>("Box").Visible, $"{item.Id}: el mensaje es visible");
			await BossTestHarness.Frames(tree, 50);
			Require(!hud.IsShowing && !hud.GetNode<Control>("Box").Visible, $"{item.Id}: el mensaje desaparece solo tras su duración");
		}

		// Recoger otro lo sustituye.
		inventory.ResetForNewRun();
		ItemDefinition first = catalog.Items.First(i => i.Effect == ItemEffect.Damage);
		ItemDefinition second = catalog.Items.First(i => i.Effect == ItemEffect.Cadence);
		inventory.TryCollect(first);
		inventory.TryCollect(second);
		Require(hud.TitleText == GameText.ItemMessages[second.Id].Title, "el siguiente objeto sustituye al mensaje anterior");

		// La pausa detiene el mensaje.
		hud.Show(first);
		tree.Paused = true;
		await BossTestHarness.Frames(tree, 60);
		bool stillThere = hud.IsShowing;
		tree.Paused = false;
		Require(stillThere, "la pausa detiene el temporizador del mensaje");

		// Un objeto no recogido (curación con vida llena) no muestra mensaje.
		await BossTestHarness.Frames(tree, 50);
		inventory.ResetForNewRun();
		ItemDefinition heal = catalog.Items.First(i => i.Effect == ItemEffect.Heal);
		Require(!inventory.TryCollect(heal) && !hud.IsShowing, "una curación rechazada (vida llena) no muestra mensaje");

		hud.Free();
		player.Free();
	}

	private async Task CheckMenuToggle()
	{
		GD.Print("\n--- Casilla del menú ---");
		SceneTree tree = GetTree();
		GameSettings.ExtendedVariety = false;
		MainMenu menu = await OpenMenu();
		Require(menu.ExtendedVarietyToggle != null && !menu.ExtendedVarietyToggle.ButtonPressed && !GameSettings.ExtendedVariety, "la casilla existe y está desmarcada por defecto");
		Require(menu.ExtendedVarietyToggle.Text == GameText.ExtendedVariety && menu.ExtendedVarietyToggle.Text.Contains("experimental"), "está etiquetada como experimental");
		string hint = menu.GetNode<Label>("Center/Box/ExtendedVarietyHint").Text;
		Require(hint.Contains("excavador") && hint.Contains("huidor") && hint.Contains("no está entrenado"), "el menú avisa de que el modelo no está entrenado para el excavador ni el huidor");
		menu.ExtendedVarietyToggle.ButtonPressed = true;
		Require(GameSettings.ExtendedVariety, "marcarla guarda la opción en GameSettings");
		menu.QueueFree();
		await tree.ToSignal(menu, Node.SignalName.TreeExited);
		MainMenu again = await OpenMenu();
		Require(again.ExtendedVarietyToggle.ButtonPressed, "el menú recuerda la opción durante la sesión");
		again.ExtendedVarietyToggle.ButtonPressed = false;
		Require(!GameSettings.ExtendedVariety, "desmarcarla la apaga");
		again.QueueFree();
		await tree.ToSignal(again, Node.SignalName.TreeExited);
		tree.CurrentScene = this;
	}

	private async Task CheckVarietyInGame()
	{
		GD.Print("\n--- Variedad extendida en la partida ---");
		SceneTree tree = GetTree();
		Node testScene = this;
		foreach (bool extended in new[] { true, false })
		{
			MainMenu menu = await OpenMenu();
			menu.ExtendedVarietyToggle.ButtonPressed = extended;
			menu.StartGame();
			Node level = await BossTestHarness.WaitForScene(tree, menu, GameSettings.LevelScene);
			string hud = level.GetNode<Label>("SeedHud/Label").Text;
			if (extended)
			{
				Require(hud.Contains("generador v3") && hud.Contains(GameText.ExtendedVarietyWarning), "marcada: partida con el generador v3 y el aviso experimental en el HUD");
				level.GetNode<RunManager>("RunManager").StartNewRun();
				Node next = await BossTestHarness.WaitForScene(tree, level, GameSettings.LevelScene);
				Require(next.GetNode<Label>("SeedHud/Label").Text.Contains("generador v3"), "al reintentar sigue en v3");
				await BossTestHarness.Restore(tree, testScene, next);
			}
			else
			{
				Require(hud.Contains("generador v2") && !hud.Contains("experimental"), "desmarcada: partida con el generador v2 y sin aviso");
				await BossTestHarness.Restore(tree, testScene, level);
			}
		}

		if (OS.IsDebugBuild())
		{
			GameSettings.ExtendedVariety = true;
			RunManager.SetNextGeneratorVersion(1);
			MainMenu menu = await OpenMenu();
			menu.ExtendedVarietyToggle.SetPressedNoSignal(true);
			menu.StartGame();
			// StartGame descarta lo pendiente: se fija justo después, como hace la tecla de depuración al recargar la escena.
			RunManager.SetNextGeneratorVersion(1);
			Node level = await BossTestHarness.WaitForScene(tree, menu, GameSettings.LevelScene);
			Require(level.GetNode<Label>("SeedHud/Label").Text.Contains("generador v1"), "la tecla o el argumento de depuración mandan sobre la casilla");
			await BossTestHarness.Restore(tree, testScene, level);
		}
	}

	private async Task<MainMenu> OpenMenu()
	{
		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		GetTree().Root.AddChild(menu);
		GetTree().CurrentScene = menu;
		await BossTestHarness.Frames(GetTree(), 2);
		return menu;
	}
}
