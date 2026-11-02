using System;
using Godot;

/// <summary>
/// Pantalla de derrota: el título, una frase, qué mató a Noah (DeathCause) y los botones Reiniciar y Salir al menú. Está oculta hasta
/// Present, que solo actúa la primera vez.
///
/// Con la hoja de cuaderno (PaperSheet) la pantalla se ve como una hoja centrada con el título, la frase y lo que mató a Noah, y, a los
/// lados, dos notas pegadas (nota_salir a la izquierda, nota_reiniciar a la derecha) que son los propios botones, con su texto y su tecla
/// dibujados por el juego: Enter reinicia y Esc sale al menú. Sin las notas, los botones van dentro de la hoja; sin la hoja queda el
/// diseño de siempre. Cada imagen se busca por nombre con ResourceLoader.Exists, así que se puede reemplazar por otra con el mismo nombre. No decide nada de la partida: avisa con RetryRequested o MenuRequested (una sola vez) y quien la usa
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

	public const string RetryAction = "defeat_retry";
	public const string MenuAction = "defeat_menu";
	public const string DefaultRetryNotePath = "res://assets/ui/nota_reiniciar.png";
	public const string DefaultMenuNotePath = "res://assets/ui/nota_salir.png";

	/// <summary>Rutas de las imágenes (las pruebas las apuntan a otras para comprobar que su ausencia no rompe nada).</summary>
	public static string SheetPath { get; set; } = PaperSheet.Path;
	public static string RetryNotePath { get; set; } = DefaultRetryNotePath;
	public static string MenuNotePath { get; set; } = DefaultMenuNotePath;

	/// <summary>Ancho de las notas y cuánto pisan la hoja (px, en la ventana de 1152×648); el alto sale de la proporción de la imagen.</summary>
	public const float NoteWidth = 190.0f;
	public const float NoteOverlap = 20.0f;
	public const float NoteCenterY = 70.0f;

	private static readonly Color Ink = new(0.16f, 0.1f, 0.07f);
	private static readonly Color InkSoft = new(0.36f, 0.24f, 0.16f);

	public bool HasSheet { get; private set; }
	public bool HasNotes { get; private set; }
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
	private bool _pausedByUs;

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
		_retry.TooltipText = GameText.RetryKey;
		_menu.TooltipText = GameText.MenuKey;
		InputActions.Register(RetryAction, Key.Enter, Key.KpEnter);
		InputActions.Register(MenuAction, Key.Escape);
		ApplyArt();
	}

	// Enter reinicia y Esc sale al menú, con la pantalla a la vista y solo la primera vez.
	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsPresented || _requested || @event.IsEcho())
		{
			return;
		}

		if (@event.IsActionPressed(RetryAction))
		{
			GetViewport().SetInputAsHandled();
			Request(RetryRequested);
		}
		else if (@event.IsActionPressed(MenuAction))
		{
			GetViewport().SetInputAsHandled();
			Request(MenuRequested);
		}
	}

	// Sin imágenes no pasa nada: se queda el diseño de siempre. ResourceLoader.Exists evita cualquier error de carga.
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
		GetNode<ColorRect>("Root/Background").Color = new Color(0.0f, 0.0f, 0.0f, 0.78f);

		GetNode<Control>("Root/Center/Box").AddThemeConstantOverride("separation", 14);
		Label title = GetNode<Label>("Root/Center/Box/Title");
		ApplyInk(title, 40, Ink);
		_message.CustomMinimumSize = new Vector2(PaperSheet.Inner.Size.X - 16.0f, 0.0f);
		ApplyInk(_message, 22, Ink);
		ApplyInk(_cause, 28, Ink);

		Texture2D retryNote = PaperSheet.Load(RetryNotePath);
		Texture2D menuNote = PaperSheet.Load(MenuNotePath);
		if (retryNote != null && menuNote != null)
		{
			HasNotes = true;
			Control notes = new() { Name = "Notes", MouseFilter = Control.MouseFilterEnum.Ignore };
			notes.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_root.AddChild(notes);
			GetNode<Control>("Root/Center/Box/Buttons").Hide();
			BuildNote(notes, _retry, retryNote, 1, GameText.RetryKey);
			BuildNote(notes, _menu, menuNote, -1, GameText.MenuKey);
		}
		else
		{
			// Sin notas, los dos botones caben uno al lado del otro dentro de la hoja.
			GetNode<Control>("Root/Center/Box/Buttons").AddThemeConstantOverride("separation", 12);
			foreach (Button button in new[] { _retry, _menu })
			{
				button.CustomMinimumSize = new Vector2(PaperSheet.Inner.Size.X / 2.0f - 12.0f, 44.0f);
				button.AddThemeFontSizeOverride("font_size", 18);
			}
		}
	}

	private static void ApplyInk(Label label, int size, Color color)
	{
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeConstantOverride("outline_size", 0);
	}

	// Convierte el botón en una nota pegada a un lado de la hoja: con la imagen como fondo (a su proporción real), ligeramente girada, con
	// su texto y la tecla dibujados encima. side = 1 a la derecha (reiniciar), -1 a la izquierda (salir).
	private static void BuildNote(Control notes, Button button, Texture2D texture, int side, string key)
	{
		button.Reparent(notes, false);
		float width = NoteWidth;
		float height = width * texture.GetHeight() / texture.GetWidth();
		float x = side > 0 ? PaperSheet.Rect.End.X - NoteOverlap : PaperSheet.Rect.Position.X - width + NoteOverlap;
		PaperSheet.Place(button, new Rect2(x, NoteCenterY - height / 2.0f, width, height));
		button.CustomMinimumSize = new Vector2(width, height);
		button.PivotOffset = new Vector2(width, height) / 2.0f;
		button.RotationDegrees = side > 0 ? 4.0f : -4.0f;
		button.Flat = false;
		button.AddThemeFontSizeOverride("font_size", 26);
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color" })
		{
			button.AddThemeColorOverride(color, Ink);
		}

		button.AddThemeStyleboxOverride("normal", NoteStyle(texture, Colors.White));
		button.AddThemeStyleboxOverride("hover", NoteStyle(texture, new Color(1.08f, 1.06f, 0.95f)));
		button.AddThemeStyleboxOverride("focus", NoteStyle(texture, new Color(1.08f, 1.06f, 0.95f)));
		button.AddThemeStyleboxOverride("pressed", NoteStyle(texture, new Color(0.85f, 0.82f, 0.78f)));
		button.AddThemeStyleboxOverride("disabled", NoteStyle(texture, new Color(0.7f, 0.7f, 0.7f)));

		Label hint = new()
		{
			Name = "KeyHint",
			Text = key,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		hint.AddThemeFontSizeOverride("font_size", 17);
		hint.AddThemeColorOverride("font_color", InkSoft);
		hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		hint.OffsetTop = -height * 0.26f;
		hint.OffsetBottom = -height * 0.10f;
		button.AddChild(hint);
	}

	private static StyleBoxTexture NoteStyle(Texture2D texture, Color modulate) => new() { Texture = texture, ModulateColor = modulate };

	public void Present(string message, DeathCause cause = DeathCause.Unknown)
	{
		if (IsPresented)
		{
			return;
		}

		IsPresented = true;
		Cause = cause;
		// El juego queda congelado detrás (esta pantalla procesa siempre, igual que el audio); RunManager despausa antes de reiniciar o salir.
		GetTree().Paused = true;
		_pausedByUs = true;
		_message.Text = message;
		_cause.Text = $"{DeathTexts.KilledBy} {DeathTexts.NameOf(cause)}";
		_root.Show();
		_retry.GrabFocus();
		AudioManager.Play(SoundCatalog.Defeat);
		Presented?.Invoke(message);
	}

	public override void _ExitTree()
	{
		// Si el nivel se libera con la derrota a la vista, el juego no puede quedar congelado.
		if (_pausedByUs)
		{
			GetTree().Paused = false;
		}
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
