using Godot;

/// <summary>
/// Menú principal: título, Jugar, Dificultad (Fácil / Normal / Difícil), Controles y Salir. La dificultad elegida se
/// guarda en GameSettings y es la que usa el director de IA al empezar la partida; solo dura la sesión. La casilla «Variedad extendida
/// (experimental)» elige el generador v3 (con el huidor y el excavador) y avisa de que su modelo de dificultad no está entrenado.
/// Todo el arte es opcional y se busca por nombre con ResourceLoader.Exists, así que se puede reemplazar un PNG por otro con el mismo
/// nombre y medidas, y si falta, el menú funciona igual con el título en texto y un fondo de color:
///   - la carátula animada (MenuCover, assets/ui/caratula/capa_*.png) a pantalla completa;
///   - el logo (assets/ui/caratula/logo_lullaby.png), que trae el título y el subtítulo, y el fondo fijo (assets/ui/fondo_menu.png);
///   - el panel de papel (assets/ui/panel_menu_papel.png) detrás del menú. Con panel, el menú pasa a la mitad derecha de la ventana
///     (la cuna queda visible a la izquierda), se compacta para caber en 648 px de alto, los textos pasan a marrón oscuro para leerse
///     sobre el papel y el volumen baja a la esquina inferior izquierda.
/// </summary>
public partial class MainMenu : Control
{
	public const string LogoPath = "res://assets/ui/caratula/logo_lullaby.png";
	public const string BackgroundPath = "res://assets/ui/fondo_menu.png";
	public const string PanelPath = "res://assets/ui/panel_menu_papel.png";

	// Papel de 760×980 con las chinchetas arriba, el margen rojo a la izquierda y la esquina doblada abajo a la derecha: el menú ocupa
	// la zona útil del papel, que va de estas fracciones de su alto (arriba y abajo) y de su ancho (izquierda y derecha). El papel
	// conserva su proporción si cabe el menú.
	private const float PanelAspect = 760.0f / 980.0f;
	private const float PanelTop = 0.095f;
	private const float PanelBottom = 0.07f;
	private const float PanelLeft = 0.19f;
	private const float PanelRight = 0.955f;

	/// <summary>Ancho (px) del logo dentro del panel; el alto sale de la proporción real de su textura.</summary>
	public const float LogoWidth = 316.0f;
	private static readonly Color PaperText = new(0.25f, 0.16f, 0.1f);
	private static readonly Color PaperHint = new(0.42f, 0.27f, 0.12f);

	private static readonly DifficultyTarget[] Targets = { DifficultyTarget.Easy, DifficultyTarget.Normal, DifficultyTarget.Hard };

	public Button PlayButton { get; private set; }
	public ControlsPanel Controls { get; private set; }
	public bool HasLogo { get; private set; }
	public bool HasBackgroundArt { get; private set; }

	/// <summary>Carátula animada: hay al menos una capa cargada.</summary>
	public bool HasCover { get; private set; }

	/// <summary>Panel de papel detrás del menú.</summary>
	public bool HasPanel { get; private set; }

	public MenuCover Cover { get; private set; }

	/// <summary>Rectángulo del panel de papel en coordenadas globales (vacío si no hay panel).</summary>
	public Rect2 PanelRect => HasPanel ? GetNode<TextureRect>("MenuPanel").GetGlobalRect() : new Rect2();

	/// <summary>Casilla «Variedad extendida (experimental)»: marcada, la partida usa el generador v3.</summary>
	public CheckButton ExtendedVarietyToggle { get; private set; }

	/// <summary>Dificultad marcada en pantalla.</summary>
	public DifficultyTarget SelectedDifficulty { get; private set; } = DifficultyTarget.Normal;

	private readonly Button[] _difficultyButtons = new Button[Targets.Length];

