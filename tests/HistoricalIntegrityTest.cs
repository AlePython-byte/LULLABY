using System;
using System.IO;
using System.Security.Cryptography;
using Godot;

/// <summary>
/// Protege lo histórico mientras se añade contenido (The Mother, objetos): los datos, modelos y benchmarks de la IA v1/v2,
/// la composición de seis básicos de la sala del jefe y las colisiones de Noah y del básico. Modo headless:
///   godot --headless --path . res://tests/HistoricalIntegrityTest.tscn
/// Comprueba:
///   1. SHA-256 de los datasets de características y etiquetas (entrenamiento, prueba y confirmación) de v1 y v2, de los
///      modelos exportados (las dos copias), de los informes del benchmark del director y de jefe.tres y Catalog.tres.
///      Los de v1 coinciden con los publicados en docs/IA.md (a30d3a36…, 54b79ccf…, 5921a2fa…).
///   2. Colisión de Noah: caja de 1×1×1; del enemigo básico: 0.9×0.9×0.9.
///   3. jefe.tres: 26×26, seis puntos de aparición, todos BasicEnemy, sin tipos; TestLevel usa The Mother como override.
///   4. tests/FidelityCheck.cs sigue desactivando el override del jefe (BossScene = null).
/// Si algún dato histórico cambia a propósito, hay que actualizar aquí su hash junto con el documento que lo cita. Estas
/// comprobaciones no regeneran nada: para regenerar y comparar se ejecutan DatasetExport, LabelExport, ModelParityTest y
/// DirectorBenchmark como describe docs/IA.md. Sale con código 0 si todo pasa.
/// </summary>
public partial class HistoricalIntegrityTest : Node
{
	private static readonly (string Path, string Sha256)[] Artifacts =
	{
		("ai-training/datasets/levels_v1.csv", "a30d3a36d34d8fe21705a078b9dc60c38137a7e9574ec1b69df6442a579de85a"),
		("ai-training/datasets/levels_v1_labels.csv", "54b79ccfe911a4e12655e017f1befb6701a3a52d86c9dabca7ba0e33b3ff370d"),
		("ai-training/datasets/levels_v1_test.csv", "ce1b8f3a9890e5930a84c4ad8e4a8bfcdd36e399b182b075f2c261bc853bcd6b"),
		("ai-training/datasets/levels_v1_test_labels.csv", "f32b097eefa3adc08909e3e4f1bf313fd014cf45d650ab4f4ab396848f658036"),
		("ai-training/datasets/levels_v1_confirm.csv", "4c213e3a73c5a3874dd2185c920482fb9aaa86540548ad403aca4d249c54647e"),
		("ai-training/datasets/levels_v1_confirm_labels.csv", "3d7e4c09e13e64059d1f5c858a9f55a9cf8055e7c5ecb32ea9ba7007062efefa"),
		("ai-training/datasets/levels_v2.csv", "7f6c88f3dbcf7aea6677554bc5bf05fa856c637817f27db8f2d84863d7986139"),
		("ai-training/datasets/levels_v2_labels.csv", "6f42695a1c8a050142864e26cd808c3b56f860e1d7f0c249f2bc5215d332e2df"),
		("ai-training/datasets/levels_v2_test.csv", "bffe22daa847f51f113e9e28a5472da5ef0dae16e3fc3d174e23873a38a22395"),
		("ai-training/datasets/levels_v2_test_labels.csv", "d09b743c3f18d6c43d6850d45a86da40f40bc4726dafd3f600cd45f0e2860605"),
		("ai-training/datasets/levels_v2_confirm.csv", "0aec8952915b92ab3ec7c581e5d7aee162a3775213c21f2df4050f5fab994c0f"),
		("ai-training/datasets/levels_v2_confirm_labels.csv", "c725b9569a3a123aecbe6c9a0bd9ff99257034ff92f8a767866fa5b101b7cef9"),
		("ai-training/models/difficulty_v1.json", "96c28588077ca53e6d4d2e6e486f53a9641312a17012a06a8593254c9b5451c1"),
		("ai-training/models/difficulty_v2.json", "c03cf200951a1588705e9f6aca839510c92c26a8261767e4177ad885122fe7bd"),
		("resources/ai/difficulty_v1.json", "96c28588077ca53e6d4d2e6e486f53a9641312a17012a06a8593254c9b5451c1"),
		("resources/ai/difficulty_v2.json", "c03cf200951a1588705e9f6aca839510c92c26a8261767e4177ad885122fe7bd"),
		("ai-training/reports/director_benchmark.json", "5921a2fa28caf02f7c5b5d050cc2a2ef74e40e52e5d1ced042dfaa70d20c909b"),
		("ai-training/reports/v2/director_benchmark_v2.json", "4ec35e9b9b0d5d01ec5fe16172da09cb336eb6c5e265d8cf7a06e910fb3f4792"),
		("resources/rooms/templates/jefe.tres", "107f84f33999866f0c27a559fce3071083b31058515780351ff156ad78d6c679"),
		("resources/rooms/templates/Catalog.tres", "c2e114b27df074221c0715c579cd9b4e76bda6fc197de448b5c76c51864d66d6"),
	};

