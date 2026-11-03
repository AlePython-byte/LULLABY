using System.Collections.Generic;
using Godot;

/// <summary>
/// Estilo único de los botones de los menús: imágenes de papel y crayón (assets/ui/boton_normal.png, boton_hover.png y boton_pressed.png,
/// de 512×128) como StyleBoxTexture con los tres estados. Todas las pantallas lo piden aquí (Apply y ApplyAll); ninguna define el suyo.
///
///   - normal, hover y pressed usan sus imágenes. El foco (teclado y mando) usa la del hover, así que el botón con foco se ve igual que
///     con el ratón encima. hover_pressed usa la de pressed, y un botón de alternancia (el selector de dificultad) se ve «hundido» al estar
///     elegido.
///   - 9-slice: el dibujo útil de las imágenes está en ArtRegion (el resto es margen transparente y brillo exterior del hover, y recortarlo
///     evita que el botón quede fino); los bordes (SliceMargin) se dibujan a su tamaño y solo se estira el centro, que es liso.
///   - El texto lo pone cada pantalla, con la letra manuscrita Patrick Hand (HandFont) si existe. El arte es oscuro (relleno ≈ RGB 42,36,52),
///     así que el texto va en crema claro: sobre ese fondo el texto oscuro no se leería.
///   - Respaldo: si falta cualquiera de las tres imágenes (o no carga), no se toca nada: el botón sigue con el estilo de siempre y sin
///     errores en la consola (ResourceLoader.Exists antes de cargar).
///   - Las texturas y los estilos se guardan en campos estáticos (como DecorWarmup.Held): los menús se liberan al cambiar de escena y, si
///     cada uno cargara las suyas, el recolector podía finalizar un envoltorio mientras el menú siguiente lo pedía al caché del motor.
///
/// Solo estiliza los Button normales: no toca CheckButton, CheckBox ni OptionButton, que tienen su propio aspecto.
/// </summary>
public static class MenuButtonStyle
{
	public const string DefaultFolder = "res://assets/ui/";

	// Nombres de las imágenes. Solo las pruebas los cambian (y llaman a Reset), para simular que falta una sola.
	public static string NormalFile { get; set; } = "boton_normal.png";
	public static string HoverFile { get; set; } = "boton_hover.png";
	public static string PressedFile { get; set; } = "boton_pressed.png";

	/// <summary>Zona opaca de las imágenes de 512×128 (unión de las tres, con el brillo del hover): lo demás es transparente.</summary>
	public static readonly Rect2 ArtRegion = new(8.0f, 8.0f, 498.0f, 115.0f);

	/// <summary>Borde de la imagen (píxeles de ArtRegion) que no se estira: contiene el brillo, la línea y las marcas de las esquinas.</summary>
	public const float SliceMargin = 12.0f;

	/// <summary>Espacio entre el borde y el texto (no cambia el tamaño mínimo que ya pedía cada botón, solo su contenido).</summary>
	public const float ContentX = 14.0f;
	public const float ContentY = 4.0f;

	// Colores del texto sobre el arte oscuro.
	public static readonly Color TextColor = new(1.0f, 0.95f, 0.82f);
	public static readonly Color TextHoverColor = new(1.0f, 1.0f, 0.92f);
	public static readonly Color TextPressedColor = new(1.0f, 0.86f, 0.5f);
	public static readonly Color TextDisabledColor = new(0.6f, 0.57f, 0.55f);

	/// <summary>Carpeta de las imágenes. Solo las pruebas la apuntan a otra para simular imágenes ausentes (y llaman a Reset).</summary>
	public static string Folder { get; set; } = DefaultFolder;

	private static readonly List<Texture2D> HeldTextures = new();
	private static StyleBoxTexture _normal;
	private static StyleBoxTexture _hover;
	private static StyleBoxTexture _pressed;
	private static string _loadedFolder;

	/// <summary>Las tres texturas cargadas y retenidas (vacío si faltaba alguna).</summary>
	public static IReadOnlyList<Texture2D> Held => HeldTextures;

