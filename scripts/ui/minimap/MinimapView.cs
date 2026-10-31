using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Dibuja un MinimapModel con los iconos de pixel art de assets/ui/ (32x32) a escala ENTERA y con filtro Nearest (sin suavizado). No
/// crea nodos por sala: todo se dibuja con _Draw, así que reintentar o cambiar de nivel no deja residuos. Si falta algún icono
/// (ResourceLoader.Exists) lo dibuja por código.
///
/// Pequeño (por defecto): panel de 192x192 arriba a la derecha, opacidad ~0,6, iconos x1 con 8 px entre salas unidas por una línea
/// fina, centrado en la sala actual y recortado al panel. Expandido (mientras se mantiene Tab): panel centrado que se ajusta a las
/// salas descubiertas con tope de 512x512, opacidad ~0,92 y, si caben, iconos x2 con 32 px entre salas (la línea de conexión más el
/// icono de puerta de 32 px en el hueco); si no caben, iconos x1 como en el pequeño.
/// </summary>
public partial class MinimapView : Control
{
	public const string IconPrefix = "minimapa_";
	public const int IconSize = 32;
	public const int SmallPanel = 192;
	public const int ExpandedMax = 512;
	public const int Padding = 16;
	public const float SmallAlpha = 0.6f;
	public const float ExpandedAlpha = 0.92f;
	public const float NeighborAlpha = 0.75f;

	// Nombres de los iconos (minimapa_{nombre}.png). sala_desconocida no se usa: las salas ocultas no se dibujan.
	public const string CurrentIcon = "sala_actual";
	public const string VisitedIcon = "sala_visitada";
	public const string UnvisitedIcon = "sala_sin_visitar";
	public const string DoorIcon = "puerta";
	public const string TreasureIcon = "tesoro";
	public const string BossIcon = "jefe";

	/// <summary>Carpeta de los iconos (las pruebas la apuntan a una carpeta vacía para comprobar el dibujo por código).</summary>
	public static string IconFolder { get; set; } = "res://assets/ui/";

	public MinimapModel Model { get; set; }

	public bool Expanded { get; private set; }

	/// <summary>Escala entera de los iconos de sala (1 o 2).</summary>
	public int IconScale { get; private set; } = 1;

	/// <summary>Distancia entre centros de salas contiguas, en píxeles.</summary>
	public int Pitch => IconSize * IconScale + Gap;

	/// <summary>Hueco entre iconos de salas contiguas, donde se dibuja la conexión.</summary>
	public int Gap => Expanded && IconScale == 2 ? IconSize : 8;

