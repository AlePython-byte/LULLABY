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
/// Argumentos (tras "--"): --count=N (30), --first-seed=N (1), --runs=N (3), --version=N (1).
/// Puerta de calidad: Spearman ≥ Threshold (0.7) en daño total y en tiempo total; sale con código 0 si se supera.
///
/// --version=2 (o --generator=2): los niveles son los del GENERADOR v2 con las semillas FirstSeed.. (sin director: se
/// fijan en el juego real con RunManager.SetNextMap). Se añaden el desglose por fuente de daño (contacto, proyectil,
/// explosión: golpes, daño, sesgo relativo e intervalo del 95 % por bootstrap sobre niveles) y por composición de sala.
/// Criterio de aprobación v2, fijado ANTES de ver los datos de la prueba de 90 niveles:
///   a) Spearman de daño total y de tiempo total ≥ Threshold (0.7).
///   b) Explosión y proyectiles: con al menos MinHitsForEvidence (30) golpes reales de esa fuente, |sesgo| ≤ 30 %; con
///      menos, "sin evidencia suficiente" (no bloquea; se documenta como limitación).
///   c) Contacto: el sesgo no puede ser peor que el de v1 (V1ContactBias, aprox. −15 %) en más de 10 puntos: |sesgo| ≤ 25 %.
///   d) Sesgo de TIEMPO por sala en cada composición dentro de ±30 % (el sesgo de daño por composición es informativo).
/// sesgo = (total simulador − total real) / total real. Sin --version, la prueba es la de siempre (niveles del juego con
/// el director y RunManager.SetNextSeed).
/// </summary>
public partial class FidelityCheck : Node
{
	private const string CatalogPath = "res://resources/rooms/templates/Catalog.tres";

	[Export] public int LevelCount { get; set; } = 30;
	[Export] public int FirstSeed { get; set; } = 1;
	[Export] public int BotRuns { get; set; } = 3;
	[Export] public float MaxSeconds { get; set; } = 600.0f;
	[Export] public double Threshold { get; set; } = 0.7;
	[Export] public int Version { get; set; } = 1;

	// Criterio de aprobación v2 (ver el resumen de la clase). Fijado antes de ver los datos de la prueba de 90 niveles.
	private const double MaxCompositionTimeBias = 0.30;
	private const int MinHitsForEvidence = 30;
	private const double MaxNewSourceBias = 0.30;
	private const double V1ContactBias = -0.15;
	private const double MaxContactWorsening = 0.10;
	private const int BootstrapResamples = 2000;
	private const int BootstrapSeed = 20261029;

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
		DungeonGenerator generator = new(GD.Load<RoomCatalog>(CatalogPath));
		DungeonGenerator.Settings generatorSettings = new() { GeneratorVersion = Version };

