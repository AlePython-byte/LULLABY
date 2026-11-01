using System.Collections.Generic;
using Godot;

/// <summary>
/// Carátula animada del menú principal: las capas de assets/ui/caratula/ (fondo, cuna con Noah, móvil y partículas) apiladas a pantalla
/// completa, cada una cubriendo la ventana sin deformarse, con un parallax mínimo: cada capa se desplaza unos píxeles con un seno lento
/// (periodo de 12 a 18 s) y desfasado del resto. No hay shaders, partículas ni animaciones pesadas: solo cuatro desplazamientos por
/// fotograma.
///
/// Las capas se buscan por nombre con ResourceLoader.Exists: una que falte (o que no cargue) se omite sin error, y si no hay ninguna el
/// nodo queda vacío y no procesa nada. Se pueden reemplazar los PNG por otra versión con el mismo nombre y las mismas medidas. El título
/// pintado (capa_4_titulo) no se usa: el menú pone el logo sobre el panel de papel.
/// </summary>
public partial class MenuCover : Control
{
	/// <summary>Carpeta de las capas (las pruebas la apuntan a una carpeta sin ellas).</summary>
	public static string Folder { get; set; } = "res://assets/ui/caratula/";

	/// <summary>Capas de fondo a frente: nombre (sin extensión), amplitud (px, en el plano de 1152×648), periodo (s), fase (rad) y si se mueve en vertical.</summary>
	public static readonly (string Name, float Amplitude, float Period, float Phase, bool Vertical)[] LayerSpecs =
	{
		("capa_0_fondo", 3.0f, 17.0f, 0.0f, false),
		("capa_1_cuna_noah", 6.0f, 14.0f, 1.3f, false),
		("capa_2_movil", 10.0f, 12.0f, 2.1f, false),
		("capa_3_particulas", 16.0f, 15.0f, 0.7f, true),
	};

	/// <summary>Factor general del movimiento (1 = el de LayerSpecs; 0 deja la carátula quieta).</summary>
	[Export] public float Motion { get; set; } = 1.0f;

	private readonly List<(TextureRect Rect, int Spec)> _layers = new();
	private float _time;

	/// <summary>Capas cargadas (de 0 a LayerSpecs.Length).</summary>
	public int LayerCount => _layers.Count;

	/// <summary>Nombres de las capas cargadas, de fondo a frente.</summary>
	public IEnumerable<string> LoadedNames
	{
		get
		{
			foreach ((TextureRect _, int spec) in _layers)
			{
				yield return LayerSpecs[spec].Name;
			}
		}
	}

	/// <summary>Desplazamiento actual (px) de la capa cargada número i respecto a su reposo.</summary>
	public float DisplacementOf(int index)
	{
		TextureRect rect = _layers[index].Rect;
		bool vertical = LayerSpecs[_layers[index].Spec].Vertical;
		return (vertical ? rect.OffsetTop : rect.OffsetLeft) + Margin(_layers[index].Spec);
	}

	/// <summary>Desplazamiento máximo (px) que puede alcanzar la capa cargada número i.</summary>
	public float MaxDisplacementOf(int index) => LayerSpecs[_layers[index].Spec].Amplitude * Mathf.Max(0.0f, Motion);

	/// <summary>Cuánto sobresale cada capa de la ventana (px) para que su desplazamiento no deje bordes al descubierto.</summary>
	private static float Margin(int spec) => LayerSpecs[spec].Amplitude + 2.0f;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
		for (int i = 0; i < LayerSpecs.Length; i++)
		{
			string path = $"{Folder}{LayerSpecs[i].Name}.png";
			Texture2D texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
			if (texture == null)
			{
				continue;
			}

			TextureRect rect = new()
			{
				Name = LayerSpecs[i].Name,
				Texture = texture,
				MouseFilter = MouseFilterEnum.Ignore,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			};
			rect.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(rect);
			_layers.Add((rect, i));
			Apply(_layers.Count - 1, 0.0f);
		}

		SetProcess(_layers.Count > 0 && Motion > 0.0f);
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		for (int i = 0; i < _layers.Count; i++)
		{
			(string _, float amplitude, float period, float phase, bool _) = LayerSpecs[_layers[i].Spec];
			Apply(i, amplitude * Motion * Mathf.Sin(Mathf.Tau * _time / period + phase));
		}
	}

	// Coloca la capa con su margen de sobra y el desplazamiento dado en el eje que le toca.
	private void Apply(int index, float displacement)
	{
		(TextureRect rect, int spec) = _layers[index];
		float margin = Margin(spec);
		bool vertical = LayerSpecs[spec].Vertical;
		rect.OffsetLeft = -margin + (vertical ? 0.0f : displacement);
		rect.OffsetRight = margin + (vertical ? 0.0f : displacement);
		rect.OffsetTop = -margin + (vertical ? displacement : 0.0f);
		rect.OffsetBottom = margin + (vertical ? displacement : 0.0f);
	}
}
