using System;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Comprueba EnemyScenePreloader (las escenas de enemigo se cargan al empezar la partida, no al nacer los enemigos). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/EnemyScenePreloaderTest.tscn
/// Casos:
///   1. Precargar un tipo lo retiene, EnemyKinds.Scene devuelve esa misma escena y repetir no la vuelve a cargar.
///   2. KindsOf lee SpawnKinds cuando la sala los trae, el tipo de EnemyScene cuando no, e ignora salas sin puntos y tipos inválidos.
///   3. RoomManager.Begin precarga todos los tipos del nivel generado.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class EnemyScenePreloaderTest : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== EnemyScenePreloaderTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckSingleKind();
			CheckKindsOfMap();
			await CheckLevelBegin();
			GD.Print($"EnemyScenePreloaderTest: {_checks} comprobaciones. RESULTADO: OK");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit(1);
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private void CheckSingleKind()
	{
		EnemyScenePreloader.Reset();
		Require(EnemyScenePreloader.Held.Count == 0 && EnemyScenePreloader.TryGet(EnemyKind.Shooter) == null, "Sin precarga no hay nada retenido");

		Require(EnemyScenePreloader.Preload(EnemyKind.Shooter), "Se puede precargar el tirador");
		PackedScene held = EnemyScenePreloader.TryGet(EnemyKind.Shooter);
		Require(held != null && held.ResourcePath == EnemyKinds.Get(EnemyKind.Shooter).ScenePath, "La escena retenida es la del tipo");
		Require(ReferenceEquals(EnemyKinds.Scene(EnemyKind.Shooter), held), "EnemyKinds.Scene devuelve la escena precargada");
		Require(ResourceLoader.HasCached(held.ResourcePath), "La escena queda en el caché de recursos");

		Require(EnemyScenePreloader.Preload(EnemyKind.Shooter) && ReferenceEquals(EnemyScenePreloader.TryGet(EnemyKind.Shooter), held), "Precargar otra vez es idempotente");
		Require(EnemyScenePreloader.Failed == 0, "No hubo fallos de carga");

		// Un tipo sin precargar sigue cargándose bajo demanda.
		Require(EnemyScenePreloader.TryGet(EnemyKind.Flyer) == null && EnemyKinds.Scene(EnemyKind.Flyer) != null, "Un tipo sin precargar se carga bajo demanda");
		GD.Print("Precarga de un tipo: retenida, misma escena en EnemyKinds, idempotente: OK");
	}

	private void CheckKindsOfMap()
	{
		RoomData basic = new() { Cell = new Vector2I(0, 0), EnemyScene = EnemyKinds.Scene(EnemyKind.Chaser), SpawnPoints = new[] { Vector2.Zero, Vector2.One } };
		RoomData mixed = new() { Cell = new Vector2I(1, 0), EnemyScene = EnemyKinds.Scene(EnemyKind.Chaser), SpawnPoints = new[] { Vector2.Zero, Vector2.One, Vector2.Left }, SpawnKinds = new[] { 1, 3, 1 } };
		RoomData noPoints = new() { Cell = new Vector2I(2, 0), EnemyScene = EnemyKinds.Scene(EnemyKind.Chaser), SpawnKinds = new[] { 5 } };
		RoomData noScene = new() { Cell = new Vector2I(3, 0), SpawnPoints = new[] { Vector2.Zero, Vector2.One } };
		RoomData invalid = new() { Cell = new Vector2I(4, 0), SpawnPoints = new[] { Vector2.Zero, Vector2.One }, SpawnKinds = new[] { 99, 2 } };
		LevelMap map = new() { Rooms = new Godot.Collections.Array<RoomData> { basic, mixed, noPoints, noScene, invalid, null } };

		EnemyKind[] expected = { EnemyKind.Chaser, EnemyKind.Shooter, EnemyKind.Exploder, EnemyKind.Flyer };
		Require(EnemyScenePreloader.KindsOf(map).SequenceEqual(expected),
			$"KindsOf del mapa a mano: {string.Join(",", EnemyScenePreloader.KindsOf(map))} (esperado {string.Join(",", expected)})");

		EnemyScenePreloader.Reset();
		Require(EnemyScenePreloader.PreloadFor(map) == 4 && expected.All(kind => EnemyScenePreloader.TryGet(kind) != null), "PreloadFor retiene los cuatro tipos del mapa");
		Require(EnemyScenePreloader.TryGet(EnemyKind.Digger) == null && EnemyScenePreloader.TryGet(EnemyKind.Runner) == null, "Y no carga los que el mapa no usa");
		Require(EnemyScenePreloader.KindsOf(null).Count == 0 && EnemyScenePreloader.PreloadFor(null) == 0, "Sin mapa no hay tipos");
		GD.Print("Tipos de un mapa a mano (SpawnKinds, EnemyScene, salas sin puntos, tipo inválido): OK");
	}

	private async System.Threading.Tasks.Task CheckLevelBegin()
	{
		SceneTree tree = GetTree();
		foreach (int seed in new[] { BossTestHarness.Seed, 3 })
		{
			EnemyScenePreloader.Reset();
			BossTestHarness.Level level = BossTestHarness.Open(this, seed, withMother: false);
			await BossTestHarness.Frames(tree, 2);

			var kinds = EnemyScenePreloader.KindsOf(level.Rooms.Map);
			Require(kinds.Count > 0, $"[semilla {seed}] El nivel tiene enemigos");
			Require(kinds.All(kind => EnemyScenePreloader.TryGet(kind) != null && EnemyScenePreloader.Held.Contains(kind)),
				$"[semilla {seed}] RoomManager.Begin precargó los tipos del nivel ({string.Join(",", kinds)})");
			Require(EnemyScenePreloader.Held.Count == kinds.Count, $"[semilla {seed}] Y solo esos ({EnemyScenePreloader.Held.Count} retenidos)");
			GD.Print($"Semilla {seed}: el nivel precarga {string.Join(", ", kinds)}: OK");
			await BossTestHarness.Close(tree, level);
		}
	}
}
