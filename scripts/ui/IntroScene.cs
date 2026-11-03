using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Intro estilo libro de cuentos: ocho cuadros (IntroFrames) con su voz y un subtítulo en Patrick Hand abajo, y el canto de la madre de fondo
/// en bucle (LoopingBackdrop) durante toda la intro. Es la escena principal del proyecto: se reproduce UNA vez al abrir el juego y luego pasa a
/// MainMenu. "Salir al menú" desde la partida va directo a MainMenu, sin intro.
///
/// Cada cuadro: fundido de entrada de 0,8 s, zoom y paneo muy lentos, fundido de salida; dura lo que su voz más 0,6 s (mínimo 3,5 s). El último se
/// funde a negro más despacio y el canto se apaga con él. La voz sale por el bus SFX y el canto por el bus Musica a -7 dB (~45 % frente a la
/// voz): los dos respetan los deslizadores de settings.cfg, que se aplican al entrar. Las imágenes se cargan de una en una, con la pantalla en
/// negro, y se sueltan al terminar su cuadro: nunca hay dos a la vez.
///
/// Se salta en cualquier momento con Enter, Esc, Espacio o clic (fundido corto, voz y canto cortados). Antes de pasar al menú, dentro del
/// fundido a negro, se vacían los finalizadores de C# (solo en esa transición).
///
/// No se reproduce (pasa directo al menú) en headless, si ya se mostró en esta sesión, con el argumento --sin-intro o si no hay ninguna
/// ilustración. Tolerante a faltantes: sin imagen se omite el cuadro, sin voz dura 3,5 s en silencio, sin canto no suena fondo.
/// </summary>
public partial class IntroScene : Control
{
	public const string SkipArgument = "--sin-intro";
	/// <summary>Volumen del canto (dB) sobre el bus Musica: ~45 % frente a la voz.</summary>
	public const float SongDb = -7.0f;

	public const float SkipFadeSeconds = 0.3f;

	/// <summary>Ya se mostró (o se saltó) en esta sesión: no se repite.</summary>
	public static bool Shown { get; set; }

	/// <summary>Solo pruebas: reproducirla aunque sea headless o ya se haya mostrado (--sin-intro sigue mandando).</summary>
	public static bool ForcePlay { get; set; }

	/// <summary>Solo pruebas: multiplica todas las duraciones para no esperar (1 = real).</summary>
	public static float TimeScale { get; set; } = 1.0f;

	/// <summary>Solo pruebas: argumentos de línea de comandos en lugar de los reales (nulo = los reales).</summary>
	public static string[] CommandLineArgs { get; set; }

	/// <summary>Escena a la que pasa al terminar o saltar (nula en las pruebas que no quieren cambiar de escena).</summary>
	public string NextScene { get; set; } = GameSettings.MenuScene;

	/// <summary>Cuadro (su número, 1 a 8) a punto de empezar: la voz y el canto ya suenan con los volúmenes de los ajustes.</summary>
	public event Action<int> FrameStarted;

	/// <summary>Se acabó (terminada o saltada) y se va a cambiar de escena.</summary>
	public event Action Finished;

	public bool Playing { get; private set; }
	public bool Skipping { get; private set; }
	public bool Leaving { get; private set; }

	/// <summary>Se saltó con una tecla o un clic.</summary>
	public bool Skipped { get; private set; }

	/// <summary>Cuadros mostrados, en orden (sus números).</summary>
	public IReadOnlyList<int> FramesShown => _shown;

	/// <summary>Número del cuadro en pantalla (0 si ninguno).</summary>
	public int CurrentFrame { get; private set; }

	/// <summary>Duración (s, ya con TimeScale) del cuadro en pantalla.</summary>
	public float CurrentDuration => _duration;

	/// <summary>Duraciones (s, ya con TimeScale) de los cuadros mostrados.</summary>
	public IReadOnlyList<float> Durations => _durations;

	/// <summary>Ilustraciones cargadas ahora mismo (0 o 1) y el máximo que hubo a la vez.</summary>
	public int ResidentTextures { get; private set; }
	public int MaxResidentTextures { get; private set; }

	/// <summary>La voz del cuadro suena (o sonaría, en silencio).</summary>
	public bool VoiceActive => _voiceActive;

	public AudioStreamPlayer VoicePlayer { get; private set; }
	public LoopingBackdrop Song { get; private set; }
	public string SubtitleText => _subtitle.Text;
	public float FadeAlpha => _fade.Color.A;
	public Rect2 PictureRect => new(_picture.Position, _picture.Size);

	private readonly List<int> _shown = new();
	private readonly List<float> _durations = new();
	private TextureRect _picture;
	private Label _subtitle;
	private Label _hint;
	private ColorRect _fade;
	private int _index = -1;
	private float _t;
	private float _duration;
	private float _voiceSeconds;
	private float _skipT;
	private float _skipFrom;
	private bool _songFading;
	private bool _voiceActive;
	private bool _silent;
	private Texture2D _texture;
	private AudioStream _voice;

