using System;
using Godot;

/// <summary>
/// Pantalla de victoria: un mensaje y el botón «Nueva partida». Está oculta hasta Present, que solo actúa la primera vez.
/// Si existe assets/ui/pantalla_victoria.png (1920x1080) se usa como fondo a pantalla completa, cubriendo sin deformar, con el título, la
/// frase y el botón en la parte superior (vacía en la imagen) y en un color oscuro que se lee sobre el papel; sin la imagen queda el
/// diseño de siempre (fondo oscuro translúcido y todo centrado).
/// No decide nada de la partida: avisa con NewRunRequested (una sola vez) y quien la usa (RunManager) arranca la nueva.
/// </summary>
public partial class VictoryScreen : CanvasLayer
{
	/// <summary>Se mostró la pantalla, con el mensaje elegido.</summary>
	public event Action<string> Presented;

	/// <summary>El jugador pulsó «Nueva partida».</summary>
	public event Action NewRunRequested;

	public const string DefaultArtPath = "res://assets/ui/pantalla_victoria.png";

	/// <summary>Ruta de la imagen de fondo (las pruebas la apuntan a otra para comprobar que su ausencia no rompe nada).</summary>
	public static string ArtPath { get; set; } = DefaultArtPath;

	/// <summary>Altura (fracción de la ventana) que ocupa la zona de texto sobre la imagen: encima de la cuna.</summary>
	public const float ArtTextArea = 0.40f;

	private static readonly Color PaperInk = new(0.16f, 0.1f, 0.07f);

	public bool HasArt { get; private set; }
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
		ApplyArt();
	}

	// Sin la imagen no pasa nada: se queda el diseño de siempre. ResourceLoader.Exists evita cualquier error de carga.
	private void ApplyArt()
	{
		if (!ResourceLoader.Exists(ArtPath) || ResourceLoader.Load<Texture2D>(ArtPath) is not Texture2D texture)
		{
			return;
		}

		TextureRect art = GetNode<TextureRect>("Root/Art");
		art.Texture = texture;
		art.Show();
		GetNode<Control>("Root/Background").Hide();
		HasArt = true;

		// El texto sube a la zona vacía de la imagen (sobre la cuna) y pasa a tinta oscura, sin el borde negro.
		GetNode<Control>("Root/Center").AnchorBottom = ArtTextArea;
		GetNode<Control>("Root/Center/Box").AddThemeConstantOverride("separation", 10);
		Label title = GetNode<Label>("Root/Center/Box/Title");
		title.AddThemeFontSizeOverride("font_size", 48);
		foreach (Label label in new[] { title, _message })
		{
			label.AddThemeColorOverride("font_color", PaperInk);
			label.AddThemeConstantOverride("outline_size", 0);
		}

		_message.CustomMinimumSize = new Vector2(560.0f, 0.0f);
		_message.AddThemeFontSizeOverride("font_size", 24);
		_button.CustomMinimumSize = new Vector2(240.0f, 46.0f);
		_button.AddThemeFontSizeOverride("font_size", 22);
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
		AudioManager.Play(SoundCatalog.Victory);
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
