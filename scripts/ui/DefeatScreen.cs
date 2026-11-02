using System;
using Godot;

/// <summary>
/// Pantalla de derrota: el título, una frase, qué mató a Noah (DeathCause) y los botones Reintentar y Menú. Está oculta hasta
/// Present, que solo actúa la primera vez. No decide nada de la partida: avisa con RetryRequested o MenuRequested (una sola vez) y quien la usa
/// (RunManager) arranca la partida nueva o vuelve al menú.
/// </summary>
public partial class DefeatScreen : CanvasLayer
{
	/// <summary>Se mostró la pantalla, con la frase elegida.</summary>
	public event Action<string> Presented;

	/// <summary>El jugador pulsó Reintentar.</summary>
	public event Action RetryRequested;

	/// <summary>El jugador pulsó Menú.</summary>
	public event Action MenuRequested;

	public bool IsPresented { get; private set; }
	public string Message => _message.Text;

	/// <summary>Lo que mató a Noah (Unknown si no se sabe) y el texto de la línea «Te venció: …».</summary>
	public DeathCause Cause { get; private set; } = DeathCause.Unknown;
	public string CauseText => _cause.Text;
	public Button RetryButton => _retry;
	public Button MenuButton => _menu;

	private Control _root;
	private Label _message;
	private Label _cause;
	private Button _retry;
	private Button _menu;
	private bool _requested;

	public override void _Ready()
	{
		_root = GetNode<Control>("Root");
		GetNode<Label>("Root/Center/Box/Title").Text = DeathTexts.Title;
		_message = GetNode<Label>("Root/Center/Box/Message");
		_cause = GetNode<Label>("Root/Center/Box/Cause");
		_retry = GetNode<Button>("Root/Center/Box/Buttons/Retry");
		_menu = GetNode<Button>("Root/Center/Box/Buttons/Menu");
		_retry.Text = GameText.Retry;
		_menu.Text = GameText.Menu;
		_root.Hide();
		_retry.Pressed += () => Request(RetryRequested);
		_menu.Pressed += () => Request(MenuRequested);
	}

	public void Present(string message, DeathCause cause = DeathCause.Unknown)
	{
		if (IsPresented)
		{
			return;
		}

		IsPresented = true;
		Cause = cause;
		_message.Text = message;
		_cause.Text = $"{DeathTexts.KilledBy} {DeathTexts.NameOf(cause)}";
		_root.Show();
		_retry.GrabFocus();
		Presented?.Invoke(message);
	}

	private void Request(Action action)
	{
		if (_requested)
		{
			return;
		}

		_requested = true;
		_retry.Disabled = true;
		_menu.Disabled = true;
		action?.Invoke();
	}
}