		for (int seed = FirstSeed; seed < FirstSeed + LevelCount; seed++)
		{
			LevelComparison comparison = new() { Seed = seed };
			levels.Add(comparison);

			for (int run = 0; run < BotRuns; run++)
			{
				if (Version >= 2)
				{
					RunManager.SetNextMap(generator.Generate(seed, generatorSettings).Map);
				}
				else
				{
					// El defecto de RunManager es la v2: este modo mide el juego con la v1, así que la pide.
					RunManager.SetNextGeneratorVersion(DungeonGenerator.OriginalVersion);
					RunManager.SetNextSeed(seed);
				}
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
			else if (argument.StartsWith("--version=") && int.TryParse(argument["--version=".Length..], out int version))
			{
				Version = version;
			}
			else if (argument.StartsWith("--generator=") && int.TryParse(argument["--generator=".Length..], out int generator))
			{
				Version = generator;
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

		if (Version >= 2)
		{
			GD.Print($"  criterio a) Spearman ≥ {F(Threshold)}: {(passed ? "CUMPLE" : "NO CUMPLE")}");
			bool compositionPassed = PrintCompositionBreakdown(levels);
			bool sourcesPassed = PrintSourceBreakdown(levels);
			GD.Print("");
			GD.Print($"  criterio d) |sesgo de tiempo| ≤ {MaxCompositionTimeBias:P0} en cada composición: {(compositionPassed ? "CUMPLE" : "NO CUMPLE")}");
			passed &= compositionPassed && sourcesPassed;
			GD.Print($"CRITERIO DE APROBACIÓN v2 (a, b, c, d): {(passed ? "SUPERADO" : "NO SUPERADO")}");
		}

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

	// Por composición de sala: primera visita, misma repetición del bot en simulador y juego real.
	private bool PrintCompositionBreakdown(List<LevelComparison> levels)
	{
		Dictionary<string, (List<double> SimTime, List<double> RealTime, List<double> SimDamage, List<double> RealDamage)> rows = new();
		Dictionary<string, (double[] Sim, double[] Real)> sources = new();
		Dictionary<string, (double[] Sim, double[] Real)> contactKinds = new();
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

					foreach (string category in RoomComposition.Of(level.Level[sim.Cell]))
					{
						if (!rows.TryGetValue(category, out var lists))
						{
							lists = (new List<double>(), new List<double>(), new List<double>(), new List<double>());
							rows[category] = lists;
						}

						lists.SimTime.Add(sim.ClearTime - sim.EnterTime);
						lists.RealTime.Add(match.ClearTime - match.EnterTime);
						lists.SimDamage.Add(sim.Damage);
						lists.RealDamage.Add(match.Damage);

						if (!sources.TryGetValue(category, out var bySource))
						{
							bySource = (new double[3], new double[3]);
							sources[category] = bySource;
						}

						for (int source = 0; source < 3; source++)
						{
							bySource.Sim[source] += sim.DamageBySource[source];
							bySource.Real[source] += match.DamageBySource[source];
						}

						if (!contactKinds.TryGetValue(category, out var byKind))
						{
							byKind = (new double[4], new double[4]);
							contactKinds[category] = byKind;
						}

						for (int kind = 0; kind < 4; kind++)
						{
							byKind.Sim[kind] += sim.ContactByKind[kind];
							byKind.Real[kind] += match.ContactByKind[kind];
						}
					}
				}
			}
		}

		GD.Print("");
		GD.Print("Daño por fuente y composición (total de las primeras visitas; sesgo relativo):");
		GD.Print($"{"composición",-16}" + string.Concat(Enum.GetNames<DamageSource>().Select(name => $"{name + " sim/real",-26}")));
		foreach (string category in RoomComposition.Categories)
		{
			if (sources.TryGetValue(category, out var bySource))
			{
				GD.Print($"{category,-16}" + string.Concat(Enumerable.Range(0, 3).Select(source =>
					$"{$"{bySource.Sim[source]:0} / {bySource.Real[source]:0} ({Bias(bySource.Sim[source], bySource.Real[source])})",-26}")));
			}
		}

		GD.Print("");
		GD.Print("Daño por contacto según el tipo del enemigo que golpea (juego real: el enemigo más cercano en ese momento):");
		GD.Print($"{"composición",-16}" + string.Concat(Enum.GetNames<EnemyKind>().Select(name => $"{name + " sim/real",-26}")));
		foreach (string category in RoomComposition.Categories)
		{
			if (contactKinds.TryGetValue(category, out var byKind))
			{
				GD.Print($"{category,-16}" + string.Concat(Enumerable.Range(0, 4).Select(kind =>
					$"{$"{byKind.Sim[kind]:0} / {byKind.Real[kind]:0} ({Bias(byKind.Sim[kind], byKind.Real[kind])})",-26}")));
			}
		}

		GD.Print("");
		GD.Print("Desglose por composición de sala (primera visita; una sala mixta cuenta en cada tipo que contiene;");
		GD.Print("sesgo = (media sim − media real) / media real):");
		GD.Print($"{"composición",-16}{"n",5}{"t sim",9}{"t real",9}{"sesgo t",10}{"daño sim",10}{"daño real",11}{"sesgo daño",12}{"ρ tiempo",10}{"ρ daño",9}");
		bool passed = true;
		foreach (string category in RoomComposition.Categories)
		{
			if (!rows.TryGetValue(category, out var lists))
			{
				GD.Print($"{category,-16}    0");
				continue;
			}

			double timeBias = RelativeBias(lists.SimTime, lists.RealTime);
			double damageBias = RelativeBias(lists.SimDamage, lists.RealDamage);
			bool ok = Math.Abs(timeBias) <= MaxCompositionTimeBias;
			passed &= ok;
			GD.Print($"{category,-16}{lists.SimTime.Count,5}{F(lists.SimTime.Average()),9}{F(lists.RealTime.Average()),9}{timeBias,10:P1}" +
				$"{F(lists.SimDamage.Average()),10}{F(lists.RealDamage.Average()),11}{damageBias,12:P1}" +
				$"{F(SampleStatistics.Spearman(lists.SimTime, lists.RealTime)),10}{F(SampleStatistics.Spearman(lists.SimDamage, lists.RealDamage)),9}" +
				(ok ? "" : "   <-- sesgo de tiempo excesivo"));
		}

		return passed;
	}

