using System;
using System.Collections.Generic;

/// <summary>
/// Los conjuntos de características versionados, por número de versión: 1 = LevelMetrics (generador v1), 2 =
/// LevelMetricsV2 (generador v2, tipos de enemigo). Lo usan el modelo de dificultad (para validar su JSON), el director
/// (para calcular las características que pide el modelo cargado) y las herramientas de datasets.
/// </summary>
public static class LevelFeatureSet
{
	public static readonly IReadOnlyList<int> Versions = new[] { LevelMetrics.Version, LevelMetricsV2.Version };

	public static bool IsKnown(int version)
	{
		return version == LevelMetrics.Version || version == LevelMetricsV2.Version;
	}

	public static string[] Names(int version)
	{
		return version switch
		{
			LevelMetrics.Version => LevelMetrics.Names,
			LevelMetricsV2.Version => LevelMetricsV2.Names,
			_ => throw new ArgumentOutOfRangeException(nameof(version), $"versión de características desconocida: {version}"),
		};
	}

	public static double[] Compute(int version, LevelMap map)
	{
		return version switch
		{
			LevelMetrics.Version => LevelMetrics.Compute(map),
			LevelMetricsV2.Version => LevelMetricsV2.Compute(map),
			_ => throw new ArgumentOutOfRangeException(nameof(version), $"versión de características desconocida: {version}"),
		};
	}
}
