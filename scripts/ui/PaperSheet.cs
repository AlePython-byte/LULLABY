using Godot;

/// <summary>
/// Geometría de la hoja de cuaderno (assets/ui/panel_menu_papel.png, 760×980) de la pantalla de derrota y del menú de pausa, en la ventana de
/// diseño de 1152×648: centrada y de 600 px de alto (465 de ancho), con el contenido en su zona útil, es decir, entre las chinchetas de
/// arriba, el margen rojo de la izquierda y la esquina doblada de abajo (las mismas fracciones que el menú principal). Todo se coloca con
/// anclas al centro de la ventana, así que no depende de su tamaño.
/// </summary>
public static class PaperSheet
{
	public const string Path = "res://assets/ui/panel_menu_papel.png";

	public const float Height = 600.0f;
	public const float Aspect = 760.0f / 980.0f;

	public const float Top = 0.095f;
	public const float Bottom = 0.07f;
	public const float Left = 0.19f;
	public const float Right = 0.955f;

	public static float Width => Height * Aspect;

	/// <summary>La hoja, relativa al centro de la ventana.</summary>
	public static Rect2 Rect => new(-Width / 2.0f, -Height / 2.0f, Width, Height);

	/// <summary>La zona útil de la hoja, relativa al centro de la ventana.</summary>
	public static Rect2 Inner => new(Rect.Position.X + Left * Width, Rect.Position.Y + Top * Height, (Right - Left) * Width, (1.0f - Top - Bottom) * Height);

	/// <summary>Textura de la hoja si el archivo existe (null si falta o no carga).</summary>
	public static Texture2D Load(string path = Path) => ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;

	/// <summary>Coloca un control con anclas al centro de la ventana y estos márgenes (relativos al centro).</summary>
	public static void Place(Control control, Rect2 rect)
	{
		control.AnchorLeft = 0.5f;
		control.AnchorRight = 0.5f;
		control.AnchorTop = 0.5f;
		control.AnchorBottom = 0.5f;
		control.OffsetLeft = rect.Position.X;
		control.OffsetTop = rect.Position.Y;
		control.OffsetRight = rect.End.X;
		control.OffsetBottom = rect.End.Y;
		control.GrowHorizontal = Control.GrowDirection.Both;
		control.GrowVertical = Control.GrowDirection.Both;
	}
}
