using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de fidelidad del simulador: juega los mismos niveles con el mismo bot (mismo cerebro y misma semilla) en el
/// simulador y en el juego real (TestLevel, con BotDriver controlando al jugador y vida ilimitada) y compara.
/// Ejecutar en headless a velocidad fija y sin límite de tiempo real:
///   godot --headless --fixed-fps 60 --path . res://tests/FidelityCheck.tscn
/// Argumentos (tras "--"): --count=N (30), --first-seed=N (1), --runs=N (3).
/// Puerta de calidad: Spearman ≥ Threshold (0.7) en daño total y en tiempo total; sale con código 0 si se supera.
/// </summary>
public partial class FidelityCheck : Node
{
	[Export] public int LevelCount { get; set; } = 30;
	[Export] public int FirstSeed { get; set; } = 1;
	[Export] public int BotRuns { get; set; } = 3;
	[Export] public float MaxSeconds { get; set; } = 600.0f;
	[Export] public double Threshold { get; set; } = 0.7;

	private sealed class LevelComparison
	{
		public int Seed;
		public SimLevel Level;
		public readonly List<SimulationResult> Simulated = new();
		public readonly List<SimulationResult> Real = new();
	}

	public override async void _Ready()
	{
		ReadArguments();
		GameplayParameters parameters = GameplayParameters.FromScenes();
		PackedScene levelScene = GD.Load<PackedScene>(GameplayParameters.LevelScenePath);
		BotSettings settings = new();
		List<LevelComparison> levels = new();
		Stopwatch clock = Stopwatch.StartNew();

		for (int seed = FirstSeed; seed < FirstSeed + LevelCount; seed++)
		{
			LevelComparison comparison = new() { Seed = seed };
			levels.Add(comparison);

			for (int run = 0; run < BotRuns; run++)
			{
				RunManager.SetNextSeed(seed);
				Node level = levelScene.Instantiate();
				HealthComponent.FindIn(level.GetNode("Player")).MaxHealth = 1.0e9f;
				AddChild(level);

				RoomManager manager = level.GetNode<RoomManager>("RoomManager");
				comparison.Level ??= SimLevel.From(manager.Map, parameters);
				ulong botSeed = BotBrain.SeedFor(seed, run);

				comparison.Simulated.Add(new LevelSimulator(parameters, comparison.Level,
					new BotBrain(comparison.Level, settings, botSeed)).Run(MaxSeconds));

				BotDriver driver = new();
				AddChild(driver);
				TaskCompletionSource<SimulationResult> finished = new();
				driver.Finished += result => finished.TrySetResult(result);
				driver.Begin(manager, comparison.Level, new BotBrain(comparison.Level, settings, botSeed),
					parameters.PlayerMaxHealth, MaxSeconds);

				if (seed == _traceSeed && run == _traceRun)
				{
					Trace(manager, finished.Task);
				}

				comparison.Real.Add(await finished.Task);

				driver.QueueFree();
				level.QueueFree();
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			GD.Print($"semilla {seed}: sim daño {Mean(comparison.Simulated, r => r.DamageTotal):0.0} " +
				$"tiempo {Mean(comparison.Simulated, r => r.TimeTotal):0.0} | real daño {Mean(comparison.Real, r => r.DamageTotal):0.0} " +
				$"tiempo {Mean(comparison.Real, r => r.TimeTotal):0.0}");
		}

		bool passed = Report(levels, clock.Elapsed.TotalSeconds);
		GetTree().Quit(passed ? 0 : 1);
	}

	// Diagnóstico: --trace=SEMILLA:REPETICIÓN imprime cada segundo el estado de esa partida en el juego real.
	private async void Trace(RoomManager manager, Task finished)
	{
		PlayerController player = manager.Player;
		for (int frame = 0; !finished.IsCompleted; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			RoomController room = manager.CurrentRoom;
			if (frame % 60 != 0 || finished.IsCompleted || room == null || !IsInstanceValid(room))
			{
				continue;
			}

			string enemies = string.Join(" ", room.GetNode("Enemies").GetChildren().OfType<Node3D>()
				.Select(enemy => $"({F(enemy.GlobalPosition.X)},{F(enemy.GlobalPosition.Y)},{F(enemy.GlobalPosition.Z)} vida {F(HealthComponent.FindIn(enemy).CurrentHealth)})"));
			GD.Print($"  traza t={F(frame / 60.0)} sala {room.Data.Cell} {room.State} jugador ({F(player.GlobalPosition.X)},{F(player.GlobalPosition.Y)},{F(player.GlobalPosition.Z)}) " +
				$"lágrimas {GetTree().GetNodesInGroup("tears").Count} enemigos {enemies}");
		}
	}

	private int _traceSeed = int.MinValue;
	private int _traceRun = -1;

	private void ReadArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (argument.StartsWith("--trace="))
			{
				string[] parts = argument["--trace=".Length..].Split(':');
				_traceSeed = int.Parse(parts[0], CultureInfo.InvariantCulture);
				_traceRun = int.Parse(parts[1], CultureInfo.InvariantCulture);
			}

			if (argument.StartsWith("--count=") && int.TryParse(argument["--count=".Length..], out int count))
			{
				LevelCount = count;
			}
			else if (argument.StartsWith("--first-seed=") && int.TryParse(argument["--first-seed=".Length..], out int first))
			{
				FirstSeed = first;
			}
			else if (argument.StartsWith("--runs=") && int.TryParse(argument["--runs=".Length..], out int runs))
			{
				BotRuns = runs;
			}
		}
	}

	private bool Report(List<LevelComparison> levels, double seconds)
	{
		(string Name, Func<SimulationResult, double> Value)[] measures =
		{
			("damage_total", r => r.DamageTotal),
			("damage_critical", r => r.DamageCritical),
			("time_total", r => r.TimeTotal),
			("time_to_boss", r => r.TimeToBoss),
		};

		int simTimeouts = levels.Sum(level => level.Simulated.Count(r => r.TimedOut));
		int realTimeouts = levels.Sum(level => level.Real.Count(r => r.TimedOut));

		GD.Print("");
		GD.Print($"=== Fidelidad simulador vs juego real: {levels.Count} niveles × {BotRuns} bots ({seconds:0} s) ===");
		GD.Print($"Partidas que agotaron el tiempo: simulador {simTimeouts}, juego real {realTimeouts}");
		GD.Print($"{"medida",-18}{"Pearson",10}{"Spearman",10}{"media sim",12}{"media real",12}{"sesgo",10}{"|dif| media",13}");

		double damageSpearman = 0.0;
		double timeSpearman = 0.0;
		foreach ((string name, Func<SimulationResult, double> value) in measures)
		{
			double[] sim = levels.Select(level => level.Simulated.Average(value)).ToArray();
			double[] real = levels.Select(level => level.Real.Average(value)).ToArray();
			double pearson = SampleStatistics.Pearson(sim, real);
			double spearman = SampleStatistics.Spearman(sim, real);
			double bias = sim.Zip(real, (s, r) => s - r).Average();
			double absolute = sim.Zip(real, (s, r) => Math.Abs(s - r)).Average();

			GD.Print($"{name,-18}{F(pearson),10}{F(spearman),10}{F(sim.Average()),12}{F(real.Average()),12}{F(bias),10}{F(absolute),13}");

			damageSpearman = name == "damage_total" ? spearman : damageSpearman;
			timeSpearman = name == "time_total" ? spearman : timeSpearman;
		}

		PrintTemplateBreakdown(levels);

		bool passed = damageSpearman >= Threshold && timeSpearman >= Threshold;
		GD.Print("");
		GD.Print($"PUERTA DE CALIDAD (Spearman ≥ {F(Threshold)} en daño y tiempo): {(passed ? "SUPERADA" : "NO SUPERADA")} " +
			$"(daño {F(damageSpearman)}, tiempo {F(timeSpearman)})");

		if (!passed || simTimeouts > 0 || realTimeouts > 0)
		{
			PrintWorstLevels(levels);
		}

		return passed;
	}

	// Por plantilla: duración del combate (despejar − entrar) y daño recibido en la sala, simulador frente a real.
	private static void PrintTemplateBreakdown(List<LevelComparison> levels)
	{
		Dictionary<string, (List<double> SimTime, List<double> RealTime, List<double> SimDamage, List<double> RealDamage)> byTemplate = new();

		foreach (LevelComparison level in levels)
		{
			for (int run = 0; run < level.Simulated.Count; run++)
			{
				Dictionary<Vector2I, RoomVisit> real = level.Real[run].Visits.ToDictionary(visit => visit.Cell);
				foreach (RoomVisit sim in level.Simulated[run].Visits)
				{
					if (!real.TryGetValue(sim.Cell, out RoomVisit match) || sim.ClearTime < 0.0f || match.ClearTime < 0.0f)
					{
						continue;
					}

					string template = level.Level[sim.Cell].TemplateId;
					if (!byTemplate.TryGetValue(template, out var lists))
					{
						lists = (new List<double>(), new List<double>(), new List<double>(), new List<double>());
						byTemplate[template] = lists;
					}

					lists.SimTime.Add(sim.ClearTime - sim.EnterTime);
					lists.RealTime.Add(match.ClearTime - match.EnterTime);
					lists.SimDamage.Add(sim.Damage);
					lists.RealDamage.Add(match.Damage);
				}
			}
		}

		GD.Print("");
		GD.Print("Desglose por plantilla (primera visita de cada sala; tiempo = despejar − entrar):");
		GD.Print($"{"plantilla",-18}{"n",5}{"t sim",9}{"t real",9}{"daño sim",10}{"daño real",11}{"ρ tiempo",10}{"ρ daño",9}");
		foreach (string template in byTemplate.Keys.OrderBy(key => key, StringComparer.Ordinal))
		{
			var lists = byTemplate[template];
			GD.Print($"{template,-18}{lists.SimTime.Count,5}{F(lists.SimTime.Average()),9}{F(lists.RealTime.Average()),9}" +
				$"{F(lists.SimDamage.Average()),10}{F(lists.RealDamage.Average()),11}" +
				$"{F(SampleStatistics.Spearman(lists.SimTime, lists.RealTime)),10}{F(SampleStatistics.Spearman(lists.SimDamage, lists.RealDamage)),9}");
		}
	}

	private static void PrintWorstLevels(List<LevelComparison> levels)
	{
		GD.Print("");
		GD.Print("Niveles con más diferencia de daño, sala por sala (la repetición con más diferencia):");
		foreach (LevelComparison level in levels
			.OrderByDescending(level => Math.Abs(level.Simulated.Average(r => r.DamageTotal) - level.Real.Average(r => r.DamageTotal)))
			.Take(5))
		{
			int run = Enumerable.Range(0, level.Simulated.Count)
				.OrderByDescending(i => Math.Abs(level.Simulated[i].DamageTotal - level.Real[i].DamageTotal))
				.First();
			GD.Print($"  semilla {level.Seed}, repetición {run}: real {(level.Real[run].TimedOut ? "AGOTÓ EL TIEMPO" : "terminó")}");
			Dictionary<Vector2I, RoomVisit> real = level.Real[run].Visits.ToDictionary(visit => visit.Cell);
			foreach (RoomVisit sim in level.Simulated[run].Visits)
			{
				real.TryGetValue(sim.Cell, out RoomVisit match);
				SimLevel.Room room = level.Level[sim.Cell];
				GD.Print($"    {sim.Cell} {room.TemplateId,-16} sim: entra {F(sim.EnterTime)} despeja {F(sim.ClearTime)} daño {F(sim.Damage)}" +
					$" | real: entra {F(match?.EnterTime ?? -1)} despeja {F(match?.ClearTime ?? -1)} daño {F(match?.Damage ?? -1)}");
			}
		}
	}

	private static double Mean(List<SimulationResult> results, Func<SimulationResult, double> value)
	{
		return results.Average(value);
	}

	private static string F(double value)
	{
		return value.ToString("0.000", CultureInfo.InvariantCulture);
	}
}
