using Godot;

/// <summary>
/// Plantilla de sala: tamaño y puntos de aparición. El generador la copia en un RoomData.
///
/// Reglas que comprueba el LevelValidator para cada punto de aparición (X, Z relativo al centro):
///   - Dentro del interior, a RoomRules.SpawnWallMargin (1.5) o más de cada pared.
///   - A RoomRules.MinSpawnDistanceFromArrival (6) o más de los cuatro posibles puntos de llegada del
///     jugador: el centro de cada lado, a RoomRules.ArrivalOffset (2.5) de la pared. Se comprueban los
///     cuatro aunque la sala acabe sin puerta en algún lado. Así ningún enemigo aparece encima del jugador.
/// Con esas reglas, una sala cuadrada necesita unos 15×15 para admitir enemigos en las esquinas
/// y 17×17 para admitir uno en el centro.
/// </summary>
[GlobalClass]
public partial class RoomTemplate : Resource
{
	[Export] public string Id { get; set; } = "";

	/// <summary>Interior jugable en metros (X, Z).</summary>
	[Export] public Vector2 Size { get; set; } = new Vector2(20.0f, 20.0f);

	[Export] public PackedScene EnemyScene { get; set; }

	/// <summary>Posiciones (X, Z) relativas al centro de la sala.</summary>
	[Export] public Vector2[] SpawnPoints { get; set; } = System.Array.Empty<Vector2>();

	/// <summary>
	/// Tipo de enemigo de cada punto de aparición (valores de EnemyKind: 0 Básico, 1 Tirador, 2 Explosivo, 3 Volador),
	/// en el mismo orden que SpawnPoints. Vacío = todos son EnemyScene. Si no está vacío debe tener la misma longitud.
	/// </summary>
	[Export] public int[] SpawnKinds { get; set; } = System.Array.Empty<int>();
}
