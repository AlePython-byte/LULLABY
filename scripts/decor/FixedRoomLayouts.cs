using System.Collections.Generic;
using Godot;

/// <summary>
/// Disposición FIJA de las piezas exentas por tipo de sala (inicio, tesoro y jefe), sin azar: la misma en cada partida. Sin
/// colisión. Las posiciones se dan como separación (m) hacia dentro desde las paredes, así que se adaptan al tamaño de la sala.
///
/// Reglas que cumplen todas:
///   - nada en la mitad sur (la cámara no la ve) ni en los puntos de llegada del jugador (centro de cada lado, a 2,5 m de la pared);
///   - nada en el área central: la cuadrícula de objetos del tesoro (4 m de lado) y el centro del jefe, que Noah y The Mother
///     recorren (el centro de The Mother queda a 3 m de las paredes; su cuerpo llega a ~1,6 m de ellas).
///   - mecedora_gigante y cuna_gigante solo en las esquinas norte del jefe, con su lado largo paralelo a la pared. La cuna
///     (2,5 m de fondo) invade ~0,9 m el pasillo del jefe: solo se solapa visualmente en los extremos de sus embestidas.
/// </summary>
public static class FixedRoomLayouts
{
	public static List<DecorPiece> For(RoomType type, Vector2 size)
	{
		float hx = size.X / 2.0f;
		float hz = size.Y / 2.0f;
		List<DecorPiece> pieces = new();

		// Esquinas y lados: dx = hacia dentro desde la pared este/oeste; dz = hacia dentro desde la pared norte.
		void NW(string model, float dx, float dz, float yawDegrees) => Add(model, -hx + dx, -hz + dz, yawDegrees);
		void NE(string model, float dx, float dz, float yawDegrees) => Add(model, hx - dx, -hz + dz, yawDegrees);
		void West(string model, float dx, float z, float yawDegrees) => Add(model, -hx + dx, z, yawDegrees);
		void East(string model, float dx, float z, float yawDegrees) => Add(model, hx - dx, z, yawDegrees);
		void North(string model, float x, float dz, float yawDegrees) => Add(model, x, -hz + dz, yawDegrees);
		void Add(string model, float x, float z, float yawDegrees) =>
			pieces.Add(new DecorPiece(model, new Vector3(x, 0.0f, z), Mathf.DegToRad(yawDegrees)));

		// Frente hacia el interior: norte 180°, este +90°, oeste -90°.
		switch (type)
		{
			case RoomType.Start:
				NW(PropModels.Blocks, 2.4f, 1.8f, 200.0f);
				NW(PropModels.Ball, 1.3f, 4.8f, 0.0f);
				NE(PropModels.Lamp, 1.2f, 1.2f, 0.0f);
				NE(PropModels.Xylophone, 2.8f, 2.2f, 250.0f);
				North(PropModels.Castle, -4.6f, 1.3f, 180.0f);
				West(PropModels.Chest, 1.8f, 3.4f, 0.0f);
				West(PropModels.Drum, 1.5f, -3.2f, 0.0f);
				East(PropModels.Train, 1.2f, 3.6f, 90.0f);
				break;
			case RoomType.Treasure:
				NW(PropModels.Chest, 1.8f, 1.6f, 160.0f);
				NE(PropModels.Lamp, 1.1f, 1.1f, 0.0f);
				NE(PropModels.Ball, 1.5f, 3.6f, 0.0f);
				West(PropModels.Blocks, 1.4f, 3.4f, -90.0f);
				East(PropModels.Drum, 1.0f, 3.8f, 0.0f);
				North(PropModels.Xylophone, -2.6f, 1.2f, 180.0f);
				break;
			case RoomType.Boss:
				NW(PropModels.GiantCrib, 2.2f, 1.4f, -90.0f);
				NE(PropModels.Rocker, 2.0f, 0.9f, 90.0f);
				North(PropModels.Castle, -5.0f, 0.9f, 180.0f);
				North(PropModels.Drum, 5.2f, 1.0f, 0.0f);
				West(PropModels.Xylophone, 0.9f, -5.5f, -90.0f);
				West(PropModels.Train, 0.8f, 5.0f, -90.0f);
				West(PropModels.Ball, 1.0f, 8.5f, 0.0f);
				East(PropModels.Lamp, 1.0f, -6.5f, 0.0f);
				East(PropModels.Train, 0.8f, 5.5f, 90.0f);
				East(PropModels.Blocks, 1.0f, 8.0f, 90.0f);
				break;
		}

		return pieces;
	}
}
