using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// Director de IA: dada una semilla de partida y una dificultad objetivo, genera varios niveles candidatos, predice la
/// dificultad de cada uno con el DifficultyModel y elige uno.
///
/// Algoritmo (todo determinista dada la semilla de partida):
///   1. Genera CandidateCount candidatos (30) con el DungeonGenerator. La semilla del candidato i sale de la semilla de
///      partida con un hash propio (CandidateSeed), que no coincide con las semillas de reintento del generador.
///   2. Calcula las características que pide el modelo (LevelMetrics para el modelo v1, LevelMetricsV2 para el v2) y
///      predice damage_total_mean.
///   3. Ordena los candidatos por |predicción - dificultad objetivo| (empates por índice) y elige AL AZAR uno de los
///      TopCount (5) más cercanos. El azar sale de un RandomNumberGenerator sembrado desde la misma semilla de partida:
///      la misma semilla de partida y la misma dificultad dan siempre el mismo nivel.
///      Con PreferEnemyVariety, solo entre esos mismos candidatos se priorizan los tipos de enemigo distintos;
///      los empates se resuelven por distancia al objetivo y después por índice, sin azar adicional.
/// Devuelve el LevelMap elegido y un resumen (semillas, predicciones y cuál se eligió).
///
/// El director solo depende del generador, de LevelMetrics y del modelo: no usa el simulador ni el bot.
/// </summary>
public sealed class AIDungeonDirector
{
	public sealed class Candidate
	{
		public int Index { get; init; }

		/// <summary>Semilla derivada que se pidió al generador.</summary>
		public int Seed { get; init; }

		/// <summary>Semilla con la que se reproduce exactamente el nivel (tras los reintentos del generador).</summary>
		public int EffectiveSeed { get; init; }

		public LevelMap Map { get; init; }

		/// <summary>damage_total_mean predicho por el modelo.</summary>
		public double Predicted { get; init; }

		/// <summary>Tipos presentes realmente en los puntos de aparición del nivel, sin modificar sus datos.</summary>
		public int EnemyKindCount
		{
			get
			{
				HashSet<EnemyKind> kinds = new();
				if (Map != null)
				{
					foreach (RoomData room in Map.Rooms)
					{
						if (room.EnemyScene == null) continue;
						for (int spawn = 0; spawn < room.SpawnPoints.Length; spawn++)
							kinds.Add(LevelMetricsV2.KindAt(room, spawn));
					}
				}
				return kinds.Count;
			}
		}
	}

	public sealed class Result
	{
		public int GameSeed { get; init; }
		public DifficultyTarget Target { get; init; }
		public double TargetValue { get; init; }
		public int TopCount { get; init; }

		/// <summary>Candidatos en el orden en que se generaron.</summary>
		public IReadOnlyList<Candidate> Candidates { get; init; }

		/// <summary>Candidatos de más cerca a más lejos del objetivo.</summary>
		public IReadOnlyList<Candidate> Ranking { get; init; }

		/// <summary>El candidato con la predicción más cercana al objetivo (lo que elegiría "siempre el mejor").</summary>
		public Candidate Best => Ranking.Count > 0 ? Ranking[0] : null;

		public Candidate Chosen { get; init; }

		public bool Success => Chosen != null;
		public LevelMap Map => Chosen?.Map;

		/// <summary>Posición del elegido en el ranking (0 = el mejor).</summary>
		public int ChosenRank => Chosen == null ? -1 : Ranking.ToList().IndexOf(Chosen);

		public string Describe(bool all = false)
		{
			if (!Success)
			{
				return $"Director: sin candidatos válidos para la semilla {GameSeed}.";
			}

			StringBuilder text = new();
			text.Append($"Director: semilla {GameSeed}, objetivo {Target} ({TargetValue:0.0}), {Candidates.Count} candidatos; ");
			text.Append($"elegido el #{ChosenRank + 1} de {Math.Min(TopCount, Ranking.Count)} mejores: nivel {Chosen.EffectiveSeed}, ");
			text.Append($"predicción {Chosen.Predicted:0.0}, tipos de enemigo {Chosen.EnemyKindCount}");

			IEnumerable<Candidate> shown = all ? Ranking : Ranking.Take(TopCount);
			foreach (Candidate candidate in shown)
			{
				text.Append($"\n  {(candidate == Chosen ? "*" : " ")} semilla {candidate.EffectiveSeed,10}  predicción {candidate.Predicted,6:0.0}  " +
					$"distancia {Math.Abs(candidate.Predicted - TargetValue),5:0.0}  salas {candidate.Map.Rooms.Count}");
			}

			return text.ToString();
		}
	}