	/// <summary>Si la intro debe verse: no con --sin-intro, ni en headless o repetida (salvo ForcePlay) ni sin ninguna ilustración.</summary>
	public static bool ShouldPlay(IEnumerable<string> args, bool headless, bool alreadyShown, bool forced, bool anyFrame)
	{
		if (args != null && args.Contains(SkipArgument))
		{
			return false;
		}

		return anyFrame && (forced || (!headless && !alreadyShown));
	}

	/// <summary>Duración real (s) de un cuadro dado el largo de su voz: voz más pausa, mínimo 3,5 s.</summary>
	public static float FrameSeconds(double voiceSeconds) => IntroFrames.Duration(voiceSeconds);

	public override void _Ready()
	{
		_picture = GetNode<TextureRect>("Stage/Picture");
		_subtitle = GetNode<Label>("Subtitle");
		_hint = GetNode<Label>("Hint");
		_fade = GetNode<ColorRect>("Fade");
		_picture.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		_subtitle.Text = string.Empty;
		_hint.Text = IntroFrames.SkipHint;
		_fade.Color = Colors.Black;
		StyleTexts();

		// El AudioManager (autoload) ya cargó settings.cfg al iniciar; se repite aquí para que voz y canto respeten los deslizadores desde el primer segundo.
		GameSettings.LoadSettings();
		AudioManager.ApplyVolumes();

		string[] args = CommandLineArgs ?? OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
		bool headless = DisplayServer.GetName() == "headless";
		if (!ShouldPlay(args, headless, Shown, ForcePlay, IntroFrames.All.Any(f => IntroFrames.Exists(f.ImagePath))))
		{
			Callable.From(() => Leave(false)).CallDeferred();
			return;
		}

		AudioManager audio = AudioManager.Instance;
		_silent = audio == null || audio.Silent || !audio.Enabled;
		Shown = true;
		Playing = true;
		VoicePlayer = new AudioStreamPlayer { Bus = SoundCatalog.BusSfx };
		AddChild(VoicePlayer);
		Song = new LoopingBackdrop { Silent = _silent };
		AddChild(Song);
		if (IntroFrames.Exists(IntroFrames.SongPath))
		{
			Song.Start(ResourceLoader.Load<AudioStream>(IntroFrames.SongPath), SoundCatalog.BusMusic, SongDb);
		}

		NextFrame();
	}

	public override void _ExitTree()
	{
		Song?.Stop();
		VoicePlayer?.Stop();
	}

