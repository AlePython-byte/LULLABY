using Godot;

/// <summary>
/// Letra manuscrita (Patrick Hand, licencia OFL en assets/fonts) de las hojas de cuaderno: la pantalla de derrota y el menú de pausa.
/// Sin el archivo no pasa nada: se queda la letra del tema.
/// </summary>
public static class HandFont
{
	public const string Path = "res://assets/fonts/PatrickHand-Regular.ttf";

	/// <summary>La fuente si el archivo existe (null si falta o no carga).</summary>
	public static Font Load(string path = Path) => ResourceLoader.Exists(path) ? ResourceLoader.Load<Font>(path) : null;

	/// <summary>Usa la letra manuscrita en este control y en todo lo que cuelga de él. Devuelve false si no hay fuente.</summary>
	public static bool Apply(Control root, string path = Path)
	{
		Font font = Load(path);
		if (font == null)
		{
			return false;
		}

		root.Theme = new Theme { DefaultFont = font };
		return true;
	}
}
