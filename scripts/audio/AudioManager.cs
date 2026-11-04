using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Gestor central de audio (autoload). Todo el juego pide los sonidos por id (SoundCatalog) con Play (planos: Noah y la interfaz) o
/// PlayAt (posicionales, en 3D: los enemigos); él decide si suenan.
///
/// Buses: Master, SFX y Musica (default_bus_layout.tres; si faltaran se crean). Los volúmenes (GameSettings.MasterVolume y
/// SfxVolume) y MusicVolume se aplican a Master, SFX y Musica, se guardan en user://settings.cfg y se cargan al iniciar.
///
/// Música: PlayMusic(id) con los ids de SoundCatalog (menu, the_cradle, jefe) la pone en bucle con un fundido cruzado de un segundo desde
/// la que sonara (MusicPlayer); pedir la que ya suena no la reinicia, FadeOutMusic la apaga con fundido y StopMusic la corta. Sigue sonando
/// con el juego en pausa y, si falta el archivo, queda en silencio sin errores.
///
/// Tolerante a faltantes: si un archivo no existe (ResourceLoader.Exists) no suena y no lanza ningún error; los sonidos con Fallback
/// (The Mother) usan el de los enemigos con otro tono mientras no exista el suyo.
///
/// Voces: 8 planas y 16 en 3D como mucho a la vez, MaxVoices por sonido y MinInterval entre repeticiones; sin hueco se descarta el
/// sonido nuevo (solo los prioritarios roban la voz más antigua). Variación de tono (±5 % en los repetidos) con un generador de
/// azar PROPIO: no toca el azar global.
///
/// Bucle del volador: un único reproductor 3D en la posición media de los voladores vivos (grupo "flyers"); se activa por código
/// (se duplica el stream con Loop = true, la importación no lo hace) y se detiene al quedar ninguno.
///
/// A prueba de fallos: el audio NUNCA puede alterar el juego. Toda la API estática que usa la lógica (Play, PlayAt, PlayFrom, PlayMusic,
/// StopMusic, StopAll, HookButtons) captura cualquier excepción, la avisa una sola vez por mensaje y sigue; tras MaxFailures fallos el
/// audio se desactiva solo (Enabled = false) y el resto del juego ni se entera. La lógica pide los sonidos DESPUÉS de aplicar el daño
/// y de liberar lo que corresponda. Enabled también sirve para probar con el audio apagado.
///
/// Sin dispositivo (--headless o Silent): no reproduce nada pero lleva la misma contabilidad y registra cada petición en Requests,
/// que es lo que miran las pruebas.
/// </summary>
public partial class AudioManager : Node
{
	public const int FlatVoices = 8;
	public const int SpatialVoices = 16;
	public const float MaxDistance = 30.0f;
	public const float UnitSize = 10.0f;
	public static readonly StringName FlyerGroup = "flyers";

	public enum Outcome
	{
		Played,
		Silent,
		Missing,
		Unknown,
		Dropped,
		Fallback,
	}

	/// <summary>Una petición de sonido y qué pasó con ella (para las pruebas y el registro).</summary>
	public readonly record struct Request(string Id, bool Positional, Vector3 Position, float PitchScale, string Bus, Outcome Result);

	private sealed class Voice
	{
		public string Id;
		public bool Positional;
		public double EndTime;
		public double StartTime;
		public Node Player; // null en silencio
	}

	/// <summary>Fallos de audio que se toleran antes de desactivarlo del todo.</summary>
	public const int MaxFailures = 5;

	public static AudioManager Instance { get; private set; }

	/// <summary>Interruptor general: apagado, todas las peticiones se ignoran (las pruebas lo usan; también se apaga solo tras MaxFailures fallos).</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Excepciones capturadas hasta ahora (nunca llegaron a la lógica del juego).</summary>
	public int FailureCount { get; private set; }

	/// <summary>Solo para pruebas: se invoca al empezar cada petición; si lanza, simula un fallo de audio.</summary>
	public static System.Action<string> FaultInjection { get; set; }

	/// <summary>No reproduce nada (se activa solo en --headless; las pruebas también pueden activarlo).</summary>
	public bool Silent { get; set; }

	public IReadOnlyList<Request> Requests => _requests;

