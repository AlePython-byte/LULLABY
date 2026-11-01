using System.Collections.Generic;
using Godot;

/// <summary>Anillo de proyectiles de The Mother: direcciones repartidas por toda la circunferencia salvo un hueco para escapar.</summary>
public static class MotherRing
{
	/// <summary>
	/// `count` direcciones en el plano XZ repartidas por igual fuera del hueco (de gapDegrees grados, centrado en gapCenter).
	/// Las dos de los extremos quedan a gapDegrees/2 del centro del hueco.
	/// </summary>
	public static List<Vector3> Directions(int count, Vector3 gapCenter, float gapDegrees)
	{
		List<Vector3> directions = new();
		if (count <= 0) return directions;
		Vector3 center = Flat(gapCenter);
		if (center.LengthSquared() < 0.0001f) center = Vector3.Forward;
		center = center.Normalized();
		float gap = Mathf.Clamp(gapDegrees, 0.0f, 340.0f);
		if (count == 1)
		{
			directions.Add(center.Rotated(Vector3.Up, Mathf.DegToRad(180.0f)));
			return directions;
		}

		float step = (360.0f - gap) / (count - 1);
		for (int i = 0; i < count; i++)
		{
			directions.Add(center.Rotated(Vector3.Up, Mathf.DegToRad(gap * 0.5f + step * i)));
		}

		return directions;
	}

	/// <summary>Centro del hueco: la dirección hacia Noah girada offsetDegrees grados.</summary>
	public static Vector3 GapCenter(Vector3 toPlayer, float offsetDegrees)
	{
		Vector3 flat = Flat(toPlayer);
		if (flat.LengthSquared() < 0.0001f) flat = Vector3.Forward;
		return flat.Normalized().Rotated(Vector3.Up, Mathf.DegToRad(offsetDegrees));
	}

	/// <summary>True si el proyectil nacería a menos de safeDistance (plano) del objetivo: ese no se dispara.</summary>
	public static bool SpawnsOnTarget(Vector3 origin, Vector3 direction, float spawnDistance, Vector3 target, float safeDistance)
	{
		Vector3 spawn = origin + Flat(direction).Normalized() * spawnDistance;
		return Flat(spawn - target).Length() < safeDistance;
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0.0f, v.Z);
}
