using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la intro estilo libro de cuentos. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/IntroTest.tscn
/// Con las duraciones reducidas (IntroScene.TimeScale) y, salvo donde se dice, con AudioManager en silencio.
///
///   1. Cuándo se reproduce: no en headless, no repetida, no con --sin-intro (que manda incluso con ForcePlay) y no sin ilustraciones; es la
///      escena principal del proyecto. "Salir al menú" no pasa por la intro (RunManager va a MainMenu).
///   2. Los ocho cuadros: archivos y subtítulos exactos, importados con pérdida y mipmaps, duración = voz + 0,6 s con mínimo de 3,5 s, y el
///      movimiento cubre siempre la ventana (sin bordes) con un zoom de 1,00 a 1,06 y un recorrido distinto en cada cuadro.
///   3. Reproducción completa: los ocho en orden, con su subtítulo, una sola imagen cargada a la vez, el canto apagado al final y el paso al menú.
///   4. Volúmenes: los de settings.cfg están aplicados cuando suena el primer cuadro; la voz va por SFX y el canto por Musica a -7 dB.
///   5. Canto en bucle: el empalme se cruza un segundo antes del final (con reproductores reales y en silencio) y se apaga con fundido.
///   6. Saltar con Enter, Esc, Espacio o clic: fundido corto, nada sonando y paso al menú.
///   7. Argumento --sin-intro: va directo al menú, sin cuadros ni sonido.
///   8. Archivos ausentes: sin una imagen se omite el cuadro, sin voz dura 3,5 s, sin canto no suena fondo, sin nada pasa al menú; sin errores.
///   9. Paso al menú de verdad (cambio de escena) con la música del menú arrancando con fundido.
/// Antes de salir y antes de cada cambio de escena recolecta los envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class IntroTest : Node
{
	private const string TestSettings = "user://intro_test_settings.cfg";

	private readonly List<string> _failures = new();
	private AudioManager _audio;
	private bool _audioSilent;

	public override async void _Ready()
	{
		GD.Print("=== IntroTest ===");
		string originalPath = GameSettings.SettingsPath;
		try
		{
			_audio = AudioManager.Instance;
			Check("el AudioManager está registrado como autoload", _audio != null, "sin AudioManager");
			_audioSilent = _audio.Silent;
			_audio.Silent = true;
			GameSettings.SettingsPath = TestSettings;
			Reset();
			WhenItPlays();
			FrameData();
			await FullRun();
			await Volumes();
			await Loop();
			await Skips();
			await NoIntroArgument();
			await MissingFiles();
			await ToMenu();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}
		finally
		{
			Reset();
			_audio.Silent = _audioSilent;
			GameSettings.SettingsPath = originalPath;
			DirAccess.RemoveAbsolute(TestSettings);
			GameSettings.MasterVolume = GameSettings.DefaultMasterVolume;
			GameSettings.SfxVolume = GameSettings.DefaultSfxVolume;
			GameSettings.MusicVolume = GameSettings.DefaultMusicVolume;
			AudioManager.ApplyVolumes();
			AudioManager.StopMusic();
			GetTree().Paused = false;
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		await BossTestHarness.CollectWrappers(GetTree());
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private void Check(string what, bool ok, string detail)
	{
		GD.Print($"  {(ok ? "OK   " : "FALLO")} {what}");
		if (!ok)
		{
			GD.Print($"         {detail}");
			_failures.Add(what);
		}
	}

	private static Task Frames(int count, SceneTree tree) => BossTestHarness.Frames(tree, count);

	private void Reset()
	{
		IntroScene.Shown = false;
		IntroScene.ForcePlay = false;
		IntroScene.TimeScale = 1.0f;
		IntroScene.CommandLineArgs = null;
		IntroFrames.Folder = "res://assets/intro/";
		IntroFrames.ExistsOverride = null;
	}

	// Una intro como hija de la prueba (no cambia de escena) con los tiempos reducidos.
	private IntroScene NewIntro(float scale = 0.05f, bool silent = true)
	{
		IntroScene.Shown = false;
		IntroScene.ForcePlay = true;
		IntroScene.TimeScale = scale;
		_audio.Silent = silent;
		IntroScene intro = GD.Load<PackedScene>("res://scenes/ui/IntroScene.tscn").Instantiate<IntroScene>();
		intro.NextScene = null;
		return intro;
	}

	private async Task Dispose(IntroScene intro)
	{
		if (GodotObject.IsInstanceValid(intro))
		{
			intro.QueueFree();
			await GetTree().ToSignal(intro, Node.SignalName.TreeExited);
		}

		_audio.Silent = true;
		await Frames(1, GetTree());
	}

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void WhenItPlays()
	{
		GD.Print("\n--- Cuándo se reproduce ---");
		string[] none = Array.Empty<string>();
		Check("con ventana y la primera vez: se reproduce", IntroScene.ShouldPlay(none, false, false, false, true), "debería verse");
		Check("en headless se omite", !IntroScene.ShouldPlay(none, true, false, false, true), "headless");
		Check("la segunda vez en la sesión se omite", !IntroScene.ShouldPlay(none, false, true, false, true), "repetida");
		Check("--sin-intro la omite", !IntroScene.ShouldPlay(new[] { "--sin-intro" }, false, false, false, true), "--sin-intro");
		Check("--sin-intro manda incluso sobre ForcePlay", !IntroScene.ShouldPlay(new[] { "--sin-intro" }, false, false, true, true), "--sin-intro con ForcePlay");
		Check("otros argumentos no la afectan", IntroScene.ShouldPlay(new[] { "--fixed-fps", "60" }, false, false, false, true), "otros argumentos");
		Check("sin ninguna ilustración se omite", !IntroScene.ShouldPlay(none, false, false, false, false), "sin imágenes");
		Check("ForcePlay (pruebas) la reproduce aunque sea headless y repetida", IntroScene.ShouldPlay(none, true, true, true, true), "ForcePlay");
		Check("la escena principal del proyecto es la intro", (string)ProjectSettings.GetSetting("application/run/main_scene") == "res://scenes/ui/IntroScene.tscn", $"{ProjectSettings.GetSetting("application/run/main_scene")}");
		Check("el menú sigue siendo MainMenu (es adonde va «Salir al menú»)", GameSettings.MenuScene == "res://scenes/ui/MainMenu.tscn", GameSettings.MenuScene);
		Check("el argumento se llama --sin-intro", IntroScene.SkipArgument == "--sin-intro", IntroScene.SkipArgument);
		Check("sin ventana, la intro de verdad (sin ForcePlay) no se reproduce en esta prueba", DisplayServer.GetName() != "headless" || !IntroScene.ShouldPlay(none, DisplayServer.GetName() == "headless", false, false, true), "headless");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private void FrameData()
	{
		GD.Print("\n--- Los ocho cuadros ---");
		string[] subtitles =
		{
			"Querido diario: esta es mi cuna. Es pequeña, pero es mía.",
			"Mamá me canta todas las noches. Dice que las nanas nos protegen.",
			"Mis juguetes siempre me cuidaron. Eso creía.",
			"Hoy la canción sonó distinta.",
			"Y la cuna se hizo enorme.",
			"Mis juguetes ya no quieren abrazarme. Quieren alcanzarme.",
			"Mamá está al final. Todavía canta.",
			"Voy a llegar hasta ella. Aunque tenga que gatear.",
		};
		Check("hay ocho cuadros numerados del 1 al 8", IntroFrames.All.Select(f => f.Number).SequenceEqual(Enumerable.Range(1, 8)), "numeración");
		Check("los subtítulos son los acordados", IntroFrames.All.Select(f => f.Subtitle).SequenceEqual(subtitles), "subtítulos");
		Check("la pista de salto es «Pulsa Enter para saltar»", IntroFrames.SkipHint == "Pulsa Enter para saltar", IntroFrames.SkipHint);

		double total = 0.0;
		foreach (IntroFrames.Frame frame in IntroFrames.All)
		{
			Check($"cuadro {frame.Number}: existen su ilustración, su voz", ResourceLoader.Exists(frame.ImagePath) && ResourceLoader.Exists(frame.VoicePath), frame.ImagePath);
			Texture2D texture = ResourceLoader.Load<Texture2D>(frame.ImagePath);
			Check($"cuadro {frame.Number}: la ilustración es de 1920x1080 y está importada como textura comprimida", texture is CompressedTexture2D && texture.GetWidth() == 1920 && texture.GetHeight() == 1080, $"{texture?.GetWidth()}x{texture?.GetHeight()}");
			AudioStream voice = ResourceLoader.Load<AudioStream>(frame.VoicePath);
			double length = voice.GetLength();
			float seconds = IntroFrames.Duration(length);
			total += seconds;
			Check($"cuadro {frame.Number}: voz de {length:0.00} s y cuadro de {seconds:0.00} s (voz + 0,6 s, mínimo 3,5 s)", length > 0.5 && Mathf.IsEqualApprox(seconds, Mathf.Max(3.5f, (float)length + 0.6f)), $"{length} {seconds}");
		}

		Check("sin voz un cuadro dura el mínimo de 3,5 s y una voz muy corta también", IntroFrames.Duration(0.0) == 3.5f && IntroFrames.Duration(1.0) == 3.5f && Mathf.IsEqualApprox(IntroFrames.Duration(6.0), 6.6f), "mínimo");
		GD.Print($"  Duración total de la intro (voces + pausas): {total:0.0} s ({(int)total / 60}:{(int)total % 60:00})");

		Check("el canto existe", ResourceLoader.Exists(IntroFrames.SongPath) && ResourceLoader.Load<AudioStream>(IntroFrames.SongPath).GetLength() > 20.0, IntroFrames.SongPath);

		// Movimiento: cubre la ventana en cualquier proporción, zoom dentro de 1,00 a 1,06 y cada cuadro con su recorrido.
		Vector2 texture1920 = new(1920.0f, 1080.0f);
		bool covers = true;
		bool zoomRange = true;
		string detail = string.Empty;
		foreach (IntroFrames.Frame frame in IntroFrames.All)
		{
			zoomRange &= frame.ZoomFrom >= 1.0f && frame.ZoomFrom <= 1.06f && frame.ZoomTo >= 1.0f && frame.ZoomTo <= 1.06f;
			foreach (Vector2 stage in new[] { new Vector2(1152.0f, 648.0f), new Vector2(1920.0f, 1080.0f), new Vector2(1280.0f, 1024.0f), new Vector2(800.0f, 1200.0f) })
			{
				for (int i = 0; i <= 20; i++)
				{
					(float zoom, Vector2 focus) = IntroFrames.At(frame, i / 20.0f);
					Rect2 rect = IntroFrames.Layout(stage, texture1920, zoom, focus);
					bool ok = rect.Position.X <= 0.001f && rect.Position.Y <= 0.001f && rect.End.X >= stage.X - 0.001f && rect.End.Y >= stage.Y - 0.001f;
					covers &= ok;
					if (!ok && detail.Length == 0)
					{
						detail = $"cuadro {frame.Number}, ventana {stage}, {rect}";
					}
				}
			}
		}

		Check("la ilustración cubre siempre la ventana (sin bordes) en cualquier proporción", covers, detail);
		Check("el zoom queda entre 1,00 y 1,06", zoomRange, "zoom fuera de rango");
		Vector2 stage648 = new(1152.0f, 648.0f);
		Rect2 first = IntroFrames.Layout(stage648, texture1920, IntroFrames.At(IntroFrames.All[0], 0.0f).Zoom, IntroFrames.At(IntroFrames.All[0], 0.0f).Focus);
		Check("al empezar a zoom 1,00 la ilustración llena exactamente la ventana", first.Size.IsEqualApprox(stage648) && first.Position.IsEqualApprox(Vector2.Zero), $"{first}");
		(float z1, _) = IntroFrames.At(IntroFrames.All[0], 1.0f);
		(float z5a, _) = IntroFrames.At(IntroFrames.All[4], 0.0f);
		(float z5b, _) = IntroFrames.At(IntroFrames.All[4], 1.0f);
		Check("el cuadro 1 se acerca y el 5 se aleja (zoom hacia atrás)", z1 > 1.0f && z5b < z5a, $"{z1} {z5a} {z5b}");
		IntroFrames.Frame six = IntroFrames.All[5];
		Rect2 six0 = IntroFrames.Layout(stage648, texture1920, six.ZoomFrom, six.FocusFrom);
		Rect2 six1 = IntroFrames.Layout(stage648, texture1920, six.ZoomTo, six.FocusTo);
		Check("el cuadro 6 es un paneo lateral (la imagen se desplaza en horizontal)", Mathf.Abs(six0.Position.X - six1.Position.X) > 10.0f && Mathf.Abs(six0.Position.Y - six1.Position.Y) < 1.0f, $"{six0.Position} {six1.Position}");
		IntroFrames.Frame two = IntroFrames.All[1];
		Check("el cuadro 2 hace un paneo a la cara de la madre (hacia arriba y a la izquierda)", two.FocusTo.Y < two.FocusFrom.Y && two.FocusTo.X < two.FocusFrom.X, $"{two.FocusFrom} {two.FocusTo}");
		bool distinct = IntroFrames.All.Select(f => (f.ZoomFrom, f.ZoomTo, f.FocusFrom, f.FocusTo)).Distinct().Count() == 8;
		Check("cada cuadro tiene un movimiento distinto", distinct, "movimientos repetidos");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task FullRun()
	{
		GD.Print("\n--- Reproducción completa ---");
		SceneTree tree = GetTree();
		IntroScene intro = NewIntro(0.05f);
		List<int> started = new();
		List<string> subtitles = new();
		bool finished = false;
		intro.FrameStarted += number =>
		{
			started.Add(number);
			subtitles.Add(intro.SubtitleText);
		};
		intro.Finished += () => finished = true;
		AddChild(intro);
		await Frames(2, tree);
		Check("empieza en negro y con la pista de salto", intro.Playing && intro.GetNode<Label>("Hint").Text == "Pulsa Enter para saltar", "estado inicial");
		Check("la pista de salto es discreta (pequeña y casi transparente)", intro.GetNode<Label>("Hint").GetThemeFontSize("font_size") <= 16 && intro.GetNode<Label>("Hint").GetThemeColor("font_color").A <= 0.5f, "pista visible");
		Check("el subtítulo usa la letra manuscrita con sombra", intro.GetNode<Label>("Subtitle").GetThemeFont("font") == HandFont.Load() && intro.GetNode<Label>("Subtitle").GetThemeConstant("shadow_offset_y") > 0 && intro.GetNode<Label>("Subtitle").GetThemeConstant("outline_size") > 0, "fuente");

		await BossTestHarness.WaitUntil(tree, () => finished, 1500, "el final de la intro");
		Check("se muestran los ocho cuadros en orden", started.SequenceEqual(Enumerable.Range(1, 8)) && intro.FramesShown.SequenceEqual(started), string.Join(",", started));
		Check("cada cuadro enseña su subtítulo", subtitles.SequenceEqual(IntroFrames.All.Select(f => f.Subtitle)), string.Join(" | ", subtitles));
		Check("nunca hay más de una ilustración cargada a la vez", intro.MaxResidentTextures == 1, $"{intro.MaxResidentTextures}");
		Check("al terminar no queda ninguna cargada", intro.ResidentTextures == 0 && intro.GetNode<TextureRect>("Stage/Picture").Texture == null, $"{intro.ResidentTextures}");
		float scale = IntroScene.TimeScale;
		bool durations = true;
		for (int i = 0; i < 8; i++)
		{
			float expected = IntroFrames.Duration(ResourceLoader.Load<AudioStream>(IntroFrames.All[i].VoicePath).GetLength()) * scale;
			durations &= Mathf.IsEqualApprox(intro.Durations[i], expected);
		}

		Check("la duración de cada cuadro es la de su voz más 0,6 s (mínimo 3,5 s)", durations, string.Join(",", intro.Durations));
		Check("la intro termina en negro, con el canto apagado y avisando", intro.FadeAlpha == 1.0f && !intro.Song.Active && intro.Leaving && !intro.VoiceActive, $"{intro.FadeAlpha} {intro.Song.Active}");
		Check("marca la intro como ya mostrada (no se repite)", IntroScene.Shown, "sin marca");
		Check("sin pedirlo no se salta nada", !intro.Skipped, "saltada");
		await Dispose(intro);
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task Volumes()
	{
		GD.Print("\n--- Volúmenes de los ajustes ---");
		SceneTree tree = GetTree();
		WriteSettings("[audio]\nmaster=0.35\nsfx=0.6\nmusic=0.2\n");
		// Como si el autoload no los hubiera aplicado: se deja todo a otros valores y la intro tiene que aplicar los del archivo.
		GameSettings.MasterVolume = 1.0f;
		GameSettings.SfxVolume = 1.0f;
		GameSettings.MusicVolume = 1.0f;
		AudioManager.ApplyVolumes();

		IntroScene intro = NewIntro(0.05f, silent: false);
		(float master, float sfx, float music, bool songPlaying, bool voicePlaying, string voiceBus, string songBus, float songDb) seen = default;
		intro.FrameStarted += number =>
		{
			if (number != 1)
			{
				return;
			}

			AudioStreamPlayer songPlayer = intro.Song.GetChildren().OfType<AudioStreamPlayer>().FirstOrDefault();
			seen = (AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master")), AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("SFX")),
				AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Musica")), intro.Song.AnyPlayerPlaying, intro.VoicePlayer.Playing, intro.VoicePlayer.Bus, songPlayer?.Bus, songPlayer?.VolumeDb ?? 0.0f);
		};
		AddChild(intro);
		await Frames(2, tree);
		Check("el volumen general del archivo está aplicado cuando suena el primer cuadro", Mathf.Abs(seen.master - Mathf.LinearToDb(0.35f)) < 0.01f, $"{seen.master}");
		Check("los efectos (la voz) y la música (el canto) también", Mathf.Abs(seen.sfx - Mathf.LinearToDb(0.6f)) < 0.01f && Mathf.Abs(seen.music - Mathf.LinearToDb(0.2f)) < 0.01f, $"{seen.sfx} {seen.music}");
		Check("la voz y el canto suenan desde el primer cuadro", seen.voicePlaying && seen.songPlaying, $"{seen.voicePlaying} {seen.songPlaying}");
		Check("la voz va por el bus SFX y el canto por el bus Musica", seen.voiceBus == "SFX" && seen.songBus == "Musica", $"{seen.voiceBus} {seen.songBus}");
		Check("el canto suena a -7 dB (~45 % frente a la voz a 0 dB)", Mathf.Abs(IntroScene.SongDb - -7.0f) < 0.001f && Mathf.Abs(Mathf.DbToLinear(IntroScene.SongDb) - 0.447f) < 0.01f, $"{IntroScene.SongDb}");
		Check("el canto no pisa los deslizadores: el volumen del bus Musica no se toca", Mathf.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Musica")) - Mathf.LinearToDb(0.2f)) < 0.01f, "bus Musica");
		await Dispose(intro);
		AudioManager.StopMusic();
		Reset();
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task Loop()
	{
		GD.Print("\n--- Canto en bucle ---");
		SceneTree tree = GetTree();
		AudioStream song = ResourceLoader.Load<AudioStream>(IntroFrames.SongPath);
		double length = song.GetLength();
		Check($"el canto dura unos 25 s ({length:0.0} s)", length > 20.0 && length < 30.0, $"{length}");

		// En silencio (posición virtual): el cruce empieza un segundo antes del final y el empalme cuenta una vuelta.
		LoopingBackdrop silent = new() { Silent = true };
		AddChild(silent);
		silent.Start(song, "Musica", -10.0f);
		silent.Advance(length - 1.5);
		Check("antes del último segundo no hay cruce", !silent.Crossing && silent.LoopCount == 0, $"{silent.Crossing}");
		silent.Advance(0.6);
		Check("a un segundo del final empieza el cruce", silent.Crossing && silent.LoopCount == 0, $"{silent.Crossing}");
		silent.Advance(0.5);
		silent.Advance(0.5);
		Check("al llegar al final vuelve al principio sin cortarse (una vuelta) y sigue activo", !silent.Crossing && silent.LoopCount == 1 && silent.Active, $"{silent.LoopCount}");
		silent.Advance(length - 1.2);
		silent.Advance(1.5);
		Check("da la segunda vuelta igual", silent.LoopCount == 2 && silent.Active, $"{silent.LoopCount}");
		silent.FadeOut(1.0f);
		silent.Advance(0.5);
		Check("FadeOut baja a la mitad y sigue sonando", Mathf.IsEqualApprox(silent.Master, 0.5f) && silent.Active, $"{silent.Master}");
		silent.Advance(0.6);
		Check("y al segundo lo detiene del todo", !silent.Active && silent.Master <= 0.0f, $"{silent.Active}");
		silent.Free();

		// Con reproductores reales: se arranca a 2,2 s del final y se espera el empalme (tiempo real).
		LoopingBackdrop real = new() { Silent = false };
		AddChild(real);
		Check("arranca con reproductores reales", real.Start(song, "Musica", -80.0f, length - 2.2) && real.AnyPlayerPlaying, "sin reproducción");
		bool crossed = false;
		ulong began = Time.GetTicksMsec();
		while (Time.GetTicksMsec() - began < 3600)
		{
			await Frames(1, tree);
			crossed |= real.Crossing;
		}

		Check("hubo un cruce un segundo antes del final y el bucle dio la vuelta", crossed && real.LoopCount == 1, $"{crossed} {real.LoopCount}");
		Check("tras el empalme sigue sonando desde el principio (no desde el final)", real.Active && real.AnyPlayerPlaying && real.Position < 3.0, $"{real.Position}");
		real.Stop();
		Check("Stop lo deja sin ningún reproductor sonando", !real.Active && !real.AnyPlayerPlaying, "sigue sonando");
		real.Free();
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task Skips()
	{
		GD.Print("\n--- Saltar la intro ---");
		SceneTree tree = GetTree();
		InputEvent[] inputs =
		{
			new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true },
			new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true },
			new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true },
			new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(100.0f, 100.0f) },
		};
		string[] names = { "Enter", "Esc", "Espacio", "clic" };
		for (int i = 0; i < inputs.Length; i++)
		{
			IntroScene intro = NewIntro(0.5f, silent: false);
			bool finished = false;
			intro.Finished += () => finished = true;
			AddChild(intro);
			await BossTestHarness.WaitUntil(tree, () => intro.CurrentFrame >= 2, 600, "el segundo cuadro");
			Check($"{names[i]}: antes de saltar suena la voz y el canto de verdad", intro.VoicePlayer.Playing && intro.Song.AnyPlayerPlaying, "sin sonido");
			Input.ParseInputEvent(inputs[i]);
			await Frames(2, tree);
			Check($"{names[i]}: salta con un fundido corto (no corta en seco)", intro.Skipping && intro.Skipped && !intro.Leaving, $"{intro.Skipping}");
			await BossTestHarness.WaitUntil(tree, () => finished, 300, "el fin del salto");
			await Frames(2, tree);
			Check($"{names[i]}: termina en negro con solo los cuadros que se vieron y sin ninguno cargado", intro.FadeAlpha == 1.0f && intro.FramesShown.Count < 8 && intro.ResidentTextures == 0, $"{intro.FadeAlpha} {intro.FramesShown.Count}");
			Check($"{names[i]}: no queda nada sonando (ni voz ni canto)", !intro.VoicePlayer.Playing && !intro.VoiceActive && !intro.Song.Active && !intro.Song.AnyPlayerPlaying, "sigue sonando");
			Check($"{names[i]}: cuenta como mostrada", IntroScene.Shown, "sin marca");
			await Dispose(intro);
		}

		// Teclas que no saltan.
		IntroScene other = NewIntro(0.5f);
		AddChild(other);
		await Frames(3, tree);
		Input.ParseInputEvent(new InputEventKey { Keycode = Key.A, PhysicalKeycode = Key.A, Pressed = true });
		Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
		await Frames(3, tree);
		Check("otra tecla, o soltar Enter, no salta", !other.Skipping && !other.Skipped, "saltó sin querer");
		other.RequestSkip();
		other.RequestSkip();
		Check("saltar dos veces seguidas no rompe nada", other.Skipping, "doble salto");
		await Dispose(other);
		Reset();
	}

	// --- 7 ------------------------------------------------------------------------------------------------------------

	private async Task NoIntroArgument()
	{
		GD.Print("\n--- Argumento --sin-intro ---");
		SceneTree tree = GetTree();
		IntroScene intro = NewIntro(0.05f, silent: false);
		IntroScene.CommandLineArgs = new[] { "--sin-intro" };
		bool finished = false;
		int started = 0;
		intro.FrameStarted += _ => started++;
		intro.Finished += () => finished = true;
		AddChild(intro);
		await BossTestHarness.WaitUntil(tree, () => finished, 60, "pasar al menú sin intro");
		Check("con --sin-intro no se muestra ningún cuadro y pasa al menú", started == 0 && !intro.Playing && intro.FramesShown.Count == 0, $"{started}");
		Check("sin intro no suena nada (ni voz ni canto)", intro.VoicePlayer == null && intro.Song == null, "audio creado");
		await Dispose(intro);

		IntroScene.CommandLineArgs = Array.Empty<string>();
		intro = NewIntro(0.05f);
		IntroScene.CommandLineArgs = Array.Empty<string>();
		AddChild(intro);
		await Frames(3, tree);
		Check("con otros argumentos sí se muestra", intro.Playing && intro.CurrentFrame == 1, $"{intro.CurrentFrame}");
		await Dispose(intro);

		// Sin ForcePlay y en headless (la situación de las pruebas y de la suite) no se muestra, sin tocar nada.
		Reset();
		_audio.Silent = true;
		intro = GD.Load<PackedScene>("res://scenes/ui/IntroScene.tscn").Instantiate<IntroScene>();
		intro.NextScene = null;
		bool headless = DisplayServer.GetName() == "headless";
		AddChild(intro);
		await Frames(3, tree);
		Check("en headless la intro se omite", !headless || (!intro.Playing && intro.FramesShown.Count == 0), "se reprodujo en headless");
		await Dispose(intro);
		Reset();
	}

	// --- 8 ------------------------------------------------------------------------------------------------------------

	private async Task MissingFiles()
	{
		GD.Print("\n--- Archivos ausentes ---");
		SceneTree tree = GetTree();

		// Sin una ilustración y sin una voz y sin el canto.
		IntroFrames.ExistsOverride = path => !(path.EndsWith("escena_03.png") || path.EndsWith("voz_05.mp3") || path.EndsWith("canto_mama.mp3")) && ResourceLoader.Exists(path);
		IntroScene intro = NewIntro(0.05f);
		bool finished = false;
		intro.Finished += () => finished = true;
		AddChild(intro);
		await BossTestHarness.WaitUntil(tree, () => finished, 1500, "el final con archivos ausentes");
		Check("sin la ilustración 3 se omite ese cuadro y siguen los demás", intro.FramesShown.SequenceEqual(new[] { 1, 2, 4, 5, 6, 7, 8 }), string.Join(",", intro.FramesShown));
		int fifth = intro.FramesShown.ToList().IndexOf(5);
		Check("sin la voz 5 el cuadro dura el mínimo (3,5 s) en silencio", Mathf.IsEqualApprox(intro.Durations[fifth], 3.5f * IntroScene.TimeScale), $"{intro.Durations[fifth]}");
		Check("sin canto no suena fondo y no hay error", !intro.Song.Active && intro.Leaving, "canto");
		await Dispose(intro);

		// Sin ningún archivo: pasa al menú sin cuadros.
		IntroFrames.ExistsOverride = _ => false;
		intro = NewIntro(0.05f);
		finished = false;
		intro.Finished += () => finished = true;
		AddChild(intro);
		await BossTestHarness.WaitUntil(tree, () => finished, 60, "pasar al menú sin archivos");
		Check("sin ningún archivo pasa directo al menú sin mostrar nada", intro.FramesShown.Count == 0 && !intro.Playing, $"{intro.FramesShown.Count}");
		await Dispose(intro);

		// Carpeta que no existe.
		IntroFrames.ExistsOverride = null;
		IntroFrames.Folder = "res://assets/intro_no_existe/";
		intro = NewIntro(0.05f);
		finished = false;
		intro.Finished += () => finished = true;
		AddChild(intro);
		await BossTestHarness.WaitUntil(tree, () => finished, 60, "pasar al menú con la carpeta ausente");
		Check("con la carpeta ausente tampoco hay error y pasa al menú", intro.FramesShown.Count == 0, "carpeta ausente");
		await Dispose(intro);
		Reset();
	}

	// --- 9 ------------------------------------------------------------------------------------------------------------

	private async Task ToMenu()
	{
		GD.Print("\n--- Paso real al menú ---");
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		AudioManager.StopMusic();
		IntroScene intro = NewIntro(0.05f);
		intro.NextScene = GameSettings.MenuScene;
		tree.Root.AddChild(intro);
		tree.CurrentScene = intro;
		await Frames(3, tree);
		Check("la intro corre como escena actual", intro.Playing, "sin reproducir");
		await BossTestHarness.CollectWrappers(tree);
		Node menu = await BossTestHarness.WaitForScene(tree, intro, GameSettings.MenuScene);
		Check("al terminar la escena actual es el menú principal", menu is MainMenu, $"{menu?.Name}");
		await Frames(3, tree);
		Check("el menú arranca con su música (menu) con fundido", _audio.CurrentMusic == "menu" && _audio.Music.Layers.Any(l => l.Name == "menu" && l.Gain < 1.0f), $"{_audio.CurrentMusic}");
		Check("la intro no se repite al volver al menú", IntroScene.Shown, "sin marca");
		await BossTestHarness.CollectWrappers(tree);
		await BossTestHarness.Restore(tree, testScene, menu);
		AudioManager.StopMusic();

		// Saltada: también termina en el menú.
		intro = NewIntro(0.5f);
		intro.NextScene = GameSettings.MenuScene;
		tree.Root.AddChild(intro);
		tree.CurrentScene = intro;
		await Frames(5, tree);
		intro.RequestSkip();
		await BossTestHarness.CollectWrappers(tree);
		menu = await BossTestHarness.WaitForScene(tree, intro, GameSettings.MenuScene);
		Check("saltada también pasa al menú principal", menu is MainMenu, $"{menu?.Name}");
		await BossTestHarness.CollectWrappers(tree);
		await BossTestHarness.Restore(tree, testScene, menu);
		AudioManager.StopMusic();
		Reset();
	}

	private static void WriteSettings(string text)
	{
		using Godot.FileAccess file = Godot.FileAccess.Open(TestSettings, Godot.FileAccess.ModeFlags.Write);
		file.StoreString(text);
	}
}
