using Godot;

/// <summary>
/// Sonido de fondo en bucle con el empalme escondido: dos reproductores que se alternan. Un segundo (CrossfadeSeconds) antes del final de la
/// pista, el otro empieza desde el principio y los dos se cruzan con potencia constante, así el salto del final al principio no se oye.
/// El cruce se calcula con la posición de reproducción del reproductor que suena (no con el reloj del juego). FadeOut lo apaga con fundido
/// y Stop lo corta. Es de la intro (el canto de la madre), pero no sabe nada de ella.
///
/// Sin dispositivo de audio (Silent) no crea sonido: lleva la misma cuenta con una posición virtual que avanza con el tiempo del juego.
/// </summary>
public partial class LoopingBackdrop : Node
{
	public const float CrossfadeSeconds = 1.0f;

	private AudioStreamPlayer _playing;
	private AudioStreamPlayer _other;
	private AudioStream _stream;
	private double _length;
	private double _position;      // virtual (Silent) del que suena
	private double _otherPosition; // virtual (Silent) del que entra
	private bool _crossing;
	private float _master = 1.0f;
	private float _masterTarget = 1.0f;
	private float _masterRate;
	private float _baseDb;

	/// <summary>Silencio: no se crea ni se toca ningún reproductor.</summary>
	public bool Silent { get; set; }

	/// <summary>Suena (o sonaría, en silencio).</summary>
	public bool Active { get; private set; }

	/// <summary>Cuántas veces ha vuelto al principio (empalmes hechos).</summary>
	public int LoopCount { get; private set; }

	/// <summary>Hay un cruce de bucle en curso.</summary>
	public bool Crossing => _crossing;

	/// <summary>Volumen general del fondo (1 = el pedido, 0 = apagado).</summary>
	public float Master => _master;

	/// <summary>Algún reproductor está sonando de verdad.</summary>
	public bool AnyPlayerPlaying => (_playing?.Playing ?? false) || (_other?.Playing ?? false);

	/// <summary>Posición de reproducción del que suena (segundos).</summary>
	public double Position => Silent ? _position : _playing?.GetPlaybackPosition() ?? 0.0;

	/// <summary>Empieza a sonar en el bus dado, a volumeDb, desde fromSeconds. Devuelve false si no hay pista (no hace nada).</summary>
	public bool Start(AudioStream stream, string bus, float volumeDb, double fromSeconds = 0.0)
	{
		Stop();
		if (stream == null)
		{
			return false;
		}

		_stream = stream;
		_length = stream.GetLength();
		_baseDb = volumeDb;
		_master = 1.0f;
		_masterTarget = 1.0f;
		_position = fromSeconds;
		_otherPosition = 0.0;
		_crossing = false;
		LoopCount = 0;
		Active = true;
		if (!Silent)
		{
			_playing = Player(bus);
			_other = Player(bus);
			_playing.Stream = stream;
			_playing.Play((float)fromSeconds);
			Apply(1.0f, 0.0f);
		}

		return true;
	}

	/// <summary>Lo apaga con fundido de seconds segundos y, al llegar a cero, lo detiene.</summary>
	public void FadeOut(float seconds)
	{
		if (!Active)
		{
			return;
		}

		_masterTarget = 0.0f;
		_masterRate = seconds <= 0.0f ? float.PositiveInfinity : 1.0f / seconds;
	}

	/// <summary>Lo corta al momento.</summary>
	public void Stop()
	{
		Active = false;
		_crossing = false;
		_playing?.Stop();
		_other?.Stop();
		_playing?.QueueFree();
		_other?.QueueFree();
		_playing = null;
		_other = null;
	}

	public override void _Process(double delta)
	{
		Advance(delta);
	}

	/// <summary>Avanza delta segundos el fundido de salida y, en silencio, la posición virtual; reparte el cruce del bucle. Público para las pruebas.</summary>
	public void Advance(double delta)
	{
		if (!Active)
		{
			return;
		}

		if (!Mathf.IsEqualApprox(_master, _masterTarget))
		{
			_master = Mathf.MoveToward(_master, _masterTarget, (float)delta * _masterRate);
			if (_master <= 0.0f)
			{
				Stop();
				return;
			}
		}

		if (Silent)
		{
			_position += delta;
			if (_crossing)
			{
				_otherPosition += delta;
			}
		}

		double position = Position;
		if (!_crossing && position >= _length - CrossfadeSeconds)
		{
			_crossing = true;
			_otherPosition = Silent ? position - (_length - CrossfadeSeconds) : 0.0;
			if (!Silent)
			{
				_other.Stream = _stream;
				_other.Play(0.0f);
			}
		}

		float into = 0.0f;
		bool ended = Silent ? _position >= _length : !_playing.Playing;
		if (_crossing)
		{
			into = ended ? 1.0f : Mathf.Clamp((float)((position - (_length - CrossfadeSeconds)) / CrossfadeSeconds), 0.0f, 1.0f);
			if (into >= 1.0f)
			{
				// El que sonaba llegó al final: el que entró pasa a ser el principal.
				_crossing = false;
				LoopCount++;
				(_playing, _other) = (_other, _playing);
				_other?.Stop();
				_position = _otherPosition;
				into = 0.0f;
				Apply(1.0f, 0.0f);
				return;
			}
		}

		Apply(1.0f - into, into);
	}

	private AudioStreamPlayer Player(string bus)
	{
		AudioStreamPlayer player = new() { Bus = bus };
		AddChild(player);
		return player;
	}

	// Potencia constante: la que entra y la que sale suman el mismo volumen percibido a mitad del cruce.
	private void Apply(float playingGain, float otherGain)
	{
		if (Silent || _playing == null)
		{
			return;
		}

		_playing.VolumeDb = Db(playingGain);
		if (_other != null)
		{
			_other.VolumeDb = Db(otherGain);
		}
	}

	private float Db(float gain)
	{
		float amplitude = Mathf.Sin(gain * Mathf.Pi / 2.0f) * _master;
		return amplitude <= 0.0001f ? -80.0f : Mathf.LinearToDb(amplitude) + _baseDb;
	}
}
