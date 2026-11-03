using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la música, sin reproducir nada real (AudioManager en modo Silent, que es también lo que hace --headless). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/AudioMusicTest.tscn
///
///   1. Catálogo e importación: menu, the_cradle y jefe están registrados como música en bucle, sus archivos existen y el bucle quedó activo
///      en la importación de los .ogg.
///   2. Transiciones: una pista entra con fundido, otra la cruza en ~1 s (las dos suenan a la vez a mitad del cruce), pedir la que ya suena no
///      la reinicia (de una sala normal a otra normal) y apagarla con fundido o cortarla funciona.
///   2b. Inicio y bucle: cada pista arranca en su StartSeconds y el bucle vuelve a su LoopOffsetSeconds (comprobado con un reproductor real, no
///      solo con las propiedades); 0/0 deja el stream importado tal cual y el de la caché no se toca.
///   3. Pausa: con el árbol en pausa la música sigue y el fundido avanza.
///   4. Tolerancia: sin archivo (o con un id desconocido) queda en silencio, sin errores ni fallos de audio contados.
///   5. Deslizador «Música»: 70 % por defecto, mueve el bus Musica, se guarda en settings.cfg y se recupera; el menú principal y el de
///      pausa lo traen y caben (1152x648).
///   6. Partida real: el menú suena menu; la partida, the_cradle (sin reiniciarse de sala a sala); la sala del jefe, jefe; al ganar o al morir
///      la música se apaga con fundido y suena victoria o derrota; volver al menú devuelve menu.
/// Antes de salir y antes de cada cambio de escena recolecta los envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class AudioMusicTest : Node
{
	private const string TestSettings = "user://audio_music_test_settings.cfg";

	private readonly List<string> _failures = new();
	private AudioManager _audio;

	public override async void _Ready()
	{
		GD.Print("=== AudioMusicTest ===");
		string originalPath = GameSettings.SettingsPath;
		string originalFolder = SoundCatalog.MusicFolder;
		try
		{
			_audio = AudioManager.Instance;
			Check("el AudioManager está registrado como autoload", _audio != null, "sin AudioManager");
			_audio.Silent = true;
			GameSettings.SettingsPath = TestSettings;
			CatalogAndImport();
			Transitions();
			await StartAndLoop();
			await Pause();
			MissingFiles();
			await Slider();
			await FullRun();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}
		finally
		{
			SoundCatalog.MusicFolder = originalFolder;
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

	private MusicPlayer Music => _audio.Music;

	private AudioManager.Request LastMusicRequest() => _audio.Requests.Last(r => r.Id.StartsWith("musica:"));

	private static Task Frames(SceneTree tree, int count) => BossTestHarness.Frames(tree, count);

	private void Reset()
	{
		AudioManager.StopMusic();
		_audio.ResetFailures();
		_audio.ClearRequests();
	}

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void CatalogAndImport()
	{
		GD.Print("\n--- Catálogo e importación ---");
		string[] ids = { "menu", "the_cradle", "jefe" };
		Check("los tres ids son los de SoundCatalog", ids.SequenceEqual(new[] { SoundCatalog.MusicMenu, SoundCatalog.MusicCradle, SoundCatalog.MusicBoss }), "ids");
		Check("el catálogo trae exactamente tres pistas", SoundCatalog.AllMusic.Count() == 3, $"{SoundCatalog.AllMusic.Count()}");
		Check("el bus Musica existe", AudioServer.GetBusIndex(SoundCatalog.BusMusic) >= 0, "sin bus");
		(string Id, double Min, double Max)[] lengths = { ("menu", 50, 62), ("the_cradle", 115, 127), ("jefe", 70, 82) };
		foreach ((string id, double min, double max) in lengths)
		{
			SoundCatalog.MusicTrack track = SoundCatalog.GetMusic(id);
			Check($"{id}: registrada como música en bucle en el bus Musica", track != null && track.Loop && track.Bus == "Musica", "sin registro");
			Check($"{id}: su archivo existe", track != null && ResourceLoader.Exists(track.Path), track?.Path);
			AudioStreamOggVorbis ogg = ResourceLoader.Load<AudioStream>(track.Path) as AudioStreamOggVorbis;
			Check($"{id}: es un ogg y el bucle quedó activo en la importación", ogg != null && ogg.Loop, "bucle desactivado");
			Check($"{id}: dura lo esperado ({ogg?.GetLength():0.0} s)", ogg != null && ogg.GetLength() >= min && ogg.GetLength() <= max, $"{ogg?.GetLength()}");
			Check($"{id}: el bucle vuelve al principio (sin desplazamiento)", ogg != null && Mathf.IsZeroApprox((float)ogg.LoopOffset), $"{ogg?.LoopOffset}");
		}

		Check("un id que no es música no está en el catálogo", SoundCatalog.GetMusic("no_existe") == null && SoundCatalog.GetMusic(null) == null, "id desconocido");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private void Transitions()
	{
		GD.Print("\n--- Transiciones ---");
		Reset();
		Check("el fundido cruzado dura un segundo", Mathf.IsEqualApprox(MusicPlayer.FadeTime, 1.0f), $"{MusicPlayer.FadeTime}");
		Check("sin música: nada suena", Music.Current == null && Music.Layers.Count == 0, "estado inicial");

		AudioManager.PlayMusic("menu");
		Check("pedir menu: es la pista actual y entra con fundido desde el silencio", _audio.CurrentMusic == "menu" && Music.Layers.Count == 1 && Music.GainOf("menu") == 0.0f, $"{_audio.CurrentMusic} {Music.GainOf("menu")}");
		Check("la petición queda registrada en el bus Musica", LastMusicRequest().Id == "musica:menu" && LastMusicRequest().Bus == "Musica" && LastMusicRequest().Result == AudioManager.Outcome.Silent, $"{LastMusicRequest()}");
		Check("la pista suena en bucle", Music.CurrentStream is AudioStreamOggVorbis { Loop: true }, "sin bucle");
		Music.Advance(0.5f);
		Check("a medio segundo entra a la mitad", Mathf.IsEqualApprox(Music.GainOf("menu"), 0.5f), $"{Music.GainOf("menu")}");
		Music.Advance(0.6f);
		Check("al segundo suena entera y ya no hay fundido", Music.GainOf("menu") == 1.0f && !Music.Fading, $"{Music.GainOf("menu")}");

		AudioManager.PlayMusic("the_cradle");
		Check("pedir the_cradle: las dos suenan a la vez durante el cruce", Music.Layers.Count == 2 && Music.Current == "the_cradle", $"{Music.Layers.Count}");
		Music.Advance(0.5f);
		Check("a mitad del cruce cada una suena a la mitad", Mathf.IsEqualApprox(Music.GainOf("menu"), 0.5f) && Mathf.IsEqualApprox(Music.GainOf("the_cradle"), 0.5f), $"{Music.GainOf("menu")} {Music.GainOf("the_cradle")}");
		Music.Advance(0.5f);
		Check("al segundo solo queda the_cradle, entera", Music.Layers.Count == 1 && Music.GainOf("the_cradle") == 1.0f && Music.GainOf("menu") == 0.0f, $"{Music.Layers.Count}");

		// De una sala normal a otra normal: se vuelve a pedir the_cradle y no se reinicia ni se toca el volumen.
		int starts = Music.StartCount;
		int requests = _audio.Requests.Count;
		for (int room = 0; room < 5; room++)
		{
			AudioManager.PlayMusic("the_cradle");
			Music.Advance(0.2f);
		}

		Check("de sala normal a sala normal no se reinicia", Music.StartCount == starts && Music.Layers.Count == 1 && Music.GainOf("the_cradle") == 1.0f && _audio.Requests.Count == requests, $"{Music.StartCount} {starts}");

		AudioManager.PlayMusic("jefe");
		Music.Advance(1.0f);
		Check("la sala del jefe cruza a jefe", Music.Current == "jefe" && Music.Layers.Count == 1 && Music.StartCount == starts + 1, $"{Music.Current}");

		AudioManager.PlayMusic("the_cradle");
		AudioManager.PlayMusic("jefe");
		Music.Advance(0.3f);
		Check("un cambio en pleno fundido no deja pistas colgadas (no pasa de tres capas)", Music.Layers.Count <= MusicPlayer.PlayerCount && Music.Current == "jefe", $"{Music.Layers.Count}");
		Music.Advance(1.5f);
		Check("tras cambios seguidos solo queda la última", Music.Layers.Count == 1 && Music.Layers[0].Name == "jefe" && Music.GainOf("jefe") == 1.0f, $"{Music.Layers.Count}");

		AudioManager.FadeOutMusic();
		Check("apagar con fundido: ya no hay pista actual y baja en un segundo", Music.Current == null && _audio.CurrentMusic == null && Music.Layers.Count == 1, $"{Music.Current}");
		Music.Advance(0.5f);
		Check("a medio segundo suena a la mitad", Mathf.IsEqualApprox(Music.GainOf("jefe"), 0.5f), $"{Music.GainOf("jefe")}");
		Music.Advance(0.6f);
		Check("al segundo se apaga del todo", Music.Layers.Count == 0, $"{Music.Layers.Count}");

		AudioManager.PlayMusic("the_cradle");
		Music.Advance(1.0f);
		AudioManager.StopMusic();
		Check("StopMusic la corta al momento", Music.Layers.Count == 0 && Music.Current == null, $"{Music.Layers.Count}");

		AudioManager.PlayMusic("the_cradle");
		Music.Advance(1.0f);
		AudioManager.FadeOutMusic();
		Music.Advance(0.4f);
		starts = Music.StartCount;
		AudioManager.PlayMusic("the_cradle");
		Check("pedirla de nuevo mientras se apaga la vuelve a subir como pista nueva", Music.Current == "the_cradle" && Music.StartCount == starts + 1, $"{Music.StartCount}");
		Music.Advance(1.0f);
		Check("y acaba sonando entera, sola", Music.Layers.Count == 1 && Music.GainOf("the_cradle") == 1.0f, $"{Music.Layers.Count}");
		Reset();
	}

	// --- 2b -----------------------------------------------------------------------------------------------------------

	private async Task StartAndLoop()
	{
		GD.Print("\n--- Inicio y bucle de cada pista ---");
		SceneTree tree = GetTree();
		(string Id, float Start, float Offset)[] expected = { ("menu", 0.0f, 0.0f), ("the_cradle", SoundCatalog.CradleStartSeconds, SoundCatalog.CradleLoopOffsetSeconds), ("jefe", SoundCatalog.BossStartSeconds, SoundCatalog.BossLoopOffsetSeconds) };
		foreach ((string id, float start, float offset) in expected)
		{
			Reset();
			SoundCatalog.MusicTrack track = SoundCatalog.GetMusic(id);
			Check($"{id}: el catálogo dice empezar en {start:0.00} s y volver a {offset:0.00} s", track.StartSeconds == start && track.LoopOffsetSeconds == offset, $"{track.StartSeconds} {track.LoopOffsetSeconds}");
			AudioStreamOggVorbis cached = ResourceLoader.Load<AudioStream>(track.Path) as AudioStreamOggVorbis;
			Check($"{id}: el inicio cabe dentro de la pista y deja música por delante", start >= 0.0f && start < cached.GetLength() - 30.0 && offset >= 0.0f && offset < cached.GetLength() - 30.0, $"{cached.GetLength()}");

			AudioManager.PlayMusic(id);
			MusicPlayer.Layer layer = Music.Layers[^1];
			AudioStreamOggVorbis playing = Music.CurrentStream as AudioStreamOggVorbis;
			Check($"{id}: la capa arranca en su StartSeconds ({layer.StartSeconds:0.00} s)", Mathf.IsEqualApprox(layer.StartSeconds, start), $"{layer.StartSeconds}");
			Check($"{id}: el bucle está activo y vuelve a su LoopOffsetSeconds ({playing?.LoopOffset:0.00} s)", playing != null && playing.Loop && Mathf.IsEqualApprox((float)playing.LoopOffset, offset), $"{playing?.Loop} {playing?.LoopOffset}");
			Check($"{id}: el stream de la caché no se modifica (sigue con el desplazamiento importado)", Mathf.IsZeroApprox((float)cached.LoopOffset) && cached.Loop, $"{cached.LoopOffset}");
			if (offset == 0.0f)
			{
				Check($"{id}: con 0/0 no cambia nada, es el stream importado tal cual", ReferenceEquals(playing, cached), "se duplicó sin necesidad");
			}
			else
			{
				Check($"{id}: el stream que suena es una copia de la misma pista", !ReferenceEquals(playing, cached) && Mathf.IsEqualApprox((float)playing.GetLength(), (float)cached.GetLength()), $"{playing?.GetLength()} {cached.GetLength()}");
			}

			// Pedirla otra vez no cambia el inicio ni la reinicia.
			int starts = Music.StartCount;
			AudioManager.PlayMusic(id);
			Check($"{id}: pedirla de nuevo no la reinicia", Music.StartCount == starts && Music.Layers.Count == 1, $"{Music.StartCount}");

			// Con un reproductor real: arranca en StartSeconds y, al llegar al final, el bucle vuelve a LoopOffsetSeconds.
			AudioStreamPlayer probe = new() { Stream = playing, VolumeDb = -80.0f };
			AddChild(probe);
			probe.Play(start);
			await Frames(tree, 6);
			float position = probe.GetPlaybackPosition();
			Check($"{id}: un reproductor real arranca en {start:0.00} s (está en {position:0.00} s)", probe.Playing && position >= start - 0.01f && position < start + 1.0f, $"{position}");
			float tail = 0.3f;
			probe.Play((float)playing.GetLength() - tail);
			ulong began = Time.GetTicksMsec();
			while (Time.GetTicksMsec() - began < 900)
			{
				await Frames(tree, 1);
			}

			position = probe.GetPlaybackPosition();
			Check($"{id}: al terminar el bucle vuelve a {offset:0.00} s y no al principio ni al final (está en {position:0.00} s)", probe.Playing && position >= offset - 0.01f && position < offset + 1.5f, $"{position}");
			probe.Stop();
			probe.Free();
			Reset();
		}
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task Pause()
	{
		GD.Print("\n--- Pausa ---");
		Reset();
		SceneTree tree = GetTree();
		AudioManager.PlayMusic("menu");
		await Frames(tree, 5);
		tree.Paused = true;
		Check("con el juego en pausa el reproductor sigue activo", Music.CanProcess() && _audio.CanProcess(), "el reproductor se detiene");
		AudioManager.PlayMusic("the_cradle");
		await Frames(tree, 90);
		Check("en pausa el fundido avanza y acaba (no se queda a medias)", Music.Current == "the_cradle" && Music.Layers.Count == 1 && Music.GainOf("the_cradle") == 1.0f, $"{Music.Layers.Count} {Music.GainOf("the_cradle")}");
		int starts = Music.StartCount;
		tree.Paused = false;
		await Frames(tree, 5);
		Check("al reanudar sigue la misma pista, sin reiniciarse", Music.Current == "the_cradle" && Music.StartCount == starts && Music.Layers.Count == 1, $"{Music.StartCount}");
		Reset();
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private void MissingFiles()
	{
		GD.Print("\n--- Archivos ausentes ---");
		Reset();
		AudioManager.PlayMusic("the_cradle");
		Music.Advance(1.0f);
		SoundCatalog.MusicFolder = "res://assets/audio/no_hay_musica/";
		AudioManager.PlayMusic("jefe");
		Check("sin su archivo queda como faltante, sin pista actual", _audio.CurrentMusic == null && LastMusicRequest().Result == AudioManager.Outcome.Missing, $"{_audio.CurrentMusic}");
		Check("la que sonaba se apaga con fundido", Music.Layers.Count == 1 && Music.Layers[0].Target == 0.0f, $"{Music.Layers.Count}");
		Music.Advance(1.0f);
		Check("y queda silencio", Music.Layers.Count == 0, $"{Music.Layers.Count}");
		AudioManager.PlayMusic("menu");
		AudioManager.PlayMusic("the_cradle");
		Check("cualquier pista sin archivo es silencio y no empieza nada", Music.Layers.Count == 0 && _audio.Requests.Count(r => r.Id.StartsWith("musica:") && r.Result == AudioManager.Outcome.Missing) == 3, $"{Music.Layers.Count}");
		Check("sin archivo no se cuenta ningún fallo de audio y el audio sigue activo", _audio.FailureCount == 0 && _audio.Enabled, $"{_audio.FailureCount}");
		AudioManager.FadeOutMusic();
		AudioManager.StopMusic();

		SoundCatalog.MusicFolder = "res://assets/audio/music/";
		AudioManager.PlayMusic("menu");
		Music.Advance(1.0f);
		AudioManager.PlayMusic("no_existe");
		Check("un id desconocido queda como desconocido y no toca la música", LastMusicRequest().Result == AudioManager.Outcome.Unknown && _audio.CurrentMusic == "menu" && Music.GainOf("menu") == 1.0f, $"{_audio.CurrentMusic}");
		AudioManager.PlayMusic(null);
		Check("un id nulo tampoco rompe nada", _audio.FailureCount == 0 && _audio.CurrentMusic == "menu", "id nulo");

		// Con el audio desactivado (tras fallos) ninguna llamada de música lanza.
		_audio.Enabled = false;
		AudioManager.PlayMusic("jefe");
		AudioManager.FadeOutMusic();
		AudioManager.StopMusic();
		Check("con el audio desactivado ninguna llamada de música lanza", true, "");
		Reset();
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task Slider()
	{
		GD.Print("\n--- Deslizador «Música» ---");
		SceneTree tree = GetTree();
		Check("por defecto la música está al 70 %", Mathf.IsEqualApprox(GameSettings.DefaultMusicVolume, 0.7f), $"{GameSettings.DefaultMusicVolume}");

		DirAccess.RemoveAbsolute(TestSettings);
		GameSettings.MasterVolume = 0.8f;
		GameSettings.SfxVolume = 1.0f;
		GameSettings.MusicVolume = 0.2f;
		bool read = GameSettings.LoadSettings();
		Check("sin archivo de ajustes: música al 70 %", !read && Mathf.IsEqualApprox(GameSettings.MusicVolume, 0.7f), $"{GameSettings.MusicVolume}");
		AudioManager.ApplyVolumes();
		Check("el bus Musica arranca al 70 %", Mathf.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Musica")) - Mathf.LinearToDb(0.7f)) < 0.01f, "bus");

		VolumeControls controls = GD.Load<PackedScene>("res://scenes/ui/VolumeControls.tscn").Instantiate<VolumeControls>();
		AddChild(controls);
		await Frames(tree, 2);
		Check("el deslizador muestra 70 y se llama «Música»", controls.MusicSlider.Value == 70.0 && controls.GetNode<Label>("MusicRow/Name").Text == "Música"
			&& controls.GetNode<Label>("MusicRow/Value").Text == "70%", $"{controls.MusicSlider.Value}");
		Check("va de 0 a 100 en pasos de 5, como los otros", controls.MusicSlider.MinValue == 0.0 && controls.MusicSlider.MaxValue == 100.0 && controls.MusicSlider.Step == 5.0, "rango");
		controls.MusicSlider.Value = 30.0;
		Check("moverlo cambia GameSettings y el bus Musica, no los otros", Mathf.IsEqualApprox(GameSettings.MusicVolume, 0.3f)
			&& Mathf.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Musica")) - Mathf.LinearToDb(0.3f)) < 0.01f
			&& Mathf.IsEqualApprox(GameSettings.MasterVolume, 0.8f) && Mathf.IsEqualApprox(GameSettings.SfxVolume, 1.0f), $"{GameSettings.MusicVolume}");
		Check("el texto del valor se actualiza", controls.GetNode<Label>("MusicRow/Value").Text == "30%", controls.GetNode<Label>("MusicRow/Value").Text);
		controls.MusicSlider.Value = 0.0;
		Check("a 0 el bus queda en silencio", AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Musica")) <= -79.0f, "no silencia");
		controls.MusicSlider.Value = 30.0;
		controls.Free();

		GameSettings.MusicVolume = 0.9f;
		bool reloaded = GameSettings.LoadSettings();
		Check("se guarda en settings.cfg y se recupera", reloaded && Mathf.IsEqualApprox(GameSettings.MusicVolume, 0.3f), $"{GameSettings.MusicVolume}");
		ConfigFile file = new();
		file.Load(TestSettings);
		Check("la clave es «music» en la sección «audio», junto a master y sfx", file.HasSectionKey("audio", "music") && file.HasSectionKey("audio", "master") && file.HasSectionKey("audio", "sfx"), "claves");

		WriteSettings("[audio]\nmaster=0.5\nsfx=0.5\n");
		bool whole = GameSettings.LoadSettings();
		Check("un archivo antiguo sin «music»: la música usa su valor por defecto y lo demás se respeta", !whole && Mathf.IsEqualApprox(GameSettings.MusicVolume, 0.7f) && Mathf.IsEqualApprox(GameSettings.MasterVolume, 0.5f), $"{GameSettings.MusicVolume}");
		WriteSettings("[audio]\nmaster=0.5\nsfx=0.5\nmusic=7.5\n");
		GameSettings.LoadSettings();
		Check("un valor fuera de rango se limita a 1", GameSettings.MusicVolume == 1.0f, $"{GameSettings.MusicVolume}");
		WriteSettings("[audio]\nmaster=0.5\nsfx=0.5\nmusic=\"alto\"\n");
		GameSettings.LoadSettings();
		Check("un valor de tipo erróneo vuelve al 70 %", Mathf.IsEqualApprox(GameSettings.MusicVolume, 0.7f), $"{GameSettings.MusicVolume}");
		GameSettings.MasterVolume = 0.8f;
		GameSettings.MusicVolume = 0.7f;
		AudioManager.ApplyVolumes();

		// El menú principal (1152x648) y la hoja de la pausa tienen que traerlo y que quepa.
		SubViewport viewport = new() { Size = new Vector2I(1152, 648) };
		AddChild(viewport);
		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		viewport.AddChild(menu);
		await Frames(tree, 4);
		Check("el menú principal trae el deslizador de música", menu.GetNodeOrNull<VolumeControls>("VolumeControls")?.MusicSlider != null, "sin deslizador");
		Rect2 window = new(Vector2.Zero, new Vector2(1152.0f, 648.0f));
		Control volume = menu.GetNode<Control>("VolumeControls");
		Rect2 rect = volume.GetGlobalRect();
		Check($"y los tres caben en la ventana de 1152x648 ({rect})", window.Encloses(rect), $"{rect}");
		if (menu.HasPanel)
		{
			Check("sin pisar el panel del menú", !rect.Intersects(menu.PanelRect), $"{rect} {menu.PanelRect}");
		}

		Check("al abrir el menú suena menu", _audio.CurrentMusic == "menu", $"{_audio.CurrentMusic}");
		menu.Free();
		viewport.Free();

		PauseMenu pause = GD.Load<PackedScene>("res://scenes/ui/PauseMenu.tscn").Instantiate<PauseMenu>();
		AddChild(pause);
		await Frames(tree, 2);
		Check("la pausa trae el deslizador de música", pause.GetNodeOrNull<VolumeControls>("Root/Center/Box/VolumeControls")?.MusicSlider != null, "sin deslizador");
		pause.Free();
		Reset();
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task FullRun()
	{
		GD.Print("\n--- Partida real ---");
		SceneTree tree = GetTree();
		Reset();
		GameSettings.PlaytestLogEnabled = false;

		// Sala a sala: the_cradle no se reinicia.
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await Frames(tree, 3);
		Check("la primera sala de la partida pide the_cradle", _audio.CurrentMusic == "the_cradle", $"{_audio.CurrentMusic}");
		int starts = Music.StartCount;
		List<DoorSide> path = BossTestHarness.PathToBoss(level.Rooms.Map);
		if (path.Count > 1)
		{
			await BossTestHarness.Cross(tree, level.Rooms, path[0]);
			Check("de sala normal a sala normal la música no se reinicia", _audio.CurrentMusic == "the_cradle" && Music.StartCount == starts, $"{Music.StartCount} {starts}");
		}

		await BossTestHarness.CollectWrappers(tree);
		await BossTestHarness.Close(tree, level);

		// Muerte de Noah: la música baja a nada y suena la derrota.
		Reset();
		level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		await Frames(tree, 3);
		_audio.ClearRequests();
		level.Health.TakeDamage(1.0e6f);
		await BossTestHarness.WaitUntil(tree, () => level.Run.RunEnded, 60, "el final de la partida por derrota");
		Check("al morir Noah no queda pista actual y la música baja con fundido", _audio.CurrentMusic == null && Music.Layers.All(l => l.Target == 0.0f), $"{_audio.CurrentMusic}");
		await BossTestHarness.WaitUntil(tree, () => Music.Layers.Count == 0, 240, "el fin del fundido");
		await Frames(tree, 120);
		Check("la derrota suena una vez y la música no vuelve", _audio.Requests.Count(r => r.Id == "derrota") == 1 && _audio.CurrentMusic == null && Music.Layers.Count == 0, $"{_audio.Requests.Count(r => r.Id == "derrota")}");
		await BossTestHarness.CollectWrappers(tree);
		await BossTestHarness.Close(tree, level);

		// Jefe y victoria.
		Reset();
		level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await Frames(tree, 3);
		await BossTestHarness.GoToBoss(tree, level);
		await Frames(tree, 3);
		Check("al entrar en la sala del jefe suena jefe", _audio.CurrentMusic == "jefe", $"{_audio.CurrentMusic}");
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		await BossTestHarness.WaitUntil(tree, () => Music.Layers.Count == 1 && !Music.Fading, 240, "el fin del cruce");
		Check("el cruce a jefe acaba con una sola pista, entera", Music.Layers[0].Name == "jefe" && Music.GainOf("jefe") == 1.0f, string.Join(", ", Music.Layers.Select(l => $"{l.Name} {l.Gain} {l.Target}")));
		_audio.ClearRequests();
		room.BossEnemy.Kill();
		await BossTestHarness.WaitUntil(tree, () => level.Victory.IsPresented, 300, "la pantalla de victoria");
		Check("al ganar no queda pista actual", _audio.CurrentMusic == null, $"{_audio.CurrentMusic}");
		await BossTestHarness.WaitUntil(tree, () => Music.Layers.Count == 0, 240, "el fin del fundido");
		await Frames(tree, 30);
		Check("la victoria suena una vez y la música no vuelve", _audio.Requests.Count(r => r.Id == "victoria") == 1 && _audio.CurrentMusic == null && Music.Layers.Count == 0, "victoria");
		await BossTestHarness.CollectWrappers(tree);
		await BossTestHarness.Close(tree, level);

		// Partida -> menú -> Jugar. Se recolecta antes de cada cambio de escena.
		Reset();
		Node testScene = tree.CurrentScene;
		level = BossTestHarness.OpenAsCurrent(tree, BossTestHarness.Seed);
		await Frames(tree, 3);
		Check("en la partida suena the_cradle", _audio.CurrentMusic == "the_cradle", $"{_audio.CurrentMusic}");
		await BossTestHarness.CollectWrappers(tree);
		level.Run.ReturnToMenu();
		Node menu = await BossTestHarness.WaitForScene(tree, level.Root, GameSettings.MenuScene);
		await Frames(tree, 3);
		Check("al volver al menú suena menu", _audio.CurrentMusic == "menu", $"{_audio.CurrentMusic}");
		await BossTestHarness.WaitUntil(tree, () => Music.Layers.Count == 1 && !Music.Fading, 240, "el cruce al menú");
		Check("el cruce al menú deja una sola pista", Music.Layers[0].Name == "menu", $"{Music.Layers.Count}");

		await BossTestHarness.CollectWrappers(tree);
		((MainMenu)menu).StartGame();
		Node game = await BossTestHarness.WaitForScene(tree, menu, GameSettings.LevelScene);
		await Frames(tree, 3);
		Check("al pulsar Jugar suena the_cradle", _audio.CurrentMusic == "the_cradle", $"{_audio.CurrentMusic}");
		await BossTestHarness.CollectWrappers(tree);
		await BossTestHarness.Restore(tree, testScene, game);
		Reset();
	}

	private static void WriteSettings(string text)
	{
		using Godot.FileAccess file = Godot.FileAccess.Open(TestSettings, Godot.FileAccess.ModeFlags.Write);
		file.StoreString(text);
	}
}