	// Letra manuscrita, subtítulo grande con contorno y sombra para que se lea sobre cualquier ilustración, y la pista de salto casi invisible.
	private void StyleTexts()
	{
		HandFont.Apply(this);
		_subtitle.AddThemeFontSizeOverride("font_size", 34);
		_subtitle.AddThemeColorOverride("font_color", new Color(1.0f, 0.97f, 0.9f));
		_subtitle.AddThemeColorOverride("font_shadow_color", new Color(0.0f, 0.0f, 0.0f, 0.9f));
		_subtitle.AddThemeConstantOverride("shadow_offset_x", 2);
		_subtitle.AddThemeConstantOverride("shadow_offset_y", 3);
		_subtitle.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.85f));
		_subtitle.AddThemeConstantOverride("outline_size", 7);
		_hint.AddThemeFontSizeOverride("font_size", 15);
		_hint.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 1.0f, 0.4f));
		_hint.AddThemeColorOverride("font_shadow_color", new Color(0.0f, 0.0f, 0.0f, 0.6f));
		_hint.AddThemeConstantOverride("shadow_offset_x", 1);
		_hint.AddThemeConstantOverride("shadow_offset_y", 1);
	}

	public override void _Input(InputEvent @event)
	{
		if (!Playing || Leaving)
		{
			return;
		}

		bool skip = @event switch
		{
			InputEventKey { Pressed: true, Echo: false } key => key.Keycode is Key.Enter or Key.KpEnter or Key.Escape or Key.Space
				|| key.PhysicalKeycode is Key.Enter or Key.KpEnter or Key.Escape or Key.Space,
			InputEventMouseButton { Pressed: true } => true,
			_ => false,
		};
		if (skip)
		{
			GetViewport().SetInputAsHandled();
			RequestSkip();
		}
	}

	/// <summary>Salta la intro: fundido corto a negro, voz y canto cortados, y al menú.</summary>
	public void RequestSkip()
	{
		if (!Playing || Skipping || Leaving)
		{
			return;
		}

		Skipped = true;
		Skipping = true;
		_skipT = 0.0f;
		_skipFrom = _fade.Color.A;
		Song?.FadeOut(Scaled(SkipFadeSeconds));
	}

	public override void _Process(double delta)
	{
		if (!Playing || Leaving)
		{
			return;
		}

		float step = (float)delta;
		if (Skipping)
		{
			_skipT += step;
			float k = Mathf.Clamp(_skipT / Scaled(SkipFadeSeconds), 0.0f, 1.0f);
			SetFade(Mathf.Lerp(_skipFrom, 1.0f, k));
			if (VoicePlayer != null && VoicePlayer.Playing)
			{
				VoicePlayer.VolumeDb = k >= 1.0f ? -80.0f : Mathf.LinearToDb(1.0f - k);
			}

			if (k >= 1.0f)
			{
				Leave(true);
			}

			return;
		}

		_t += step;
		if (_t >= _duration)
		{
			NextFrame();
			return;
		}

		UpdateFrame();
	}

	private static float Scaled(float seconds) => seconds * TimeScale;

	private void SetFade(float alpha)
	{
		_fade.Color = new Color(0.0f, 0.0f, 0.0f, Mathf.Clamp(alpha, 0.0f, 1.0f));
		_subtitle.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f - Mathf.Clamp(alpha, 0.0f, 1.0f));
	}

	// Movimiento, fundidos y subtítulo del cuadro en pantalla.
	private void UpdateFrame()
	{
		IntroFrames.Frame frame = IntroFrames.All[_index];
		(float zoom, Vector2 focus) = IntroFrames.At(frame, _t / _duration);
		Rect2 rect = IntroFrames.Layout(Size, _texture.GetSize(), zoom, focus);
		_picture.Position = rect.Position;
		_picture.Size = rect.Size;

		bool last = !HasLaterFrame(_index);
		float outSeconds = Scaled(last ? IntroFrames.LastFadeOutSeconds : IntroFrames.FadeOutSeconds);
		float fadeIn = 1.0f - Mathf.Clamp(_t / Scaled(IntroFrames.FadeInSeconds), 0.0f, 1.0f);
		float fadeOut = Mathf.Clamp((_t - (_duration - outSeconds)) / outSeconds, 0.0f, 1.0f);
		SetFade(Mathf.Max(fadeIn, fadeOut));

		if (last && !_songFading && _t >= _duration - outSeconds)
		{
			_songFading = true;
			Song?.FadeOut(outSeconds);
		}

		if (_voiceActive && _t >= _voiceSeconds * TimeScale)
		{
			_voiceActive = false;
		}
	}

	private bool HasLaterFrame(int index)
	{
		for (int i = index + 1; i < IntroFrames.All.Length; i++)
		{
			if (IntroFrames.Exists(IntroFrames.All[i].ImagePath))
			{
				return true;
			}
		}

		return false;
	}

	// Suelta lo del cuadro anterior (con la pantalla ya en negro) y carga el siguiente que tenga ilustración; si no queda ninguno, termina.
	private void NextFrame()
	{
		ReleaseFrame();
		for (int i = _index + 1; i < IntroFrames.All.Length; i++)
		{
			if (BeginFrame(i))
			{
				return;
			}
		}

		Leave(true);
	}

	private bool BeginFrame(int index)
	{
		IntroFrames.Frame frame = IntroFrames.All[index];
		if (!IntroFrames.Exists(frame.ImagePath) || ResourceLoader.Load<Texture2D>(frame.ImagePath) is not Texture2D texture)
		{
			return false;
		}

		_index = index;
		_texture = texture;
		ResidentTextures++;
		MaxResidentTextures = Mathf.Max(MaxResidentTextures, ResidentTextures);
		_picture.Texture = texture;
		_voice = IntroFrames.Exists(frame.VoicePath) ? ResourceLoader.Load<AudioStream>(frame.VoicePath) : null;
		_voiceSeconds = _voice != null ? (float)_voice.GetLength() : 0.0f;
		_duration = Scaled(FrameSeconds(_voice != null ? _voice.GetLength() : 0.0));
		_t = 0.0f;
		CurrentFrame = frame.Number;
		_shown.Add(frame.Number);
		_durations.Add(_duration);
		_subtitle.Text = frame.Subtitle;
		SetFade(1.0f);
		UpdateFrame();

		_voiceActive = _voice != null;
		if (_voice != null && !_silent)
		{
			VoicePlayer.Stream = _voice;
			VoicePlayer.VolumeDb = 0.0f;
			VoicePlayer.Play();
		}

		FrameStarted?.Invoke(frame.Number);
		return true;
	}

	private void ReleaseFrame()
	{
		VoicePlayer?.Stop();
		if (VoicePlayer != null)
		{
			VoicePlayer.Stream = null;
		}

		_voice = null;
		_voiceActive = false;
		if (_texture != null)
		{
			_picture.Texture = null;
			_texture = null;
			ResidentTextures--;
		}
	}

	// Paso al siguiente escena: sin nada sonando y, con la pantalla en negro, se vacían los finalizadores (solo en esta transición).
	private async void Leave(bool collect)
	{
		if (Leaving)
		{
			return;
		}

		Leaving = true;
		Shown = true;
		VoicePlayer?.Stop();
		_voiceActive = false;
		Song?.Stop();
		ReleaseFrame();
		SetFade(1.0f);
		if (collect && IsInsideTree())
		{
			SceneTree tree = GetTree();
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			GC.Collect();
			GC.WaitForPendingFinalizers();
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		Finished?.Invoke();
		if (NextScene != null)
		{
			GetTree().ChangeSceneToFile(NextScene);
		}
	}
}