	/// <summary>Voces sonando ahora (planas y en 3D, sin contar el bucle del volador ni la música).</summary>
	public int ActiveVoiceCount => _voices.Count;

	public bool FlyerLoopActive { get; private set; }

	public Vector3 FlyerLoopPosition { get; private set; }

	/// <summary>Pista de música que debería sonar ahora (nula si ninguna).</summary>
	public string CurrentMusic => Music?.Current;

	public MusicPlayer Music { get; private set; }

	private readonly List<Request> _requests = new();
	private readonly List<Voice> _voices = new();
	private readonly Dictionary<string, double> _lastPlayed = new();
	private readonly Dictionary<string, AudioStream> _streams = new();
	private readonly HashSet<string> _warned = new();
	private readonly List<AudioStreamPlayer> _flat = new();
	private readonly List<AudioStreamPlayer3D> _spatial = new();
	private readonly RandomNumberGenerator _random = new();
	private AudioStreamPlayer3D _flyerLoop;
	private double _loopCheck;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public override void _Ready()
	{
		// El juego se pausa (menú de pausa, pantalla de derrota) con SceneTree.Paused: el audio tiene que seguir (sonidos de la interfaz, música
		// y los que ya sonaban), y sus reproductores heredan este modo.
		ProcessMode = ProcessModeEnum.Always;
		Silent = DisplayServer.GetName() == "headless";
		_random.Randomize();
		EnsureBuses();
		GameSettings.LoadSettings();
		ApplyVolumes();

		for (int i = 0; i < FlatVoices; i++)
		{
			AudioStreamPlayer player = new() { Bus = SoundCatalog.BusSfx };
			AddChild(player);
			_flat.Add(player);
		}

		for (int i = 0; i < SpatialVoices; i++)
		{
			AudioStreamPlayer3D player = new() { Bus = SoundCatalog.BusSfx, MaxDistance = MaxDistance, UnitSize = UnitSize };
			AddChild(player);
			_spatial.Add(player);
		}

		_flyerLoop = new AudioStreamPlayer3D { Bus = SoundCatalog.BusSfx, MaxDistance = MaxDistance, UnitSize = UnitSize };
		AddChild(_flyerLoop);
		Music = new MusicPlayer { Silent = Silent };
		AddChild(Music);
	}

	// --- volúmenes y buses --------------------------------------------------------------------------------------------

	/// <summary>Crea los buses SFX y Musica si el diseño de buses no los trae.</summary>
	public static void EnsureBuses()
	{
		foreach (string bus in new[] { SoundCatalog.BusSfx, SoundCatalog.BusMusic })
		{
			if (AudioServer.GetBusIndex(bus) >= 0)
			{
				continue;
			}

			AudioServer.AddBus();
			int index = AudioServer.BusCount - 1;
			AudioServer.SetBusName(index, bus);
			AudioServer.SetBusSend(index, SoundCatalog.BusMaster);
		}
	}

	/// <summary>Aplica GameSettings.MasterVolume, SfxVolume y MusicVolume a los buses Master, SFX y Musica.</summary>
	public static void ApplyVolumes()
	{
		SetBusLinear(SoundCatalog.BusMaster, GameSettings.MasterVolume);
		SetBusLinear(SoundCatalog.BusSfx, GameSettings.SfxVolume);
		SetBusLinear(SoundCatalog.BusMusic, GameSettings.MusicVolume);
	}

	public static void SetMasterVolume(float volume)
	{
		GameSettings.MasterVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
		ApplyVolumes();
		GameSettings.SaveSettings();
	}

	public static void SetSfxVolume(float volume)
	{
		GameSettings.SfxVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
		ApplyVolumes();
		GameSettings.SaveSettings();
	}

	public static void SetMusicVolume(float volume)
	{
		GameSettings.MusicVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
		ApplyVolumes();
		GameSettings.SaveSettings();
	}

	private static void SetBusLinear(string bus, float linear)
	{
		int index = AudioServer.GetBusIndex(bus);
		if (index >= 0)
		{
			AudioServer.SetBusVolumeDb(index, linear <= 0.0001f ? -80.0f : Mathf.LinearToDb(linear));
		}
	}

	// --- peticiones ---------------------------------------------------------------------------------------------------

