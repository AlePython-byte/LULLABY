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
///   2a. Diseño de tiza (el de siempre ahora): pantalla completa con el fondo del color exacto de las imágenes, biberón a 16:9 centrado y sin
///      deformar en ventanas de cualquier proporción, el recorte del biberón lleno sigue el progreso de 0 a 100 % sin saltos, el consejo
///      va debajo del biberón, centrado respecto a él, en una línea y de 90 caracteres como máximo, sin título, Noah ni barra del diario,
///      y solo se cargan las dos imágenes del biberón.
///   2b. Diseño del diario (respaldo): la pantalla se instancia con hoja, Noah y barra; el título es fijo y el consejo sale de la lista; la barra sigue al progreso y
///      llega al 100 %; las dos poses de Noah se alternan; Noah avanza con la barra; no desaparece antes del mínimo visible (1 s).
///   3. Respaldo: sin carga_vacio o carga_lleno se usa el diario; y en el diario, sin las imágenes (hoja, poses, marco o relleno) funciona con fondo liso, texto y barra lisa, sin errores.
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
			await CheckChalkScreen();
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
			LoadingScreen.ChalkEmptyPath = LoadingScreen.DefaultChalkEmpty;
			LoadingScreen.ChalkFullPath = LoadingScreen.DefaultChalkFull;
			GetWindow().Size = new Vector2I(1152, 648);
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

	// --- 2a -----------------------------------------------------------------------------------------------------------

	private async Task CheckChalkScreen()
	{
		LoadingScreen.ResetHeld();
		LoadingScreen screen = new() { MinVisibleSeconds = 100.0f };
		AddChild(screen);
		await Frames(3);
		Require(screen.HasChalkArt && !screen.HasSheet && !screen.HasNoah && !screen.HasBarArt, "Diseño de tiza: sin hoja, sin Noah gateando y sin barra de crayón");
		Require(screen.TitleText == "" && GetTree().Root.FindChild("Title", true, false) == null, "Sin el título «Consejo del diario»");
		Require(LoadingScreen.Held.Count == 2 && LoadingScreen.Held.Contains(LoadingScreen.LoadHeld(LoadingScreen.DefaultChalkEmpty)) && LoadingScreen.Held.Contains(LoadingScreen.LoadHeld(LoadingScreen.DefaultChalkFull)),
			$"Solo carga y retiene las dos imágenes del biberón ({LoadingScreen.Held.Count})");

		// El fondo es del color exacto de la esquina de la imagen y cubre toda la ventana.
		Image source = Image.LoadFromFile(ProjectSettings.GlobalizePath(LoadingScreen.DefaultChalkEmpty));
		Color corner = source.GetPixel(0, 0);
		ColorRect background = (ColorRect)GetTree().Root.FindChild("Background", true, false);
		Require(source.GetSize() == new Vector2I(1920, 1080) && Mathf.Abs(corner.R - background.Color.R) < 0.003f && Mathf.Abs(corner.G - background.Color.G) < 0.003f && Mathf.Abs(corner.B - background.Color.B) < 0.003f,
			$"El fondo es del color de la esquina de la imagen ({corner} frente a {background.Color})");
		Vector2 view = screen.GetViewport().GetVisibleRect().Size;
		Require(background.GetGlobalRect().Size.IsEqualApprox(view) && background.GetGlobalRect().Position.IsZeroApprox(), "El fondo cubre toda la ventana");

		// Imagen a 16:9, centrada y sin deformar, en ventanas de varias proporciones.
		foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1000, 700), new Vector2I(1600, 600), new Vector2I(1920, 1080) })
		{
			GetWindow().Size = size;
			await Frames(4);
			view = screen.GetViewport().GetVisibleRect().Size;
			Rect2 stage = screen.Stage.GetGlobalRect();
			Require(Mathf.Abs(stage.Size.X / stage.Size.Y - 16.0f / 9.0f) < 0.01f, $"{size}: la imagen mantiene 16:9 sin deformarse ({stage.Size})");
			Require(stage.GetCenter().DistanceTo(view / 2.0f) < 1.5f, $"{size}: y está centrada");
			Require(stage.Position.X >= -0.5f && stage.Position.Y >= -0.5f && stage.End.X <= view.X + 0.5f && stage.End.Y <= view.Y + 0.5f
				&& (Mathf.IsEqualApprox(stage.Size.X, view.X, 0.01f) || Mathf.IsEqualApprox(stage.Size.Y, view.Y, 0.01f)), $"{size}: cabe entera y toca los bordes por un lado ({stage} en {view})");

			// El consejo, debajo del biberón y centrado respecto a él, en una línea, a escala de la imagen.
			float scale = stage.Size.X / LoadingScreen.ChalkWidth;
			Rect2 bottle = new(stage.Position + LoadingScreen.BottleFill.Position * scale, LoadingScreen.BottleFill.Size * scale);
			Rect2 tip = screen.TipLabel.GetGlobalRect();
			Require(tip.Position.Y >= bottle.End.Y && tip.End.Y <= stage.End.Y + 0.5f, $"{size}: el consejo está debajo del biberón y dentro de la imagen");
			float bottleCenter = stage.Position.X + 938.0f * scale;
			Require(Mathf.Abs(tip.GetCenter().X - bottleCenter) < 1.5f, $"{size}: centrado respecto al biberón ({tip.GetCenter().X:0.0} y {bottleCenter:0.0})");
			Font font = screen.TipLabel.GetThemeFont("font");
			int fontSize = screen.TipLabel.GetThemeFontSize("font_size");
			Require(screen.Tip.Length <= LoadingTips.MaxLength && !screen.Tip.Contains('\n') && screen.TipLabel.GetLineCount() == 1
				&& font.GetStringSize(screen.Tip, HorizontalAlignment.Left, -1, fontSize).X <= tip.Size.X, $"{size}: en una sola línea de {screen.Tip.Length} caracteres, que cabe en su caja");
			Require(fontSize <= Mathf.RoundToInt(LoadingScreen.TipFontSize * scale) + 1 && fontSize >= 8, $"{size}: el texto escala con la imagen (letra {fontSize})");
		}

		GetWindow().Size = new Vector2I(1152, 648);
		await Frames(4);

		// El recorte: de 0 al 100 % del interior del biberón, de izquierda a derecha, y suave.
		Control clip = screen.FillClip;
		Rect2 stageRect = screen.Stage.GetGlobalRect();
		float s = stageRect.Size.X / LoadingScreen.ChalkWidth;
		Require(clip.ClipContents && clip.Size.X == 0.0f && screen.ShownProgress == 0.0f, "Al 0 % no se ve nada del biberón lleno");
		Require(clip.GetGlobalRect().Position.IsEqualApprox(stageRect.Position + LoadingScreen.BottleFill.Position * s) && Mathf.IsEqualApprox(clip.Size.Y, LoadingScreen.BottleFill.Size.Y * s, 0.01f),
			"El recorte arranca en el borde izquierdo del interior del biberón y cubre su altura");
		TextureRect full = (TextureRect)clip.GetChild(0);
		Require(full.GetGlobalRect().Position.IsEqualApprox(stageRect.Position) && full.GetGlobalRect().Size.IsEqualApprox(stageRect.Size), "La imagen llena queda alineada exactamente con la vacía");
		float maxWidth = LoadingScreen.BottleFill.Size.X * s;
		screen.SetProgress(0.5f);
		float previous = 0.0f;
		float biggestStep = 0.0f;
		for (int i = 0; i < 200 && screen.ShownProgress < 0.5f; i++)
		{
			await Frames(1);
			biggestStep = Mathf.Max(biggestStep, clip.Size.X - previous);
			Require(clip.Size.X >= previous - 0.001f, "El recorte nunca retrocede");
			previous = clip.Size.X;
		}

		Require(Mathf.IsEqualApprox(clip.Size.X, maxWidth * 0.5f, 0.01f), $"Al 50 % el recorte mide la mitad ({clip.Size.X / maxWidth:0.000})");
		Require(biggestStep <= maxWidth * (screen.BarSpeed / 60.0f) * 1.5f, $"Avanza suave, sin saltos (paso máximo {biggestStep / maxWidth * 100.0f:0.0} % del biberón por fotograma)");
		screen.SetProgress(1.0f);
		for (int i = 0; i < 200 && screen.ShownProgress < 1.0f; i++) await Frames(1);
		await Frames(2);
		Require(Mathf.IsEqualApprox(clip.Size.X, maxWidth, 0.01f), "Al 100 % se ve todo el interior lleno");

		LoadingScreen quick = new() { MinVisibleSeconds = 1.0f, FadeSeconds = 0.1f };
		screen.QueueFree();
		await ToSignal(screen, Node.SignalName.TreeExited);
		AddChild(quick);
		quick.SetProgress(1.0f);
		await quick.Complete();
		Require(quick.Finished && quick.Elapsed >= 1.0f && quick.Elapsed < 2.5f, $"Mantiene el mínimo visible de 1 s y se desvanece ({quick.Elapsed:0.00} s)");
		await Frames(2);
		Require(!LoadingScreen.Busy && LoadingScreen.Held.Count == 2, "Se libera y las dos texturas siguen retenidas");
		GD.Print("Pantalla de tiza: fondo exacto, 16:9 centrada, recorte de 0 a 100 % suave, consejo debajo y centrado en una línea, solo dos imágenes: OK");
	}

	// --- 2b -----------------------------------------------------------------------------------------------------------

	private const string Missing = "res://assets/ui/no_existe.png";

	private async Task CheckScreen()
	{
		LoadingScreen.ChalkEmptyPath = Missing; // diseño del diario
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
		LoadingScreen.ChalkEmptyPath = LoadingScreen.DefaultChalkEmpty;
		GD.Print($"Pantalla del diario: hoja, Noah, barra, título fijo, consejo de la lista, dos poses cada {perPose:0.00} s, mínimo visible 1 s y 100 %: OK");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task CheckMissingArt()
	{
		// Sin una de las dos imágenes del biberón (o sin ambas) se usa el diario completo, sin errores.
		foreach ((string label, string empty, string full) in new[]
		{
			("sin carga_vacio", Missing, LoadingScreen.DefaultChalkFull),
			("sin carga_lleno", LoadingScreen.DefaultChalkEmpty, Missing),
			("sin ninguna de las dos", Missing, Missing),
		})
		{
			LoadingScreen.ChalkEmptyPath = empty;
			LoadingScreen.ChalkFullPath = full;
			LoadingScreen chalkless = new() { MinVisibleSeconds = 0.1f, FadeSeconds = 0.05f };
			AddChild(chalkless);
			await Frames(3);
			Require(!chalkless.HasChalkArt && chalkless.FillClip == null && chalkless.HasSheet && chalkless.HasNoah && chalkless.HasBarArt, $"{label}: usa el diseño del diario");
			Require(chalkless.TitleText == LoadingTips.Title && LoadingTips.All.Contains(chalkless.Tip), $"{label}: con su título y su consejo");
			chalkless.SetProgress(0.5f);
			await Frames(30);
			Require(chalkless.ShownProgress > 0.1f, $"{label}: la barra avanza");
			await chalkless.Complete();
			await Frames(2);
			Require(!LoadingScreen.Busy, $"{label}: termina y libera");
		}

		LoadingScreen.ChalkEmptyPath = Missing;
		LoadingScreen.ChalkFullPath = LoadingScreen.DefaultChalkFull;
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
		LoadingScreen.ChalkEmptyPath = LoadingScreen.DefaultChalkEmpty;
		LoadingScreen.ChalkFullPath = LoadingScreen.DefaultChalkFull;
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
