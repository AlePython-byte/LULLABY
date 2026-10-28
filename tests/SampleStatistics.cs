using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Estadística descriptiva y correlaciones para las herramientas de datos (solo desarrollo).</summary>
public static class SampleStatistics
{
	public static double Mean(IReadOnlyList<double> values)
	{
		return values.Count == 0 ? 0.0 : values.Sum() / values.Count;
	}

	/// <summary>Desviación típica poblacional (divide entre n).</summary>
	public static double PopulationStd(IReadOnlyList<double> values)
	{
		if (values.Count == 0)
		{
			return 0.0;
		}

		double mean = Mean(values);
		return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Count);
	}

	/// <summary>Desviación típica muestral (divide entre n - 1); 0 con menos de dos valores.</summary>
	public static double SampleStd(IReadOnlyList<double> values)
	{
		if (values.Count < 2)
		{
			return 0.0;
		}

		double mean = Mean(values);
		return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1));
	}

	/// <summary>Percentil con interpolación lineal (p entre 0 y 1).</summary>
	public static double Percentile(IReadOnlyList<double> values, double p)
	{
		double[] sorted = values.OrderBy(value => value).ToArray();
		if (sorted.Length == 0)
		{
			return 0.0;
		}

		double position = p * (sorted.Length - 1);
		int lower = (int)Math.Floor(position);
		int upper = Math.Min(lower + 1, sorted.Length - 1);
		return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
	}

	/// <summary>Correlación de Pearson; NaN si alguna serie es constante.</summary>
	public static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
	{
		double meanX = Mean(x);
		double meanY = Mean(y);
		double covariance = 0.0;
		double varianceX = 0.0;
		double varianceY = 0.0;
		for (int i = 0; i < x.Count; i++)
		{
			covariance += (x[i] - meanX) * (y[i] - meanY);
			varianceX += (x[i] - meanX) * (x[i] - meanX);
			varianceY += (y[i] - meanY) * (y[i] - meanY);
		}

		return varianceX <= 0.0 || varianceY <= 0.0 ? double.NaN : covariance / Math.Sqrt(varianceX * varianceY);
	}

	/// <summary>Correlación de Spearman (Pearson sobre rangos; los empates reciben el rango medio).</summary>
	public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
	{
		return Pearson(Ranks(x), Ranks(y));
	}

	private static double[] Ranks(IReadOnlyList<double> values)
	{
		int[] order = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToArray();
		double[] ranks = new double[values.Count];
		int start = 0;
		while (start < order.Length)
		{
			int end = start;
			while (end + 1 < order.Length && values[order[end + 1]] == values[order[start]])
			{
				end++;
			}

			double rank = (start + end) / 2.0 + 1.0;
			for (int i = start; i <= end; i++)
			{
				ranks[order[i]] = rank;
			}

			start = end + 1;
		}

		return ranks;
	}
}