	/// <summary>Sonido plano (Noah, interfaz). No hace nada si no hay AudioManager; nunca lanza.</summary>
	public static void Play(string id)
	{
		AudioManager audio = Instance;
		if (audio == null || !audio.Enabled)
		{
			return;
		}

		try
		{
			audio.Submit(id, false, Vector3.Zero, 1.0f);
		}
		catch (System.Exception exception)
		{
			audio.Fail(exception);
		}
	}

	/// <summary>
	/// Carga en el caché el archivo de un sonido (y el de su respaldo, si el suyo no existe) SIN reproducirlo, para que la primera vez que
	/// suene no cueste una carga. Un id desconocido o un archivo ausente no hacen nada. Nunca lanza.
	/// </summary>
	public static void Preload(string id)
	{
		AudioManager audio = Instance;
		if (audio == null || !audio.Enabled)
		{
			return;
		}

		try
		{
			if (SoundCatalog.Get(id) is not SoundCatalog.Sound sound)
			{
				return;
			}

			if (audio.Load(sound.Path) == null && sound.Fallback != null && SoundCatalog.Get(sound.Fallback) is SoundCatalog.Sound other)
			{
				audio.Load(other.Path);
			}
		}
		catch (System.Exception exception)
		{
			audio.Fail(exception);
		}
	}

	/// <summary>Sonido posicional en el mundo (enemigos). pitch multiplica el tono (por ejemplo 0,8 = un 20 % más grave). Nunca lanza.</summary>
	public static void PlayAt(string id, Vector3 position, float pitch = 1.0f)
	{
		AudioManager audio = Instance;
		if (audio == null || !audio.Enabled)
		{
			return;
		}

		try
		{
			audio.Submit(id, true, position, pitch);
		}
		catch (System.Exception exception)
		{
			audio.Fail(exception);
		}
	}

	/// <summary>
	/// Sonido posicional en la posición del primer nodo 3D que contiene a source (él mismo o su antecesor más cercano). Sirve para los
	/// comportamientos de enemigos, que a veces cuelgan de otro comportamiento (The Mother) y no directamente del enemigo. Nunca lanza.
	/// </summary>
	public static void PlayFrom(string id, Node source, float pitch = 1.0f)
	{
		AudioManager audio = Instance;
		if (audio == null || !audio.Enabled)
		{
			return;
		}

		try
		{
			for (Node node = source; node != null; node = node.GetParent())
			{
				if (node is Node3D node3D)
				{
					if (node3D.IsInsideTree())
					{
						audio.Submit(id, true, node3D.GlobalPosition, pitch);
					}

					return;
				}
			}
		}
		catch (System.Exception exception)
		{
			audio.Fail(exception);
		}
	}

	/// <summary>Música en bucle (assets/audio/music/{name}.ogg, bus Musica) con fundido cruzado desde la que suene. Si ya suena no se reinicia; si no existe, silencio. Nunca lanza.</summary>
	public static void PlayMusic(string name)
	{
		AudioManager audio = Instance;
		if (audio == null || !audio.Enabled)
		{
			return;
		}

		try
		{
			audio.StartMusic(name);
		}
		catch (System.Exception exception)
		{
			audio.Fail(exception);
		}
	}

	/// <summary>Apaga la música con un fundido de un segundo (al morir Noah o ganar). Nunca lanza.</summary>
	public static void FadeOutMusic()
	{
		AudioManager audio = Instance;
		try
		{
			audio?.Music?.FadeOut();
		}
		catch (System.Exception exception)
		{
			audio?.Fail(exception);
		}
	}

	/// <summary>Corta la música al momento. Nunca lanza.</summary>
	public static void StopMusic()
	{
		AudioManager audio = Instance;
		try
		{
			audio?.EndMusic();
		}
		catch (System.Exception exception)
		{
			audio?.Fail(exception);
		}
	}

	/// <summary>Corta todas las voces (también noah_muere, que dura casi 7 s) y el bucle del volador. Se llama al reiniciar o volver al menú. Nunca lanza.</summary>
	public static void StopAll()
	{
		AudioManager audio = Instance;
		try
		{
			audio?.StopEverything();
		}
		catch (System.Exception exception)
		{
			audio?.Fail(exception);
		}
	}

