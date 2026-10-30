using Godot;

/// <summary>
/// Materiales de suelo y paredes de la sala decorada. Las texturas son OPCIONALES (las pone una persona del equipo en
/// assets/textures/): se cargan solo por código con ResourceLoader.Exists, así ninguna escena depende de ellas y, si no están,
/// la sala usa colores pastel apagados sin fallar ni romper el export.
///
/// Reparto: parquet = suelo de salas normales y de inicio, papel_tapiz = paredes, alfombra = suelo del tesoro y
/// madera_cuna = suelo del jefe. Se aplican en triplanar de espacio mundo (sin depender de las UV de las cajas): una repetición
/// cada tileMeters metros.
/// </summary>
public static class RoomSurfaces
{
	/// <summary>Carpeta de las texturas opcionales (las pruebas la apuntan a tests/fixtures/textures/).</summary>
	public static string TextureFolder { get; set; } = "res://assets/textures/";
	public const string Parquet = "parquet";
	public const string Wallpaper = "papel_tapiz";
	public const string Rug = "alfombra";
	public const string CribWood = "madera_cuna";

	/// <summary>Material de suelo para un tipo de sala (la textura de su tipo si existe, si no el color).</summary>
	public static StandardMaterial3D Floor(RoomType type, Color color, float tileMeters)
	{
		string texture = type switch
		{
			RoomType.Treasure => Rug,
			RoomType.Boss => CribWood,
			_ => Parquet,
		};
		return Surface(texture, color, tileMeters);
	}

	/// <summary>Material de las paredes.</summary>
	public static StandardMaterial3D Wall(Color color, float tileMeters) => Surface(Wallpaper, color, tileMeters);

	/// <summary>Ruta de una textura opcional por su nombre (sin extensión).</summary>
	public static string PathOf(string name) => $"{TextureFolder}{name}.png";

	/// <summary>Textura opcional; null si el archivo no existe o no se puede cargar.</summary>
	public static Texture2D LoadOptional(string name)
	{
		string path = PathOf(name);
		return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
	}

	private static StandardMaterial3D Surface(string textureName, Color color, float tileMeters)
	{
		StandardMaterial3D material = new() { AlbedoColor = color };
		Texture2D texture = LoadOptional(textureName);
		if (texture == null)
		{
			return material;
		}

		float scale = 1.0f / Mathf.Max(0.1f, tileMeters);
		material.AlbedoTexture = texture;
		material.AlbedoColor = Colors.White;
		material.Uv1Triplanar = true;
		material.Uv1WorldTriplanar = true;
		material.Uv1Scale = new Vector3(scale, scale, scale);
		return material;
	}
}
