using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Sonda de medición (no es una prueba de regresión): separa la espera fija de aparición (SpawnDelay) del coste de cargar e
/// instanciar las escenas de enemigos. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/SpawnTimingProbe.tscn
/// Parte 1: por cada tipo de enemigo, tiempo de Load, Instantiate y AddChild en frío y en caliente.
/// Parte 2: en salas reales de varias semillas, pasos de física desde que empieza el combate hasta que nacen los enemigos y
///          coste real (reloj de pared) del fotograma en el que nacen, frente a la mediana de los fotogramas de esa sala.
/// Los tiempos son de CPU: en headless no hay GPU, así que la subida de mallas y texturas a la tarjeta gráfica no se mide.
/// Con el argumento propio `-- skip1` se salta la parte 1, para que la parte 2 vea la caché de escenas fría (primera sala de la partida).
/// No forma parte de la suite: solo imprime números y sale con 0 salvo que falle.
/// </summary>
public partial class SpawnTimingProbe : Node
{
	private static readonly int[] Seeds = { 12345, 1, 2, 3, 4, 5 };

	public override async void _Ready()
	{
		try
		{
			SceneTree tree = GetTree();
			if (!OS.GetCmdlineUserArgs().Contains("skip1"))
			{
				ColdWarm();
			}

			await BossTestHarness.Frames(tree, 3);
			foreach (int seed in Seeds)
			{
				await RoomsOfSeed(tree, seed);
			}

			GD.Print("SpawnTimingProbe: fin");
			await BossTestHarness.CollectWrappers(tree);
			tree.Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GetTree().Quit(1);
		}
	}

	private static double Ms(ulong from, ulong to) => (to - from) / 1000.0;

	private void ColdWarm()
	{
		GD.Print("--- Parte 1: Load / Instantiate / AddChild por tipo (ms) ---");
		GD.Print("tipo        ya_en_cache  Load_frio  Inst_frio  Add_frio | Load_calie  Inst_calie  Add_calie");
		Node holder = new();
		AddChild(holder);
		foreach (EnemyKinds.Info info in EnemyKinds.List)
		{
			bool cached = ResourceLoader.HasCached(info.ScenePath);
			ulong t0 = Time.GetTicksUsec();
			PackedScene scene = EnemyKinds.Scene(info.Kind);
			ulong t1 = Time.GetTicksUsec();
			Node3D first = scene.Instantiate<Node3D>();
			ulong t2 = Time.GetTicksUsec();
			holder.AddChild(first);
			ulong t3 = Time.GetTicksUsec();
			first.QueueFree();

			ulong w0 = Time.GetTicksUsec();
			PackedScene again = EnemyKinds.Scene(info.Kind);
			ulong w1 = Time.GetTicksUsec();
			Node3D second = again.Instantiate<Node3D>();
			ulong w2 = Time.GetTicksUsec();
			holder.AddChild(second);
			ulong w3 = Time.GetTicksUsec();
			second.QueueFree();

			GD.Print($"{info.Name,-11} {cached,-12} {Ms(t0, t1),9:F2}  {Ms(t1, t2),9:F2}  {Ms(t2, t3),8:F2} | {Ms(w0, w1),9:F3}  {Ms(w1, w2),10:F2}  {Ms(w2, w3),9:F2}");
		}

		holder.QueueFree();
	}

	private async Task RoomsOfSeed(SceneTree tree, int seed)
	{
		BossTestHarness.Level level = BossTestHarness.Open(this, seed, withMother: false);
		await BossTestHarness.Frames(tree, 3);
		GD.Print($"--- Parte 2: semilla {seed} ---");
		List<DoorSide> path = BossTestHarness.PathToBoss(level.Rooms.Map);

		foreach (DoorSide side in path)
		{
			RoomController room = level.Rooms.CurrentRoom;
			Vector2I expected = room.Data.Cell + side.GridOffset();
			room.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);

			RoomController target = null;
			Node enemies = null;
			int combatTick = -1;
			int tick = 0;
			ulong last = Time.GetTicksUsec();
			List<double> gaps = new();
			double spike = 0.0;
			int spawnTick = -1;
			int count = 0;
			List<string> kinds = new();

			for (int i = 0; i < 400; i++)
			{
				await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
				ulong now = Time.GetTicksUsec();
				double gap = Ms(last, now);
				last = now;
				tick++;

				if (GodotObject.IsInstanceValid(level.Rooms.CurrentRoom) && level.Rooms.CurrentRoom.Data.Cell == expected && target == null && level.Rooms.Player.IsPhysicsProcessing())
				{
					target = level.Rooms.CurrentRoom;
					enemies = target.GetNode("Enemies");
				}

				if (target != null && !GodotObject.IsInstanceValid(target))
				{
					break;
				}

				if (target != null && combatTick < 0 && target.State == RoomState.Combat)
				{
					combatTick = tick;
					gaps.Clear();
				}

				if (combatTick >= 0 && spawnTick < 0)
				{
					if (enemies.GetChildCount() > 0)
					{
						spawnTick = tick;
						spike = gap;
						count = enemies.GetChildCount();
						foreach (Node child in enemies.GetChildren())
						{
							kinds.Add(child is EnemyBase enemy ? enemy.Kind.ToString() : child.Name.ToString());
						}

						break;
					}

					gaps.Add(gap);
				}
			}

			if (target == null)
			{
				GD.Print($"  sala {expected}: no se llegó a entrar");
				continue;
			}

			if (spawnTick < 0)
			{
				GD.Print($"  sala {expected} ({target.Data.Type}): sin enemigos / sin combate");
				await BossTestHarness.Frames(tree, 2);
				continue;
			}

			double[] sorted = gaps.OrderBy(g => g).ToArray();
			double median = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0.0;
			int frames = spawnTick - combatTick;
			GD.Print($"  sala {expected} ({target.Data.Type}): {count} enemigos [{string.Join(",", kinds.Distinct())}] "
				+ $"nacen a los {frames} fotogramas del combate (~{frames / 60.0 * 1000.0:F0} ms de juego); "
				+ $"fotograma de aparición {spike:F2} ms vs mediana {median:F2} ms (extra {spike - median:F2} ms)");
			await BossTestHarness.Frames(tree, 2);
		}

		await BossTestHarness.Close(tree, level);
	}
}