	// Un fallo de audio se avisa una vez por mensaje; tras MaxFailures el audio se desactiva solo. Nunca se relanza.
	private void Fail(System.Exception exception)
	{
		FailureCount++;
		string message = $"{exception.GetType().Name}: {exception.Message}";
		if (_warned.Add(message))
		{
			GD.PushWarning($"AudioManager: fallo de audio capturado (el juego sigue): {message}");
		}

		if (FailureCount >= MaxFailures && Enabled)
		{
			Enabled = false;
			GD.PushWarning($"AudioManager: audio desactivado tras {FailureCount} fallos.");
		}
	}

	/// <summary>Vuelve a activar el audio y pone a cero el contador de fallos (pruebas).</summary>
	public void ResetFailures()
	{
		FailureCount = 0;
		Enabled = true;
		_warned.Clear();
	}

	/// <summary>Borra el registro de peticiones (para las pruebas).</summary>
	public void ClearRequests() => _requests.Clear();

	private void Submit(string id, bool positional, Vector3 position, float pitchMultiplier)
	{
		FaultInjection?.Invoke(id);
		SoundCatalog.Sound requested = SoundCatalog.Get(id);
		if (requested == null)
		{
			Record(id, positional, position, 1.0f, SoundCatalog.BusSfx, Outcome.Unknown);
			return;
		}

		// Sin su propio archivo, un sonido con Fallback usa el de otro id (más grave en The Mother) y comparte sus límites de voces.
		SoundCatalog.Sound sound = requested;
		bool fallback = false;
		AudioStream stream = Load(requested.Path);
		if (stream == null && requested.Fallback != null && SoundCatalog.Get(requested.Fallback) is SoundCatalog.Sound other)
		{
			sound = other;
			stream = Load(other.Path);
			pitchMultiplier *= requested.FallbackPitch;
			fallback = true;
		}

		float variation = sound.PitchVariation > 0.0f ? _random.RandfRange(-sound.PitchVariation, sound.PitchVariation) : 0.0f;
		float pitch = pitchMultiplier * (1.0f + variation);
		if (stream == null)
		{
			Record(id, positional, position, pitch, sound.Bus, Outcome.Missing);
			return;
		}

		double now = Time.GetTicksMsec() / 1000.0;
		Prune(now);
		if (!Admit(sound, now))
		{
			Record(id, positional, position, pitch, sound.Bus, Outcome.Dropped);
			return;
		}

		Voice voice = new() { Id = sound.Id, Positional = positional, StartTime = now, EndTime = now + stream.GetLength() / pitch };
		if (!Silent)
		{
			Node player = positional ? TakePlayer3D(position, sound.Priority) : TakePlayer(sound.Priority);
			if (player == null)
			{
				Record(id, positional, position, pitch, sound.Bus, Outcome.Dropped);
				return;
			}

			voice.Player = player;
			switch (player)
			{
				case AudioStreamPlayer3D p3:
					p3.Stream = stream;
					p3.VolumeDb = sound.VolumeDb;
					p3.PitchScale = pitch;
					p3.Play();
					break;
				case AudioStreamPlayer p:
					p.Stream = stream;
					p.VolumeDb = sound.VolumeDb;
					p.PitchScale = pitch;
					p.Play();
					break;
			}
		}

		_voices.Add(voice);
		_lastPlayed[sound.Id] = now;
		Record(id, positional, position, pitch, sound.Bus, fallback ? Outcome.Fallback : Silent ? Outcome.Silent : Outcome.Played);
	}

	// Decide si cabe: intervalo mínimo, voces del sonido y voces totales (con robo para los prioritarios).
	private bool Admit(SoundCatalog.Sound sound, double now)
	{
		if (sound.MinInterval > 0.0f && _lastPlayed.TryGetValue(sound.Id, out double last) && now - last < sound.MinInterval)
		{
			return false;
		}

		if (_voices.Count(v => v.Id == sound.Id) >= sound.MaxVoices)
		{
			if (!sound.Priority)
			{
				return false;
			}

			StealOldest(v => v.Id == sound.Id);
		}

		// Sin reproductor real (silencio) el total se limita igual que con los reproductores.
		if (Silent)
		{
			int capacity = FlatVoices + SpatialVoices;
			if (_voices.Count >= capacity)
			{
				if (!sound.Priority)
				{
					return false;
				}

				StealOldest(_ => true);
			}
		}

		return true;
	}

