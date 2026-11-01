using System.Collections.Generic;
using Godot;

/// <summary>
/// Espiral de The Mother (fase 3): tres brazos de proyectiles que giran durante unos 2 s. Sin nodos, así se puede probar sola.
///
/// Geometría (ángulos en grados alrededor de Y, medidos desde el centro de un hueco): el círculo se reparte en tres sectores de
/// SweepDegrees (45°) donde barre cada brazo y tres huecos de GapDegrees (75°) por los que nunca pasa nada. El primer brazo empieza
/// en el borde de un hueco y el hueco más cercano a Noah queda a GapOffsetDegrees de ella (Noah empieza dentro de un sector, así que
/// hay que moverse, pero su hueco está a unos metros y se ve en el aviso). Cada brazo emite un proyectil cada Interval segundos
/// mientras gira: Emissions proyectiles por brazo, todos a ProjectileSpeed m/s en línea recta desde el jefe, que está quieto.
/// </summary>
public static class MotherSpiral
{
	public const int Arms = 3;
	public const float SweepDegrees = 45.0f;
	public const float Duration = 2.0f;
	public const float Interval = 0.2f;
	public const float ProjectileSpeed = 5.0f;

	/// <summary>Aviso antes de empezar (más de los 0,6 s mínimos del resto de ataques).</summary>
	public const float Warning = 0.8f;

	/// <summary>Cuánto se gira el hueco respecto a la dirección de Noah (con signo alterno): ella queda dentro de un sector, no del hueco.</summary>
	public const float GapOffsetDegrees = 55.0f;

	/// <summary>Reparto entre brazos (120°).</summary>
	public static float ArmSpacingDegrees => 360.0f / Arms;

	/// <summary>Ancho (75°) de cada hueco de escape.</summary>
	public static float GapDegrees => ArmSpacingDegrees - SweepDegrees;

	/// <summary>Velocidad angular de cada brazo (22,5°/s).</summary>
	public static float AngularSpeed => SweepDegrees / Duration;

	/// <summary>Proyectiles por brazo (11): el primero al empezar y el último al terminar.</summary>
	public static int Emissions => Mathf.RoundToInt(Duration / Interval) + 1;

	public static int TotalProjectiles => Arms * Emissions;

	/// <summary>Ángulo (desde el centro del hueco) al que dispara el brazo en esa emisión. reverse = gira en el sentido contrario.</summary>
	public static float OffsetDegrees(int arm, int emission, bool reverse)
	{
		float progress = Emissions > 1 ? (float)emission / (Emissions - 1) : 0.0f;
		float start = GapDegrees * 0.5f + ArmSpacingDegrees * arm;
		return start + SweepDegrees * (reverse ? 1.0f - progress : progress);
	}

	/// <summary>Dirección plana de un disparo: la del centro del hueco girada OffsetDegrees.</summary>
	public static Vector3 Direction(int arm, int emission, Vector3 gapCenter, bool reverse)
	{
		Vector3 center = new(gapCenter.X, 0.0f, gapCenter.Z);
		center = center.LengthSquared() < 0.0001f ? Vector3.Forward : center.Normalized();
		return center.Rotated(Vector3.Up, Mathf.DegToRad(OffsetDegrees(arm, emission, reverse)));
	}

	/// <summary>Todas las direcciones de una emisión (una por brazo).</summary>
	public static List<Vector3> Directions(int emission, Vector3 gapCenter, bool reverse)
	{
		List<Vector3> directions = new();
		for (int arm = 0; arm < Arms; arm++) directions.Add(Direction(arm, emission, gapCenter, reverse));
		return directions;
	}

	/// <summary>Centro del hueco: la dirección hacia Noah girada GapOffsetDegrees por el lado dado (1 o -1).</summary>
	public static Vector3 GapCenter(Vector3 toPlayer, int side) => MotherRing.GapCenter(toPlayer, GapOffsetDegrees * (side >= 0 ? 1.0f : -1.0f));

	/// <summary>True si un ángulo (grados desde el centro de un hueco) cae dentro de alguno de los tres huecos, con un margen.</summary>
	public static bool InGap(float offsetDegrees, float marginDegrees = 0.0f)
	{
		float wrapped = Mathf.PosMod(offsetDegrees + ArmSpacingDegrees * 0.5f, ArmSpacingDegrees) - ArmSpacingDegrees * 0.5f;
		return Mathf.Abs(wrapped) <= GapDegrees * 0.5f - marginDegrees;
	}

	/// <summary>
	/// Segundos que tiene Noah, a `distance` metros del jefe, antes de que el primer proyectil de la espiral llegue hasta ella:
	/// el aviso más lo que tarda en recorrer la distancia desde donde nace (spawnDistance).
	/// </summary>
	public static float TimeToFirstHit(float distance, float spawnDistance) => Warning + Mathf.Max(0.0f, distance - spawnDistance) / ProjectileSpeed;
}
