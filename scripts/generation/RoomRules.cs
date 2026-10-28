using Godot;

/// <summary>
/// Reglas de colocación compartidas por el generador, el validador y RoomManager.
/// </summary>
public static class RoomRules
{
	/// <summary>Distancia del jugador a la pared interior al llegar a una sala.</summary>
	public const float ArrivalOffset = 2.5f;

	/// <summary>Separación mínima entre un punto de aparición y las paredes.</summary>
	public const float SpawnWallMargin = 1.5f;

	/// <summary>Distancia mínima entre un punto de aparición y cualquier punto de llegada del jugador.</summary>
	public const float MinSpawnDistanceFromArrival = 6.0f;

	/// <summary>
	/// Los cuatro posibles puntos de llegada (X, Z): centro de cada lado, a ArrivalOffset de la pared.
	/// Se comprueban los cuatro aunque la sala no tenga puerta en ese lado: la plantilla no sabe dónde acabará.
	/// </summary>
	public static Vector2[] ArrivalPoints(Vector2 size)
	{
		float x = size.X / 2.0f - ArrivalOffset;
		float z = size.Y / 2.0f - ArrivalOffset;
		return new[] { new Vector2(0.0f, -z), new Vector2(x, 0.0f), new Vector2(0.0f, z), new Vector2(-x, 0.0f) };
	}
}
