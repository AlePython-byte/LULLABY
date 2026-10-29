using System.Collections.Generic;
using Godot;

/// <summary>
/// Clasifica las salas con enemigos por su composición, para los desgloses de la prueba de fidelidad y de las etiquetas:
///   solo_basicos    todos sus enemigos son básicos;
///   con_tirador     tiene al menos un tirador;
///   con_explosivo   tiene al menos un explosivo;
///   con_volador     tiene al menos un volador.
/// Una sala mixta cuenta en todas las categorías "con_" que le correspondan. Las salas sin enemigos no cuentan.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public static class RoomComposition
{
	public const string OnlyBasic = "solo_basicos";
	public const string WithShooter = "con_tirador";
	public const string WithExploder = "con_explosivo";
	public const string WithFlyer = "con_volador";

	public static readonly string[] Categories = { OnlyBasic, WithShooter, WithExploder, WithFlyer };

	public static List<string> Of(SimLevel.Room room)
	{
		List<string> categories = new();
		if (room.StartsCleared)
		{
			return categories;
		}

		HashSet<EnemyKind> kinds = new();
		foreach (GameplayParameters.EnemyStats stats in room.SpawnStats)
		{
			kinds.Add(stats.Kind);
		}

		if (kinds.Count == 1 && kinds.Contains(EnemyKind.Chaser))
		{
			categories.Add(OnlyBasic);
		}

		if (kinds.Contains(EnemyKind.Shooter))
		{
			categories.Add(WithShooter);
		}

		if (kinds.Contains(EnemyKind.Exploder))
		{
			categories.Add(WithExploder);
		}

		if (kinds.Contains(EnemyKind.Flyer))
		{
			categories.Add(WithFlyer);
		}

		return categories;
	}

	/// <summary>Daño medio y duración media del combate (despejar − entrar) de la primera visita, por categoría.</summary>
	public static SortedDictionary<string, (int Rooms, double Damage, double Time)> Summarize(
		IEnumerable<(SimLevel Level, SimulationResult Result)> runs)
	{
		Dictionary<string, (int Count, double Damage, double Time)> sums = new();
		foreach ((SimLevel level, SimulationResult result) in runs)
		{
			foreach (RoomVisit visit in result.Visits)
			{
				if (visit.ClearTime < 0.0f)
				{
					continue;
				}

				foreach (string category in Of(level[visit.Cell]))
				{
					(int count, double damage, double time) = sums.GetValueOrDefault(category);
					sums[category] = (count + 1, damage + visit.Damage, time + visit.ClearTime - visit.EnterTime);
				}
			}
		}

		SortedDictionary<string, (int, double, double)> summary = new();
		foreach (string category in Categories)
		{
			if (sums.TryGetValue(category, out var sum))
			{
				summary[category] = (sum.Count, sum.Damage / sum.Count, sum.Time / sum.Count);
			}
		}

		return summary;
	}
}