	private readonly DungeonGenerator _generator;
	private readonly DifficultyModel _model;
	private readonly DungeonGenerator.Settings _settings;

	public int CandidateCount { get; set; } = 30;
	public int TopCount { get; set; } = 5;

	/// <summary>
	/// Prioriza la variedad solo dentro de los TopCount candidatos más cercanos. Desactivado por defecto para conservar
	/// la selección histórica de pruebas y benchmarks; no cambia el generador, las características ni el modelo.
	/// </summary>
	public bool PreferEnemyVariety { get; set; } = false;

	public AIDungeonDirector(DungeonGenerator generator, DifficultyModel model, DungeonGenerator.Settings settings)
	{
		_generator = generator;
		_model = model;
		_settings = settings;
	}

	public Result Select(int gameSeed, DifficultyTarget target)
	{
		return Choose(gameSeed, target, GenerateCandidates(gameSeed));
	}

	/// <summary>Los candidatos dependen solo de la semilla de partida (no de la dificultad objetivo).</summary>
	public List<Candidate> GenerateCandidates(int gameSeed)
	{
		List<Candidate> candidates = new();
		for (int index = 0; index < CandidateCount; index++)
		{
			int seed = CandidateSeed(gameSeed, index);
			DungeonGenerator.Result result = _generator.Generate(seed, _settings);
			if (!result.Success)
			{
				continue; // el generador ya registró el error; se sigue con el resto de candidatos
			}

			candidates.Add(new Candidate
			{
				Index = index,
				Seed = seed,
				EffectiveSeed = result.EffectiveSeed,
				Map = result.Map,
				Predicted = _model.PredictMain(_model.Features(result.Map)),
			});
		}

		return candidates;
	}

	public Result Choose(int gameSeed, DifficultyTarget target, IReadOnlyList<Candidate> candidates)
	{
		double targetValue = _model.TargetValue(target);

		// OrderBy es estable y, además, los empates se desempatan por índice: el orden no depende de nada más.
		List<Candidate> ranking = candidates
			.OrderBy(candidate => Math.Abs(candidate.Predicted - targetValue))
			.ThenBy(candidate => candidate.Index)
			.ToList();

		Candidate chosen = null;
		if (ranking.Count > 0)
		{
			if (PreferEnemyVariety)
			{
				chosen = ranking.Take(TopCount)
					.OrderByDescending(candidate => candidate.EnemyKindCount)
					.ThenBy(candidate => Math.Abs(candidate.Predicted - targetValue))
					.ThenBy(candidate => candidate.Index)
					.First();
			}
			else
			{
				RandomNumberGenerator random = new() { Seed = SelectionSeed(gameSeed) };
				chosen = ranking[random.RandiRange(0, Math.Min(TopCount, ranking.Count) - 1)];
			}
		}

		return new Result
		{
			GameSeed = gameSeed,
			Target = target,
			TargetValue = targetValue,
			TopCount = TopCount,
			Candidates = candidates,
			Ranking = ranking,
			Chosen = chosen,
		};
	}

	/// <summary>Semilla (positiva, 31 bits) del candidato índice de la partida gameSeed.</summary>
	public static int CandidateSeed(int gameSeed, int index)
	{
		return (int)(Mix(Mix((ulong)(uint)gameSeed) + (ulong)(index + 1)) & 0x7FFFFFFFUL);
	}

	private static ulong SelectionSeed(int gameSeed)
	{
		return Mix(Mix((ulong)(uint)gameSeed) ^ 0xA5A5A5A5A5A5A5A5UL);
	}

	// SplitMix64.
	private static ulong Mix(ulong value)
	{
		unchecked
		{
			value += 0x9E3779B97F4A7C15UL;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}
	}
}
