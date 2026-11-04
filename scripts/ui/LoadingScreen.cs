using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Pantalla de carga a pantalla completa: un biberón de tiza que se llena como barra de progreso. Un fondo liso del color exacto de las
/// imágenes (ChalkBackground, medido en su esquina) cubre la ventana y encima va la imagen del biberón vacío (carga_vacio.jpg), centrada y a
/// 16:9 sin deformarse (barras del color del fondo si la ventana es de otra proporción). Encima, carga_lleno.jpg recortada de izquierda a
/// derecha (BottleFill, en coordenadas de la imagen de 1920×1080) según el progreso mostrado. Debajo del biberón va el consejo al azar, en
/// una sola línea y centrado respecto al biberón; todo se coloca y escala respecto a la imagen, no a la ventana.
/// Si falta una de las dos imágenes se usa el diseño anterior, descrito a continuación.
///
/// Diseño anterior (respaldo): una página de diario (la misma hoja de papel que la pausa y la derrota) con Noah gateando abajo, que alterna sus dos
/// poses a ritmo pausado y avanza a lo largo de la barra de progreso (assets/ui/barra_marco.png encima de barra_relleno.png, que el motor
/// recorta según el progreso), el título fijo «Consejo del diario» (LoadingTips.Title) y debajo un consejo al azar. Letra Patrick Hand.
///
/// La crea RunManager al empezar o reiniciar una partida, antes de la parte pesada (precarga, precalentamiento, director de IA y primera
/// sala), y le va diciendo el progreso con SetProgress; al terminar llama a Complete, que espera al menos MinVisibleSeconds desde que se
/// mostró (para que no parpadee) y a que la barra llegue al final, y se desvanece. No alarga la carga real: solo la completa hasta ese mínimo.
///
/// Se construye por código y todo es opcional: sin la hoja de papel queda un fondo liso con el texto en crema, sin las poses de Noah no se
/// dibuja, y sin las imágenes de la barra se usa una barra lisa. Sin errores en ninguno de los casos (ResourceLoader.Exists antes de cargar).
/// Las texturas se guardan en campos estáticos (como DecorWarmup.Held): la pantalla se libera con la escena y, si cada una cargara las suyas,
/// el recolector de basura podía finalizar un envoltorio mientras la siguiente lo pedía al caché del motor.
///
/// En headless (pruebas, bot, simulador) no se muestra salvo ForceInHeadless, como la intro: ShouldShow lo decide.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
	public const string DefaultChalkEmpty = "res://assets/ui/carga/carga_vacio.jpg";
	public const string DefaultChalkFull = "res://assets/ui/carga/carga_lleno.jpg";
	public const string DefaultNoahOne = "res://assets/ui/noah_gateo_1.png";
	public const string DefaultNoahTwo = "res://assets/ui/noah_gateo_2.png";
	public const string DefaultFrame = "res://assets/ui/barra_marco.png";
	public const string DefaultFill = "res://assets/ui/barra_relleno.png";

	/// <summary>Rutas de las imágenes. Solo las pruebas las cambian, para simular que faltan.</summary>
	public static string ChalkEmptyPath { get; set; } = DefaultChalkEmpty;
	public static string ChalkFullPath { get; set; } = DefaultChalkFull;
	public static string NoahOnePath { get; set; } = DefaultNoahOne;
	public static string NoahTwoPath { get; set; } = DefaultNoahTwo;
	public static string FramePath { get; set; } = DefaultFrame;
	public static string FillPath { get; set; } = DefaultFill;
	public static string SheetPath { get; set; } = PaperSheet.Path;

	/// <summary>Solo pruebas: mostrarla aunque sea headless.</summary>
	public static bool ForceInHeadless { get; set; }

	/// <summary>Se está viendo (desde que se crea hasta que termina de desvanecerse). La pausa no se abre mientras tanto.</summary>
	public static bool Busy { get; private set; }

	/// <summary>Texturas retenidas (hoja, poses, marco y relleno).</summary>
	public static IReadOnlyCollection<Texture2D> Held => HeldTextures.Values;

	private static readonly Dictionary<string, Texture2D> HeldTextures = new();

	/// <summary>Segundos mínimos que se ve desde que aparece, para que no parpadee cuando la carga real es más corta.</summary>
	[Export] public float MinVisibleSeconds { get; set; } = 1.0f;

	/// <summary>Segundos que dura cada pose de Noah (ritmo pausado).</summary>
	[Export] public float PoseSeconds { get; set; } = 0.45f;

	[Export] public float FadeSeconds { get; set; } = 0.25f;

	/// <summary>Lo máximo que sube la barra mostrada por segundo (el progreso real manda; esto solo suaviza los saltos).</summary>
	[Export] public float BarSpeed { get; set; } = 3.0f;

	/// <summary>Color del fondo de las dos imágenes (píxel de su esquina: 16, 16, 16).</summary>
	public static readonly Color ChalkBackground = new(16.0f / 255.0f, 16.0f / 255.0f, 16.0f / 255.0f);

	// Medidas sobre la imagen de referencia de 1920×1080 (ver docs/LULLABY.md): tamaño, interior del biberón que se rellena y caja del consejo.
	public const float ChalkWidth = 1920.0f;
	public const float ChalkHeight = 1080.0f;
	public static readonly Rect2 BottleFill = new(525.0f, 634.0f, 623.0f, 334.0f);
	public static readonly Rect2 TipBox = new(88.0f, 985.0f, 1700.0f, 65.0f);
	public const float TipFontSize = 38.0f;

	// Tinta sobre el papel y crema sobre fondo liso.
	private static readonly Color Ink = new(0.16f, 0.1f, 0.07f);
	private static readonly Color Cream = new(1.0f, 0.95f, 0.82f);
	private const float NoahSize = 150.0f;
	private const float BarArtWidth = 800.0f;
	private const float BarArtHeight = 48.0f;

	/// <summary>Lo que va de trabajo hecho, de 0 a 1 (el que pide RunManager).</summary>
	public float Progress { get; private set; }

	/// <summary>Lo que muestra la barra ahora (sigue a Progress sin pasarse).</summary>
	public float ShownProgress { get; private set; }

	/// <summary>Segundos desde que se mostró.</summary>
	public float Elapsed { get; private set; }

	/// <summary>Terminó de desvanecerse.</summary>
	public bool Finished { get; private set; }

	public string Tip => _tip.Text;
	public string TitleText => _title?.Text ?? "";
	public bool HasSheet { get; private set; }
	public bool HasNoah { get; private set; }

	/// <summary>Se usa el diseño de tiza (las dos imágenes del biberón existen).</summary>
	public bool HasChalkArt { get; private set; }

	/// <summary>Zona recortada que enseña el biberón lleno (solo con el diseño de tiza).</summary>
	public Control FillClip => _clip;

	/// <summary>Lámina de 16:9 sobre la que se coloca todo (solo con el diseño de tiza).</summary>
	public Control Stage => _stage;

	/// <summary>La etiqueta del consejo.</summary>
	public Label TipLabel => _tip;
	public bool HasBarArt { get; private set; }

	/// <summary>Pose de Noah que se ve (0 o 1) y cuántas veces ha cambiado.</summary>
	public int PoseIndex { get; private set; }
	public int PoseSwitches { get; private set; }

	/// <summary>La pose que se dibuja ahora (null si no hay imágenes de Noah).</summary>
	public Texture2D CurrentPose => _noah?.Texture;

	public event Action Completed;

	private Control _root;
	private Control _stage;
	private Control _clip;
	private TextureRect _fullPicture;
	private float _tipSize = TipFontSize;
	private Label _title;
	private Label _tip;
	private TextureRect _noah;
	private Control _track;
	private TextureProgressBar _bar;
	private ProgressBar _plainBar;
	private readonly Texture2D[] _poses = new Texture2D[2];
	private float _poseTime;
	private bool _completing;
	private readonly Random _random = new();

	/// <summary>Si debe verse: sí, salvo en headless (pruebas) a menos que se fuerce.</summary>
	public static bool ShouldShow(bool headless, bool forced) => forced || !headless;

	/// <summary>Carga una textura una sola vez y la retiene. Null si el archivo falta o no carga (sin errores).</summary>
	public static Texture2D LoadHeld(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		if (HeldTextures.TryGetValue(path, out Texture2D held))
		{
			return held;
		}

		if (!ResourceLoader.Exists(path) || ResourceLoader.Load<Texture2D>(path) is not Texture2D texture)
		{
			return null;
		}

		HeldTextures[path] = texture;
		return texture;
	}

	/// <summary>Olvida las texturas retenidas (solo para pruebas).</summary>
	public static void ResetHeld() => HeldTextures.Clear();

	public override void _Ready()
	{
		Layer = 100;
		ProcessMode = ProcessModeEnum.Always;
		Busy = true;
		Build();
		SetProgress(0.0f);
		AudioManager.Play(SoundCatalog.PageTurn);
	}

	public override void _ExitTree()
	{
		Busy = false;
	}

	/// <summary>Sube el progreso (nunca baja). De 0 a 1.</summary>
	public void SetProgress(float progress)
	{
		Progress = Mathf.Max(Progress, Mathf.Clamp(progress, 0.0f, 1.0f));
	}

	/// <summary>
	/// El trabajo terminó: lleva la barra al 100 %, espera al mínimo visible, se desvanece y se libera. Devuelve cuando ya no está.
	/// Llamarla otra vez no hace nada.
	/// </summary>
	public async Task Complete()
	{
		if (_completing)
		{
			return;
		}

		_completing = true;
		SetProgress(1.0f);
		SceneTree tree = GetTree();
		while (IsInstanceValid(this) && IsInsideTree() && (Elapsed < MinVisibleSeconds || ShownProgress < 0.999f))
		{
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		float fade = 0.0f;
		while (fade < FadeSeconds && IsInstanceValid(this) && IsInsideTree())
		{
			_root.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f - fade / FadeSeconds);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			fade += (float)GetProcessDeltaTime();
		}

		if (!IsInstanceValid(this))
		{
			return;
		}

		Finished = true;
		Busy = false;
		Completed?.Invoke();
		QueueFree();
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		Elapsed += step;

		ShownProgress = Mathf.MoveToward(ShownProgress, Progress, BarSpeed * step);
		ApplyBar();

		_poseTime += step;
		if (_poseTime >= PoseSeconds && _poses[0] != null && _poses[1] != null)
		{
			_poseTime -= PoseSeconds;
			PoseIndex = 1 - PoseIndex;
			PoseSwitches++;
			_noah.Texture = _poses[PoseIndex];
		}
	}

	private void ApplyBar()
	{
		if (_clip != null)
		{
			float scale = _stage.Size.X / ChalkWidth;
			_clip.Size = new Vector2(BottleFill.Size.X * scale * ShownProgress, BottleFill.Size.Y * scale);
		}

		if (_bar != null)
		{
			_bar.Value = ShownProgress * 100.0;
		}

		if (_plainBar != null)
		{
			_plainBar.Value = ShownProgress * 100.0;
		}

		// Noah gatea a lo largo de la barra: su borde derecho sigue al final del relleno.
		if (_noah != null && HasNoah)
		{
			_noah.Position = new Vector2(ShownProgress * Mathf.Max(0.0f, _track.Size.X - NoahSize), 0.0f);
		}
	}

	private void Build()
	{
		_root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		Texture2D chalkEmpty = LoadHeld(ChalkEmptyPath);
		Texture2D chalkFull = LoadHeld(ChalkFullPath);
		if (chalkEmpty != null && chalkFull != null)
		{
			BuildChalk(chalkEmpty, chalkFull);
			return;
		}

		ColorRect background = new() { Name = "Background", Color = new Color(0.07f, 0.06f, 0.08f), MouseFilter = Control.MouseFilterEnum.Ignore };
		background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(background);

		Texture2D sheetTexture = LoadHeld(SheetPath);
		Control content = new() { Name = "Content", MouseFilter = Control.MouseFilterEnum.Ignore };
		_root.AddChild(content);
		float width;
		Color textColor;
		if (sheetTexture != null)
		{
			HasSheet = true;
			TextureRect sheet = new()
			{
				Name = "Sheet",
				Texture = sheetTexture,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			_root.AddChild(sheet);
			_root.MoveChild(sheet, 1);
			PaperSheet.Place(sheet, PaperSheet.Rect);
			PaperSheet.Place(content, PaperSheet.Inner);
			width = PaperSheet.Inner.Size.X - 16.0f;
			textColor = Ink;
		}
		else
		{
			// Fondo liso y texto: una columna centrada.
			content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			content.OffsetLeft = 256.0f;
			content.OffsetRight = -256.0f;
			content.OffsetTop = 96.0f;
			content.OffsetBottom = -96.0f;
			width = 640.0f;
			textColor = Cream;
		}

		HandFont.Apply(_root);

		VBoxContainer box = new() { Name = "Box" };
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		box.AddThemeConstantOverride("separation", 14);
		content.AddChild(box);

		_title = NewLabel("Title", LoadingTips.Title, 40, textColor);
		box.AddChild(_title);
		_tip = NewLabel("Tip", LoadingTips.Pick(_random), 28, textColor);
		_tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_tip.CustomMinimumSize = new Vector2(width, 0.0f);
		box.AddChild(_tip);

		Control spacer = new() { Name = "Spacer", SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddChild(spacer);

		BuildCrawl(box, width);
	}

	private void BuildChalk(Texture2D empty, Texture2D full)
	{
		HasChalkArt = true;
		ColorRect background = new() { Name = "Background", Color = ChalkBackground, MouseFilter = Control.MouseFilterEnum.Ignore };
		background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(background);

		AspectRatioContainer frame = new()
		{
			Name = "Frame",
			Ratio = ChalkWidth / ChalkHeight,
			StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
			AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Center,
			AlignmentVertical = AspectRatioContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(frame);

		_stage = new Control { Name = "Stage", MouseFilter = Control.MouseFilterEnum.Ignore };
		frame.AddChild(_stage);

		TextureRect emptyPicture = new() { Name = "Empty", Texture = empty, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
		emptyPicture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_stage.AddChild(emptyPicture);

		_clip = new Control { Name = "FillClip", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
		_stage.AddChild(_clip);
		_fullPicture = new TextureRect { Name = "Full", Texture = full, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
		_clip.AddChild(_fullPicture);

		_tip = NewLabel("Tip", LoadingTips.Pick(_random), (int)TipFontSize, Cream);
		_tip.VerticalAlignment = VerticalAlignment.Center;
		_tip.AutowrapMode = TextServer.AutowrapMode.Off;
		_stage.AddChild(_tip);

		HandFont.Apply(_root);

		// El consejo cabe en una línea de la caja: si el texto es más largo que ella al tamaño de referencia, se reduce.
		Font font = _tip.GetThemeFont("font");
		if (font != null)
		{
			float width = font.GetStringSize(_tip.Text, HorizontalAlignment.Left, -1, (int)TipFontSize).X;
			if (width > TipBox.Size.X) _tipSize = TipFontSize * TipBox.Size.X / width;
		}

		_stage.Resized += LayoutChalk;
		LayoutChalk();
	}

	// Todo en coordenadas de la imagen de referencia, multiplicadas por lo que se escala la lámina.
	private void LayoutChalk()
	{
		float scale = _stage.Size.X / ChalkWidth;
		if (scale <= 0.0f)
		{
			return;
		}

		_clip.Position = BottleFill.Position * scale;
		_fullPicture.Position = -_clip.Position;
		_fullPicture.Size = _stage.Size;
		_tip.Position = TipBox.Position * scale;
		_tip.Size = TipBox.Size * scale;
		_tip.AddThemeFontSizeOverride("font_size", Mathf.Max(8, Mathf.RoundToInt(_tipSize * scale)));
		ApplyBar();
	}

	private void BuildCrawl(VBoxContainer box, float width)
	{
		_poses[0] = LoadHeld(NoahOnePath);
		_poses[1] = LoadHeld(NoahTwoPath);
		HasNoah = _poses[0] != null || _poses[1] != null;

		_track = new Control { Name = "Track", CustomMinimumSize = new Vector2(width, HasNoah ? NoahSize : 0.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddChild(_track);
		_noah = new TextureRect
		{
			Name = "Noah",
			Texture = _poses[0] ?? _poses[1],
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(NoahSize, NoahSize),
			Size = new Vector2(NoahSize, NoahSize),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = HasNoah,
		};
		_track.AddChild(_noah);

		Texture2D frame = LoadHeld(FramePath);
		Texture2D fill = LoadHeld(FillPath);
		HasBarArt = frame != null && fill != null;
		if (HasBarArt)
		{
			// La barra se dibuja a su tamaño de imagen y se reduce a la anchura de la hoja.
			float scale = width / BarArtWidth;
			Control holder = new() { Name = "BarHolder", CustomMinimumSize = new Vector2(width, BarArtHeight * scale), MouseFilter = Control.MouseFilterEnum.Ignore };
			box.AddChild(holder);
			_bar = new TextureProgressBar
			{
				Name = "Bar",
				MinValue = 0.0,
				MaxValue = 100.0,
				Step = 0.0,
				TextureProgress = fill,
				TextureOver = frame,
				FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight,
				Scale = new Vector2(scale, scale),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			holder.AddChild(_bar);
		}
		else
		{
			_plainBar = new ProgressBar { Name = "PlainBar", MinValue = 0.0, MaxValue = 100.0, Step = 0.0, ShowPercentage = false, CustomMinimumSize = new Vector2(width, 24.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
			box.AddChild(_plainBar);
		}
	}

	private static Label NewLabel(string name, string text, int size, Color color)
	{
		Label label = new()
		{
			Name = name,
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeConstantOverride("outline_size", 0);
		return label;
	}
}
