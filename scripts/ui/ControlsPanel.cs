using System;
using Godot;

/// <summary>
/// Panel con las teclas del juego (GameText.ControlLines) y un botón para volver. Es un Control a pantalla completa,
/// oculto al nacer: el menú principal y la pausa lo abren con Open y escuchan Closed.
/// </summary>
public partial class ControlsPanel : Control
{
	/// <summary>Se cerró el panel (botón Volver o Esc).</summary>
	public event Action Closed;

	private Label _lines;
	private Button _back;

	public override void _Ready()
	{
		GetNode<Label>("Center/Box/Title").Text = GameText.Controls;
		_lines = GetNode<Label>("Center/Box/Lines");
		_lines.Text = string.Join("\n", GameText.ControlLines);
		_back = GetNode<Button>("Center/Box/Back");
		_back.Text = GameText.Back;
		_back.Pressed += Close;
		Hide();
	}

	public void Open()
	{
		Show();
		_back.GrabFocus();
	}

	public void Close()
	{
		if (!Visible)
		{
			return;
		}

		Hide();
		Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Close();
		}
	}
}