	public override void _Ready()
	{
		GetTree().Paused = false;
		GameSettings.PlaytestLogEnabled = false;

		GetNode<Label>("Center/Box/Title").Text = GameText.Title;
		GetNode<Label>("Center/Box/Subtitle").Text = GameText.Subtitle;
		GetNode<Label>("Center/Box/DifficultyLabel").Text = GameText.Difficulty;

		PlayButton = GetNode<Button>("Center/Box/Play");
		PlayButton.Text = GameText.Play;
		PlayButton.Pressed += StartGame;

		Button controls = GetNode<Button>("Center/Box/ControlsButton");
		controls.Text = GameText.Controls;
		Controls = GetNode<ControlsPanel>("ControlsPanel");
		controls.Pressed += Controls.Open;
		Controls.Closed += () => controls.GrabFocus();

		Button quit = GetNode<Button>("Center/Box/Quit");
		quit.Text = GameText.Quit;
		quit.Pressed += () => GetTree().Quit();

		SelectedDifficulty = GameSettings.Difficulty ?? DifficultyTarget.Normal;
		ButtonGroup group = new();
		for (int i = 0; i < Targets.Length; i++)
		{
			DifficultyTarget target = Targets[i];
			Button button = GetNode<Button>($"Center/Box/Difficulties/{target}");
			button.Text = GameText.DifficultyName(target);
			button.ToggleMode = true;
			button.ButtonGroup = group;
			button.ButtonPressed = target == SelectedDifficulty;
			button.Pressed += () => Select(target);
			_difficultyButtons[i] = button;
		}

		GetNode<Label>("Center/Box/ExtendedVarietyHint").Text = GameText.ExtendedVarietyHint;
		ExtendedVarietyToggle = GetNode<CheckButton>("Center/Box/ExtendedVariety");
		ExtendedVarietyToggle.Text = GameText.ExtendedVariety;
		ExtendedVarietyToggle.SetPressedNoSignal(GameSettings.ExtendedVariety);
		ExtendedVarietyToggle.Toggled += pressed => GameSettings.ExtendedVariety = pressed;

		ApplyArt();
		MenuButtonStyle.ApplyAll(this);
		PlayButton.SetMeta(AudioManager.UiSoundMeta, SoundCatalog.MenuConfirm);
		AudioManager.HookButtons(this);
		PlayButton.GrabFocus();
		AudioManager.PlayMusic(SoundCatalog.MusicMenu);

		// Mientras el jugador mira el menú se precargan las texturas y los modelos de las salas (y se dibujan una vez), para que la primera
		// sala de combate no dé un tirón. Idempotente: si ya se hizo, no añade nada.
		DecorWarmupRunner.Attach(this);

		// Después de eso (el runner espera al anterior), un enemigo de cada tipo se carga, se instancia y se dibuja una vez en un visor oculto:
		// el JIT y la subida de mallas y texturas se hacen aquí y no al nacer los enemigos de la primera sala.
		EnemyWarmupRunner.Attach(this);
	}

	public void Select(DifficultyTarget target)
	{
		SelectedDifficulty = target;
		GameSettings.Difficulty = target;
		for (int i = 0; i < Targets.Length; i++)
		{
			_difficultyButtons[i].SetPressedNoSignal(Targets[i] == target);
		}
	}

	/// <summary>Empieza la partida con la dificultad marcada.</summary>
	public void StartGame()
	{
		GameSettings.Difficulty = SelectedDifficulty;
		GameSettings.ExtendedVariety = ExtendedVarietyToggle.ButtonPressed;
		GameSettings.PlaytestLogEnabled = GameSettings.CanRecordPlaytest;
		RunManager.DiscardPending();
		GetTree().Paused = false;
		SceneChangeGuard.Collect();
		GetTree().ChangeSceneToFile(GameSettings.LevelScene);
	}

	// Sin las imágenes no pasa nada: se queda el texto y el color. ResourceLoader.Exists evita cualquier error de carga.
	private void ApplyArt()
	{
		Cover = GetNode<MenuCover>("Cover");
		HasCover = Cover.LayerCount > 0;

		Texture2D logo = LoadOptional(LogoPath);
		if (logo != null)
		{
			TextureRect rect = GetNode<TextureRect>("Center/Box/Logo");
			rect.Texture = logo;
			rect.Show();
			GetNode<Label>("Center/Box/Title").Hide();
			// El logo ya trae la cinta del subtítulo.
			GetNode<Label>("Center/Box/Subtitle").Hide();
			HasLogo = true;
		}

		Texture2D panel = LoadOptional(PanelPath);
		if (panel != null)
		{
			ApplyPanel(panel);
		}

		Texture2D background = LoadOptional(BackgroundPath);
		if (background != null)
		{
			TextureRect rect = GetNode<TextureRect>("BackgroundArt");
			rect.Texture = background;
			rect.Show();
			GetNode<ColorRect>("Background").Color = new Color(0.0f, 0.0f, 0.0f, 0.5f);
			HasBackgroundArt = true;
		}
	}

