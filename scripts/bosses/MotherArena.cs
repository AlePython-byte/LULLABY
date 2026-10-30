using Godot;

/// <summary>Límite del centro del jefe: conserva la trayectoria recta y un pasillo junto a todas las paredes.</summary>
public static class MotherArena
{
	public static Vector3 LimitVelocity(Vector3 position, Vector3 velocity, Vector2 roomSize, float margin, float delta)
	{
		if (delta <= 0.0f) return Vector3.Zero;
		Vector2 limit = new(Mathf.Max(0.0f, roomSize.X * 0.5f - margin),
			Mathf.Max(0.0f, roomSize.Y * 0.5f - margin));
		float fraction = 1.0f;
		fraction = Mathf.Min(fraction, AxisFraction(position.X, velocity.X * delta, limit.X));
		fraction = Mathf.Min(fraction, AxisFraction(position.Z, velocity.Z * delta, limit.Y));
		return velocity * fraction;
	}

	private static float AxisFraction(float position, float displacement, float limit)
	{
		if (displacement > 0.0f) return Mathf.Clamp((limit - position) / displacement, 0.0f, 1.0f);
		if (displacement < 0.0f) return Mathf.Clamp((-limit - position) / displacement, 0.0f, 1.0f);
		return 1.0f;
	}
}
