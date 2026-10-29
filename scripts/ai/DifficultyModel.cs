using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Modelo que predice la dificultad de un nivel a partir de sus características. Lee el JSON que exporta
/// ai-training/training/export_model.py. Hay uno por versión del generador: res://resources/ai/difficulty_v1.json
/// (generador v1, las 14 características de LevelMetrics) y res://resources/ai/difficulty_v2.json (generador v2, las 22
/// de LevelMetricsV2). C# puro: no usa Python ni bibliotecas externas.
///
/// Validación al cargar (InvalidDataException con un mensaje claro si algo no cuadra):
///   - format_version soportada;
///   - feature_set_version conocida (LevelFeatureSet);
///   - feature_names idénticos, en nombre y orden, a los de esa versión;
///   - existe el modelo de la etiqueta principal y los tres objetivos de dificultad.
/// generator_version es opcional (el JSON v1 no lo tiene): por defecto 1.
///
/// Modelos soportados (campo "kind"):
///   linear / ridge     intercept + sum_i coefficients[i] * (x[i] - mean[i]) / scale[i]
///   gradient_boosting  init + learning_rate * sum_t hoja(arbol_t, x)
///   random_forest      media_t hoja(arbol_t, x)
/// Los árboles se recorren por el hijo izquierdo si x[feature] &lt;= threshold. Como indica tree_feature_dtype, scikit-learn
/// compara en float32: cada característica se convierte a float antes de compararla con el umbral (double).
/// Hoy solo se usan los modelos lineales; los árboles están probados con un fixture (tests/ModelParityTest).
/// </summary>
public sealed class DifficultyModel
{
	public const string DefaultPath = "res://resources/ai/difficulty_v1.json";
	public const string PathV2 = "res://resources/ai/difficulty_v2.json";
	public const int SupportedFormatVersion = 1;

	/// <summary>Ruta del modelo de una versión del generador.</summary>
	public static string PathFor(int generatorVersion)
	{
		return generatorVersion >= 2 ? PathV2 : DefaultPath;
	}

	private abstract class Predictor
	{
		public abstract double Predict(double[] features);
	}

	private sealed class LinearPredictor : Predictor
	{
		public double[] Mean, Scale, Coefficients;
		public double Intercept;

		public override double Predict(double[] features)
		{
			double sum = 0.0;
			for (int i = 0; i < Coefficients.Length; i++)
			{
				sum += (features[i] - Mean[i]) / Scale[i] * Coefficients[i];
			}

			return Intercept + sum;
		}
	}

	private sealed class Tree
	{
		public int[] Feature, Left, Right;
		public double[] Threshold, Value;

		public double Leaf(double[] features)
		{
			int node = 0;
			while (Feature[node] >= 0)
			{
				float x = (float)features[Feature[node]];
				node = x <= Threshold[node] ? Left[node] : Right[node];
			}

			return Value[node];
		}
	}

	private sealed class BoostingPredictor : Predictor
	{
		public double Init, LearningRate;
		public Tree[] Trees;

		public override double Predict(double[] features)
		{
			double total = Init;
			foreach (Tree tree in Trees)
			{
				total += LearningRate * tree.Leaf(features);
			}

			return total;
		}
	}

	private sealed class ForestPredictor : Predictor
	{
		public Tree[] Trees;

		public override double Predict(double[] features)
		{
			double sum = 0.0;
			foreach (Tree tree in Trees)
			{
				sum += tree.Leaf(features);
			}

			return sum / Trees.Length;
		}
	}

	private readonly Dictionary<string, Predictor> _predictors = new();
	private readonly Dictionary<DifficultyTarget, double> _targetValues = new();

	public int FeatureSetVersion { get; private set; }
	public IReadOnlyList<string> FeatureNames { get; private set; }
	public string MainTarget { get; private set; }

	/// <summary>Versión del generador con cuyos niveles se entrenó.</summary>
	public int GeneratorVersion { get; private set; } = DungeonGenerator.OriginalVersion;

