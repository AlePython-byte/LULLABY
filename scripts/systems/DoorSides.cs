using Godot;

public static class DoorSides
{
	public static DoorSide Opposite(this DoorSide side)
	{
		return side switch
		{
			DoorSide.North => DoorSide.South,
			DoorSide.East => DoorSide.West,
			DoorSide.South => DoorSide.North,
			_ => DoorSide.East,
		};
	}

	// Norte = y-1, Este = x+1.
	public static Vector2I GridOffset(this DoorSide side)
	{
		return side switch
		{
			DoorSide.North => new Vector2I(0, -1),
			DoorSide.East => new Vector2I(1, 0),
			DoorSide.South => new Vector2I(0, 1),
			_ => new Vector2I(-1, 0),
		};
	}

	// Norte = -Z, Este = +X.
	public static Vector3 Direction(this DoorSide side)
	{
		return side switch
		{
			DoorSide.North => new Vector3(0.0f, 0.0f, -1.0f),
			DoorSide.East => new Vector3(1.0f, 0.0f, 0.0f),
			DoorSide.South => new Vector3(0.0f, 0.0f, 1.0f),
			_ => new Vector3(-1.0f, 0.0f, 0.0f),
		};
	}
}
