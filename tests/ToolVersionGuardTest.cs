using Godot;

/// <summary>
/// Comprueba que las herramientas de datos de la IA rechazan --generator=3 mientras no exista la IA v3. Modo headless:
///   godot --headless --path . res://tests/ToolVersionGuardTest.tscn
/// 1. ToolVersionGuard acepta las versiones 1 y 2 y rechaza la 0 y la 3.
/// 2. Ejecuta de verdad DatasetExport, LabelExport, FidelityCheck y DirectorBenchmark con --generator=3, cada una como un
///    proceso de Godot aparte (de uno en uno), y exige que salgan con código distinto de 0 y con el error claro.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class ToolVersionGuardTest : Node
{
	private static readonly string[] Tools = { "DatasetExport", "LabelExport", "FidelityCheck", "DirectorBenchmark" };

	public override void _Ready()
	{
		bool ok = true;
		GD.Print("=== 1. ToolVersionGuard ===");
		foreach ((int version, bool expected) in new[] { (0, false), (1, true), (2, true), (3, false) })
		{
			bool supported = ToolVersionGuard.IsSupported(version);
			GD.Print($"  versión {version}: {(supported ? "aceptada" : "rechazada")} {(supported == expected ? "(ok)" : "(MAL)")}");
			ok &= supported == expected;
		}

		GD.Print("\n=== 2. Las herramientas rechazan --generator=3 ===");
		string project = ProjectSettings.GlobalizePath("res://");
		foreach (string tool in Tools)
		{
			Godot.Collections.Array output = new();
			string[] arguments = { "--headless", "--path", project, $"res://tests/{tool}.tscn", "--", "--generator=3" };
			int exitCode = OS.Execute(OS.GetExecutablePath(), arguments, output, readStderr: true);
			// En Windows llega UTF-8 leído como Latin-1 ("versiÃ³n"); se reinterpreta para que el informe se lea bien.
			string text = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.Latin1.GetBytes(string.Join("\n", output)));
			bool rejected = exitCode != 0 && text.Contains(ToolVersionGuard.RejectionMarker);
			string message = System.Array.Find(text.Split('\n'), line => line.Contains(ToolVersionGuard.RejectionMarker))?.Trim() ?? "(sin mensaje)";
			GD.Print($"  {tool}: código de salida {exitCode}; {(rejected ? "rechaza la v3" : "NO la rechaza")} -> {message}");
			ok &= rejected;
		}

		GD.Print(ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(ok ? 0 : 1);
	}
}