	/// <summary>Las características que espera este modelo, calculadas para un nivel.</summary>
	public double[] Features(LevelMap map)
	{
		return LevelFeatureSet.Compute(FeatureSetVersion, map);
	}

	public double TargetValue(DifficultyTarget target)
	{
		return _targetValues[target];
	}

	public bool HasTarget(string label)
	{
		return _predictors.ContainsKey(label);
	}

	public double Predict(string label, double[] features)
	{
		if (!_predictors.TryGetValue(label, out Predictor predictor))
		{
			throw new ArgumentException($"DifficultyModel: el modelo no tiene la etiqueta '{label}'.");
		}

		if (features.Length != FeatureNames.Count)
		{
			throw new ArgumentException($"DifficultyModel: se esperaban {FeatureNames.Count} características y llegaron {features.Length}.");
		}

		return predictor.Predict(features);
	}

	public double PredictMain(double[] features)
	{
		return Predict(MainTarget, features);
	}

	public static DifficultyModel Load(string path = DefaultPath)
	{
		if (!Godot.FileAccess.FileExists(path))
		{
			throw new FileNotFoundException($"DifficultyModel: no existe {path}. Ejecuta ai-training/training/export_model.py.");
		}

		return FromText(Godot.FileAccess.GetFileAsString(path), path);
	}

	public static DifficultyModel FromJsonResource(Json json, string origin = "recurso Json")
	{
		if (json == null || json.Data.VariantType != Variant.Type.Dictionary)
		{
			throw new InvalidDataException($"DifficultyModel: {origin} no contiene un objeto JSON válido.");
		}

		return FromVariant(json.Data, origin);
	}

	public static DifficultyModel FromText(string text, string origin = "texto")
	{
		Variant data = Json.ParseString(text);
		if (data.VariantType != Variant.Type.Dictionary)
		{
			throw new InvalidDataException($"DifficultyModel: {origin} no es un JSON válido (se esperaba un objeto).");
		}

		return FromVariant(data, origin);
	}

	private static DifficultyModel FromVariant(Variant data, string origin)
	{
		Godot.Collections.Dictionary root = data.AsGodotDictionary();
		DifficultyModel model = new();

		int format = Required(root, "format_version", origin).AsInt32();
		if (format != SupportedFormatVersion)
		{
			throw new InvalidDataException($"DifficultyModel: {origin} usa format_version {format}; este código entiende la {SupportedFormatVersion}.");
		}

		model.FeatureSetVersion = Required(root, "feature_set_version", origin).AsInt32();
		if (!LevelFeatureSet.IsKnown(model.FeatureSetVersion))
		{
			throw new InvalidDataException($"DifficultyModel: {origin} es para las características versión {model.FeatureSetVersion}, " +
				$"que este código no conoce (conoce: {string.Join(", ", LevelFeatureSet.Versions)}). Hay que reentrenar y volver a exportar.");
		}

		string[] names = Required(root, "feature_names", origin).AsGodotArray().Select(item => item.AsString()).ToArray();
		CheckFeatureNames(names, LevelFeatureSet.Names(model.FeatureSetVersion), model.FeatureSetVersion, origin);
		model.FeatureNames = names;

		if (root.ContainsKey("generator_version"))
		{
			model.GeneratorVersion = root["generator_version"].AsInt32();
		}

		model.MainTarget = Required(root, "main_target", origin).AsString();

		Godot.Collections.Dictionary targets = Required(root, "difficulty_targets", origin).AsGodotDictionary();
		foreach (DifficultyTarget target in Enum.GetValues<DifficultyTarget>())
		{
			model._targetValues[target] = Required(targets, target.ToString().ToLowerInvariant(), origin + " (difficulty_targets)").AsDouble();
		}

		bool treesNeedFloat32 = Required(root, "tree_feature_dtype", origin).AsString() == "float32";
		Godot.Collections.Dictionary models = Required(root, "models", origin).AsGodotDictionary();
		foreach (Variant key in models.Keys)
		{
			string label = key.AsString();
			model._predictors[label] = ParsePredictor(models[key].AsGodotDictionary(), label, treesNeedFloat32, origin);
		}

		if (!model._predictors.ContainsKey(model.MainTarget))
		{
			throw new InvalidDataException($"DifficultyModel: {origin} no incluye el modelo de la etiqueta principal '{model.MainTarget}'.");
		}

		return model;
	}