	// Por fuente de daño, partidas completas: golpes con daño y daño total en simulador y juego real, sesgo relativo e
	// intervalo del 95 % por bootstrap sobre NIVELES (se remuestrean niveles con reemplazo; cada nivel aporta la suma de
	// sus repeticiones del bot). Aplica los criterios b (explosión, proyectiles) y c (contacto).
	private static bool PrintSourceBreakdown(List<LevelComparison> levels)
	{
		GD.Print("");
		GD.Print($"Daño por fuente (partidas completas, {levels.Count} niveles; sesgo = (sim − real) / real; IC 95 % por bootstrap " +
			$"sobre niveles, {BootstrapResamples} remuestreos):");
		GD.Print($"{"fuente",-12}{"golpes sim",12}{"golpes real",13}{"daño sim",10}{"daño real",11}{"sesgo",9}{"IC 95 %",22}  criterio");
		bool passed = true;
		foreach (DamageSource source in Enum.GetValues<DamageSource>())
		{
			int index = (int)source;
			double[] sim = levels.Select(level => level.Simulated.Sum(r => (double)r.DamageBySource[index])).ToArray();
			double[] real = levels.Select(level => level.Real.Sum(r => (double)r.DamageBySource[index])).ToArray();
			int simHits = levels.Sum(level => level.Simulated.Sum(r => r.HitsBySource[index]));
			int realHits = levels.Sum(level => level.Real.Sum(r => r.HitsBySource[index]));
			double bias = real.Sum() > 0.0 ? sim.Sum() / real.Sum() - 1.0 : double.NaN;
			(double low, double high) = BootstrapBias(sim, real);

			string verdict;
			if (source == DamageSource.Contact)
			{
				double limit = Math.Abs(V1ContactBias) + MaxContactWorsening;
				bool ok = !double.IsNaN(bias) && Math.Abs(bias) <= limit;
				passed &= ok;
				verdict = $"c) |sesgo| ≤ {limit:P0} (v1 ≈ {V1ContactBias:P0} + {MaxContactWorsening * 100:0} puntos): {(ok ? "CUMPLE" : "NO CUMPLE")}";
			}
			else if (realHits < MinHitsForEvidence)
			{
				verdict = $"b) {realHits} golpes reales < {MinHitsForEvidence}: SIN EVIDENCIA SUFICIENTE (no bloquea; limitación)";
			}
			else
			{
				bool ok = Math.Abs(bias) <= MaxNewSourceBias;
				passed &= ok;
				verdict = $"b) |sesgo| ≤ {MaxNewSourceBias:P0}: {(ok ? "CUMPLE" : "NO CUMPLE")}";
			}

			string interval = double.IsNaN(low) ? "—" : $"[{low:+0.0%;-0.0%;0.0%}, {high:+0.0%;-0.0%;0.0%}]";
			string biasText = double.IsNaN(bias) ? "—" : $"{bias:+0.0%;-0.0%;0.0%}";
			GD.Print($"{source,-12}{simHits,12}{realHits,13}{sim.Sum(),10:0}{real.Sum(),11:0}{biasText,9}{interval,22}  {verdict}");
		}

		return passed;
	}

	private static (double Low, double High) BootstrapBias(double[] sim, double[] real)
	{
		Random random = new(BootstrapSeed);
		List<double> biases = new();
		for (int sample = 0; sample < BootstrapResamples; sample++)
		{
			double simSum = 0.0, realSum = 0.0;
			for (int i = 0; i < sim.Length; i++)
			{
				int pick = random.Next(sim.Length);
				simSum += sim[pick];
				realSum += real[pick];
			}

			if (realSum > 0.0)
			{
				biases.Add(simSum / realSum - 1.0);
			}
		}

		if (biases.Count < BootstrapResamples / 2)
		{
			return (double.NaN, double.NaN);
		}

		biases.Sort();
		return (biases[(int)(0.025 * (biases.Count - 1))], biases[(int)(0.975 * (biases.Count - 1))]);
	}

	private static string Bias(double simulated, double real)
	{
		return real > 0.0 ? $"{(simulated - real) / real:+0%;-0%;0%}" : simulated > 0.0 ? "real 0" : "—";
	}

	private static double RelativeBias(List<double> simulated, List<double> real)
	{
		double realMean = real.Average();
		double simMean = simulated.Average();
		if (Math.Abs(realMean) < 1e-9)
		{
			return Math.Abs(simMean) < 1e-9 ? 0.0 : double.PositiveInfinity;
		}

		return (simMean - realMean) / realMean;
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
