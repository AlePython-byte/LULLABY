using System.Collections.Generic;
using Godot;

/// <summary>Cómo se ve un corazón de la fila de vida.</summary>
public enum HeartState
{
	Empty,
	Half,
	Full,
}

/// <summary>
/// Fila de corazones pixel art (assets/ui/corazon_lleno|medio|vacio.png, 16x16) con la vida del jugador: 1 corazón = 1 punto de vida y
/// tantos corazones como la vida máxima redondeada hacia arriba (como mucho MaxHearts). Escala entera (x3) y filtro Nearest. Solo
/// MUESTRA la vida: no cambia ningún valor de vida ni de daño. Se dibuja con _Draw (sin un nodo por corazón: reintentar no deja
/// residuos); si falta alguna textura (ResourceLoader.Exists) dibuja el corazón por código.
///
/// Regla de redondeo: al corazón i (0 = el primero) le corresponde v = clamp(vida - i, 0, 1):
///   v >= 0,75 → lleno;  0,25 <= v < 0,75 → medio;  v < 0,25 → vacío.
/// Garantía: si la vida es mayor que 0, nunca se muestran 0 corazones: si la regla los dejara todos vacíos (por ejemplo con 0,1), el
/// primero se muestra medio. Ejemplos: 5,2/6 → 5 llenos y 1 vacío; 4,5/6 → 4 llenos, 1 medio y 1 vacío; 0,5 → 1 medio.
///
/// Al perder vida, el corazón que baja de estado se sacude y se tiñe de rojo un momento (solo visual).
/// </summary>
public partial class HeartsRow : Control
{
	public const int IconSize = 16;
	public const int IconScale = 3;
	public const int Pitch = IconSize * IconScale + 4;
	public const int MaxHearts = 24;
	public const float FullThreshold = 0.75f;
	public const float HalfThreshold = 0.25f;
	public const float ShakeTime = 0.35f;

	/// <summary>Carpeta de las texturas (las pruebas la apuntan a una carpeta vacía para comprobar el dibujo por código).</summary>
	public static string IconFolder { get; set; } = "res://assets/ui/";

	public const string FullIcon = "corazon_lleno";
	public const string HalfIcon = "corazon_medio";
	public const string EmptyIcon = "corazon_vacio";

	// Contorno de un corazón, en un cuadrado de 1x1, para el dibujo por código.
	private static readonly Vector2[] HeartOutline = BuildOutline();

	private readonly List<HeartState> _states = new();
	private readonly List<float> _shake = new();
	private readonly Dictionary<string, Texture2D> _icons = new();

	/// <summary>Estado de cada corazón, de izquierda a derecha.</summary>
	public IReadOnlyList<HeartState> States => _states;

	public int HeartCount => _states.Count;

	/// <summary>Hay algún corazón sacudiéndose ahora mismo.</summary>
	public bool IsShaking(int index) => index >= 0 && index < _shake.Count && _shake[index] > 0.0f;