	private Node TakePlayer(bool priority)
	{
		AudioStreamPlayer free = _flat.FirstOrDefault(p => !p.Playing && _voices.All(v => v.Player != p));
		if (free == null && priority)
		{
			StealOldest(v => !v.Positional);
			free = _flat.FirstOrDefault(p => !p.Playing && _voices.All(v => v.Player != p));
		}

		return free;
	}

	private Node TakePlayer3D(Vector3 position, bool priority)
	{
		AudioStreamPlayer3D free = _spatial.FirstOrDefault(p => !p.Playing && _voices.All(v => v.Player != p));
		if (free == null && priority)
		{
			StealOldest(v => v.Positional);
			free = _spatial.FirstOrDefault(p => !p.Playing && _voices.All(v => v.Player != p));
		}

		if (free != null)
		{
			free.GlobalPosition = position;
		}

		return free;
	}

	private void StealOldest(System.Func<Voice, bool> match)
	{
		Voice oldest = _voices.Where(match).OrderBy(v => v.StartTime).FirstOrDefault();
		if (oldest != null)
		{
			Release(oldest);
		}
	}

	private void Release(Voice voice)
	{
		(voice.Player as AudioStreamPlayer)?.Stop();
		(voice.Player as AudioStreamPlayer3D)?.Stop();
		_voices.Remove(voice);
	}

	private void Prune(double now)
	{
		_voices.RemoveAll(v => v.EndTime <= now || (v.Player is AudioStreamPlayer { Playing: false }) || (v.Player is AudioStreamPlayer3D { Playing: false }));
	}

	private void Record(string id, bool positional, Vector3 position, float pitch, string bus, Outcome result)
	{
		_requests.Add(new Request(id, positional, position, pitch, bus, result));
		if (_requests.Count > 2000)
		{
			_requests.RemoveRange(0, 1000);
		}
	}

	/// <summary>
	/// Rutas de archivo que se tratan como ausentes aunque existan (solo pruebas: simulan «sin archivo» sin depender de qué haya en el disco,
	/// que va cambiando según llegan los sonidos). Se consulta antes del caché, así que se puede ocultar y mostrar un archivo ya cargado.
	/// </summary>
	public HashSet<string> HiddenFiles { get; } = new();

	// Carga con caché; null si el archivo no existe o no es un audio (sin errores en el log; una advertencia por ruta como mucho).
	private AudioStream Load(string path)
	{
		if (HiddenFiles.Contains(path))
		{
			return null;
		}

		if (_streams.TryGetValue(path, out AudioStream cached))
		{
			return cached;
		}

		AudioStream stream = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
		_streams[path] = stream;
		return stream;
	}

	// --- bucle del volador, música y corte ----------------------------------------------------------------------------

	public override void _Process(double delta)
	{
		_loopCheck -= delta;
		if (_loopCheck > 0.0)
		{
			return;
		}

		_loopCheck = 0.1;
		if (!Enabled)
		{
			return;
		}

		try
		{
			List<Node3D> flyers = GetTree().GetNodesInGroup(FlyerGroup).OfType<Node3D>().Where(f => IsInstanceValid(f) && f.IsInsideTree() && f.IsVisibleInTree() && !f.IsQueuedForDeletion()).ToList();
			UpdateFlyerLoop(flyers);
		}
		catch (System.Exception exception)
		{
			Fail(exception);
		}
	}

