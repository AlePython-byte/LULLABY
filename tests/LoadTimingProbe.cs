using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Sonda de medición (no es una prueba de regresión): cuánto tarda en estar lista la primera sala de una partida desde que se carga el nivel,
/// y los fotogramas largos que hay por el camino. Pensada para ejecutarse CON ventana (es lo que ve el jugador):
///   godot --path . --resolution 1152x648 res://tests/LoadTimingProbe.tscn
/// Con `-- warm` ejecuta antes los precalentamientos del menú (ambientación y enemigos), como en una partida que sale del menú.
/// Imprime una línea por semilla: milisegundos hasta que la primera sala está construida, milisegundos hasta que la pantalla de carga (si la
/// hay) desaparece, el fotograma más largo y cuántos pasan de 50 ms (un tirón perceptible), todo medido en reloj de pared.
/// </summary>
public partial class LoadTimingProbe : Node
{
	public override async void _Ready()
	{
		try
		{
			if (OS.GetCmdlineUserArgs().Contains("warm"))
			{
				// Como el menú: los precalentamientos de la ambientación y de los enemigos antes de empezar la partida.
				DecorWarmupRunner decor = DecorWarmupRunner.Attach(this);
				EnemyWarmupRunner enemies = EnemyWarmupRunner.Attach(this);
				for (int i = 0; i < 3000 && ((decor != null && !decor.Finished) || (enemies != null && !enemies.Finished)); i++)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				}

				GD.Print("CARGA precalentamiento del menú terminado");
			}

			foreach (int seed in new[] { 12345, 3, 77 })
			{
				await Measure(seed);
			}
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
		}

		GetTree().Quit();
	}

	private async System.Threading.Tasks.Task Measure(int seed)
	{
		RunManager.SetNextSeed(seed);
		Node level = GD.Load<PackedScene>("res://scenes/levels/TestLevel.tscn").Instantiate();
		RoomManager rooms = level.GetNode<RoomManager>("RoomManager");
		SceneTree tree = GetTree();
		ulong t0 = Time.GetTicksUsec();
		AddChild(level);
		ulong afterAdd = Time.GetTicksUsec();

		ulong last = afterAdd;
		double max = (afterAdd - t0) / 1000.0;
		int over50 = max > 50.0 ? 1 : 0;
		double ready = rooms.CurrentRoom != null ? (afterAdd - t0) / 1000.0 : -1.0;
		double overlayGone = -1.0;
		for (int i = 0; i < 400; i++)
		{
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			ulong now = Time.GetTicksUsec();
			double gap = (now - last) / 1000.0;
			last = now;
			max = Math.Max(max, gap);
			if (gap > 50.0) over50++;
			double elapsed = (now - t0) / 1000.0;
			if (ready < 0.0 && rooms.CurrentRoom != null) ready = elapsed;
			if (ready >= 0.0 && overlayGone < 0.0 && level.FindChild("LoadingScreen", true, false) == null) overlayGone = elapsed;
			if (overlayGone >= 0.0 && elapsed > overlayGone + 700.0) break;
		}

		GD.Print($"CARGA semilla {seed}: _Ready del nivel {(afterAdd - t0) / 1000.0:F0} ms; primera sala lista a los {ready:F0} ms; "
			+ $"pantalla de carga fuera a los {overlayGone:F0} ms; fotograma más largo {max:F0} ms; fotogramas de más de 50 ms: {over50}; "
			+ $"recolección de basura previa {SceneChangeGuard.LastMilliseconds:F1} ms");
		level.QueueFree();
		await ToSignal(level, Node.SignalName.TreeExited);
		await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}
}
