using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Reproductor de música con fundido cruzado (hijo de AudioManager, que es quien decide qué pista suena). Cada pista sonando es una capa
/// con su volumen (Gain, de 0 a 1) que sube o baja hacia su Target en FadeTime segundos; pedir otra pista baja todas las demás y sube la
/// nueva, así que las dos se oyen a la vez durante ese tiempo. Pedir la que ya suena no la reinicia (cambiar de una sala normal a otra
/// normal no corta la música). Las pistas van en bucle: si la importación no lo activó, se duplica el stream y se activa aquí.
///
/// El fundido avanza con el tiempo real aunque el árbol esté en pausa (ProcessMode Always heredado de AudioManager): pausar el juego no
/// corta la música ni deja un fundido a medias. En silencio (sin dispositivo) no hay reproductores, pero la contabilidad es la misma.
/// </summary>
public partial class MusicPlayer : Node
{
	public const float FadeTime = 1.0f;

	/// <summary>Reproductores disponibles: la pista actual, la que se va y una más por si llega otro cambio en pleno fundido.</summary>
	public const int PlayerCount = 3;

	public sealed class Layer
	{
		public string Name;
		public AudioStream Stream;
		public AudioStreamPlayer Player; // null en silencio
		public float VolumeDb;
		public float Gain;
		public float Target;
	}

	private readonly List<Layer> _layers = new();
	private readonly List<AudioStreamPlayer> _players = new();

	/// <summary>Silencio: sin dispositivo no se crea ni se toca ningún reproductor.</summary>
	public bool Silent { get; set; }

	/// <summary>Pista que debería sonar ahora (la última pedida y que existe); nula si no hay ninguna o se está apagando.</summary>
	public string Current { get; private set; }

	/// <summary>Stream de la pista actual, ya con el bucle activo.</summary>
	public AudioStream CurrentStream => _layers.LastOrDefault(l => l.Name == Current)?.Stream;

	/// <summary>Cuántas veces ha empezado una pista desde cero (pedir la que ya suena no cuenta).</summary>
	public int StartCount { get; private set; }

	public IReadOnlyList<Layer> Layers => _layers;

	/// <summary>Hay un fundido en curso (alguna capa sube o baja).</summary>
	public bool Fading => _layers.Any(l => l.Gain != l.Target); // MoveToward acaba exactamente en el objetivo

	public float GainOf(string name) => _layers.Where(l => l.Name == name).Select(l => l.Gain).DefaultIfEmpty(0.0f).Max();

	public override void _Ready()
	{
		if (Silent)
		{
			return;
		}

		for (int i = 0; i < PlayerCount; i++)
		{
			AudioStreamPlayer player = new() { Bus = SoundCatalog.BusMusic };
			AddChild(player);
			_players.Add(player);
		}
	}

	/// <summary>Empieza la pista name con fundido cruzado desde la que suena. Devuelve false si ya sonaba (no se reinicia).</summary>
	public bool Play(SoundCatalog.MusicTrack track, AudioStream stream)
	{
		Layer same = _layers.LastOrDefault(l => l.Name == track.Id && l.Target > 0.0f);
		if (same != null)
		{
			Current = track.Id;
			return false;
		}

		foreach (Layer layer in _layers)
		{
			layer.Target = 0.0f;
		}

		Layer added = new() { Name = track.Id, Stream = Looped(stream, track.Loop), VolumeDb = track.VolumeDb, Gain = 0.0f, Target = 1.0f };
		if (!Silent)
		{
			added.Player = TakePlayer();
			if (added.Player != null)
			{
				added.Player.Stream = added.Stream;
				added.Player.VolumeDb = -80.0f;
				added.Player.Play();
			}
		}

		_layers.Add(added);
		Current = track.Id;
		StartCount++;
		return true;
	}

	/// <summary>Apaga la música con fundido (o la corta al momento si immediate).</summary>
	public void FadeOut(bool immediate = false)
	{
		Current = null;
		foreach (Layer layer in _layers)
		{
			layer.Target = 0.0f;
			if (immediate)
			{
				layer.Gain = 0.0f;
			}
		}

		if (immediate)
		{
			Advance(0.0f);
		}
	}

	public override void _Process(double delta)
	{
		Advance((float)delta);
	}

	/// <summary>Avanza los fundidos delta segundos (público para las pruebas).</summary>
	public void Advance(float delta)
	{
		float step = delta / FadeTime;
		foreach (Layer layer in _layers.ToList())
		{
			layer.Gain = Mathf.MoveToward(layer.Gain, layer.Target, step);
			if (layer.Player != null)
			{
				// Potencia constante: la que entra y la que sale suman el mismo volumen percibido a mitad del cruce.
				float amplitude = Mathf.Sin(layer.Gain * Mathf.Pi / 2.0f);
				layer.Player.VolumeDb = amplitude <= 0.0001f ? -80.0f : Mathf.LinearToDb(amplitude) + layer.VolumeDb;
			}

			if (layer.Target <= 0.0f && layer.Gain <= 0.0f)
			{
				layer.Player?.Stop();
				_layers.Remove(layer);
			}
		}
	}

	private AudioStreamPlayer TakePlayer()
	{
		AudioStreamPlayer free = _players.FirstOrDefault(p => _layers.All(l => l.Player != p));
		if (free != null)
		{
			return free;
		}

		// Sin reproductor libre (cambios seguidos en pleno fundido): se descarta la capa más callada que ya se va.
		Layer quietest = _layers.Where(l => l.Target <= 0.0f).OrderBy(l => l.Gain).FirstOrDefault();
		if (quietest == null)
		{
			return null;
		}

		quietest.Player.Stop();
		AudioStreamPlayer player = quietest.Player;
		_layers.Remove(quietest);
		return player;
	}

	// La importación de los .ogg trae el bucle activado; si algún día no, se duplica el stream y se activa aquí.
	private static AudioStream Looped(AudioStream stream, bool loop)
	{
		if (loop && stream is AudioStreamOggVorbis { Loop: false } ogg)
		{
			AudioStreamOggVorbis copy = (AudioStreamOggVorbis)ogg.Duplicate();
			copy.Loop = true;
			return copy;
		}

		return stream;
	}
}
