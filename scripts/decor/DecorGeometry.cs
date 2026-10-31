using Godot;

/// <summary>Huellas de las piezas de ambientación sobre el suelo, para colocarlas sin solapes y para las pruebas.</summary>
public static class DecorGeometry
{
	/// <summary>
	/// Caja (en coordenadas de la sala) de una pieza ya girada: el modelo está centrado en XZ y apoyado en y = 0, así que
	/// el espejado no la cambia. Es la caja que contiene al rectángulo girado, es decir, conservadora.
	/// </summary>
	public static Aabb Footprint(DecorPiece piece)
	{
		Aabb box = ModelLibrary.BoundsOf(piece.Path).Value;
		Transform3D transform = new(Basis.FromEuler(new Vector3(0.0f, piece.Yaw, 0.0f)), piece.Position);
		Aabb local = new(new Vector3(-box.Size.X / 2.0f, 0.0f, -box.Size.Z / 2.0f), box.Size);
		return transform * local;
	}

	/// <summary>Las huellas (en el plano del suelo) están a menos de gap metros o se solapan.</summary>
	public static bool Overlaps(Aabb a, Aabb b, float gap)
	{
		return a.Position.X < b.End.X + gap && a.End.X > b.Position.X - gap
			&& a.Position.Z < b.End.Z + gap && a.End.Z > b.Position.Z - gap;
	}

	/// <summary>Giro (Y) que deja el frente del modelo (-Z) mirando hacia dentro de la sala desde la pared dada.</summary>
	public static float FacingYaw(DoorSide wall) => wall switch
	{
		DoorSide.North => Mathf.Pi,
		DoorSide.East => Mathf.Pi / 2.0f,
		DoorSide.West => -Mathf.Pi / 2.0f,
		_ => 0.0f,
	};

	/// <summary>Normal de la pared hacia el interior de la sala.</summary>
	public static Vector3 Inward(DoorSide wall) => wall switch
	{
		DoorSide.North => Vector3.Back,
		DoorSide.South => Vector3.Forward,
		DoorSide.East => Vector3.Left,
		_ => Vector3.Right,
	};

	/// <summary>Punto de la cara interior de la pared dada, a la distancia along (X en norte y sur, Z en este y oeste) de su centro.</summary>
	public static Vector3 WallPoint(DoorSide wall, float along, float hx, float hz) => wall switch
	{
		DoorSide.North => new Vector3(along, 0.0f, -hz),
		DoorSide.South => new Vector3(along, 0.0f, hz),
		DoorSide.East => new Vector3(hx, 0.0f, along),
		_ => new Vector3(-hx, 0.0f, along),
	};
}
