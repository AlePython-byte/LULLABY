using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

/// <summary>Contrato de cobertura, persistencia y alarmas sin ejecutar combates.</summary>
public partial class FidelityV3ToolsTest : Node
{
	private int _checks;
	public override void _Ready()
	{
		try
		{
			using FidelityV3Protocol protocol = new();
			Check(protocol.Sample("development") == (600001, 30) && protocol.Runs == 3, "Muestra preregistrada");
			Check(protocol.Hash == FidelityV3Protocol.RegisteredHash, "Protocolo intacto");
			Check(protocol.Root.GetProperty("criteria").GetProperty("contact").GetProperty("absolute_relative_bias_max").GetDouble() == 0.25,
				"Umbral leido del protocolo");
			var basic = new GameplayParametersV3.EnemyStats { Kind = EnemyKind.Chaser };
			var runner = new GameplayParametersV3.EnemyStats { Kind = EnemyKind.Runner };
			var digger = new GameplayParametersV3.EnemyStats { Kind = EnemyKind.Digger };
			Check(FidelityV3Protocol.Categories(new[] { basic }).SequenceEqual(new[] { "basic_only" }), "Solo basicos");
			Check(FidelityV3Protocol.Categories(new[] { basic, runner, digger }).SequenceEqual(new[] { "with_runner", "with_digger" }),
				"Sala mixta cuenta en cada tipo, sin contaminar basic_only");
			Check(FidelityV3Protocol.Categories(Array.Empty<GameplayParametersV3.EnemyStats>()).Length == 0, "Sala sin combate no entra en sesgos");

			var monitor = new FidelityV3StallMonitor(60, 0.1f);
			var actors = new[] { new FidelityV3StallMonitor.Actor(7, new Vector2(5, 0), 3) };
			Check(!monitor.Observe(Vector2I.Zero, Vector2.Zero, actors, 0), "Alarma no salta al empezar");
			Check(!monitor.Observe(Vector2I.Zero, Vector2.Zero, actors, 59), "Respeta ventana de 60 s");
			Check(monitor.Observe(Vector2I.Zero, Vector2.Zero, actors, 60), "Atasco estatico detiene ejecucion");
			Check(!monitor.Observe(Vector2I.Zero, new Vector2(0.2f, 0), actors, 61), "Movimiento reinicia ventana");
			var hurt = new[] { actors[0] with { Health = 2 } };
			Check(!monitor.Observe(Vector2I.Zero, new Vector2(0.2f, 0), hurt, 120), "Dano al enemigo es progreso");
			Check(!monitor.Observe(Vector2I.Right, new Vector2(0.2f, 0), hurt, 179), "Transicion es progreso");
			Check(!monitor.Observe(Vector2I.Right, new Vector2(0.2f, 0), Array.Empty<FidelityV3StallMonitor.Actor>(), 238), "Muerte es progreso");

			string path = Path.Combine("build", "fidelity_v3_write_" + Guid.NewGuid().ToString("N") + ".json");
			Directory.CreateDirectory("build");
			FidelityV3Protocol.WriteNew(path, new { Cell = new[] { 1, -2 }, DamageTotal = 3.0 });
			string before = File.ReadAllText(path);
			try { FidelityV3Protocol.WriteNew(path, new { Overwritten = true }); throw new Exception("Sobrescritura permitida"); }
			catch (IOException) { Check(File.ReadAllText(path) == before, "Preserva informes previos"); }
			using var json = JsonDocument.Parse(before);
			Check(json.RootElement.GetProperty("cell")[1].GetInt32() == -2
				&& json.RootElement.GetProperty("damage_total").GetDouble() == 3, "Coordenadas y nombres serializados sin perder informacion");
			File.Delete(path);
			GD.Print($"FidelityV3ToolsTest: {_checks} comprobaciones. RESULTADO: OK (sin combates)");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
}
