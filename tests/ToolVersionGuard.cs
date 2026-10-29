using Godot;

/// <summary>
/// Las herramientas de datos de la IA (DatasetExport, LabelExport, FidelityCheck y DirectorBenchmark) solo existen para
/// las versiones 1 y 2 del generador: el simulador, el bot y las características (LevelMetrics, LevelMetricsV2) no
/// conocen todavía al Huidor ni al Excavador. Mientras no exista la IA v3, rechazan cualquier otra versión con un error
/// claro y salen con código 1, antes de hacer nada. Lo comprueba tests/ToolVersionGuardTest.
/// </summary>
public static class ToolVersionGuard
{
	public const int MaxSupportedVersion = 2;

	/// <summary>
	/// Palabra que aparece siempre en el error y que busca la prueba. Sin acentos a propósito: en Windows, la salida
	/// capturada de otro proceso puede llegar con otra codificación.
	/// </summary>
	public const string RejectionMarker = "soportada";

	public static bool IsSupported(int version)
	{
		return version >= DungeonGenerator.OriginalVersion && version <= MaxSupportedVersion;
	}

	/// <summary>true si la versión vale; si no, escribe el error y devuelve false.</summary>
	public static bool Check(string tool, int version)
	{
		if (IsSupported(version))
		{
			return true;
		}

		GD.PrintErr($"{tool}: la versión {version} del generador no está {RejectionMarker}. El simulador, el bot y las características " +
			$"de la IA solo llegan hasta la v{MaxSupportedVersion}; usa --generator=1 o --generator=2.");
		return false;
	}
}