	private readonly Dictionary<string, Texture2D> _icons = new();

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		ClipContents = true;
		MouseFilter = MouseFilterEnum.Ignore;
		Relayout();
	}

	/// <summary>Cambia entre el tamaño pequeño y el expandido.</summary>
	public void SetExpanded(bool expanded)
	{
		Expanded = expanded;
		Relayout();
	}

	/// <summary>Recalcula tamaño y posición (el modelo cambió) y vuelve a dibujar.</summary>
	public void Relayout()
	{
		if (!Expanded)
		{
			IconScale = 1;
			SetAnchorsPreset(LayoutPreset.TopRight);
			OffsetLeft = -(SmallPanel + 8);
			OffsetTop = 16;
			OffsetRight = -8;
			OffsetBottom = 16 + SmallPanel;
			Modulate = new Color(1, 1, 1, SmallAlpha);
		}
		else
		{
			Rect2I bounds = Model != null ? Model.VisibleBounds() : new Rect2I(0, 0, 1, 1);
			bounds = new Rect2I(bounds.Position, new Vector2I(Mathf.Max(1, bounds.Size.X), Mathf.Max(1, bounds.Size.Y)));
			IconScale = 2;
			Vector2I panel = PanelFor(bounds);
			if (panel.X > ExpandedMax || panel.Y > ExpandedMax)
			{
				IconScale = 1;
				panel = PanelFor(bounds);
			}

			panel = new Vector2I(Mathf.Clamp(panel.X, SmallPanel, ExpandedMax), Mathf.Clamp(panel.Y, SmallPanel, ExpandedMax));
			SetAnchorsPreset(LayoutPreset.Center);
			OffsetLeft = -panel.X / 2;
			OffsetRight = OffsetLeft + panel.X;
			OffsetTop = -panel.Y / 2;
			OffsetBottom = OffsetTop + panel.Y;
			Modulate = new Color(1, 1, 1, ExpandedAlpha);
		}

		QueueRedraw();
	}

	// Tamaño del panel para unas salas visibles con la escala actual: el contenido más un margen.
	private Vector2I PanelFor(Rect2I bounds)
	{
		int pitch = IconSize * IconScale + (Expanded && IconScale == 2 ? IconSize : 8);
		int gap = Expanded && IconScale == 2 ? IconSize : 8;
		return new Vector2I(bounds.Size.X * pitch - gap + 2 * Padding, bounds.Size.Y * pitch - gap + 2 * Padding);
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.03f, 0.06f, 0.55f));
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.7f, 0.65f, 0.55f, 0.6f), false, 2.0f);
		if (Model == null)
		{
			return;
		}

		List<Vector2I> cells = Model.VisibleCells.ToList();
		if (cells.Count == 0)
		{
			return;
		}

		Vector2I origin = Origin(cells);
		int size = IconSize * IconScale;
		Vector2I Center(Vector2I cell) => origin + cell * Pitch;

		// Conexiones: línea fina bajo los iconos (solo se ve en el hueco) y, en el expandido con x2, el icono de puerta en el hueco.
		foreach ((Vector2I a, Vector2I b) in Model.Connections())
		{
			DrawLine(Center(a), Center(b), new Color(0.85f, 0.78f, 0.6f, 0.9f), Expanded ? 4.0f : 2.0f);
		}

		foreach (Vector2I cell in cells)
		{
			Rect2 rect = new(Center(cell) - new Vector2I(size / 2, size / 2), new Vector2(size, size));
			RoomVisibility state = Model.StateOf(cell);
			Color tint = state == RoomVisibility.Neighbor ? new Color(1, 1, 1, NeighborAlpha) : Colors.White;
			DrawIcon(state switch
			{
				RoomVisibility.Current => CurrentIcon,
				RoomVisibility.Visited => VisitedIcon,
				_ => UnvisitedIcon,
			}, rect, tint);

			if (Model.IconType(cell) is RoomType type)
			{
				DrawIcon(type == RoomType.Boss ? BossIcon : TreasureIcon, rect, tint);
			}

			if (state == RoomVisibility.Current)
			{
				DrawRect(rect.Grow(1.0f), Colors.White, false, 2.0f);
			}
		}

		if (Expanded && IconScale == 2)
		{
			foreach ((Vector2I a, Vector2I b) in Model.Connections())
			{
				Vector2I mid = (Center(a) + Center(b)) / 2;
				DrawIcon(DoorIcon, new Rect2(mid - new Vector2I(IconSize / 2, IconSize / 2), new Vector2(IconSize, IconSize)), Colors.White);
			}
		}
	}

	// Posición del centro de la celda (0, 0). Pequeño: la sala actual en el centro del panel. Expandido: las salas visibles centradas.
	private Vector2I Origin(List<Vector2I> cells)
	{
		Vector2I panelCenter = new((int)(Size.X / 2.0f), (int)(Size.Y / 2.0f));
		if (!Expanded && Model.Current is Vector2I current)
		{
			return panelCenter - current * Pitch;
		}

		int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
		return panelCenter - new Vector2I((minX + maxX) * Pitch / 2, (minY + maxY) * Pitch / 2);
	}

	/// <summary>El icono tiene su PNG (true) o se dibuja por código (false).</summary>
	public bool HasTexture(string name) => Icon(name) != null;

	private Texture2D Icon(string name)
	{
		if (!_icons.TryGetValue(name, out Texture2D texture))
		{
			string path = $"{IconFolder}{IconPrefix}{name}.png";
			texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
			_icons[name] = texture;
		}

		return texture;
	}

	private void DrawIcon(string name, Rect2 rect, Color tint)
	{
		Texture2D texture = Icon(name);
		if (texture != null)
		{
			DrawTextureRect(texture, rect, false, tint);
			return;
		}

		// Respaldo por código, con los mismos colores generales que el pixel art.
		Color Tinted(Color color) => new(color.R, color.G, color.B, color.A * tint.A);
		switch (name)
		{
			case CurrentIcon:
				DrawRect(rect.Grow(-2), Tinted(new Color(0.55f, 0.5f, 0.35f)));
				DrawRect(rect.Grow(-2), Tinted(Colors.White), false, 2.0f);
				DrawCircle(rect.GetCenter(), rect.Size.X * 0.15f, Tinted(Colors.White));
				break;
			case VisitedIcon:
				DrawRect(rect.Grow(-2), Tinted(new Color(0.28f, 0.28f, 0.36f)));
				DrawRect(rect.Grow(-2), Tinted(new Color(0.6f, 0.65f, 0.7f)), false, 2.0f);
				break;
			case UnvisitedIcon:
				DrawRect(rect.Grow(-2), Tinted(new Color(0.12f, 0.12f, 0.16f)));
				DrawRect(rect.Grow(-2), Tinted(new Color(0.28f, 0.27f, 0.34f)), false, 2.0f);
				break;
			case DoorIcon:
				DrawRect(rect.Grow(-6), Tinted(new Color(0.55f, 0.35f, 0.18f)));
				DrawRect(rect.Grow(-6), Tinted(new Color(0.65f, 0.65f, 0.7f)), false, 2.0f);
				break;
			case TreasureIcon:
				DrawRect(new Rect2(rect.GetCenter() - rect.Size * 0.28f, rect.Size * 0.56f), Tinted(new Color(0.95f, 0.75f, 0.15f)));
				break;
			case BossIcon:
				DrawCircle(rect.GetCenter(), rect.Size.X * 0.3f, Tinted(new Color(0.9f, 0.9f, 0.85f)));
				DrawCircle(rect.GetCenter() + new Vector2(-rect.Size.X * 0.1f, 0.0f), rect.Size.X * 0.06f, Tinted(new Color(0.85f, 0.1f, 0.1f)));
				DrawCircle(rect.GetCenter() + new Vector2(rect.Size.X * 0.1f, 0.0f), rect.Size.X * 0.06f, Tinted(new Color(0.85f, 0.1f, 0.1f)));
				break;
		}
	}
}
