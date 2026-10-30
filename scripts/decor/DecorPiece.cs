using Godot;

/// <summary>
/// Una pieza de ambientación ya colocada: modelo (id de assets/models/props/), posición del centro de su base en coordenadas
/// de la sala (X, Z; la base apoyada en el suelo) y giro en Y. Las piezas no tienen colisión.
/// </summary>
public readonly record struct DecorPiece(string Model, Vector3 Position, float Yaw)
{
	public string Path => PropModels.Path(Model);
}

/// <summary>Ids de las piezas de assets/models/props/.</summary>
public static class PropModels
{
	public const string Shelf = "estanteria_gigante";
	public const string Curtain = "cortina_ventana";
	public const string Rug = "alfombra";
	public const string Pedestal = "pedestal_tesoro";
	public const string Blocks = "bloques_letras";
	public const string Ball = "pelota_colores";
	public const string Drum = "tambor";
	public const string Xylophone = "xilofono";
	public const string Lamp = "lampara_velador";
	public const string Chest = "baul_juguetes";
	public const string Train = "tren_juguete";
	public const string Castle = "castillo_bloques";
	public const string Rocker = "mecedora_gigante";
	public const string GiantCrib = "cuna_gigante";

	public static string Path(string id) => $"res://assets/models/props/{id}.glb";
}
