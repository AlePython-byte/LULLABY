using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba del precalentamiento de la ambientación (DecorWarmup y DecorWarmupRunner). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/DecorWarmupTest.tscn
///
///   1. Desde el menú principal: el menú lanza el runner, que termina solo; las texturas y los modelos existentes quedan guardados,
///      medidos y con sus triángulos contados (ModelLibrary y FloorPropsPlanner), sin cargas pendientes, y el runner y su SubViewport
///      de dibujo desaparecen.
///   2. Idempotencia: pedir otra vez, medir otra vez o abrir otro menú no repite nada (mismos recuentos) y no cuelga otro runner.
///   3. Tolerancia: desde cero, sin ninguna textura (carpeta vacía) y con una ruta de modelo inexistente, termina sin error, cuenta lo que
///      falta y sigue con lo que hay.
///   4. DecorWarmup.Enabled = false no lanza nada.
/// Antes de salir recolecta los envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class DecorWarmupTest : Node
{
	private const int MaxFrames = 1500;

	private int _checks;

	public override async void _Ready()
	{
		string folder = RoomSurfaces.TextureFolder;
		try
		{
			GD.Print($"=== DecorWarmupTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			await CheckFromMenu();
			await CheckIdempotent();
			CheckBudget();
			await CheckMissingFiles();
			await CheckDisabled();
			GD.Print($"DecorWarmupTest: {_checks} comprobaciones. RESULTADO: OK");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
		finally
		{
			RoomSurfaces.TextureFolder = folder;
			DecorWarmup.ExtraPaths.Clear();
			DecorWarmup.Enabled = true;
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition)
		{
			throw new InvalidDataException(message);
		}
	}

	private async Task WaitFinished(DecorWarmupRunner runner)
	{
		for (int i = 0; i < MaxFrames && !runner.Finished; i++)
		{
			await BossTestHarness.Frames(GetTree(), 1);
		}

		Require(runner.Finished, $"El precalentamiento no terminó en {MaxFrames} fotogramas (quedan {DecorWarmup.Remaining}, medidos {DecorWarmup.Primed})");
	}

	private static string[] ExistingModels() => DecorWarmup.ModelPaths().Where(path => ResourceLoader.Exists(path)).ToArray();

	private static string[] ExistingTextures() => DecorWarmup.TexturePaths().Where(path => ResourceLoader.Exists(path)).ToArray();

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private async Task CheckFromMenu()
	{
		GD.Print("\n--- Desde el menú ---");
		DecorWarmup.Reset();
		Require(!DecorWarmup.Done && DecorWarmup.Requested == 0, "Parte de cero");

		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		AddChild(menu);
		DecorWarmupRunner runner = menu.GetNodeOrNull<DecorWarmupRunner>(DecorWarmupRunner.NodeName);
		Require(runner != null, "El menú lanza el precalentamiento");
		await WaitFinished(runner);
		await BossTestHarness.Frames(GetTree(), 2);

		string[] models = ExistingModels();
		string[] textures = ExistingTextures();
		GD.Print($"  modelos {models.Length}, texturas {textures.Length}, guardados {DecorWarmup.Held.Count}, medidos {DecorWarmup.Primed}, faltan {DecorWarmup.Missing}");
		Require(models.Length > 20, $"Hay modelos de decoración que precalentar ({models.Length})");
		Require(DecorWarmup.Done && DecorWarmup.Remaining == 0 && DecorWarmup.Failed == 0, "Todo cargado, sin elementos pendientes ni fallos");
		Require(DecorWarmup.Held.Count == models.Length + textures.Length, $"Guarda todas las texturas y escenas existentes ({DecorWarmup.Held.Count} de {models.Length + textures.Length})");
		Require(DecorWarmup.Primed == models.Length, "Mide todos los modelos existentes");
		Require(models.All(ModelLibrary.IsLoaded), "Todos los modelos están cargados y medidos en ModelLibrary");
		Require(models.All(FloorPropsPlanner.IsCosted), "Todos los modelos tienen sus triángulos contados");
		Require(textures.All(path => DecorWarmup.Held.Any(resource => resource.ResourcePath == path)), "Las texturas existentes quedan guardadas");
		Require(!GodotObject.IsInstanceValid(runner), "El runner desaparece al terminar");
		Require(GetTree().Root.FindChild("DecorWarmupViewport", true, false) == null, "La escena de dibujo previo se libera");
		Require(menu.PlayButton != null && menu.PlayButton.Text == GameText.Play, "El menú sigue funcional");
		menu.QueueFree();
		await ToSignal(menu, Node.SignalName.TreeExited);
		GD.Print("Precalentamiento desde el menú: OK");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task CheckIdempotent()
	{
		GD.Print("\n--- Idempotencia ---");
		(int requested, int held, int primed, int missing) = (DecorWarmup.Requested, DecorWarmup.Held.Count, DecorWarmup.Primed, DecorWarmup.Missing);
		DecorWarmup.Request();
		DecorWarmup.Request();
		Require(!DecorWarmup.Step(1000.0), "No queda nada por cargar ni por medir");
		Require((requested, held, primed, missing) == (DecorWarmup.Requested, DecorWarmup.Held.Count, DecorWarmup.Primed, DecorWarmup.Missing),
			"Pedir de nuevo no repite nada (mismos recuentos)");
		Require(DecorWarmupRunner.Attach(this) == null, "Con todo hecho no se cuelga otro runner");

		MainMenu again = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		AddChild(again);
		await BossTestHarness.Frames(GetTree(), 3);
		Require(again.GetNodeOrNull(DecorWarmupRunner.NodeName) == null, "Un segundo menú no lanza otro precalentamiento");
		Require(DecorWarmup.Held.Count == held && DecorWarmup.Requested == requested, "El segundo menú no cambia los recuentos");
		again.QueueFree();
		await ToSignal(again, Node.SignalName.TreeExited);
		GD.Print("Idempotencia: OK");
	}

	// El calentamiento no usa hilos (la carga en segundo plano chocaba entre sí): carga de uno en uno en el hilo principal, y con presupuesto
	// de tiempo nulo hace exactamente un elemento por llamada.
	private void CheckBudget()
	{
		GD.Print("\n--- Presupuesto por fotograma ---");
		DecorWarmup.Reset();
		DecorWarmup.Request();
		int total = DecorWarmup.Remaining;
		Require(total > 20 && DecorWarmup.Held.Count == 0, $"Tras pedir hay {total} elementos en cola y nada cargado todavía");
		Require(DecorWarmup.Step(0.0) && DecorWarmup.Remaining == total - 1, "Con presupuesto nulo hace exactamente un elemento");
		Require(DecorWarmup.Step(0.0) && DecorWarmup.Remaining == total - 2 && DecorWarmup.Held.Count == 2, "Y otro más en la siguiente llamada");
		Require(!DecorWarmup.Step(1000.0) && DecorWarmup.Remaining == 0 && DecorWarmup.Failed == 0, "Con presupuesto de sobra termina la cola sin fallos");
		Require(DecorWarmup.Held.Count == total, $"Todo queda guardado ({DecorWarmup.Held.Count} de {total})");
		GD.Print("Presupuesto de tiempo por fotograma: OK");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task CheckMissingFiles()
	{
		GD.Print("\n--- Archivos que faltan ---");
		DecorWarmup.Reset();
		RoomSurfaces.TextureFolder = "res://tests/fixtures/none/";
		const string missingModel = "res://assets/models/props/no_existe.glb";
		DecorWarmup.ExtraPaths.Add(missingModel);

		DecorWarmupRunner runner = DecorWarmupRunner.Attach(this);
		Require(runner != null, "Desde cero se vuelve a lanzar");
		await WaitFinished(runner);
		await BossTestHarness.Frames(GetTree(), 2);

		int textureNames = DecorWarmup.TexturePaths().Count();
		string[] models = ExistingModels();
		GD.Print($"  sin texturas: faltan {DecorWarmup.Missing} (texturas {textureNames} + modelo extra), modelos medidos {DecorWarmup.Primed}");
		Require(DecorWarmup.Done && DecorWarmup.Failed == 0, "Termina sin error aunque falten archivos");
		Require(DecorWarmup.Missing == textureNames + 1, $"Cuenta lo que falta ({DecorWarmup.Missing} de {textureNames + 1})");
		Require(DecorWarmup.Held.OfType<Texture2D>().Count(resource => resource.ResourcePath.StartsWith("res://tests/fixtures/none/")) == 0, "No inventa texturas");
		Require(DecorWarmup.Primed == models.Length && models.All(ModelLibrary.IsLoaded), "Sigue con los modelos que sí existen");
		Require(GetTree().Root.FindChild("DecorWarmupViewport", true, false) == null, "También libera la escena de dibujo previo");

		RoomSurfaces.TextureFolder = "res://assets/textures/";
		DecorWarmup.ExtraPaths.Clear();
		GD.Print("Archivos que faltan: OK");
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task CheckDisabled()
	{
		GD.Print("\n--- Apagado ---");
		DecorWarmup.Reset();
		DecorWarmup.Enabled = false;
		Require(DecorWarmupRunner.Attach(this) == null, "Con Enabled = false no se lanza nada");
		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		AddChild(menu);
		await BossTestHarness.Frames(GetTree(), 3);
		Require(menu.GetNodeOrNull(DecorWarmupRunner.NodeName) == null && DecorWarmup.Requested == 0, "El menú tampoco lanza nada");
		menu.QueueFree();
		await ToSignal(menu, Node.SignalName.TreeExited);
		DecorWarmup.Enabled = true;
		GD.Print("Apagado: OK");
	}
}
