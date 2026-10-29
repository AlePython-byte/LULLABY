using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

/// <summary>
/// Diagnóstico de composición: 200 semillas consecutivas, director v2 y alternativa de variedad dentro del top 5.
/// No modifica el director ni los informes históricos. Ejecutar con DirectorVarietyAudit.tscn.
/// Argumentos: --first-seed=1 --count=200 --out=ai-training/reports/diagnostics/director_variety_v2.json.
/// --check-report compara byte a byte sin sobrescribir; --verify-selector exige comprobar la opción implementada.
/// </summary>
public partial class DirectorVarietyAudit : Node
{
	public override void _Ready()
	{
		try { Run(); GetTree().Quit(); }
		catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
	}

	private static void Run()
	{
		int first = 1, count = 200;
		bool checkReport = false, requireSelector = false;
		string output = "ai-training/reports/diagnostics/director_variety_v2.json";
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--first-seed=")) first = int.Parse(argument[13..]);
			else if (argument.StartsWith("--count=")) count = int.Parse(argument[8..]);
			else if (argument.StartsWith("--out=")) output = argument[6..];
			else if (argument == "--check-report") checkReport = true;
			else if (argument == "--verify-selector") requireSelector = true;
		}
		if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
		RoomCatalog catalog = GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres");
		DifficultyModel model = DifficultyModel.Load(DifficultyModel.PathV2);
		if (model.GeneratorVersion != 2) throw new InvalidDataException("Se requiere el modelo v2");
		List<RoomTemplate> templates = new();
		using (DirAccess directory = DirAccess.Open("res://resources/rooms/templates"))
		{
			foreach (string file in directory.GetFiles().OrderBy(name => name, StringComparer.Ordinal))
				if (file.EndsWith(".tres") && file != "Catalog.tres")
					templates.Add(GD.Load<RoomTemplate>("res://resources/rooms/templates/" + file));
		}
		string[] ids = templates.Select(template => template.Id).ToArray();
		var inventory = templates.Select(template => new
		{
			id = template.Id,
			path = template.ResourcePath,
			enemies = KindCounts(new RoomData { EnemyScene = template.EnemyScene,
				SpawnPoints = template.SpawnPoints, SpawnKinds = template.SpawnKinds }),
			versions = Enumerable.Range(1, 3).Where(version => Enum.GetValues<RoomType>()
				.Any(type => catalog.For(type, version).Any(item => item.Id == template.Id))).ToArray(),
		}).ToArray();
		AIDungeonDirector director = new(new DungeonGenerator(catalog), model,
			new DungeonGenerator.Settings { GeneratorVersion = 2 }) { CandidateCount = 30, TopCount = 5 };
		// Permite ejecutar el diagnóstico también sobre revisiones anteriores sin la opción.
		var varietyProperty = typeof(AIDungeonDirector).GetProperty("PreferEnemyVariety");
		if (requireSelector && varietyProperty == null) throw new InvalidOperationException("Falta PreferEnemyVariety");
		if (varietyProperty != null && (bool)varietyProperty.GetValue(director))
			throw new InvalidOperationException("PreferEnemyVariety debe estar desactivado por defecto");
		Dictionary<string, List<Observation>> current = new(), varied = new(), pool = new();
		Dictionary<string, List<object>> decisions = new();
		Dictionary<string, int> availableThree = new(), changed = new();
		foreach (DifficultyTarget target in Enum.GetValues<DifficultyTarget>())
		{
			string name = target.ToString();
			current[name] = new(); varied[name] = new(); pool[name] = new(); decisions[name] = new();
			availableThree[name] = 0; changed[name] = 0;
		}
		for (int seed = first; seed < first + count; seed++)
		{
			List<AIDungeonDirector.Candidate> candidates = director.GenerateCandidates(seed);
			if (candidates.Count != 30) throw new InvalidDataException($"Semilla {seed}: no hay 30 candidatos");
			foreach (DifficultyTarget target in Enum.GetValues<DifficultyTarget>())
			{
				string name = target.ToString();
				AIDungeonDirector.Result result = director.Choose(seed, target, candidates);
				AIDungeonDirector.Candidate alternative = result.Ranking.Take(5)
					.OrderByDescending(candidate => DistinctKinds(candidate.Map))
					.ThenBy(candidate => Math.Abs(candidate.Predicted - result.TargetValue))
					.ThenBy(candidate => candidate.Index).First();
				if (varietyProperty != null)
				{
					varietyProperty.SetValue(director, true);
					AIDungeonDirector.Result enabled = director.Choose(seed, target, candidates);
					AIDungeonDirector.Result repeated = director.Select(seed, target);
					if (enabled.Chosen.Index != alternative.Index || enabled.Chosen.EffectiveSeed != alternative.EffectiveSeed
						|| enabled.Chosen.Index != repeated.Chosen.Index
						|| DungeonGenerator.Describe(enabled.Map) != DungeonGenerator.Describe(repeated.Map))
						throw new InvalidDataException($"Variedad o determinismo incorrectos: {seed}, {target}");
					if (!enabled.Describe().Contains($"tipos de enemigo {DistinctKinds(enabled.Map)}"))
						throw new InvalidDataException("El log no indica los tipos de enemigo");
					varietyProperty.SetValue(director, false);
				}
				if (result.Ranking.Take(5).Any(candidate => DistinctKinds(candidate.Map) >= 3)) availableThree[name]++;
				if (alternative.Index != result.Chosen.Index) changed[name]++;
				current[name].Add(Observe(result.Chosen, result.TargetValue));
				varied[name].Add(Observe(alternative, result.TargetValue));
				pool[name].AddRange(candidates.Select(candidate => Observe(candidate, result.TargetValue)));
				if (director.Choose(seed, target, candidates).Chosen.Index != result.Chosen.Index)
					throw new InvalidDataException("Selección no determinista");
				decisions[name].Add(new { seed, chosen_index = result.Chosen.Index,
					chosen_seed = result.Chosen.EffectiveSeed, varied_index = alternative.Index,
					varied_seed = alternative.EffectiveSeed,
					top5 = result.Ranking.Take(5).Select(candidate => new { candidate.Index,
						candidate.EffectiveSeed, candidate.Predicted, distinct_kinds = DistinctKinds(candidate.Map) }).ToArray() });
			}
		}
		SortedDictionary<string, object> byTarget = new(StringComparer.Ordinal);
		foreach (DifficultyTarget target in Enum.GetValues<DifficultyTarget>())
		{
			string name = target.ToString();
			byTarget[name] = new { target = model.TargetValue(target),
				current = Summarize(current[name], ids), max_variety_top5 = Summarize(varied[name], ids),
				all_candidates_unselected = Summarize(pool[name], ids),
				top5_has_at_least_three_types_count = availableThree[name],
				changed_selection_count = changed[name], decisions = decisions[name] };
		}
		var report = new { first_seed = first, count, last_seed = first + count - 1,
			generator_version = 2, candidates = 30, top_count = 5,
			alternative_ties = "predicted distance ascending, then candidate index ascending",
			model_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ProjectSettings.GlobalizePath(DifficultyModel.PathV2)))),
			inventory, by_target = byTarget };
		string path = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), output));
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
		if (checkReport)
		{
			if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(text)))
				throw new InvalidDataException("El informe no reproduce el diagnóstico de referencia");
		}
		else File.WriteAllText(path, text);
		GD.Print($"DirectorVarietyAudit: {count} semillas ({first}..{first + count - 1}), tres objetivos, informe: {path}");
		GD.Print($"Referencia {(checkReport ? "idéntica byte a byte" : "generada")}; " +
			$"selector {(varietyProperty != null ? "comprobado en ambos modos, con regeneración determinista" : "todavía no disponible")}. RESULTADO: OK");
	}

	private static int[] KindCounts(RoomData room)
	{
		int[] counts = new int[EnemyKinds.List.Count];
		if (room.EnemyScene != null)
			for (int i = 0; i < room.SpawnPoints.Length; i++) counts[(int)LevelMetricsV2.KindAt(room, i)]++;
		return counts;
	}

	private static int[] KindCounts(LevelMap map)
	{
		int[] counts = new int[EnemyKinds.List.Count];
		foreach (RoomData room in map.Rooms)
		{
			int[] roomCounts = KindCounts(room);
			for (int i = 0; i < counts.Length; i++) counts[i] += roomCounts[i];
		}
		return counts;
	}

	private static int DistinctKinds(LevelMap map) => KindCounts(map).Count(value => value > 0);
	private sealed record Observation(Dictionary<string, int> Templates, int[] Kinds, double Predicted, double Error);
	private static Observation Observe(AIDungeonDirector.Candidate candidate, double target) => new(
		candidate.Map.Rooms.GroupBy(room => room.TemplateId).ToDictionary(group => group.Key, group => group.Count()),
		KindCounts(candidate.Map), candidate.Predicted, Math.Abs(candidate.Predicted - target));

	private static object Summarize(List<Observation> observations, string[] ids)
	{
		int total = observations.Sum(item => item.Kinds.Sum());
		return new
		{
			levels = observations.Count,
			rooms_mean = observations.Average(item => item.Templates.Values.Sum()),
			templates_mean = ids.ToDictionary(id => id, id => observations.Average(item => item.Templates.GetValueOrDefault(id))),
			enemy_types = EnemyKinds.List.Select((kind, index) => new { name = kind.Name,
				total = observations.Sum(item => item.Kinds[index]), mean = observations.Average(item => item.Kinds[index]),
				level_presence_count = observations.Count(item => item.Kinds[index] > 0),
				level_presence_percent = observations.Count(item => item.Kinds[index] > 0) * 100.0 / observations.Count,
				enemy_fraction_percent = observations.Sum(item => item.Kinds[index]) * 100.0 / total }).ToArray(),
			enemies_mean = observations.Average(item => item.Kinds.Sum()),
			distinct_types_mean = observations.Average(item => item.Kinds.Count(value => value > 0)),
			distinct_types_histogram = Enumerable.Range(0, 7).ToDictionary(value => value.ToString(),
				value => observations.Count(item => item.Kinds.Count(number => number > 0) == value)),
			at_least_three_types_count = observations.Count(item => item.Kinds.Count(value => value > 0) >= 3),
			at_least_three_types_percent = observations.Count(item => item.Kinds.Count(value => value > 0) >= 3) * 100.0 / observations.Count,
			predicted_mean = observations.Average(item => item.Predicted),
			predicted_distance_mean = observations.Average(item => item.Error),
			predicted_distance_max = observations.Max(item => item.Error),
		};
	}
}
