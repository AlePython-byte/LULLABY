using System;
using Godot;

/// <summary>
/// Pantalla de victoria: un mensaje y el botón «Nueva partida». Está oculta hasta Present, que solo actúa la primera vez.
/// No decide nada de la partida: avisa con NewRunRequested (una sola vez) y quien la usa (RunManager) arranca la nueva.
/// </summary>
public partial class VictoryScreen : CanvasLayer
{
	/// <summary>Se mostró la pantalla, con el mensaje elegido.</summary>
	public event Action<string> Presented;

	/// <summary>El jugador pulsó «Nueva partida».</summary>
	public event Action NewRunRequested;

	public bool IsPresented { get; private set; }
	public string Message => _message.Text;
	public Button NewRunButton => _button;

	private Control _root;
	private Label _message;
	private Button _button;
	private bool _requested;

	public override void _Ready()
	{
		_root = GetNode<Control>("Root");
		_message = GetNode<Label>("Root/Center/Box/Message");
		_button = GetNode<Button>("Root/Center/Box/NewRun");
		_root.Hide();
		_button.Pressed += OnNewRunPressed;
	}

	public void Present(string message)
	{
		if (IsPresented)
		{
			return;
		}

		IsPresented = true;
		_message.Text = message;
		_root.Show();
		_button.GrabFocus();
		Presented?.Invoke(message);
	}

	private void OnNewRunPressed()
	{
		if (_requested)
		{
			return;
		}

		_requested = true;
		_button.Disabled = true;
		NewRunRequested?.Invoke();
	}
}
