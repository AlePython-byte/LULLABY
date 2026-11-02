using System.Linq;
using Godot;

/// <summary>
/// Menú de pausa: Esc o P congelan el juego (SceneTree.Paused) y abren Reanudar, Controles, Reiniciar partida (semilla
/// nueva, misma dificultad) y Volver al menú. El nodo procesa siempre (ProcessMode = Always en la escena); todo lo demás
/// se detiene: el jefe, los enemigos, los proyectiles, los temporizadores de las salas y los efectos.
///
/// Con la hoja de cuaderno (PaperSheet, la misma de la pantalla de derrota y del menú principal) el menú se ve como una hoja centrada con
/// el título y los botones dentro; sin la imagen (se busca por nombre con ResourceLoader.Exists) queda el diseño de siempre.
///
/// No se puede pausar mientras la partida está terminada (victoria o derrota), durante un cambio de sala ni con el
/// juego ya en pausa por otra causa. Antes de reiniciar o volver al menú se despausa (RunManager lo hace).
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	public const string PauseAction = "pause_game";
	private static readonly StringName PauseActionName = new(PauseAction);

	[Export] public RunManager Run { get; set; }
	[Export] public RoomManager Rooms { get; set; }

	/// <summary>Ruta de la hoja (las pruebas la apuntan a otra para comprobar que su ausencia no rompe nada).</summary>
	public static string SheetPath { get; set; } = PaperSheet.Path;

	private static readonly Color Ink = new(0.16f, 0.1f, 0.07f);

	public bool HasSheet { get; private set; }
	public bool IsOpen { get; private set; }
	public Button ResumeButton { get; private set; }
	public Button RestartButton { get; private set; }
	public Button MenuButton { get; private set; }
	public ControlsPanel Controls { get; private set; }

	private Control _root;

	public override void _Ready()
	{
		InputActions.Register(PauseAction, Key.Escape, Key.P);

		_root = GetNode<Control>("Root");
		GetNode<Label>("Root/Center/Box/Title").Text = GameText.Pause;
		ResumeButton = GetNode<Button>("Root/Center/Box/Resume");
		Button controls = GetNode<Button>("Root/Center/Box/ControlsButton");
		RestartButton = GetNode<Button>("Root/Center/Box/Restart");
		MenuButton = GetNode<Button>("Root/Center/Box/Menu");
		ResumeButton.Text = GameText.Resume;
		controls.Text = GameText.Controls;
		RestartButton.Text = GameText.Restart;
		MenuButton.Text = GameText.BackToMenu;

		Controls = GetNode<ControlsPanel>("Root/ControlsPanel");
		controls.Pressed += Controls.Open;
		Controls.Closed += () => controls.GrabFocus();

		ResumeButton.Pressed += Resume;
		RestartButton.Pressed += () => Run?.StartNewRun();
		MenuButton.Pressed += () => Run?.ReturnToMenu();
		AudioManager.HookButtons(_root);
		ApplyArt();
		_root.Hide();
	}

	// Hoja de cuaderno con el menú en su zona útil (465 px de ancho: el volumen y los botones se compactan para caber).
	private void ApplyArt()
	{
		Texture2D sheetTexture = PaperSheet.Load(SheetPath);
		if (sheetTexture == null)
		{
			return;
		}

		HasSheet = true;
		TextureRect sheet = GetNode<TextureRect>("Root/Sheet");
		sheet.Texture = sheetTexture;
		sheet.Show();
		PaperSheet.Place(sheet, PaperSheet.Rect);
		PaperSheet.Place(GetNode<Control>("Root/Center"), PaperSheet.Inner);
		HandFont.Apply(_root);

		Control box = GetNode<Control>("Root/Center/Box");
		box.AddThemeConstantOverride("separation", 12);
		Label title = GetNode<Label>("Root/Center/Box/Title");
		title.AddThemeFontSizeOverride("font_size", 44);
		title.AddThemeColorOverride("font_color", Ink);
		title.AddThemeConstantOverride("outline_size", 0);

		float width = PaperSheet.Inner.Size.X - 16.0f;
		foreach (Button button in new[] { ResumeButton, GetNode<Button>("Root/Center/Box/ControlsButton"), RestartButton, MenuButton })
		{
			button.CustomMinimumSize = new Vector2(width, 46.0f);
			button.AddThemeFontSizeOverride("font_size", 22);
		}

		// El volumen pide 380 px: se estrecha (nombre, deslizador y valor) hasta que cabe en la hoja.
		Control volume = GetNode<Control>("Root/Center/Box/VolumeControls");
		volume.CustomMinimumSize = new Vector2(width, 0.0f);
		foreach (HBoxContainer row in volume.GetChildren().OfType<HBoxContainer>())
		{
			foreach (Control child in row.GetChildren().OfType<Control>())
			{
				if (child is Label label && label.CustomMinimumSize.X >= 100.0f)
				{
					label.CustomMinimumSize = new Vector2(width * 0.34f, 0.0f);
					label.AddThemeColorOverride("font_color", Ink);
				}
				else if (child is Label value)
				{
					value.AddThemeColorOverride("font_color", Ink);
				}
				else if (child is HSlider slider)
				{
					slider.CustomMinimumSize = new Vector2(width * 0.34f, 24.0f);
				}
			}
		}
	}

	public override void _ExitTree()
	{
		// Si el nivel se libera con la pausa abierta, el juego no puede quedar congelado.
		if (IsOpen)
		{
			GetTree().Paused = false;
		}
	}

	public bool CanPause =>
		!IsOpen && !GetTree().Paused
		&& (Run == null || !Run.RunEnded)
		&& (Rooms == null || (!Rooms.IsTransitioning && !Rooms.RunEnded && Rooms.CurrentRoom != null));

	public bool TryPause()
	{
		if (!CanPause)
		{
			return false;
		}

		IsOpen = true;
		GetTree().Paused = true;
		_root.Show();
		ResumeButton.GrabFocus();
		return true;
	}

	public void Resume()
	{
		if (!IsOpen)
		{
			return;
		}

		IsOpen = false;
		Controls.Hide();
		_root.Hide();
		GetTree().Paused = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!@event.IsActionPressed(PauseActionName))
		{
			return;
		}

		// Con la pausa abierta, Esc o P la cierran; el panel de Controles, hijo de este nodo, ya consumió Esc si estaba abierto.
		if (IsOpen)
		{
			Resume();
			GetViewport().SetInputAsHandled();
		}
		else if (TryPause())
		{
			GetViewport().SetInputAsHandled();
		}
	}
}
