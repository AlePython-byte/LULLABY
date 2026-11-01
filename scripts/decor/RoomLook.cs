using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Aspecto propio de cada sala decorada, para que las salas se distingan entre sí. Solo visual; el azar sale de SeedMix (semilla de
/// partida y celda), así que la misma sala se ve siempre igual y elegir no consume el azar global.
///
/// Salas de combate (Normal):
///   1. si existen texturas de variante en assets/textures/ (floor_*.png y wall_*.png, los nombres de FloorVariants y WallVariants),
///      se elige una por sala entre las que existan, en triplanar de espacio mundo (RoomSurfaces.Textured);
///   2. si no hay ninguna, se usa la textura de siempre de RoomSurfaces (parquet, papel_tapiz...);
///   3. si tampoco, el color de siempre con una variación sutil de tono, saturación y brillo por sala.
/// El resto de tipos (inicio, tesoro, jefe) conservan su textura de RoomSurfaces y, sin ella, solo varían el tono del color.
/// Ninguna textura es obligatoria: una ausente (o que no carga) cae al siguiente paso sin error.
/// </summary>
public static class RoomLook
{
	public static readonly string[] FloorVariants = { "floor_madera", "floor_baldosas", "floor_alfombra" };
	public static readonly string[] WallVariants = { "wall_estrellas", "wall_nubes", "wall_rayas" };

	/// <summary>Variación máxima del tono (fracción de la rueda de color) y del brillo y la saturación (proporción) de un color.</summary>
	public const float HueRange = 0.025f;
	public const float ValueRange = 0.12f;
	public const float SaturationRange = 0.10f;

	private const ulong FloorSalt = 0x464C4F4FUL;
	private const ulong WallSalt = 0x574C4F4FUL;

	public static StandardMaterial3D Floor(RoomType type, Color color, float tileMeters, int gameSeed, Vector2I cell)
	{
		return Build(type, FloorVariants, FloorSalt, RoomSurfaces.Floor(type, color, tileMeters), color, tileMeters, gameSeed, cell);
	}

	public static StandardMaterial3D Wall(RoomType type, Color color, float tileMeters, int gameSeed, Vector2I cell)
	{
		return Build(type, WallVariants, WallSalt, RoomSurfaces.Wall(color, tileMeters), color, tileMeters, gameSeed, cell);
	}

	/// <summary>Nombres de las variantes cuyo archivo existe ahora mismo en RoomSurfaces.TextureFolder, en el orden del catálogo.</summary>
	public static List<string> Available(string[] names) => names.Where(name => ResourceLoader.Exists(RoomSurfaces.PathOf(name))).ToList();

	/// <summary>Variante elegida para una sala (null si no hay ninguna): depende de la semilla y de qué archivos existen.</summary>
	public static string PickVariant(string[] names, int gameSeed, Vector2I cell, bool floor)
	{
		List<string> available = Available(names);
		if (available.Count == 0)
		{
			return null;
		}

		using RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, floor ? FloorSalt : WallSalt);
		return available[random.RandiRange(0, available.Count - 1)];
	}

	/// <summary>Color con tono, saturación y brillo desplazados un poco (dentro de HueRange, SaturationRange y ValueRange).</summary>
	public static Color Vary(Color color, RandomNumberGenerator random)
	{
		float hue = Mathf.Wrap(color.H + random.RandfRange(-HueRange, HueRange), 0.0f, 1.0f);
		float saturation = Mathf.Clamp(color.S * (1.0f + random.RandfRange(-SaturationRange, SaturationRange)), 0.0f, 1.0f);
		float value = Mathf.Clamp(color.V * (1.0f + random.RandfRange(-ValueRange, ValueRange)), 0.0f, 1.0f);
		return Color.FromHsv(hue, saturation, value, color.A);
	}

	/// <summary>El color es el original con, como mucho, la variación de Vary (tono, saturación y brillo dentro de sus rangos).</summary>
	public static bool IsVariationOf(Color original, Color varied)
	{
		const float epsilon = 0.002f;
		float hue = Mathf.Abs(varied.H - original.H);
		hue = Mathf.Min(hue, 1.0f - hue);
		return hue <= HueRange + epsilon
			&& Mathf.Abs(varied.S - original.S) <= original.S * SaturationRange + epsilon
			&& Mathf.Abs(varied.V - original.V) <= original.V * ValueRange + epsilon;
	}

	private static StandardMaterial3D Build(RoomType type, string[] variants, ulong salt, StandardMaterial3D standard, Color color,
		float tileMeters, int gameSeed, Vector2I cell)
	{
		if (type == RoomType.Normal)
		{
			string name = PickVariant(variants, gameSeed, cell, salt == FloorSalt);
			Texture2D texture = name == null ? null : RoomSurfaces.LoadOptional(name);
			if (texture != null)
			{
				return RoomSurfaces.Textured(texture, Colors.White, tileMeters);
			}
		}

		// Sin variante: la textura de siempre si existe; si no, el mismo color con una variación sutil propia de la sala.
		if (standard.AlbedoTexture == null)
		{
			using RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, salt);
			standard.AlbedoColor = Vary(color, random);
		}

		return standard;
	}
}
