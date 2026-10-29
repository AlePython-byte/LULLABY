using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Comprueba que el DifficultyModel de C# reproduce las predicciones de Python. Modo headless:
///   godot --headless --path . res://tests/ModelParityTest.tscn
///   godot --headless --path . res://tests/ModelParityTest.tscn -- --version=2   (difficulty_v2.json, generador v2,
///                                                                              reports/v2/predictions_test_python.csv)
///
/// 1. Copia del juego (res://resources/ai/difficulty_vN.json) idéntica byte a byte a ai-training/models/difficulty_vN.json,
///    y la misma predicción cargando el modelo como texto o como recurso Json.
/// 2. El validador falla con un error claro (versión, nombres/orden de características y formato).
/// 3. Árboles: tests/fixtures/tree_model_fixture.json (Gradient Boosting y Random Forest pequeños con predicciones de
///    scikit-learn), con la conversión a float32 que indica el JSON. Los modelos del juego son lineales: esto prueba
///    el código de árboles, que hoy no se usa.
/// 4. Paridad con predictions_test_python.csv sobre los 1000 niveles de prueba (semillas 100001..101000 regenerados con
///    el generador):
///      - ESTRICTA: C# con las características redondeadas a 6 decimales, como el CSV con el que Python predijo
///        (Math.Round(v, 6)). Prueba que C# evalúa igual que Python; tolerancia 1e-6.
///      - INFORMATIVA: C# con las características EXACTAS de LevelMetrics.Compute (lo que verá el juego). Solo se muestra
///        la diferencia: el CSV redondea a 6 decimales y enemy_density pesa -7750 por unidad, así que el redondeo
///        mueve la predicción unas milésimas.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class ModelParityTest : Node
{
	private const string CatalogPath = "res://resources/rooms/templates/Catalog.tres";
	private const string FixturePath = "res://tests/fixtures/tree_model_fixture.json";
	[Export] public int Version { get; set; } = 1;

	private string PredictionsPath => Version >= 2
		? $"ai-training/reports/v{Version}/predictions_test_python.csv"
		: "ai-training/reports/predictions_test_python.csv";

	private string TrainingCopyPath => $"ai-training/models/difficulty_v{Version}.json";
	private string GamePath => DifficultyModel.PathFor(Version);
	private const double Tolerance = 1e-6;
	private const double TreeTolerance = 1e-9;

	[Export] public int FirstSeed { get; set; } = 100001;
	[Export] public int LevelCount { get; set; } = 1000;

	public override void _Ready()
	{
		bool ok = Run();
		GD.Print(ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(ok ? 0 : 1);
	}

	private bool Run()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--version=") && int.TryParse(argument["--version=".Length..], out int version))
			{
				Version = version;
			}
		}

		bool ok = true;
		string projectRoot = ProjectSettings.GlobalizePath("res://");
		DifficultyModel model = DifficultyModel.Load(GamePath);
		GD.Print($"Modelo: {GamePath} (características versión {model.FeatureSetVersion}, {model.FeatureNames.Count}; generador v{model.GeneratorVersion})");
		ok &= model.GeneratorVersion == Version;

		GD.Print("=== 1. Copia del juego y vías de carga ===");
		byte[] gameCopy = Godot.FileAccess.GetFileAsBytes(GamePath);
		byte[] trainingCopy = File.ReadAllBytes(Path.Combine(projectRoot, TrainingCopyPath));
		bool identical = gameCopy.AsSpan().SequenceEqual(trainingCopy);
		GD.Print($"Copia de resources/ai idéntica a ai-training/models: {(identical ? "sí" : "NO")} ({gameCopy.Length} bytes)");
		ok &= identical;

		Json resource = GD.Load<Json>(GamePath);
		DifficultyModel fromResource = DifficultyModel.FromJsonResource(resource, GamePath);
		double[] sample = Enumerable.Range(0, model.FeatureNames.Count).Select(i => 1.0 + i * 0.37).ToArray();
		double fromText = model.PredictMain(sample);
		double fromJsonResource = fromResource.PredictMain(sample);
		GD.Print($"Predicción de muestra: texto {fromText:R}, recurso Json {fromJsonResource:R} (diferencia {Math.Abs(fromText - fromJsonResource):0.0e+00})");
		ok &= Math.Abs(fromText - fromJsonResource) == 0.0;
		GD.Print($"Objetivos de dificultad: Easy {model.TargetValue(DifficultyTarget.Easy):0.###}, " +
			$"Normal {model.TargetValue(DifficultyTarget.Normal):0.###}, Hard {model.TargetValue(DifficultyTarget.Hard):0.###}");

		ok &= CheckValidator(Godot.FileAccess.GetFileAsString(GamePath), model.FeatureSetVersion);
		ok &= CheckTrees();
		ok &= CheckParity(model, projectRoot);
		return ok;
	}

	private static bool CheckValidator(string text, int featureVersion)
	{
		GD.Print("\n=== 2. El validador falla con un error claro ===");
		int otherVersion = featureVersion == 1 ? 2 : 1;
		(string Name, string Broken)[] cases =
		{
			("versión de características distinta", text.Replace($"\"feature_set_version\":{featureVersion}", $"\"feature_set_version\":{otherVersion}")),
			("versión de características desconocida", text.Replace($"\"feature_set_version\":{featureVersion}", "\"feature_set_version\":99")),
			("nombre de característica cambiado", text.Replace("\"room_count\"", "\"room_countX\"")),
			("orden de características alterado", text.Replace("\"boss_distance\",\"dead_end_count\"", "\"dead_end_count\",\"boss_distance\"")),
			("format_version no soportada", text.Replace("\"format_version\":1", "\"format_version\":9")),
		};

		bool ok = true;
		foreach ((string name, string broken) in cases)
		{
			if (broken == text)
			{
				GD.Print($"  {name}: NO se pudo preparar el caso (el texto no cambió)");
				ok = false;
				continue;
			}

			try
			{
				DifficultyModel.FromText(broken, "caso de prueba");
				GD.Print($"  {name}: NO falló");
				ok = false;
			}
			catch (InvalidDataException exception)
			{
				GD.Print($"  {name}: falla -> {exception.Message}");
			}
		}

		return ok;
	}

	private static bool CheckTrees()
	{
		GD.Print("\n=== 3. Árboles (fixture de Python) ===");
		Variant data = Json.ParseString(Godot.FileAccess.GetFileAsString(FixturePath));
		Godot.Collections.Dictionary fixture = data.AsGodotDictionary()["fixture"].AsGodotDictionary();
		double[][] rows = fixture["rows"].AsGodotArray().Select(row => row.AsGodotArray().Select(v => v.AsDouble()).ToArray()).ToArray();

		DifficultyModel model = DifficultyModel.FromText(Godot.FileAccess.GetFileAsString(FixturePath), FixturePath);
		bool ok = true;
		foreach ((string label, string kind) in new[] { ("damage_total_mean", "Gradient Boosting"), ("time_total_mean", "Random Forest") })
		{
			double[] expected = fixture["expected"].AsGodotDictionary()[label].AsGodotArray().Select(v => v.AsDouble()).ToArray();
			double max = 0.0;
			for (int i = 0; i < rows.Length; i++)
			{
				max = Math.Max(max, Math.Abs(model.Predict(label, rows[i]) - expected[i]));
			}

			GD.Print($"  {kind} ({label}): {rows.Length} filas, diferencia máxima con scikit-learn = {max:0.0e+00} (tolerancia {TreeTolerance:0e+0})");
			ok &= max < TreeTolerance;
		}

		return ok;
	}

	private bool CheckParity(DifficultyModel model, string projectRoot)
	{
		GD.Print($"\n=== 4. Paridad con predictions_test_python.csv ({LevelCount} niveles, semillas {FirstSeed}..{FirstSeed + LevelCount - 1}) ===");
		Dictionary<int, double[]> python = new();
		foreach (string line in File.ReadAllLines(Path.Combine(projectRoot, PredictionsPath)).Skip(1))
		{
			string[] parts = line.Split(',');
			python[int.Parse(parts[0], CultureInfo.InvariantCulture)] =
				new[] { double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture) };
		}

		DungeonGenerator generator = new(GD.Load<RoomCatalog>(CatalogPath));
		DungeonGenerator.Settings settings = new() { GeneratorVersion = Version };
		string[] labels = { "damage_total_mean", "time_total_mean" };
		double[] strictMax = new double[2], exactMax = new double[2], exactSum = new double[2];
		int matched = 0, missing = 0;

		for (int seed = FirstSeed; seed < FirstSeed + LevelCount; seed++)
		{
			DungeonGenerator.Result result = generator.Generate(seed, settings);
			if (!result.Success || !python.TryGetValue(result.EffectiveSeed, out double[] expected))
			{
				missing++;
				continue;
			}

			matched++;
			double[] exact = model.Features(result.Map);
			double[] rounded = exact.Select(value => Math.Round(value, 6) + 0.0).ToArray();
			for (int target = 0; target < 2; target++)
			{
				double strict = Math.Abs(model.Predict(labels[target], rounded) - expected[target]);
				double loose = Math.Abs(model.Predict(labels[target], exact) - expected[target]);
				strictMax[target] = Math.Max(strictMax[target], strict);
				exactMax[target] = Math.Max(exactMax[target], loose);
				exactSum[target] += loose;
			}
		}

		GD.Print($"Niveles emparejados por semilla efectiva: {matched} de {LevelCount} (sin pareja: {missing})");
		bool ok = matched == LevelCount && python.Count == LevelCount;
		for (int target = 0; target < 2; target++)
		{
			GD.Print($"  {labels[target]}:");
			GD.Print($"    ESTRICTA (características redondeadas a 6 decimales): diferencia máxima = {strictMax[target]:0.0e+00} (tolerancia {Tolerance:0e+0})");
			GD.Print($"    INFORMATIVA (características exactas, como verá el juego): máxima {exactMax[target]:0.0000}, media {exactSum[target] / Math.Max(matched, 1):0.0000}");
			ok &= strictMax[target] < Tolerance;
		}

		return ok;
	}
}