	private static void CheckFeatureNames(string[] names, string[] expected, int version, string origin)
	{
		if (names.Length != expected.Length)
		{
			throw new InvalidDataException($"DifficultyModel: {origin} tiene {names.Length} características y la versión {version} " +
				$"tiene {expected.Length}.");
		}

		for (int i = 0; i < names.Length; i++)
		{
			if (names[i] != expected[i])
			{
				throw new InvalidDataException($"DifficultyModel: {origin}: la característica {i + 1} es '{names[i]}' pero la versión " +
					$"{version} espera '{expected[i]}'. El orden y los nombres tienen que coincidir exactamente.");
			}
		}
	}

	private static Predictor ParsePredictor(Godot.Collections.Dictionary data, string label, bool float32Trees, string origin)
	{
		string kind = Required(data, "kind", origin + $" (modelo {label})").AsString();
		switch (kind)
		{
			case "linear":
			case "ridge":
				Godot.Collections.Dictionary normalization = Required(data, "normalization", origin).AsGodotDictionary();
				return new LinearPredictor
				{
					Mean = Doubles(Required(normalization, "mean", origin)),
					Scale = Doubles(Required(normalization, "scale", origin)),
					Coefficients = Doubles(Required(data, "coefficients", origin)),
					Intercept = Required(data, "intercept", origin).AsDouble(),
				};

			case "gradient_boosting":
				RequireFloat32(float32Trees, kind, origin);
				return new BoostingPredictor
				{
					Init = Required(data, "init", origin).AsDouble(),
					LearningRate = Required(data, "learning_rate", origin).AsDouble(),
					Trees = ParseTrees(Required(data, "trees", origin)),
				};

			case "random_forest":
				RequireFloat32(float32Trees, kind, origin);
				return new ForestPredictor { Trees = ParseTrees(Required(data, "trees", origin)) };

			default:
				throw new InvalidDataException($"DifficultyModel: {origin}: el modelo '{label}' es de un tipo desconocido ('{kind}').");
		}
	}

	private static void RequireFloat32(bool float32Trees, string kind, string origin)
	{
		if (!float32Trees)
		{
			throw new InvalidDataException($"DifficultyModel: {origin}: los árboles de '{kind}' deben declarar tree_feature_dtype = float32.");
		}
	}

	private static Tree[] ParseTrees(Variant trees)
	{
		return trees.AsGodotArray().Select(item =>
		{
			Godot.Collections.Dictionary tree = item.AsGodotDictionary();
			return new Tree
			{
				Feature = Ints(tree["feature"]),
				Threshold = Doubles(tree["threshold"]),
				Left = Ints(tree["left"]),
				Right = Ints(tree["right"]),
				Value = Doubles(tree["value"]),
			};
		}).ToArray();
	}

	private static Variant Required(Godot.Collections.Dictionary dictionary, string key, string origin)
	{
		if (!dictionary.ContainsKey(key))
		{
			throw new InvalidDataException($"DifficultyModel: a {origin} le falta el campo '{key}'.");
		}

		return dictionary[key];
	}

	private static double[] Doubles(Variant array)
	{
		return array.AsGodotArray().Select(item => item.AsDouble()).ToArray();
	}

	private static int[] Ints(Variant array)
	{
		return array.AsGodotArray().Select(item => item.AsInt32()).ToArray();
	}
}
