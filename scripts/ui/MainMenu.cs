using Godot;

/// <summary>
/// Menú principal: título, Jugar, Dificultad (Fácil / Normal / Difícil), Controles y Salir. La dificultad elegida se
/// guarda en GameSettings y es la que usa el director de IA al empezar la partida; solo dura la sesión. La casilla «Variedad extendida
/// (experimental)» elige el generador v3 (con el huidor y el excavador) y avisa de que su modelo de dificultad no está entrenado.
/// El logo (assets/ui/logo_lullaby.png) y el fondo (assets/ui/fondo_menu.png) son opcionales: si no existen, el menú
/// funciona igual con el título en texto y un fondo de color.
/// </summary>
public partial class MainMenu : Control
{
	public const string LogoPath = "res://assets/ui/logo_lullaby.png";
	public const string BackgroundPath = "res://assets/ui/fondo_menu.png";

	private static readonly DifficultyTarget[] Targets = { DifficultyTarget.Easy, DifficultyTarget.Normal, DifficultyTarget.Hard };

	public Button PlayButton { get; private set; }
	public ControlsPanel Controls { get; private set; }
	public bool HasLogo { get; private set; }
	public bool HasBackgroundArt { get; private set; }

	/// <summary>Casilla «Variedad extendida (experimental)»: marcada, la partida usa el generador v3.</summary>
	public CheckButton ExtendedVarietyToggle { get; private set; }

	/// <summary>Dificultad marcada en pantalla.</summary>
	public DifficultyTarget SelectedDifficulty { get; private set; } = DifficultyTarget.Normal;

	private readonly Button[] _difficultyButtons = new Button[Targets.Length];

	public override void _Ready()
	{
		GetTree().Paused = false;

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
		PlayButton.GrabFocus();
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
		RunManager.DiscardPending();
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(GameSettings.LevelScene);
	}

	// Sin las imágenes no pasa nada: se queda el texto y el color. ResourceLoader.Exists evita cualquier error de carga.
	private void ApplyArt()
	{
		Texture2D logo = LoadOptional(LogoPath);
		if (logo != null)
		{
			TextureRect rect = GetNode<TextureRect>("Center/Box/Logo");
			rect.Texture = logo;
			rect.Show();
			GetNode<Label>("Center/Box/Title").Hide();
			HasLogo = true;
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

	private static Texture2D LoadOptional(string path)
	{
		return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
	}
}
