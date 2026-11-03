using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el estilo único de los botones de los menús (MenuButtonStyle). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/MenuButtonStyleTest.tscn
///   1. El estilo carga las tres imágenes de assets/ui (512×128), las retiene, y cada estado es un StyleBoxTexture de 9 trozos con la
///      zona útil de la imagen y los márgenes previstos.
///   2. Los menús (principal, pausa, controles, victoria y derrota) se instancian y sus botones normales llevan los tres estados: normal,
///      hover, pressed; el foco se ve igual que el hover; la letra es Patrick Hand y el texto es legible sobre el arte oscuro. Los botones con
///      su propio aspecto (CheckButton y las notas de la derrota) no se tocan.
///   3. El sonido: el hover pide menu_hover (sin archivo, queda como faltante sin error), una sola vez por botón aunque se conecte dos veces
///      (el panel de controles dentro de la pausa); el clic conserva el suyo (menu_click o menu_confirmar) y un botón desactivado no suena.
///   4. El comportamiento no cambia: textos, selector de dificultad y deslizadores de volumen, y el menú principal sigue cabiendo en pantalla.
///   5. Respaldo: sin cualquiera de las imágenes (o sin la carpeta) los botones quedan con el estilo de siempre, siguen funcionando y no hay
///      errores; la derrota sin notas de papel usa el estilo.
///   6. Retención: las texturas y los estilos siguen válidos después de liberar los menús y de recolectar la basura.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class MenuButtonStyleTest : Node
{
	private const string TestSettings = "user://menu_style_test_settings.cfg";

	private static readonly string[] Scenes =
	{
		GameSettings.MenuScene,
		"res://scenes/ui/PauseMenu.tscn",
		"res://scenes/ui/ControlsPanel.tscn",
		"res://scenes/ui/VictoryScreen.tscn",
		"res://scenes/ui/DefeatScreen.tscn",
	};

	private int _checks;

	public override async void _Ready()
	{
		string settings = GameSettings.SettingsPath;
		DifficultyTarget? difficulty = GameSettings.Difficulty;
		GameSettings.SettingsPath = TestSettings;
		try
		{
			GD.Print($"=== MenuButtonStyleTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			MenuButtonStyle.Reset();
			CheckLoadedStyle();
			await CheckScreensAreStyled();
			await CheckSounds();
			await CheckBehaviourIsUnchanged();
			await CheckFallback();
			await CheckRetention();
			GD.Print($"MenuButtonStyleTest: {_checks} comprobaciones. RESULTADO: OK");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit(1);
		}
		finally
		{
			GameSettings.SettingsPath = settings;
			GameSettings.Difficulty = difficulty;
			MenuButtonStyle.Folder = MenuButtonStyle.DefaultFolder;
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TestSettings));
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private static List<Button> PlainButtons(Node root)
	{
		List<Button> buttons = new();
		Collect(root, buttons);
		return buttons;
	}

	private static void Collect(Node node, List<Button> buttons)
	{
		if (node is Button button && button.GetType() == typeof(Button))
		{
			buttons.Add(button);
		}

		foreach (Node child in node.GetChildren())
		{
			Collect(child, buttons);
		}
	}

	private static StyleBoxTexture TextureStyle(Button button, string name) => button.GetThemeStylebox(name) as StyleBoxTexture;

	private static bool UsesOurStyle(Button button) =>
		TextureStyle(button, "normal") is StyleBoxTexture normal && MenuButtonStyle.Held.Contains(normal.Texture);

	private async Task<Node> Open(string scene)
	{
		Node node = GD.Load<PackedScene>(scene).Instantiate();
		AddChild(node);
		await BossTestHarness.Frames(GetTree(), 2);
		return node;
	}

	private async Task Free(Node node)
	{
		node.QueueFree();
		await ToSignal(node, Node.SignalName.TreeExited);
		await BossTestHarness.Frames(GetTree(), 1);
	}

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void CheckLoadedStyle()
	{
		Require(MenuButtonStyle.Available, "El estilo carga las tres imágenes");
		Require(MenuButtonStyle.Held.Count == 3, "Y las retiene");
		string[] files = { MenuButtonStyle.NormalFile, MenuButtonStyle.HoverFile, MenuButtonStyle.PressedFile };
		for (int i = 0; i < 3; i++)
		{
			Texture2D texture = MenuButtonStyle.Held[i];
			Require(texture.ResourcePath == MenuButtonStyle.DefaultFolder + files[i], $"La textura {i} es {files[i]}");
			Require(texture.GetWidth() == 512 && texture.GetHeight() == 128, $"{files[i]} mide 512×128");
		}

		StyleBoxTexture[] styles = { MenuButtonStyle.Normal, MenuButtonStyle.Hover, MenuButtonStyle.Pressed };
		for (int i = 0; i < 3; i++)
		{
			StyleBoxTexture style = styles[i];
			Require(style.Texture == MenuButtonStyle.Held[i], $"El estado {i} usa su textura");
			Require(style.RegionRect == MenuButtonStyle.ArtRegion, "Recorta la zona útil de la imagen (sin el margen transparente)");
			Require(MenuButtonStyle.ArtRegion.End.X <= 512.0f && MenuButtonStyle.ArtRegion.End.Y <= 128.0f, "La zona útil cabe en la imagen");
			Require(style.GetTextureMargin(Side.Left) == MenuButtonStyle.SliceMargin && style.GetTextureMargin(Side.Right) == MenuButtonStyle.SliceMargin
				&& style.GetTextureMargin(Side.Top) == MenuButtonStyle.SliceMargin && style.GetTextureMargin(Side.Bottom) == MenuButtonStyle.SliceMargin,
				"Los cuatro márgenes de 9 trozos son los previstos");
			Require(style.AxisStretchHorizontal == StyleBoxTexture.AxisStretchMode.Stretch && style.AxisStretchVertical == StyleBoxTexture.AxisStretchMode.Stretch,
				"Solo se estira el centro");
		}

		Require(MenuButtonStyle.SliceMargin * 2.0f <= 36.0f, "Los márgenes caben en el botón más bajo (36 px): los bordes no se deforman");
		Require(MenuButtonStyle.Held.Distinct().Count() == 3, "Las tres texturas son distintas");
		GD.Print("Estilo: tres texturas de 512×128 retenidas, 9 trozos con zona útil y márgenes: OK");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task CheckScreensAreStyled()
	{
		Font font = HandFont.Load();
		foreach (string scene in Scenes)
		{
			Node screen = await Open(scene);
			List<Button> buttons = PlainButtons(screen);
			Require(buttons.Count > 0, $"{scene} se instancia y tiene botones");
			bool defeatWithNotes = screen is DefeatScreen defeat && defeat.HasNotes;

			foreach (Button button in buttons)
			{
				string who = $"{scene}: «{button.Text}»";
				if (defeatWithNotes)
				{
					Require(!UsesOurStyle(button), $"{who} conserva su nota de papel");
					continue;
				}

				StyleBoxTexture normal = TextureStyle(button, "normal");
				StyleBoxTexture hover = TextureStyle(button, "hover");
				StyleBoxTexture pressed = TextureStyle(button, "pressed");
				Require(normal != null && hover != null && pressed != null, $"{who} lleva los tres estados con StyleBoxTexture");
				Require(normal.Texture == MenuButtonStyle.Held[0] && hover.Texture == MenuButtonStyle.Held[1] && pressed.Texture == MenuButtonStyle.Held[2],
					$"{who}: normal, hover y pressed usan sus imágenes");
				Require(TextureStyle(button, "focus")?.Texture == MenuButtonStyle.Held[1], $"{who}: el foco se ve como el hover");
				Require(TextureStyle(button, "hover_pressed")?.Texture == MenuButtonStyle.Held[2], $"{who}: hover y pulsado usan la imagen de pulsado");
				Require(button.GetThemeColor("font_color") == MenuButtonStyle.TextColor && button.GetThemeColor("font_focus_color") == button.GetThemeColor("font_hover_color"),
					$"{who}: texto claro legible, y con foco igual que con hover");
				Require(font == null || button.GetThemeFont("font") == font, $"{who}: letra Patrick Hand");
				Require(!string.IsNullOrEmpty(button.Text), $"{who} conserva su texto");
			}

			await Free(screen);
		}

		// Los botones con aspecto propio no se tocan.
		MainMenu menu = (MainMenu)await Open(GameSettings.MenuScene);
		Require(!UsesOurStyle(menu.PlayButton.GetParent().GetNode<Button>("ExtendedVariety")), "El interruptor «ExtendedVariety» (CheckButton) conserva su aspecto");
		await Free(menu);

		// La luminosidad del texto sobre el fondo: el arte es oscuro, así que el texto debe ser claro.
		Color fill = new(42.0f / 255.0f, 36.0f / 255.0f, 52.0f / 255.0f);
		Require(MenuButtonStyle.TextColor.Luminance > fill.Luminance + 0.5f, "El texto contrasta con el relleno del arte");
		GD.Print("Pantallas: botones con tres estados, foco como hover, letra manuscrita; CheckButton y notas intactos: OK");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task CheckSounds()
	{
		AudioManager audio = AudioManager.Instance;
		Require(SoundCatalog.Get(SoundCatalog.UiHover) is { Fallback: null } hover && hover.Id == "menu_hover", "menu_hover está en el catálogo y sin respaldo");

		PauseMenu pause = (PauseMenu)await Open("res://scenes/ui/PauseMenu.tscn");
		Button resume = pause.ResumeButton;
		Button back = PlainButtons(pause.Controls).First();

		foreach (Button button in new[] { resume, back })
		{
			audio.ClearRequests();
			button.EmitSignal(BaseButton.SignalName.MouseEntered);
			AudioManager.Request[] hovered = audio.Requests.Where(request => request.Id == SoundCatalog.UiHover).ToArray();
			Require(hovered.Length == 1, $"«{button.Text}»: el hover pide menu_hover una sola vez ({hovered.Length})");
			Require(hovered[0].Result is AudioManager.Outcome.Missing or AudioManager.Outcome.Played or AudioManager.Outcome.Silent, "Sin archivo queda como faltante, sin error");
			Require(audio.Requests.Count == 1, "Y no pide nada más");
		}

		audio.ClearRequests();
		resume.EmitSignal(BaseButton.SignalName.Pressed);
		Require(audio.Requests.Any(request => request.Id == SoundCatalog.UiClick) && audio.Requests.All(request => request.Id != SoundCatalog.UiHover),
			"El clic conserva su sonido (menu_click) y no suena el de hover");

		audio.ClearRequests();
		resume.Disabled = true;
		resume.EmitSignal(BaseButton.SignalName.MouseEntered);
		Require(audio.Requests.Count == 0, "Un botón desactivado no suena al pasar el ratón");
		await Free(pause);

		MainMenu menu = (MainMenu)await Open(GameSettings.MenuScene);
		audio.ClearRequests();
		menu.PlayButton.EmitSignal(BaseButton.SignalName.MouseEntered);
		Require(audio.Requests.Count(request => request.Id == SoundCatalog.UiHover) == 1, "El botón de jugar también suena al pasar el ratón");
		await Free(menu);

		VictoryScreen victory = (VictoryScreen)await Open("res://scenes/ui/VictoryScreen.tscn");
		audio.ClearRequests();
		victory.NewRunButton.EmitSignal(BaseButton.SignalName.MouseEntered);
		Require(audio.Requests.Count == 1 && audio.Requests[0].Id == SoundCatalog.UiHover, "La pantalla de victoria suena al pasar el ratón (y su clic sigue sin sonido propio)");
		await Free(victory);
		GD.Print("Sonido: menu_hover una vez por botón y sin error, clic intacto, desactivado en silencio: OK");
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task CheckBehaviourIsUnchanged()
	{
		MainMenu menu = (MainMenu)await Open(GameSettings.MenuScene);
		Require(menu.PlayButton.Text == GameText.Play, "El texto de jugar no cambia");
		Require(menu.GetNode<Button>("Center/Box/ControlsButton").Text == GameText.Controls && menu.GetNode<Button>("Center/Box/Quit").Text == GameText.Quit, "Ni los de controles y salir");

		foreach (DifficultyTarget target in new[] { DifficultyTarget.Easy, DifficultyTarget.Hard, DifficultyTarget.Normal })
		{
			Button button = menu.GetNode<Button>($"Center/Box/Difficulties/{target}");
			button.EmitSignal(BaseButton.SignalName.Pressed);
			Require(menu.SelectedDifficulty == target && GameSettings.Difficulty == target, $"El selector elige {target}");
			Require(button.ButtonPressed && new[] { "Easy", "Normal", "Hard" }.Where(name => name != target.ToString())
				.All(name => !menu.GetNode<Button>($"Center/Box/Difficulties/{name}").ButtonPressed), $"Solo {target} queda marcado");
			Require(button.ToggleMode && button.ButtonGroup != null, "Sigue siendo un botón de alternancia con grupo");
		}

		VolumeControls volume = menu.GetNode<VolumeControls>("VolumeControls");
		volume.MasterSlider.Value = 35.0;
		Require(Mathf.IsEqualApprox(GameSettings.MasterVolume, 0.35f), "El deslizador de volumen general sigue aplicando su valor");
		volume.SfxSlider.Value = 60.0;
		volume.MusicSlider.Value = 20.0;
		Require(Mathf.IsEqualApprox(GameSettings.SfxVolume, 0.6f) && Mathf.IsEqualApprox(GameSettings.MusicVolume, 0.2f), "Y los de efectos y música");
		Require(volume.GetNode<Label>("MasterRow/Value").Text == "35%", "Con su valor en pantalla");

		Control box = menu.GetNode<Control>("Center/Box");
		Rect2 rect = box.GetGlobalRect();
		Rect2 screen = menu.GetViewportRect();
		Require(rect.Position.Y >= 0.0f && rect.End.Y <= screen.Size.Y && rect.End.X <= screen.Size.X, $"El menú principal cabe en pantalla ({rect} dentro de {screen})");
		Require(menu.GetNode<Control>("Center/Box").GetChildren().OfType<Button>().All(button => button.Size.Y >= button.CustomMinimumSize.Y), "Ningún botón queda por debajo de su tamaño mínimo");
		await Free(menu);

		PauseMenu pause = (PauseMenu)await Open("res://scenes/ui/PauseMenu.tscn");
		Require(pause.ResumeButton.Text == GameText.Resume && pause.RestartButton.Text == GameText.Restart && pause.MenuButton.Text == GameText.BackToMenu, "Los textos de la pausa no cambian");
		await Free(pause);
		GD.Print("Comportamiento: textos, selector de dificultad, volumen y cabida en pantalla sin cambios: OK");
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task CheckFallback()
	{
		string defaultFolder = MenuButtonStyle.DefaultFolder;
		Dictionary<string, string> previous = new();
		try
		{
			string hoverFile = MenuButtonStyle.HoverFile;
			foreach ((string label, string folder, string hover) in new[]
			{
				("sin la carpeta", "res://assets/no_existe/", hoverFile),
				("sin la imagen de hover (las otras dos existen)", defaultFolder, "no_existe.png"),
			})
			{
				MenuButtonStyle.Folder = folder;
				MenuButtonStyle.HoverFile = hover;
				MenuButtonStyle.Reset();
				Require(label.StartsWith("sin la carpeta") || (ResourceLoader.Exists(defaultFolder + MenuButtonStyle.NormalFile) && ResourceLoader.Exists(defaultFolder + MenuButtonStyle.PressedFile)),
					"En el caso parcial, normal y pressed sí existen");
				Require(!MenuButtonStyle.Available && MenuButtonStyle.Held.Count == 0, $"{label}: el estilo no está disponible");

				foreach (string scene in Scenes)
				{
					Node screen = await Open(scene);
					List<Button> buttons = PlainButtons(screen);
					Require(buttons.Count > 0, $"{label}: {scene} se instancia");
					foreach (Button button in buttons)
					{
						Require(!(TextureStyle(button, "normal") is StyleBoxTexture texture && texture.Texture != null && texture.Texture.ResourcePath.Contains("boton_")), $"{label}: «{button.Text}» queda con el estilo de siempre");
						Require(!button.HasThemeColorOverride("font_color") || button.GetThemeColor("font_color") != MenuButtonStyle.TextColor, $"{label}: «{button.Text}» no recibe el color del estilo");
						Require(!string.IsNullOrEmpty(button.Text), $"{label}: «{button.Text}» conserva su texto");
					}

					// Solo se pulsan botones inocuos (los demás arrancan o cierran la partida): el resto se comprueba como pulsable.
					Button harmless = screen switch
					{
						MainMenu => screen.GetNode<Button>("Center/Box/Difficulties/Easy"),
						PauseMenu => screen.GetNode<Button>("Root/Center/Box/ControlsButton"),
						ControlsPanel => buttons[0],
						_ => null,
					};
					if (harmless != null)
					{
						int pressed = 0;
						harmless.Pressed += () => pressed++;
						harmless.EmitSignal(BaseButton.SignalName.Pressed);
						Require(pressed == 1, $"{label}: «{harmless.Text}» sigue funcionando");
					}

					Require(buttons.All(button => !button.Disabled && button.FocusMode != Control.FocusModeEnum.None), $"{label}: los botones de {scene} siguen activos y con foco");
					await Free(screen);
				}
			}

			// Con la carpeta buena pero la derrota sin notas de papel, los botones llevan el estilo.
			MenuButtonStyle.Folder = defaultFolder;
			MenuButtonStyle.HoverFile = hoverFile;
			MenuButtonStyle.Reset();
			string retry = DefeatScreen.RetryNotePath;
			string menuNote = DefeatScreen.MenuNotePath;
			DefeatScreen.RetryNotePath = "res://assets/ui/no_existe.png";
			DefeatScreen.MenuNotePath = "res://assets/ui/no_existe.png";
			try
			{
				DefeatScreen defeat = (DefeatScreen)await Open("res://scenes/ui/DefeatScreen.tscn");
				Require(!defeat.HasNotes, "Sin notas de papel, la derrota no las usa");
				Require(defeat.RetryButton != null && UsesOurStyle(defeat.RetryButton) && UsesOurStyle(defeat.MenuButton), "Y sus botones llevan el estilo de los demás menús");
				await Free(defeat);
			}
			finally
			{
				DefeatScreen.RetryNotePath = retry;
				DefeatScreen.MenuNotePath = menuNote;
			}
		}
		finally
		{
			MenuButtonStyle.Folder = defaultFolder;
			MenuButtonStyle.HoverFile = "boton_hover.png";
			MenuButtonStyle.Reset();
		}

		Require(MenuButtonStyle.Available, "Al volver a la carpeta buena, el estilo vuelve a estar disponible");
		GD.Print("Respaldo: sin imágenes los botones siguen con el estilo de siempre y funcionan; derrota sin notas usa el estilo: OK");
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task CheckRetention()
	{
		MenuButtonStyle.Reset();
		MainMenu menu = (MainMenu)await Open(GameSettings.MenuScene);
		Texture2D[] held = MenuButtonStyle.Held.ToArray();
		StyleBoxTexture normal = MenuButtonStyle.Normal;
		await Free(menu);

		await BossTestHarness.CollectWrappers(GetTree());
		Require(held.All(GodotObject.IsInstanceValid) && GodotObject.IsInstanceValid(normal), "Las texturas y el estilo siguen válidos tras liberar el menú y recolectar la basura");
		Require(held.All(texture => texture.GetWidth() == 512 && texture.GetHeight() == 128), "Y siguen siendo usables");
		MainMenu again = (MainMenu)await Open(GameSettings.MenuScene);
		Require(ReferenceEquals(MenuButtonStyle.Normal, normal) && PlainButtons(again).All(UsesOurStyle), "Un menú nuevo reutiliza las mismas texturas y estilos");
		await Free(again);
		GD.Print("Retención: texturas y estilos válidos tras liberar menús y recolectar: OK");
	}
}
