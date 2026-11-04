using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba la pantalla de carga (LoadingScreen, LoadingTips y SceneChangeGuard). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/LoadingScreenTest.tscn
///   1. Los consejos: se leen del archivo (resources/text/consejos_diario.json), ninguno pasa de 90 caracteres, ninguno lleva el prefijo
///      «Consejo del diario» ni está vacío o repetido; y el respaldo (seis consejos propios) se usa si el archivo falta, está vacío o no
///      es válido, y se ignoran las entradas que no son texto.
///   2. La pantalla se instancia con hoja, Noah y barra; el título es fijo y el consejo sale de la lista; la barra sigue al progreso y
///      llega al 100 %; las dos poses de Noah se alternan; Noah avanza con la barra; no desaparece antes del mínimo visible (1 s).
///   3. Respaldo: sin las imágenes (hoja, poses, marco o relleno) funciona con fondo liso, texto y barra lisa, sin errores.
///   4. Con el nivel real (forzando la pantalla en headless): se ve al empezar, Noah queda congelado y la pausa bloqueada mientras dura,
///      la barra avanza por pasos hasta el 100 %, y el resultado es el mismo que de golpe: misma semilla, mismo nivel y mismo HUD.
///   5. SceneChangeGuard recolecta, mide y se puede apagar.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class LoadingScreenTest : Node
{
	private const string TipsFolder = "user://";
	private readonly List<string> _tipFiles = new();

	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== LoadingScreenTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckShippedTips();
			CheckTipsFallbacks();
			await CheckScreen();
			await CheckMissingArt();
			CheckGuard();
			await CheckRealLevel();
			GD.Print($"LoadingScreenTest: {_checks} comprobaciones. RESULTADO: OK");
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
			LoadingScreen.ForceInHeadless = false;
			LoadingScreen.NoahOnePath = LoadingScreen.DefaultNoahOne;
			LoadingScreen.NoahTwoPath = LoadingScreen.DefaultNoahTwo;
			LoadingScreen.FramePath = LoadingScreen.DefaultFrame;
			LoadingScreen.FillPath = LoadingScreen.DefaultFill;
			LoadingScreen.SheetPath = PaperSheet.Path;
			LoadingTips.Path = LoadingTips.DefaultPath;
			LoadingTips.Reload();
			SceneChangeGuard.Enabled = true;
			foreach (string file in _tipFiles) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(file));
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private async Task Frames(int count) => await BossTestHarness.Frames(GetTree(), count);

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void CheckShippedTips()
	{
		LoadingTips.Path = LoadingTips.DefaultPath;
		LoadingTips.Reload();
		Require(LoadingTips.UsingFile, "Los consejos se leen del archivo");
		IReadOnlyList<string> tips = LoadingTips.All;
		Require(tips.Count >= 6, $"Hay al menos seis consejos ({tips.Count})");
		Require(tips.All(tip => tip.Length > 0 && tip.Length <= LoadingTips.MaxLength), "Ninguno está vacío ni pasa de 90 caracteres: " + string.Join(" | ", tips.Where(tip => tip.Length > LoadingTips.MaxLength || tip.Length == 0)));
		Require(tips.All(tip => !tip.Contains("Consejo del diario", StringComparison.OrdinalIgnoreCase) && !tip.TrimStart().StartsWith("Consejo", StringComparison.OrdinalIgnoreCase)),
			"Ninguno lleva el prefijo «Consejo del diario» (el título va fijo)");
		Require(tips.Distinct().Count() == tips.Count, "No hay repetidos");
		Require(tips.All(tip => tip == tip.Trim()), "Sin espacios sobrantes");
		Require(!tips.Any(tip => tip.Contains("clic", StringComparison.OrdinalIgnoreCase) || tip.Contains("chasquido", StringComparison.OrdinalIgnoreCase)),
			"Ningún consejo habla del clic (se dispara con las flechas) ni de un chasquido (la aparición suena como un «poof»)");
		Require(tips.Any(tip => tip.Contains("poof")), "Hay un consejo sobre el «poof» de la aparición de un enemigo");
		Require(tips.Any(tip => tip.Contains("flechas")), "Los consejos dicen que se dispara con las flechas");

		LoadingTips.Reload();
		string[] fallback = LoadingTips.Fallback;
		Require(fallback.Length == 6 && fallback.All(tip => tip.Length <= LoadingTips.MaxLength && !tip.StartsWith("Consejo", StringComparison.OrdinalIgnoreCase)), "El respaldo son seis consejos de 90 caracteres como máximo y sin prefijo");
		Random random = new(7);
		HashSet<string> picked = new();
		for (int i = 0; i < 200; i++) picked.Add(LoadingTips.Pick(random));
		Require(picked.IsSubsetOf(tips) && picked.Count > 3, $"Pick elige consejos de la lista y varía ({picked.Count} distintos)");
		GD.Print($"Consejos del archivo: {tips.Count}, todos de 90 caracteres como máximo y sin prefijo: OK");
	}

	// Cada caso va a su propio archivo: el motor guarda en caché el recurso de una ruta y devolvería el anterior.
	private void UseTips(string json)
	{
		string path = $"{TipsFolder}consejos_prueba_{_tipFiles.Count}.json";
		_tipFiles.Add(path);
		using (Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
		{
			file.StoreString(json);
		}

		LoadingTips.Path = path;
		LoadingTips.Reload();
	}

	private void CheckTipsFallbacks()
	{
		// Un archivo propio con consejos válidos, uno vacío, uno con entradas raras, uno mal formado y uno inexistente.
		UseTips("{\"consejos\": [\"Uno.\", \"  Dos con espacios.  \"]}");
		Require(ResourceLoader.Exists(LoadingTips.Path), "El archivo de prueba existe como recurso");
		Require(LoadingTips.UsingFile && LoadingTips.All.SequenceEqual(new[] { "Uno.", "Dos con espacios." }), "Un archivo propio se lee y se recortan los espacios");

		UseTips("{\"consejos\": []}");
		Require(!LoadingTips.UsingFile && LoadingTips.All.SequenceEqual(LoadingTips.Fallback), "Un archivo sin consejos usa el respaldo");

		UseTips("{\"consejos\": [1, null, \"\", \"   \", [\"x\"], \"Válido.\"]}");
		Require(LoadingTips.UsingFile && LoadingTips.All.SequenceEqual(new[] { "Válido." }), "Las entradas que no son texto o están vacías se ignoran");

		UseTips("{\"consejos\": [1, null, \"\"]}");
		Require(LoadingTips.All.SequenceEqual(LoadingTips.Fallback), "Si no queda ninguno válido, el respaldo");

		UseTips("[\"esto no es un diccionario\"]");
		Require(LoadingTips.All.SequenceEqual(LoadingTips.Fallback), "Un archivo con otra forma usa el respaldo");

		UseTips("{\"otra_cosa\": []}");
		Require(LoadingTips.All.SequenceEqual(LoadingTips.Fallback), "Sin la clave «consejos» también");

		LoadingTips.Path = "user://no_existe_consejos.json";
		LoadingTips.Reload();
		Require(!ResourceLoader.Exists(LoadingTips.Path) && LoadingTips.All.SequenceEqual(LoadingTips.Fallback), "Un archivo que no existe usa el respaldo, sin error");

		LoadingTips.Path = LoadingTips.DefaultPath;
		LoadingTips.Reload();
		Require(LoadingTips.UsingFile, "Y al volver al archivo bueno, se lee otra vez");
		GD.Print("Consejos: archivo propio, vacío, con entradas raras, mal formado y ausente (respaldo de seis): OK");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task CheckScreen()
	{
		LoadingScreen screen = new();
		AddChild(screen);
		await Frames(2);

		Require(LoadingScreen.Busy, "Mientras se ve, Busy es verdadero");
		Require(screen.HasSheet && screen.HasNoah && screen.HasBarArt, "Con las imágenes: hoja, Noah y barra con arte");
		Require(screen.TitleText == "Consejo del diario" && screen.TitleText == LoadingTips.Title, "El título es fijo: «Consejo del diario»");
		Require(LoadingTips.All.Contains(screen.Tip), $"El consejo sale de la lista: «{screen.Tip}»");
		Require(!screen.Tip.StartsWith("Consejo"), "Y no lleva el prefijo");
		Require(LoadingScreen.Held.Count >= 5, $"Retiene sus texturas (hoja, dos poses, marco y relleno): {LoadingScreen.Held.Count}");
		Require(GetTree().Root.FindChild("Bar", true, false) is TextureProgressBar bar && bar.TextureOver != null && bar.TextureProgress != null, "La barra es un TextureProgressBar con marco y relleno");

		// El progreso nunca baja y la barra mostrada lo sigue sin pasarse.
		screen.SetProgress(0.4f);
		screen.SetProgress(0.2f);
		Require(Mathf.IsEqualApprox(screen.Progress, 0.4f), "El progreso no baja");
		TextureProgressBar fill = (TextureProgressBar)GetTree().Root.FindChild("Bar", true, false);
		TextureRect noah = (TextureRect)GetTree().Root.FindChild("Noah", true, false);
		float firstX = noah.Position.X;
		float shownBefore = screen.ShownProgress;
		await Frames(60);
		Require(screen.ShownProgress > shownBefore && screen.ShownProgress <= 0.4f + 0.001f, $"La barra mostrada sube y no pasa del progreso ({screen.ShownProgress:0.00})");
		Require(Mathf.IsEqualApprox((float)fill.Value, screen.ShownProgress * 100.0f, 0.5f), "El valor de la barra es el progreso mostrado");
		Require(noah.Position.X > firstX, "Noah avanza a lo largo de la barra");

		// Las dos poses se alternan a ritmo pausado.
		Texture2D one = LoadingScreen.LoadHeld(LoadingScreen.DefaultNoahOne);
		Texture2D two = LoadingScreen.LoadHeld(LoadingScreen.DefaultNoahTwo);
		Require(one != null && two != null && one != two, "Las dos poses existen y son distintas");
		List<Texture2D> seen = new();
		int switches = screen.PoseSwitches;
		float start = screen.Elapsed;
		for (int i = 0; i < 170; i++)
		{
			await Frames(1);
			if (seen.Count == 0 || seen[^1] != screen.CurrentPose) seen.Add(screen.CurrentPose);
		}

		float seconds = screen.Elapsed - start;
		int changes = screen.PoseSwitches - switches;
		Require(seen.Contains(one) && seen.Contains(two) && changes >= 4, $"Las dos poses se alternan ({changes} cambios en {seconds:0.0} s)");
		Require(seen.Zip(seen.Skip(1)).All(pair => pair.First != pair.Second), "Siempre de una a la otra");
		float perPose = seconds / changes;
		Require(perPose >= 0.3f && perPose <= 0.7f, $"A ritmo pausado ({perPose:0.00} s por pose)");

		// Terminar: espera al mínimo visible y a la barra, y desaparece. Cuando el trabajo real es corto, se ve al menos 1 s.
		LoadingScreen quick = new() { MinVisibleSeconds = 1.0f };
		screen.QueueFree();
		await ToSignal(screen, Node.SignalName.TreeExited);
		Require(!LoadingScreen.Busy, "Al liberarla, Busy vuelve a falso");
		AddChild(quick);
		quick.SetProgress(1.0f);
		bool finished = false;
		quick.Completed += () => finished = true;
		await quick.Complete();
		Require(finished && quick.Finished, "Complete la termina y avisa");
		Require(quick.Elapsed >= 1.0f && quick.Elapsed < 2.5f, $"Se ve al menos 1 s aunque el trabajo sea instantáneo, y no mucho más ({quick.Elapsed:0.00} s)");
		Require(Mathf.IsEqualApprox(quick.ShownProgress, 1.0f, 0.01f) && Mathf.IsEqualApprox(quick.Progress, 1.0f), "La barra llega al 100 %");
		await Frames(2);
		Require(!IsInstanceValid(quick) && !LoadingScreen.Busy, "Y se libera");
		GD.Print($"Pantalla: hoja, Noah, barra, título fijo, consejo de la lista, dos poses cada {perPose:0.00} s, mínimo visible 1 s y 100 %: OK");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task CheckMissingArt()
	{
		foreach ((string label, Action<string> set) in new (string, Action<string>)[]
		{
			("sin la hoja de papel", path => LoadingScreen.SheetPath = path),
			("sin las poses de Noah", path => { LoadingScreen.NoahOnePath = path; LoadingScreen.NoahTwoPath = path; }),
			("sin el marco de la barra", path => LoadingScreen.FramePath = path),
			("sin el relleno de la barra", path => LoadingScreen.FillPath = path),
		})
		{
			LoadingScreen.SheetPath = PaperSheet.Path;
			LoadingScreen.NoahOnePath = LoadingScreen.DefaultNoahOne;
			LoadingScreen.NoahTwoPath = LoadingScreen.DefaultNoahTwo;
			LoadingScreen.FramePath = LoadingScreen.DefaultFrame;
			LoadingScreen.FillPath = LoadingScreen.DefaultFill;
			set("res://assets/ui/no_existe.png");

			LoadingScreen screen = new() { MinVisibleSeconds = 0.1f, FadeSeconds = 0.05f };
			AddChild(screen);
			await Frames(3);
			Require(LoadingTips.All.Contains(screen.Tip) && screen.TitleText == LoadingTips.Title, $"{label}: sigue habiendo título y consejo");
			Require(label != "sin la hoja de papel" || !screen.HasSheet, $"{label}: fondo liso");
			Require(label != "sin las poses de Noah" || (!screen.HasNoah && screen.CurrentPose == null), $"{label}: no se dibuja a Noah");
			Require(!label.Contains("la barra") || (!screen.HasBarArt && GetTree().Root.FindChild("PlainBar", true, false) is ProgressBar), $"{label}: barra lisa");
			screen.SetProgress(0.5f);
			await Frames(30);
			Require(screen.ShownProgress > 0.1f, $"{label}: la barra avanza");
			await screen.Complete();
			Require(screen.Finished || !IsInstanceValid(screen), $"{label}: termina");
			await Frames(2);
		}

		// Sin ninguna imagen a la vez.
		LoadingScreen.SheetPath = "res://assets/ui/no_existe.png";
		LoadingScreen.NoahOnePath = "res://assets/ui/no_existe.png";
		LoadingScreen.NoahTwoPath = "res://assets/ui/no_existe.png";
		LoadingScreen.FramePath = "res://assets/ui/no_existe.png";
		LoadingScreen.FillPath = "res://assets/ui/no_existe.png";
		LoadingScreen bare = new() { MinVisibleSeconds = 0.1f, FadeSeconds = 0.05f };
		AddChild(bare);
		await Frames(3);
		Require(!bare.HasSheet && !bare.HasNoah && !bare.HasBarArt && LoadingTips.All.Contains(bare.Tip), "Sin ninguna imagen: fondo liso, texto y barra lisa");
		await bare.Complete();
		await Frames(2);
		Require(!LoadingScreen.Busy, "Sin imágenes también termina y libera");
		LoadingScreen.SheetPath = PaperSheet.Path;
		LoadingScreen.NoahOnePath = LoadingScreen.DefaultNoahOne;
		LoadingScreen.NoahTwoPath = LoadingScreen.DefaultNoahTwo;
		LoadingScreen.FramePath = LoadingScreen.DefaultFrame;
		LoadingScreen.FillPath = LoadingScreen.DefaultFill;
		GD.Print("Respaldo: sin hoja, sin poses, sin barra y sin nada, funciona con fondo liso y texto: OK");
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private void CheckGuard()
	{
		SceneChangeGuard.Reset();
		SceneChangeGuard.Enabled = true;
		double ms = SceneChangeGuard.Collect();
		Require(ms >= 0.0 && SceneChangeGuard.Calls == 1 && Mathf.IsEqualApprox((float)ms, (float)SceneChangeGuard.LastMilliseconds), $"Collect mide lo que tarda ({ms:0.0} ms)");
		SceneChangeGuard.Collect();
		Require(SceneChangeGuard.Calls == 2 && SceneChangeGuard.TotalMilliseconds >= SceneChangeGuard.LastMilliseconds, "Y acumula");
		SceneChangeGuard.Enabled = false;
		Require(SceneChangeGuard.Collect() == 0.0 && SceneChangeGuard.Calls == 2, "Apagado no hace nada");
		SceneChangeGuard.Enabled = true;
		GD.Print($"SceneChangeGuard: recolecta, mide ({ms:0.0} ms) y se puede apagar: OK");
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private static string Describe(LevelMap map)
	{
		return string.Join(";", map.Rooms.OrderBy(room => room.Cell.X).ThenBy(room => room.Cell.Y).Select(room =>
			$"{room.Cell}|{room.Type}|{room.TemplateId}|{room.Size}|{string.Join(",", room.SpawnPoints)}|{string.Join(",", room.SpawnKinds)}")) + $"|start {map.StartCell}";
	}

	private async Task CheckRealLevel()
	{
		SceneTree tree = GetTree();
		foreach (int seed in new[] { BossTestHarness.Seed, 3 })
		{
			// De golpe (el camino de siempre en headless).
			LoadingScreen.ForceInHeadless = false;
			BossTestHarness.Level instant = BossTestHarness.Open(this, seed, withMother: false);
			Require(instant.Rooms.Map != null && instant.Rooms.CurrentRoom != null, $"[semilla {seed}] De golpe, el nivel está listo al instante (como antes)");
			string expected = Describe(instant.Rooms.Map);
			string expectedHud = instant.Root.GetNode<Label>("SeedHud/Label").Text;
			Require(tree.Root.FindChild("LoadingScreen", true, false) == null, $"[semilla {seed}] Y sin pantalla de carga");
			await BossTestHarness.Close(tree, instant);

			// Con la pantalla de carga.
			LoadingScreen.ForceInHeadless = true;
			int guardCalls = SceneChangeGuard.Calls;
			BossTestHarness.Level level = BossTestHarness.Open(this, seed, withMother: false);
			PauseMenu pause = level.Root.GetNode<PauseMenu>("PauseMenu");
			Require(level.Rooms.Map == null && level.Rooms.CurrentRoom == null, $"[semilla {seed}] Con pantalla de carga, el nivel aún no está construido al terminar _Ready");
			LoadingScreen screen = level.Run.GetNodeOrNull<LoadingScreen>("LoadingScreen");
			Require(LoadingScreen.Busy && screen != null, $"[semilla {seed}] La pantalla de carga se ve al empezar");
			Require(!level.Player.IsPhysicsProcessing(), $"[semilla {seed}] Noah está congelado mientras dura");

			List<float> samples = new();
			int frames = 0;
			bool pauseBlocked = true;
			while (IsInstanceValid(screen) && !screen.Finished && frames < 1500)
			{
				await Frames(1);
				frames++;
				if (IsInstanceValid(screen))
				{
					if (samples.Count == 0 || samples[^1] != screen.Progress) samples.Add(screen.Progress);
					if (LoadingScreen.Busy && !screen.Finished) pauseBlocked &= !pause.CanPause && !pause.TryPause();
				}
			}

			Require(frames < 1500, $"[semilla {seed}] La carga termina");
			Require(samples.Count >= 4 && samples.Zip(samples.Skip(1)).All(pair => pair.Second > pair.First), $"[semilla {seed}] La barra avanza por pasos ({samples.Count} valores distintos y crecientes)");
			Require(samples[^1] >= 0.97f, $"[semilla {seed}] Llega casi al final antes de completarse ({samples[^1]:0.00})");
			Require(pauseBlocked, $"[semilla {seed}] La pausa no se abre mientras dura");
			await Frames(2);
			Require(!LoadingScreen.Busy && level.Run.GetNodeOrNull("LoadingScreen") == null, $"[semilla {seed}] Se libera y deja de estar activa");
			Require(level.Player.IsPhysicsProcessing(), $"[semilla {seed}] Noah vuelve a moverse");
			Require(pause.CanPause, $"[semilla {seed}] Y la pausa vuelve a abrirse");
			Require(level.Rooms.Map != null && level.Rooms.CurrentRoom != null, $"[semilla {seed}] La primera sala está construida");
			Require(SceneChangeGuard.Calls > guardCalls, $"[semilla {seed}] La carga recolectó la basura antes de construir ({SceneChangeGuard.Calls - guardCalls} vez)");
			Require(EnemyScenePreloader.Held.Count == EnemyKinds.List.Count, $"[semilla {seed}] Precargó los {EnemyKinds.List.Count} tipos de enemigo");

			Require(Describe(level.Rooms.Map) == expected, $"[semilla {seed}] Mismo nivel que de golpe");
			Require(level.Root.GetNode<Label>("SeedHud/Label").Text == expectedHud, $"[semilla {seed}] Mismo HUD de semilla y dificultad");
			GD.Print($"Semilla {seed}: carga en {frames} fotogramas, {samples.Count} pasos de barra, pausa bloqueada y nivel idéntico al de golpe: OK");
			LoadingScreen.ForceInHeadless = false;
			await BossTestHarness.Close(tree, level);
		}
	}
}
