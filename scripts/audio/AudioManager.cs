using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Gestor central de audio (autoload). Todo el juego pide los sonidos por id (SoundCatalog) con Play (planos: Noah y la interfaz) o
/// PlayAt (posicionales, en 3D: los enemigos); él decide si suenan.
///
/// Buses: Master, SFX y Musica (default_bus_layout.tres; si faltaran se crean). Los volúmenes (GameSettings.MasterVolume y
/// SfxVolume) se aplican a Master y SFX, se guardan en user://settings.cfg y se cargan al iniciar. La música aún no existe: PlayMusic
/// y StopMusic son el gancho (assets/audio/music/{nombre}.ogg).
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
	public const string FlyerGroup = "flyers";

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

	public string CurrentMusic { get; private set; }

	private readonly List<Request> _requests = new();
	private readonly List<Voice> _voices = new();
	private readonly Dictionary<string, double> _lastPlayed = new();
	private readonly Dictionary<string, AudioStream> _streams = new();
	private readonly HashSet<string> _warned = new();
	private readonly List<AudioStreamPlayer> _flat = new();
	private readonly List<AudioStreamPlayer3D> _spatial = new();
	private readonly RandomNumberGenerator _random = new();
	private AudioStreamPlayer3D _flyerLoop;
	private AudioStreamPlayer _music;
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
		_music = new AudioStreamPlayer { Bus = SoundCatalog.BusMusic };
		AddChild(_music);
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

	/// <summary>Aplica GameSettings.MasterVolume y SfxVolume a los buses Master y SFX.</summary>
	public static void ApplyVolumes()
	{
		SetBusLinear(SoundCatalog.BusMaster, GameSettings.MasterVolume);
		SetBusLinear(SoundCatalog.BusSfx, GameSettings.SfxVolume);
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

	/// <summary>Música (gancho): assets/audio/music/{name}.ogg en el bus Musica. Si no existe no hace nada. Nunca lanza.</summary>
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

	// Carga con caché; null si el archivo no existe o no es un audio (sin errores en el log; una advertencia por ruta como mucho).
	private AudioStream Load(string path)
	{
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
			List<Node3D> flyers = GetTree().GetNodesInGroup(FlyerGroup).OfType<Node3D>().Where(f => IsInstanceValid(f) && f.IsInsideTree() && !f.IsQueuedForDeletion()).ToList();
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
		string path = SoundCatalog.MusicPath(name);
		AudioStream stream = Load(path);
		if (stream == null)
		{
			CurrentMusic = null;
			Record("musica:" + name, false, Vector3.Zero, 1.0f, SoundCatalog.BusMusic, Outcome.Missing);
			return;
		}

		CurrentMusic = name;
		Record("musica:" + name, false, Vector3.Zero, 1.0f, SoundCatalog.BusMusic, Silent ? Outcome.Silent : Outcome.Played);
		if (!Silent && _music != null)
		{
			_music.Stream = stream;
			_music.Play();
		}
	}

	private void EndMusic()
	{
		CurrentMusic = null;
		_music?.Stop();
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

	/// <summary>Conecta el clic de interfaz a todos los botones bajo un nodo (los que ya existen).</summary>
	public static void HookButtons(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is BaseButton button)
			{
				button.Pressed += () => Play(SoundCatalog.UiClick); // Play nunca lanza
			}

			HookButtons(child);
		}
	}
}