	private int _checks;

	public override void _Ready()
	{
		try
		{
			CheckArtifacts();
			CheckCollisions();
			CheckBossComposition();
			CheckFidelityOverride();
			GD.Print($"HistoricalIntegrityTest: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private static string Absolute(string projectPath) => ProjectSettings.GlobalizePath("res://" + projectPath);

	private void CheckArtifacts()
	{
		foreach ((string path, string expected) in Artifacts)
		{
			string file = Absolute(path);
			Require(File.Exists(file), $"Falta el archivo histórico {path}");
			string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
			Require(actual == expected, $"{path} cambió: SHA-256 {actual}, esperado {expected}");
		}

		GD.Print($"Datos, modelos, benchmarks y plantillas históricas: {Artifacts.Length} SHA-256 idénticos: OK");
	}

	private void CheckCollisions()
	{
		RequireBox("res://scenes/player/Player.tscn", new Vector3(1.0f, 1.0f, 1.0f), "Noah");
		RequireBox("res://scenes/enemies/BasicEnemy.tscn", new Vector3(0.9f, 0.9f, 0.9f), "El enemigo básico");
		GD.Print("Colisiones: Noah 1×1 y básico 0.9×0.9: OK");
	}

	private void RequireBox(string scenePath, Vector3 expected, string who)
	{
		Node instance = GD.Load<PackedScene>(scenePath).Instantiate();
		try
		{
			BoxShape3D box = instance.GetNode<CollisionShape3D>("CollisionShape3D").Shape as BoxShape3D;
			Require(box != null && box.Size.IsEqualApprox(expected), $"{who} conserva su colisión {expected} (hay {box?.Size})");
		}
		finally
		{
			instance.Free();
		}
	}

	private void CheckBossComposition()
	{
		RoomTemplate template = GD.Load<RoomTemplate>("res://resources/rooms/templates/jefe.tres");
		Vector2[] expectedPoints = { new(-5, -5), new(5, -5), new(-5, 5), new(5, 5), new(-10, -10), new(10, -10) };
		Require(template.Id == "jefe" && template.Size == new Vector2(26.0f, 26.0f) && template.SpawnKinds.Length == 0
			&& template.EnemyScene.ResourcePath == "res://scenes/enemies/BasicEnemy.tscn"
			&& template.SpawnPoints.Length == expectedPoints.Length, "jefe.tres: 26×26 con seis básicos");
		for (int i = 0; i < expectedPoints.Length; i++)
		{
			Require(template.SpawnPoints[i] == expectedPoints[i], $"jefe.tres: punto de aparición {i + 1}");
		}

		Node level = GD.Load<PackedScene>("res://scenes/levels/TestLevel.tscn").Instantiate();
		try
		{
			PackedScene boss = level.GetNode<RoomManager>("RoomManager").BossScene;
			Require(boss != null && boss.ResourcePath == "res://scenes/bosses/TheMother.tscn",
				"TestLevel usa The Mother como jefe alternativo; la composición histórica sigue en jefe.tres");
		}
		finally
		{
			level.Free();
		}

		GD.Print("Sala del jefe: plantilla histórica de seis básicos intacta: OK");
	}

	private void CheckFidelityOverride()
	{
		string source = File.ReadAllText(Absolute("tests/FidelityCheck.cs"));
		Require(source.Contains("level.GetNode<RoomManager>(\"RoomManager\").BossScene = null;"),
			"FidelityCheck desactiva el override del jefe y evalúa los seis básicos");
		GD.Print("FidelityCheck sigue desactivando el override del jefe: OK");
	}
}