	// Menú a la derecha sobre un papel que se ajusta a su contenido; ver la descripción de la clase.
	private void ApplyPanel(Texture2D texture)
	{
		TextureRect paper = GetNode<TextureRect>("MenuPanel");
		paper.Texture = texture;
		paper.Show();
		HasPanel = true;

		Control center = GetNode<Control>("Center");
		center.AnchorLeft = 0.56f;

		// Todo el menú cabe en 1152×648: separaciones y botones compactos, y el logo por ancho con la proporción real de su textura.
		Control box = GetNode<Control>("Center/Box");
		box.AddThemeConstantOverride("separation", 5);
		TextureRect logo = GetNode<TextureRect>("Center/Box/Logo");
		if (logo.Texture != null)
		{
			float height = LogoWidth * logo.Texture.GetHeight() / logo.Texture.GetWidth();
			logo.CustomMinimumSize = new Vector2(LogoWidth, height);
			logo.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			logo.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			logo.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		}

		Compact("Spacer", new Vector2(0.0f, 2.0f));
		Compact("Play", new Vector2(300.0f, 44.0f), 24);
		Compact("DifficultyLabel", Vector2.Zero, 17);
		foreach (string difficulty in new[] { "Easy", "Normal", "Hard" })
		{
			Compact($"Difficulties/{difficulty}", new Vector2(96.0f, 36.0f), 18);
		}

		Compact("ExtendedVariety", new Vector2(330.0f, 36.0f), 17);
		Compact("ExtendedVarietyHint", new Vector2(330.0f, 0.0f), 13);
		Compact("ControlsButton", new Vector2(300.0f, 36.0f), 20);
		Compact("Quit", new Vector2(300.0f, 36.0f), 20);

		foreach (string name in new[] { "Subtitle", "DifficultyLabel" })
		{
			GetNode<Label>($"Center/Box/{name}").AddThemeColorOverride("font_color", PaperText);
		}

		GetNode<Label>("Center/Box/ExtendedVarietyHint").AddThemeColorOverride("font_color", PaperHint);

		Control volume = GetNode<Control>("VolumeControls");
		volume.AnchorLeft = 0.0f;
		volume.AnchorRight = 0.0f;
		volume.AnchorTop = 1.0f;
		volume.AnchorBottom = 1.0f;
		volume.OffsetLeft = 16.0f;
		volume.OffsetRight = 420.0f;
		volume.OffsetTop = -140.0f;
		volume.OffsetBottom = -16.0f;
		volume.GrowHorizontal = Control.GrowDirection.End;
		volume.GrowVertical = Control.GrowDirection.Begin;

		box.ItemRectChanged += PlacePanel;
		PlacePanel();
	}

	// Tamaño mínimo y, si se da, tamaño de letra de un hijo del menú compacto.
	private void Compact(string path, Vector2 minimum, int fontSize = 0)
	{
		Control control = GetNode<Control>($"Center/Box/{path}");
		control.CustomMinimumSize = minimum;
		if (fontSize > 0)
		{
			control.AddThemeFontSizeOverride("font_size", fontSize);
		}
	}

	// El papel rodea al menú: el alto sale de que el menú ocupe la zona útil del papel (entre las chinchetas y la esquina doblada), el ancho
	// de la proporción del papel (o más, si el menú no cabe entre el margen rojo y el borde derecho).
	private void PlacePanel()
	{
		TextureRect paper = GetNode<TextureRect>("MenuPanel");
		Rect2 box = GetNode<Control>("Center/Box").GetGlobalRect();
		float height = box.Size.Y / (1.0f - PanelTop - PanelBottom);
		float width = Mathf.Max(height * PanelAspect, (box.Size.X + 16.0f) / (PanelRight - PanelLeft));
		float x = box.GetCenter().X - width * (PanelLeft + PanelRight) / 2.0f;
		paper.GlobalPosition = new Vector2(x, box.Position.Y - height * PanelTop);
		paper.Size = new Vector2(width, height);
	}

	private static Texture2D LoadOptional(string path)
	{
		return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
	}
}
