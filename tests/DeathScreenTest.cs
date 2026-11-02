using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la pantalla de derrota y del menú de pausa con hoja de cuaderno, y de lo que mató a Noah. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/DeathScreenTest.tscn
///
///   1. Textos (DeathTexts): todas las causas conocidas tienen nombre y 2 o 3 frases cortas y distintas, la elección por semilla es
///      determinista (también con semilla negativa) y sin causa se usan las tres frases de siempre.
///   2. Causas (DeathCauses): cada tipo de enemigo, The Mother y sus proyectiles dan la causa que toca.
///   3. PlayerHealth.LastCause: solo cambia con golpes que hacen daño (no con los que cae en invulnerabilidad) y se reinicia con la partida.
///   4. De extremo a extremo: morir por cada causa (Excavador, The Mother y sin causa) muestra su frase y su nombre, congela el juego,
///      pide el sonido «derrota» y el audio sigue funcionando con el árbol en pausa.
///   5. Teclas: Enter reinicia y Esc sale al menú, una sola vez; la pantalla libera la pausa al salir del árbol.
///   6. Maquetación en 1152×648 con las imágenes reales: la hoja y su texto caben; las notas van por delante de la hoja, tienen la
///      proporción de su imagen, su papel visible pisa el borde de la hoja sin tapar el texto y el texto de cada nota (y su tecla) queda
///      dentro del papel visible; lo mismo en la pausa. Sin las notas o sin la hoja cae al diseño anterior sin error.
/// Antes de salir recolecta los envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class DeathScreenTest : Node
{
	private static readonly Vector2 DesignSize = new(1152.0f, 648.0f);
	private static readonly string NoFile = "res://tests/fixtures/none/no_existe.png";

	private int _checks;

	public override async void _Ready()
	{
		string sheet = DefeatScreen.SheetPath, retry = DefeatScreen.RetryNotePath, menu = DefeatScreen.MenuNotePath, pauseSheet = PauseMenu.SheetPath;
		Vector2I originalSize = GetTree().Root.Size;
		try
		{
			GD.Print($"=== DeathScreenTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckTexts();
			CheckCauses();
			await CheckLastCause();
			await CheckEndToEnd();
			await CheckKeys();
			GetTree().Root.Size = new Vector2I(1152, 648);
			await CheckDefeatLayout();
			await CheckPauseLayout();
			GD.Print($"DeathScreenTest: {_checks} comprobaciones. RESULTADO: OK");
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
			DefeatScreen.SheetPath = sheet;
			DefeatScreen.RetryNotePath = retry;
			DefeatScreen.MenuNotePath = menu;
			PauseMenu.SheetPath = pauseSheet;
			GetTree().Root.Size = originalSize;
			GetTree().Paused = false;
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

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void CheckTexts()
	{
		GD.Print("\n--- Textos ---");
		DeathCause[] known = DeathTexts.KnownCauses.ToArray();
		DeathCause[] expected = Enum.GetValues<DeathCause>().Where(cause => cause != DeathCause.Unknown).ToArray();
		Require(known.Length == 8 && expected.All(known.Contains), $"Las 8 causas conocidas tienen textos ({known.Length})");
		HashSet<string> all = new();
		foreach (DeathCause cause in known)
		{
			IReadOnlyList<string> phrases = DeathTexts.PhrasesOf(cause);
			Require(phrases.Count is >= 2 and <= 3, $"{cause}: 2 o 3 frases ({phrases.Count})");
			Require(phrases.All(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 100), $"{cause}: frases no vacías y cortas (≤ 100 caracteres)");
			Require(phrases.All(all.Add), $"{cause}: ninguna frase se repite");
			string name = DeathTexts.NameOf(cause);
			Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 16, $"{cause}: nombre corto ({name})");
			for (int seed = -3; seed <= 5; seed++)
			{
				Require(DeathTexts.PhraseFor(cause, seed) == DeathTexts.PhraseFor(cause, seed) && phrases.Contains(DeathTexts.PhraseFor(cause, seed)), $"{cause}: frase determinista de la lista");
			}

			Require(Enumerable.Range(0, 6).Select(seed => DeathTexts.PhraseFor(cause, seed)).Distinct().Count() == phrases.Count, $"{cause}: todas sus frases se usan según la semilla");
		}

		Require(DeathTexts.PhrasesOf(DeathCause.Unknown) == GameText.DefeatMessages && DeathTexts.PhraseFor(DeathCause.Unknown, 7) == GameText.DefeatMessageFor(7),
			"Sin causa se usan las tres frases de siempre");
		Require(DeathTexts.PhraseFor(DeathCause.Digger, int.MinValue).Length > 0, "Una semilla negativa también elige frase");
		Require(DeathTexts.Title == "Fin de la nana" && DeathTexts.KilledBy == "Te venció:", "Título y etiqueta aprobados");
		GD.Print("Textos de la derrota: OK");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private void CheckCauses()
	{
		GD.Print("\n--- Causas ---");
		Dictionary<EnemyKind, DeathCause> byKind = new()
		{
			[EnemyKind.Chaser] = DeathCause.Chaser, [EnemyKind.Shooter] = DeathCause.Shooter, [EnemyKind.Exploder] = DeathCause.Exploder,
			[EnemyKind.Flyer] = DeathCause.Flyer, [EnemyKind.Runner] = DeathCause.Runner, [EnemyKind.Digger] = DeathCause.Digger,
		};
		foreach (EnemyKinds.Info info in EnemyKinds.List)
		{
			EnemyBase enemy = GD.Load<PackedScene>(info.ScenePath).Instantiate<EnemyBase>();
			Require(DeathCauses.FromEnemy(enemy) == byKind[info.Kind], $"{info.Name}: su contacto da {byKind[info.Kind]}");
			DeathCause projectile = DeathCauses.ForProjectile(enemy);
			Require(projectile == (info.Kind == EnemyKind.Digger ? DeathCause.Digger : DeathCause.Projectile), $"{info.Name}: su proyectil da {projectile}");
			enemy.Free();
		}

		EnemyBase mother = GD.Load<PackedScene>("res://scenes/bosses/TheMother.tscn").Instantiate<EnemyBase>();
		Require(DeathCauses.FromEnemy(mother) == DeathCause.TheMother && DeathCauses.ForProjectile(mother) == DeathCause.TheMother, "The Mother, de contacto y con sus proyectiles");
		mother.Free();
		Require(DeathCauses.FromEnemy(null) == DeathCause.Unknown, "Sin enemigo, causa desconocida");
		EnemyProjectile shot = GD.Load<PackedScene>("res://scenes/combat/EnemyProjectile.tscn").Instantiate<EnemyProjectile>();
		Require(shot.Cause == DeathCause.Projectile, "Un proyectil nuevo es un proyectil");
		shot.Free();
		GD.Print("Causas por enemigo y proyectil: OK");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task CheckLastCause()
	{
		GD.Print("\n--- Último golpe ---");
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		await BossTestHarness.Frames(tree, 3);
		PlayerHealth health = level.Player.GetNode<PlayerHealth>("PlayerHealth");
		Require(health.LastCause == DeathCause.Unknown, "Sin golpes, causa desconocida");
		Require(health.TakeHit(0.5f, DeathCause.Flyer) && health.LastCause == DeathCause.Flyer, "Un golpe que hace daño fija la causa");
		Require(!health.TakeHit(0.5f, DeathCause.Digger) && health.LastCause == DeathCause.Flyer, "Un golpe en invulnerabilidad no la cambia");
		await BossTestHarness.WaitUntil(tree, () => health.TakeHit(0.5f, DeathCause.Chaser), 240, "salir de la invulnerabilidad");
		Require(health.LastCause == DeathCause.Chaser, "El siguiente golpe que hace daño la cambia");
		health.ResetItemModifiers();
		Require(health.LastCause == DeathCause.Unknown, "Una partida nueva la reinicia");
		await BossTestHarness.Close(tree, level);
		GD.Print("PlayerHealth.LastCause: OK");
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task CheckEndToEnd()
	{
		GD.Print("\n--- Morir por cada causa ---");
		foreach (DeathCause cause in new[] { DeathCause.Digger, DeathCause.TheMother, DeathCause.Unknown })
		{
			SceneTree tree = GetTree();
			BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
			await BossTestHarness.Frames(tree, 3);
			PlayerHealth health = level.Player.GetNode<PlayerHealth>("PlayerHealth");
			DefeatScreen defeat = level.Root.GetNode<DefeatScreen>("DefeatScreen");
			AudioManager.Instance.ClearRequests();
			if (cause == DeathCause.Unknown)
			{
				level.Health.TakeDamage(1000.0f);
			}
			else
			{
				health.TakeHit(1000.0f, cause);
			}

			await BossTestHarness.WaitUntil(tree, () => defeat.IsPresented, 300, "la pantalla de derrota");
			await BossTestHarness.Frames(tree, 2);
			Require(defeat.Cause == cause, $"{cause}: la pantalla sabe qué mató a Noah ({defeat.Cause})");
			Require(DeathTexts.PhrasesOf(cause).Contains(defeat.Message) && defeat.Message == DeathTexts.PhraseFor(cause, level.Run.Seed), $"{cause}: su frase («{defeat.Message}»)");
			Require(defeat.CauseText == $"Te venció: {DeathTexts.NameOf(cause)}", $"{cause}: «{defeat.CauseText}»");
			Require(tree.Paused, $"{cause}: el juego queda congelado");
			Require(AudioManager.Instance.Requests.Any(r => r.Id == SoundCatalog.Defeat), $"{cause}: se pide el sonido «derrota»");
			Require(AudioManager.Instance.ProcessMode == Node.ProcessModeEnum.Always && AudioManager.Instance.CanProcess()
				&& AudioManager.Instance.GetChildren().All(child => child.CanProcess()), $"{cause}: el audio sigue procesando con el árbol en pausa");
			int before = AudioManager.Instance.Requests.Count;
			AudioManager.Play(SoundCatalog.NoahHurt);
			Require(AudioManager.Instance.Requests.Count == before + 1, $"{cause}: con el árbol en pausa se siguen aceptando sonidos");
			await BossTestHarness.Close(tree, level);
			Require(!tree.Paused, $"{cause}: al liberar el nivel el juego no queda congelado");
		}

		Require(SoundCatalog.Get(SoundCatalog.Defeat) != null && SoundCatalog.Get(SoundCatalog.Defeat).Fallback == null, "«derrota» no tiene sonido de respaldo (no se oiría dos veces noah_muere)");
		GD.Print("Derrota de extremo a extremo, congelada y con audio: OK");
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task<DefeatScreen> OpenDefeat()
	{
		DefeatScreen screen = GD.Load<PackedScene>("res://scenes/ui/DefeatScreen.tscn").Instantiate<DefeatScreen>();
		AddChild(screen);
		await BossTestHarness.Frames(GetTree(), 2);
		return screen;
	}

	private async Task SendKey(Key key)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
		await BossTestHarness.Frames(GetTree(), 2);
		Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
		await BossTestHarness.Frames(GetTree(), 2);
	}

	private async Task CheckKeys()
	{
		GD.Print("\n--- Teclas ---");
		SceneTree tree = GetTree();
		foreach ((Key key, bool retry) in new[] { (Key.Enter, true), (Key.Escape, false) })
		{
			DefeatScreen screen = await OpenDefeat();
			int retries = 0, menus = 0;
			screen.RetryRequested += () => retries++;
			screen.MenuRequested += () => menus++;
			await SendKey(key);
			Require(retries == 0 && menus == 0, $"{key}: sin la pantalla a la vista no hace nada");
			screen.Present("Frase", DeathCause.Chaser);
			Require(tree.Paused, $"{key}: presentar congela el juego");
			await SendKey(key);
			await SendKey(key == Key.Enter ? Key.Escape : Key.Enter);
			Require(retry ? retries == 1 && menus == 0 : menus == 1 && retries == 0, $"{key}: {(retry ? "reinicia" : "sale al menú")} una sola vez (reinicios {retries}, salidas {menus})");
			screen.QueueFree();
			await tree.ToSignal(screen, Node.SignalName.TreeExited);
			Require(!tree.Paused, $"{key}: al salir del árbol la pantalla libera la pausa");
		}

		GD.Print("Enter reinicia y Esc sale al menú, una sola vez: OK");
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task CheckDefeatLayout()
	{
		GD.Print("\n--- Maquetación de la derrota (1152×648) ---");
		SceneTree tree = GetTree();
		Rect2 window = new(Vector2.Zero, DesignSize);
		Rect2 inner = PaperSheet.Inner;
		inner.Position += DesignSize / 2.0f;

		DefeatScreen screen = await OpenDefeat();
		screen.Present(DeathTexts.PhrasesOf(DeathCause.TheMother).MaxBy(p => p.Length), DeathCause.TheMother);
		await BossTestHarness.Frames(tree, 3);
		Require(screen.HasSheet && screen.HasNotes, "Con las imágenes reales hay hoja y notas");
		Rect2 sheet = screen.GetNode<Control>("Root/Sheet").GetGlobalRect();
		Require(window.Encloses(sheet) && Mathf.Abs(sheet.Size.Y - PaperSheet.Height) < 1.0f && Mathf.Abs(sheet.GetCenter().X - 576.0f) < 1.0f, $"La hoja está centrada y cabe ({sheet})");
		foreach (string name in new[] { "Title", "Message", "Cause" })
		{
			Rect2 rect = screen.GetNode<Control>($"Root/Center/Box/{name}").GetGlobalRect();
			Require(inner.Grow(1.0f).Encloses(rect), $"«{name}» cabe en la zona útil de la hoja ({rect} en {inner})");
		}

		Rect2 box = screen.GetNode<Control>("Root/Center/Box").GetGlobalRect();
		Require(inner.Grow(1.0f).Encloses(box), $"El texto completo cabe en la hoja con la frase más larga ({box} en {inner})");
		Require(screen.GetNode("Root").GetNode("Notes").GetIndex() > screen.GetNode("Root").GetNode("Sheet").GetIndex(), "Las notas van por delante de la hoja");
		foreach ((Button button, string texturePath, int side, string text, string key) in new[]
		{
			(screen.RetryButton, DefeatScreen.DefaultRetryNotePath, 1, "Reiniciar", "Enter"),
			(screen.MenuButton, DefeatScreen.DefaultMenuNotePath, -1, "Salir al menú", "Esc"),
		})
		{
			Texture2D texture = GD.Load<Texture2D>(texturePath);
			// La nota está girada unos grados: la geometría se calcula sin girar, a partir de sus márgenes (los de su colocación).
			Rect2 note = new(DesignSize / 2.0f + new Vector2(button.OffsetLeft, button.OffsetTop), button.Size);
			Rect2 visible = DefeatScreen.VisiblePart(texture);
			Rect2 paper = new(note.Position + visible.Position, visible.Size);
			float ratio = note.Size.X / note.Size.Y, textureRatio = texture.GetWidth() / (float)texture.GetHeight();
			Require(button.IsVisibleInTree() && window.Encloses(note), $"{text}: la nota se ve y cabe en la ventana ({note})");
			Require(Mathf.Abs(ratio - textureRatio) < 0.01f, $"{text}: conserva la proporción de su imagen ({ratio:0.000} frente a {textureRatio:0.000})");
			Require(side > 0 ? paper.GetCenter().X > sheet.GetCenter().X : paper.GetCenter().X < sheet.GetCenter().X, $"{text}: a {(side > 0 ? "la derecha" : "la izquierda")} de la hoja");
			float overlap = side > 0 ? sheet.End.X - paper.Position.X : paper.End.X - sheet.Position.X;
			Require(Mathf.Abs(overlap - DefeatScreen.NoteOverlap) < 1.5f, $"{text}: su papel pisa el borde de la hoja {overlap:0.0} px (esperado {DefeatScreen.NoteOverlap})");
			Require(!paper.Intersects(inner), $"{text}: no tapa el texto de la hoja");
			Require(button.Text == text && button.GetNode<Label>("KeyHint").Text == key, $"{text}: texto y tecla dibujados por el juego («{button.Text}», «{button.GetNode<Label>("KeyHint").Text}»)");
			Require(Mathf.Abs(Mathf.Abs(button.RotationDegrees) - 4.0f) < 0.01f, $"{text}: ligeramente girada");

			// El texto de la nota queda dentro de su papel visible (en el espacio de la propia nota, que no depende del giro).
			StyleBox style = button.GetThemeStylebox("normal");
			Rect2 content = new(style.GetMargin(Side.Left), style.GetMargin(Side.Top), button.Size.X - style.GetMargin(Side.Left) - style.GetMargin(Side.Right),
				button.Size.Y - style.GetMargin(Side.Top) - style.GetMargin(Side.Bottom));
			Font font = button.GetThemeFont("font");
			int fontSize = button.GetThemeFontSize("font_size");
			Vector2 textSize = new(font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X, font.GetHeight(fontSize));
			Rect2 textRect = new(content.GetCenter() - textSize / 2.0f, textSize);
			Require(visible.Encloses(textRect), $"{text}: el texto ({textSize.X:0}×{textSize.Y:0} px, letra {fontSize}) queda dentro del papel visible de la nota ({visible})");
			Label hint = button.GetNode<Label>("KeyHint");
			Rect2 hintRect = new(hint.OffsetLeft, hint.OffsetTop, hint.OffsetRight - hint.OffsetLeft, hint.OffsetBottom - hint.OffsetTop);
			float hintWidth = hint.GetThemeFont("font").GetStringSize(key, HorizontalAlignment.Left, -1, hint.GetThemeFontSize("font_size")).X;
			Require(visible.Encloses(hintRect) && hintWidth <= hintRect.Size.X, $"{text}: la tecla «{key}» queda dentro del papel visible");
		}

		screen.QueueFree();
		await tree.ToSignal(screen, Node.SignalName.TreeExited);

		// Sin las notas: los botones van dentro de la hoja.
		DefeatScreen.RetryNotePath = NoFile;
		screen = await OpenDefeat();
		screen.Present("Una frase corta", DeathCause.Flyer);
		await BossTestHarness.Frames(tree, 3);
		Require(screen.HasSheet && !screen.HasNotes, "Sin notas queda la hoja");
		foreach (Button button in new[] { screen.RetryButton, screen.MenuButton })
		{
			Rect2 rect = button.GetGlobalRect();
			Require(button.IsVisibleInTree() && inner.Grow(1.0f).Encloses(rect) && button.Text.Length > 0, $"«{button.Text}»: dentro de la hoja sin notas ({rect})");
		}

		screen.QueueFree();
		await tree.ToSignal(screen, Node.SignalName.TreeExited);

		// Sin la hoja: el diseño de siempre.
		DefeatScreen.SheetPath = NoFile;
		screen = await OpenDefeat();
		screen.Present("Una frase corta", DeathCause.Flyer);
		await BossTestHarness.Frames(tree, 3);
		Require(!screen.HasSheet && !screen.HasNotes && !screen.GetNode<Control>("Root/Sheet").Visible, "Sin hoja queda el diseño de siempre");
		Require(Mathf.IsEqualApprox(screen.GetNode<Control>("Root/Center").AnchorRight, 1.0f), "El contenido sigue centrado en toda la ventana");
		Require(screen.RetryButton.IsVisibleInTree() && screen.MenuButton.IsVisibleInTree() && window.Encloses(screen.GetNode<Control>("Root/Center/Box").GetGlobalRect()), "Los botones se ven y caben");
		Require(screen.CauseText == $"Te venció: {DeathTexts.NameOf(DeathCause.Flyer)}", "También muestra qué mató a Noah");
		screen.QueueFree();
		await tree.ToSignal(screen, Node.SignalName.TreeExited);
		DefeatScreen.SheetPath = PaperSheet.Path;
		DefeatScreen.RetryNotePath = DefeatScreen.DefaultRetryNotePath;
		GD.Print("Derrota con hoja y notas, sin notas y sin hoja: OK");
	}

	private async Task CheckPauseLayout()
	{
		GD.Print("\n--- Maquetación de la pausa (1152×648) ---");
		SceneTree tree = GetTree();
		Rect2 window = new(Vector2.Zero, DesignSize);
		Rect2 inner = PaperSheet.Inner;
		inner.Position += DesignSize / 2.0f;

		PauseMenu pause = GD.Load<PackedScene>("res://scenes/ui/PauseMenu.tscn").Instantiate<PauseMenu>();
		AddChild(pause);
		await BossTestHarness.Frames(tree, 2);
		Require(pause.TryPause() && pause.HasSheet, "Con la hoja real se abre la pausa");
		await BossTestHarness.Frames(tree, 3);
		Rect2 sheet = pause.GetNode<Control>("Root/Sheet").GetGlobalRect();
		Require(window.Encloses(sheet) && Mathf.Abs(sheet.GetCenter().X - 576.0f) < 1.0f, $"La hoja está centrada y cabe ({sheet})");
		Rect2 box = pause.GetNode<Control>("Root/Center/Box").GetGlobalRect();
		Require(inner.Grow(1.0f).Encloses(box), $"Título, botones y volumen caben en la hoja ({box} en {inner})");
		foreach (Control control in new Control[] { pause.ResumeButton, pause.RestartButton, pause.MenuButton, pause.GetNode<Control>("Root/Center/Box/ControlsButton"), pause.GetNode<Control>("Root/Center/Box/VolumeControls") })
		{
			Require(control.IsVisibleInTree() && inner.Grow(1.0f).Encloses(control.GetGlobalRect()), $"«{control.Name}» cabe en la hoja ({control.GetGlobalRect()})");
		}

		Require(pause.ResumeButton.Text == "Reanudar" && pause.RestartButton.Text == "Reiniciar partida" && pause.MenuButton.Text == "Salir al menú", "Reanudar, Reiniciar partida y Salir al menú");
		pause.Resume();
		pause.QueueFree();
		await tree.ToSignal(pause, Node.SignalName.TreeExited);

		PauseMenu.SheetPath = NoFile;
		PauseMenu plain = GD.Load<PackedScene>("res://scenes/ui/PauseMenu.tscn").Instantiate<PauseMenu>();
		AddChild(plain);
		await BossTestHarness.Frames(tree, 2);
		Require(plain.TryPause() && !plain.HasSheet && !plain.GetNode<Control>("Root/Sheet").Visible, "Sin hoja queda el diseño de siempre");
		await BossTestHarness.Frames(tree, 2);
		Require(plain.ResumeButton.IsVisibleInTree() && plain.MenuButton.IsVisibleInTree(), "Los botones de la pausa se ven");
		plain.Resume();
		plain.QueueFree();
		await tree.ToSignal(plain, Node.SignalName.TreeExited);
		PauseMenu.SheetPath = PaperSheet.Path;
		GD.Print("Pausa con hoja y sin hoja: OK");
	}
}