	/// <summary>Estados de los corazones para una vida y una vida máxima (la regla descrita arriba).</summary>
	public static HeartState[] StatesFor(float current, float max)
	{
		int count = float.IsFinite(max) ? Mathf.Clamp(Mathf.CeilToInt(max - 0.001f), 0, MaxHearts) : 0;
		float health = float.IsFinite(current) ? Mathf.Max(0.0f, current) : 0.0f;
		HeartState[] states = new HeartState[count];
		for (int i = 0; i < count; i++)
		{
			float v = Mathf.Clamp(health - i, 0.0f, 1.0f);
			states[i] = v >= FullThreshold ? HeartState.Full : v >= HalfThreshold ? HeartState.Half : HeartState.Empty;
		}

		// Con vida nunca se muestran 0 corazones.
		if (health > 0.0f && count > 0 && states[0] == HeartState.Empty)
		{
			states[0] = HeartState.Half;
		}

		return states;
	}

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		MouseFilter = MouseFilterEnum.Ignore;
	}

	/// <summary>Actualiza la fila con la vida actual y la máxima efectiva. Los corazones que bajan de estado se sacuden.</summary>
	public void SetHealth(float current, float max)
	{
		HeartState[] next = StatesFor(current, max);
		bool first = _states.Count == 0 && _shake.Count == 0;
		List<float> shake = new(next.Length);
		for (int i = 0; i < next.Length; i++)
		{
			float remaining = i < _shake.Count ? _shake[i] : 0.0f;
			if (!first && i < _states.Count && next[i] < _states[i])
			{
				remaining = ShakeTime;
			}

			shake.Add(remaining);
		}

		_states.Clear();
		_states.AddRange(next);
		_shake.Clear();
		_shake.AddRange(shake);
		Size = new Vector2(Mathf.Max(0, next.Length * Pitch - 4), IconSize * IconScale);
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		bool any = false;
		for (int i = 0; i < _shake.Count; i++)
		{
			if (_shake[i] > 0.0f)
			{
				_shake[i] = Mathf.Max(0.0f, _shake[i] - (float)delta);
				any = true;
			}
		}

		if (any)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		int size = IconSize * IconScale;
		for (int i = 0; i < _states.Count; i++)
		{
			Vector2 position = new(i * Pitch, 0);
			Color tint = Colors.White;
			if (_shake[i] > 0.0f)
			{
				float t = _shake[i] / ShakeTime;
				// Sacudida en píxeles enteros (sin medios píxeles) y tinte rojo que se apaga.
				position += new Vector2(Mathf.Round(Mathf.Sin(t * 40.0f) * 3.0f * t), 0);
				tint = Colors.White.Lerp(new Color(1.0f, 0.35f, 0.35f), t);
			}

			string icon = _states[i] switch
			{
				HeartState.Full => FullIcon,
				HeartState.Half => HalfIcon,
				_ => EmptyIcon,
			};
			Rect2 rect = new(position, new Vector2(size, size));
			Texture2D texture = Icon(icon);
			if (texture != null)
			{
				DrawTextureRect(texture, rect, false, tint);
			}
			else
			{
				DrawByCode(_states[i], rect, tint);
			}
		}
	}

	/// <summary>El icono tiene su PNG (true) o se dibuja por código (false).</summary>
	public bool HasTexture(string name) => Icon(name) != null;

	private Texture2D Icon(string name)
	{
		if (!_icons.TryGetValue(name, out Texture2D texture))
		{
			string path = $"{IconFolder}{name}.png";
			texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
			_icons[name] = texture;
		}

		return texture;
	}

	// Respaldo por código: corazón rojo (lleno), con la mitad izquierda roja (medio) o solo el contorno (vacío).
	private void DrawByCode(HeartState state, Rect2 rect, Color tint)
	{
		Vector2[] outline = new Vector2[HeartOutline.Length];
		for (int i = 0; i < outline.Length; i++)
		{
			outline[i] = rect.Position + HeartOutline[i] * rect.Size;
		}

		Color Tinted(Color color) => new(color.R * tint.R, color.G * tint.G, color.B * tint.B, color.A);
		Color red = Tinted(new Color(0.85f, 0.12f, 0.15f));
		Color dark = Tinted(new Color(0.18f, 0.06f, 0.08f));
		DrawColoredPolygon(outline, dark);
		if (state == HeartState.Full)
		{
			DrawColoredPolygon(outline, red);
		}
		else if (state == HeartState.Half)
		{
			Vector2[] left = new Vector2[outline.Length];
			for (int i = 0; i < left.Length; i++)
			{
				left[i] = new Vector2(Mathf.Min(outline[i].X, rect.GetCenter().X), outline[i].Y);
			}

			DrawColoredPolygon(left, red);
		}

		DrawPolyline(Close(outline), Tinted(new Color(0.95f, 0.85f, 0.85f)), 2.0f);
	}

	private static Vector2[] Close(Vector2[] points)
	{
		Vector2[] closed = new Vector2[points.Length + 1];
		points.CopyTo(closed, 0);
		closed[^1] = points[0];
		return closed;
	}

	private static Vector2[] BuildOutline()
	{
		const int steps = 40;
		Vector2[] points = new Vector2[steps];
		for (int i = 0; i < steps; i++)
		{
			float t = Mathf.Tau * i / steps;
			float x = 16.0f * Mathf.Pow(Mathf.Sin(t), 3.0f);
			float y = 13.0f * Mathf.Cos(t) - 5.0f * Mathf.Cos(2.0f * t) - 2.0f * Mathf.Cos(3.0f * t) - Mathf.Cos(4.0f * t);
			points[i] = new Vector2(0.5f + x / 36.0f, 0.45f - y / 36.0f);
		}

		return points;
	}
}
