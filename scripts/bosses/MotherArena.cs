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

	/// <summary>
	/// Velocidad para recorrer en arco una circunferencia de `radius` metros alrededor del centro de la sala: tangente a `speed`
	/// (direction 1 o -1 elige el sentido) más una corrección radial hacia esa circunferencia, de modo que desde cualquier punto
	/// converge a ella. Nunca supera 1.5 × speed.
	/// </summary>
	public static Vector3 OrbitVelocity(Vector3 position, float radius, float speed, int direction)
	{
		Vector3 flat = new(position.X, 0.0f, position.Z);
		float distance = flat.Length();
		Vector3 outward = distance > 0.001f ? flat / distance : Vector3.Right;
		Vector3 tangent = new Vector3(-outward.Z, 0.0f, outward.X) * (direction >= 0 ? 1.0f : -1.0f);
		float radial = Mathf.Clamp((radius - distance) * 2.0f, -speed, speed);
		return (tangent * speed + outward * radial).LimitLength(speed * 1.5f);
	}

	private static float AxisFraction(float position, float displacement, float limit)
	{
		if (displacement > 0.0f) return Mathf.Clamp((limit - position) / displacement, 0.0f, 1.0f);
		if (displacement < 0.0f) return Mathf.Clamp((-limit - position) / displacement, 0.0f, 1.0f);
		return 1.0f;
	}
}