	private void UpdateFlyerLoop(List<Node3D> flyers)
	{
		if (flyers.Count == 0)
		{
			if (FlyerLoopActive)
			{
				FlyerLoopActive = false;
				_flyerLoop?.Stop();
				Record(SoundCatalog.FlyerWings + ":stop", true, FlyerLoopPosition, 1.0f, SoundCatalog.BusSfx, Silent ? Outcome.Silent : Outcome.Played);
			}

			return;
		}

		Vector3 mean = flyers.Aggregate(Vector3.Zero, (sum, f) => sum + f.GlobalPosition) / flyers.Count;
		FlyerLoopPosition = mean;
		if (!FlyerLoopActive)
		{
			SoundCatalog.Sound sound = SoundCatalog.Get(SoundCatalog.FlyerWings);
			AudioStream stream = Load(sound.Path);
			if (stream == null)
			{
				return;
			}

			FlyerLoopActive = true;
			Record(SoundCatalog.FlyerWings + ":loop", true, mean, 1.0f, SoundCatalog.BusSfx, Silent ? Outcome.Silent : Outcome.Played);
			if (!Silent && _flyerLoop != null)
			{
				// La importación no activa el bucle: se duplica el stream y se activa aquí.
				AudioStream looped = (AudioStream)stream.Duplicate();
				if (looped is AudioStreamOggVorbis ogg)
				{
					ogg.Loop = true;
				}

				_flyerLoop.Stream = looped;
				_flyerLoop.VolumeDb = sound.VolumeDb;
				_flyerLoop.GlobalPosition = mean;
				_flyerLoop.Play();
			}
		}
		else if (!Silent && _flyerLoop != null)
		{
			_flyerLoop.GlobalPosition = mean;
		}
	}

	private void StartMusic(string name)
	{
		FaultInjection?.Invoke("musica:" + name);
		SoundCatalog.MusicTrack track = SoundCatalog.GetMusic(name);
		if (track == null)
		{
			Record("musica:" + name, false, Vector3.Zero, 1.0f, SoundCatalog.BusMusic, Outcome.Unknown);
			return;
		}

		AudioStream stream = Load(track.Path);
		if (stream == null)
		{
			// Sin su archivo no suena nada (la que sonara se apaga con fundido) y no da error.
			Music?.FadeOut();
			Record("musica:" + name, false, Vector3.Zero, 1.0f, SoundCatalog.BusMusic, Outcome.Missing);
			return;
		}

		if (Music == null)
		{
			return;
		}

		Music.Silent = Silent; // las pruebas lo cambian después de _Ready
		if (Music.Play(track, stream))
		{
			Record("musica:" + name, false, Vector3.Zero, 1.0f, SoundCatalog.BusMusic, Silent ? Outcome.Silent : Outcome.Played);
		}
	}

	private void EndMusic()
	{
		Music?.FadeOut(immediate: true);
	}

	private void StopEverything()
	{
		foreach (Voice voice in _voices.ToList())
		{
			Release(voice);
		}

		_voices.Clear();
		_lastPlayed.Clear();
		FlyerLoopActive = false;
		_flyerLoop?.Stop();
	}

	/// <summary>Metadato de un botón con el id del sonido que ha de dar al pulsarlo (por defecto, el clic de interfaz).</summary>
	public const string UiSoundMeta = "ui_sound";

	/// <summary>Metadato que marca un botón con el sonido de hover ya conectado (así no se conecta dos veces).</summary>
	public const string UiHoverMeta = "ui_hover_hooked";

	/// <summary>
	/// Conecta el sonido de interfaz a todos los botones bajo un nodo (los que ya existen): el clic o el que diga su metadato UiSoundMeta, y
	/// menu_hover al pasar el ratón (un botón desactivado no suena). El hover no se conecta dos veces al mismo botón.
	/// </summary>
	public static void HookButtons(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is BaseButton button)
			{
				string id = button.HasMeta(UiSoundMeta) ? button.GetMeta(UiSoundMeta).AsString() : SoundCatalog.UiClick;
				button.Pressed += () => Play(id); // Play nunca lanza
				HookHoverOf(button);
			}

			HookButtons(child);
		}
	}

	/// <summary>Conecta solo el sonido de hover (menu_hover) a todos los botones bajo un nodo; para las pantallas cuyo clic no suena.</summary>
	public static void HookHover(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is BaseButton button)
			{
				HookHoverOf(button);
			}

			HookHover(child);
		}
	}

	private static void HookHoverOf(BaseButton button)
	{
		if (button.HasMeta(UiHoverMeta))
		{
			return;
		}

		button.SetMeta(UiHoverMeta, true);
		button.MouseEntered += () =>
		{
			if (!button.Disabled)
			{
				Play(SoundCatalog.UiHover);
			}
		};
	}
}