	public static StyleBoxTexture Normal => EnsureLoaded() ? _normal : null;
	public static StyleBoxTexture Hover => EnsureLoaded() ? _hover : null;
	public static StyleBoxTexture Pressed => EnsureLoaded() ? _pressed : null;

	/// <summary>Las tres imágenes existen y cargan: los botones pueden usar el estilo.</summary>
	public static bool Available => EnsureLoaded();

	/// <summary>Estiliza un botón. Devuelve false (sin tocarlo) si falta alguna imagen. Se puede llamar más de una vez.</summary>
	public static bool Apply(Button button)
	{
		if (button == null || !EnsureLoaded())
		{
			return false;
		}

		button.AddThemeStyleboxOverride("normal", _normal);
		button.AddThemeStyleboxOverride("disabled", _normal);
		button.AddThemeStyleboxOverride("hover", _hover);
		button.AddThemeStyleboxOverride("focus", _hover);
		button.AddThemeStyleboxOverride("pressed", _pressed);
		button.AddThemeStyleboxOverride("hover_pressed", _pressed);

		button.AddThemeColorOverride("font_color", TextColor);
		button.AddThemeColorOverride("font_hover_color", TextHoverColor);
		button.AddThemeColorOverride("font_focus_color", TextHoverColor);
		button.AddThemeColorOverride("font_pressed_color", TextPressedColor);
		button.AddThemeColorOverride("font_hover_pressed_color", TextPressedColor);
		button.AddThemeColorOverride("font_disabled_color", TextDisabledColor);

		if (HandFont.Load() is Font font)
		{
			button.AddThemeFontOverride("font", font);
		}

		return true;
	}

	/// <summary>Estiliza todos los Button normales bajo un nodo (incluido él mismo). Devuelve cuántos.</summary>
	public static int ApplyAll(Node root)
	{
		int count = 0;
		if (root is Button self && self.GetType() == typeof(Button) && Apply(self))
		{
			count++;
		}

		foreach (Node child in root.GetChildren())
		{
			count += ApplyAll(child);
		}

		return count;
	}

	/// <summary>Olvida lo cargado para volver a empezar (solo para pruebas).</summary>
	public static void Reset()
	{
		HeldTextures.Clear();
		_normal = null;
		_hover = null;
		_pressed = null;
		_loadedFolder = null;
	}

	// Cambiar Folder o un nombre de archivo obliga a volver a cargar.
	private static string CurrentKey => Folder + "|" + NormalFile + "|" + HoverFile + "|" + PressedFile;

	// Carga las tres imágenes una sola vez por carpeta. Todo o nada: con una sola que falte no se usa ninguna.
	private static bool EnsureLoaded()
	{
		if (_loadedFolder == CurrentKey)
		{
			return _normal != null;
		}

		Reset();
		_loadedFolder = CurrentKey;
		Texture2D normal = Load(NormalFile);
		Texture2D hover = Load(HoverFile);
		Texture2D pressed = Load(PressedFile);
		if (normal == null || hover == null || pressed == null)
		{
			return false;
		}

		HeldTextures.Add(normal);
		HeldTextures.Add(hover);
		HeldTextures.Add(pressed);
		_normal = Slice(normal);
		_hover = Slice(hover);
		_pressed = Slice(pressed);
		return true;
	}

	private static Texture2D Load(string file)
	{
		string path = Folder + file;
		return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
	}

	private static StyleBoxTexture Slice(Texture2D texture)
	{
		return new StyleBoxTexture
		{
			Texture = texture,
			RegionRect = ArtRegion,
			TextureMarginLeft = SliceMargin,
			TextureMarginRight = SliceMargin,
			TextureMarginTop = SliceMargin,
			TextureMarginBottom = SliceMargin,
			ContentMarginLeft = ContentX,
			ContentMarginRight = ContentX,
			ContentMarginTop = ContentY,
			ContentMarginBottom = ContentY,
			AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch,
			AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch,
		};
	}
}
