using Godot;

/// <summary>
/// Menú de pausa: Esc o P congelan el juego (SceneTree.Paused) y abren Reanudar, Controles, Reiniciar partida (semilla
/// nueva, misma dificultad) y Volver al menú. El nodo procesa siempre (ProcessMode = Always en la escena); todo lo demás
/// se detiene: el jefe, los enemigos, los proyectiles, los temporizadores de las salas y los efectos.
///
/// No se puede pausar mientras la partida está terminada (victoria o derrota), durante un cambio de sala ni con el
/// juego ya en pausa por otra causa. Antes de reiniciar o volver al menú se despausa (RunManager lo hace).
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	public const string PauseAction = "pause_game";

	[Export] public RunManager Run { get; set; }
	[Export] public RoomManager Rooms { get; set; }

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
		_root.Hide();
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
		if (!@event.IsActionPressed(PauseAction))
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
