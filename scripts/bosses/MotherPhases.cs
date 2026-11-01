/// <summary>Fases de The Mother: umbrales de vida y números de cada fase. Sin nodos, así se puede probar sola.</summary>
public static class MotherPhases
{
	public const float SecondPhaseFraction = 0.66f;
	public const float ThirdPhaseFraction = 0.33f;

	/// <summary>Lo que cambia de una fase a otra. La fase 1 conserva los valores exportados de MotherBehavior.</summary>
	public readonly record struct Settings(float ShotInterval, float OrbitTime, int DashCount, float DashWarning,
		float ChainDashWarning, float RecoveryTime, int RingProjectiles, float RingGapDegrees, float RingGapOffsetDegrees);

	private static readonly Settings First = new(1.25f, 0.0f, 1, 1.0f, 1.0f, 1.5f, 0, 0.0f, 0.0f);
	private static readonly Settings Second = new(1.25f, 1.6f, 1, 1.0f, 1.0f, 1.5f, 10, 50.0f, 0.0f);
	private static readonly Settings Third = new(1.0f, 1.0f, 2, 0.7f, 0.6f, 0.9f, 12, 44.0f, 25.0f);

	public static Settings For(int phase) => phase >= 3 ? Third : phase == 2 ? Second : First;

	/// <summary>Fase (1 a 3) que corresponde a esa vida: 2 a partir del 66 % o menos y 3 a partir del 33 % o menos.</summary>
	public static int PhaseFor(float current, float maximum)
	{
		if (maximum <= 0.0f) return 1;
		if (current <= maximum * ThirdPhaseFraction) return 3;
		if (current <= maximum * SecondPhaseFraction) return 2;
		return 1;
	}
}
