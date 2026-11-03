using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba del precalentamiento de los enemigos (EnemyWarmup y EnemyWarmupRunner). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/EnemyWarmupTest.tscn
///   1. Desde cero termina solo: un enemigo de cada uno de los 6 tipos se carga (EnemyScenePreloader), se instancia, se revela y se dibuja
///      en un visor propio; los seis llegaron a estar en el visor y a aparecer (Spawning); después el runner y el visor desaparecen y no
///      queda ningún enemigo ni voladores en el grupo; el aleteo no llega a sonar.
///   2. Idempotencia: otro intento no cuelga otro runner.
///   3. Espera al precalentamiento de la ambientación si hay uno en marcha (un nodo hermano DecorWarmup) y sigue cuando acaba.
///   4. El menú principal lo lanza; con EnemyWarmup.Enabled = false no se lanza nada.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class EnemyWarmupTest : Node
{
	private const int MaxFrames = 1500;

	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== EnemyWarmupTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			await CheckFromScratch();
			await CheckWaitsForDecor();
			await CheckMenuAndDisabled();
			GD.Print($"EnemyWarmupTest: {_checks} comprobaciones. RESULTADO: OK");
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
		finally
		{
			EnemyWarmup.Enabled = true;
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private async Task WaitFinished(EnemyWarmupRunner runner)
	{
		for (int i = 0; i < MaxFrames && !runner.Finished; i++)
		{
			await BossTestHarness.Frames(GetTree(), 1);
		}

		Require(runner.Finished, $"El precalentamiento de enemigos no terminó en {MaxFrames} fotogramas");
	}

	private async Task CheckFromScratch()
	{
		GD.Print("\n--- Desde cero ---");
		SceneTree tree = GetTree();
		EnemyScenePreloader.Reset();
		EnemyWarmup.Reset();
		AudioManager.Instance.ClearRequests();
		Require(!EnemyWarmup.Done && EnemyWarmup.Warmed.Count == 0 && EnemyScenePreloader.Held.Count == 0, "Parte de cero");

		EnemyWarmupRunner runner = EnemyWarmupRunner.Attach(this);
		Require(runner != null && GetNodeOrNull(EnemyWarmupRunner.NodeName) == runner, "Attach cuelga el runner");

		int most = 0;
		bool sawSpawning = false;
		for (int i = 0; i < MaxFrames && !runner.Finished; i++)
		{
			await BossTestHarness.Frames(tree, 1);
			if (tree.Root.FindChild("EnemyWarmupViewport", true, false) is Node viewport)
			{
				var enemies = viewport.GetChildren().OfType<EnemyBase>().ToList();
				most = Math.Max(most, enemies.Count);
				sawSpawning |= enemies.Any(enemy => enemy.Spawning);
			}
		}

		Require(runner.Finished, "Termina solo");
		await BossTestHarness.Frames(tree, 2);
		Require(most == EnemyKinds.List.Count, $"Los {EnemyKinds.List.Count} tipos llegaron a estar en el visor a la vez ({most})");
		Require(sawSpawning, "Los enemigos del visor se revelaron y aparecieron (Spawning)");
		Require(EnemyWarmup.Done && EnemyWarmup.Failed == 0, "Marcado como hecho y sin fallos");
		Require(EnemyKinds.List.All(info => EnemyWarmup.Warmed.Contains(info.Kind)), "Los seis tipos quedan calentados");
		Require(EnemyKinds.List.All(info => EnemyScenePreloader.TryGet(info.Kind) != null), "Y sus escenas retenidas");
		Require(!GodotObject.IsInstanceValid(runner), "El runner desaparece al terminar");
		Require(tree.Root.FindChild("EnemyWarmupViewport", true, false) == null, "El visor se libera");
		Require(tree.GetNodesInGroup(AudioManager.FlyerGroup).Count == 0, "No queda ningún volador en el grupo");
		Require(AudioManager.Instance.Requests.All(request => !request.Id.StartsWith(SoundCatalog.FlyerWings)), "El aleteo no suena durante el precalentamiento");
		Require(AudioManager.Instance.Requests.All(request => request.Id != SoundCatalog.EnemySpawns), "Y el sonido de aparición se carga pero no suena");
		SoundCatalog.Sound spawnSound = SoundCatalog.Get(SoundCatalog.EnemySpawns);
		Require(spawnSound.Fallback == null, "enemigo_aparece no tiene sonido de respaldo: sin archivo es silencio");
		Require(!ResourceLoader.Exists(spawnSound.Path) || ResourceLoader.HasCached(spawnSound.Path), "Si el archivo existe, queda cargado; si no, se ignora sin error");
		GD.Print("Seis tipos calentados, visor liberado, sin sonido: OK");

		Require(EnemyWarmupRunner.Attach(this) == null, "Ya hecho: no cuelga otro runner");
		Require(GetNodeOrNull(EnemyWarmupRunner.NodeName) == null, "Idempotente");
	}

	private async Task CheckWaitsForDecor()
	{
		GD.Print("\n--- Espera a la ambientación ---");
		SceneTree tree = GetTree();
		EnemyWarmup.Reset();
		bool decorWasDone = DecorWarmup.Done;
		Require(!DecorWarmup.Done && DecorWarmup.Enabled, "La ambientación no está hecha");

		Node decor = new() { Name = DecorWarmupRunner.NodeName };
		AddChild(decor);
		EnemyWarmupRunner runner = EnemyWarmupRunner.Attach(this);
		Require(runner != null, "Se cuelga con la ambientación en marcha");
		await BossTestHarness.Frames(tree, 40);
		Require(!runner.Finished && tree.Root.FindChild("EnemyWarmupViewport", true, false) == null, "Mientras la ambientación sigue en marcha, no empieza");

		decor.QueueFree();
		await WaitFinished(runner);
		Require(EnemyWarmup.Done && EnemyWarmup.Warmed.Count == EnemyKinds.List.Count, "Al acabar la ambientación, continúa y termina");
		Require(DecorWarmup.Done == decorWasDone, "Sin tocar el estado de la ambientación");
		GD.Print("Espera a la ambientación: OK");
	}

	private async Task CheckMenuAndDisabled()
	{
		GD.Print("\n--- Menú y apagado ---");
		EnemyWarmup.Reset();
		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		AddChild(menu);
		Require(menu.GetNodeOrNull<EnemyWarmupRunner>(EnemyWarmupRunner.NodeName) != null, "El menú lanza el precalentamiento de enemigos");
		menu.QueueFree();
		await ToSignal(menu, Node.SignalName.TreeExited);

		EnemyWarmup.Reset();
		EnemyWarmup.Enabled = false;
		Require(EnemyWarmupRunner.Attach(this) == null, "Con EnemyWarmup.Enabled = false no se lanza nada");
		MainMenu quiet = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		AddChild(quiet);
		Require(quiet.GetNodeOrNull(EnemyWarmupRunner.NodeName) == null, "Ni desde el menú");
		quiet.QueueFree();
		await ToSignal(quiet, Node.SignalName.TreeExited);
		EnemyWarmup.Enabled = true;
		GD.Print("Menú y apagado: OK");
	}
}
