using System.Collections.Generic;
using Godot;

/// <summary>
/// Geometría de una sala, en coordenadas locales (centro del suelo en el origen, Y hacia arriba): suelo, paredes
/// con un hueco en cada puerta conectada, puertas, disparadores de puerta y área de entrada.
/// RoomController construye los nodos a partir de aquí; cualquier otra herramienta que necesite la misma geometría
/// debe usar también esta clase para no desincronizarse del juego.
/// </summary>
public sealed class RoomLayout
{
	public readonly record struct Box(string Name, Vector3 Center, Vector3 Size);

	public readonly record struct DoorLayout(DoorSide Side, Box Door, Box Trigger);

	public Box Floor { get; private set; }
	public List<Box> Walls { get; } = new();
	public List<DoorLayout> Doors { get; } = new();
	public Box EntryArea { get; private set; }

	public static RoomLayout Build(Vector2 roomSize, IEnumerable<DoorSide> connectedDoors, float wallHeight,
		float wallThickness, float doorWidth, float entryMargin)
	{
		HashSet<DoorSide> connected = new(connectedDoors);
		RoomLayout layout = new();

		float t = wallThickness;
		float hx = roomSize.X / 2.0f;
		float hz = roomSize.Y / 2.0f;

		layout.Floor = new Box("Floor", new Vector3(0.0f, -0.5f, 0.0f),
			new Vector3(roomSize.X + t * 2.0f, 1.0f, roomSize.Y + t * 2.0f));

		foreach (DoorSide side in System.Enum.GetValues<DoorSide>())
		{
			// Norte y Sur recorren el eje X e incluyen las esquinas; Este y Oeste cubren solo el tramo interior.
			bool alongX = side == DoorSide.North || side == DoorSide.South;
			float sign = side == DoorSide.North || side == DoorSide.West ? -1.0f : 1.0f;
			float half = alongX ? hx + t : hz;
			float fixedCoord = sign * ((alongX ? hz : hx) + t / 2.0f);
			float doorHalf = doorWidth / 2.0f;

			if (!connected.Contains(side))
			{
				layout.Walls.Add(Segment(side, "", -half, half, fixedCoord, alongX, wallHeight, t));
				continue;
			}

			layout.Walls.Add(Segment(side, "A", -half, -doorHalf, fixedCoord, alongX, wallHeight, t));
			layout.Walls.Add(Segment(side, "B", doorHalf, half, fixedCoord, alongX, wallHeight, t));

			Vector3 doorCenter = alongX
				? new Vector3(0.0f, wallHeight / 2.0f, fixedCoord)
				: new Vector3(fixedCoord, wallHeight / 2.0f, 0.0f);
			Vector3 doorSize = alongX
				? new Vector3(doorWidth, wallHeight, t)
				: new Vector3(t, wallHeight, doorWidth);

			// Disparador dentro del grosor de la pared: el jugador solo lo alcanza si la puerta está abierta.
			Vector3 triggerSize = alongX
				? new Vector3(doorWidth, 2.0f, t)
				: new Vector3(t, 2.0f, doorWidth);

			layout.Doors.Add(new DoorLayout(side,
				new Box($"Door{side}", doorCenter, doorSize),
				new Box($"DoorTrigger{side}", doorCenter with { Y = 1.0f }, triggerSize)));
		}

		float entryX = Mathf.Max(0.5f, roomSize.X - 2.0f * entryMargin);
		float entryZ = Mathf.Max(0.5f, roomSize.Y - 2.0f * entryMargin);
		layout.EntryArea = new Box("EntryArea", new Vector3(0.0f, 1.0f, 0.0f), new Vector3(entryX, 2.0f, entryZ));

		return layout;
	}

	private static Box Segment(DoorSide side, string suffix, float from, float to, float fixedCoord, bool alongX,
		float wallHeight, float wallThickness)
	{
		float length = to - from;
		float center = (from + to) / 2.0f;
		Vector3 size = alongX
			? new Vector3(length, wallHeight, wallThickness)
			: new Vector3(wallThickness, wallHeight, length);
		Vector3 position = alongX
			? new Vector3(center, wallHeight / 2.0f, fixedCoord)
			: new Vector3(fixedCoord, wallHeight / 2.0f, center);

		return new Box($"Wall{side}{suffix}", position, size);
	}
}
