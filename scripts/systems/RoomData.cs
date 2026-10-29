using Godot;

public enum RoomType
{
	Normal,
	Start,
	Boss,
	Treasure,
}

/// <summary>
/// Datos de una sala del mapa. Las puertas no se guardan aquí: salen de las salas vecinas del LevelMap.
/// </summary>
[GlobalClass]
public partial class RoomData : Resource
{
	/// <summary>Celda en la cuadrícula (x, y). Norte = y-1, Este = x+1.</summary>
	[Export] public Vector2I Cell { get; set; }

	[Export] public RoomType Type { get; set; } = RoomType.Normal;

	/// <summary>Id de la plantilla de la que salió (vacío si la sala se escribió a mano).</summary>
	[Export] public string TemplateId { get; set; } = "";

	/// <summary>Interior jugable en metros (X, Z).</summary>
	[Export] public Vector2 Size { get; set; } = new Vector2(20.0f, 20.0f);

	/// <summary>Enemigo que aparece en cada punto de aparición. Sin escena, la sala nace despejada.</summary>
	[Export] public PackedScene EnemyScene { get; set; }

	/// <summary>Posiciones (X, Z) relativas al centro de la sala.</summary>
	[Export] public Vector2[] SpawnPoints { get; set; } = System.Array.Empty<Vector2>();

	/// <summary>
	/// Tipo de enemigo (valor de EnemyKind) de cada punto de aparición, en el mismo orden que SpawnPoints. Vacío = todos
	/// salen de EnemyScene (así son todas las salas de la versión 1 del generador).
	/// </summary>
	[Export] public int[] SpawnKinds { get; set; } = System.Array.Empty<int>();
}
