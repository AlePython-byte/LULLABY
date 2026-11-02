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
///   7. Hoja de diario con Patrick Hand: para cada frase de cada causa (también las largas del explosivo y del excavador) ninguna línea
///      se corta, «Morí a causa de:» cabe en una línea, el orden es título, frase, causa, flecha y dibujo, y el dibujo (a su proporción,
///      limitado por alto y ancho a la vez) cabe en la hoja sin tocar las notas; mapeo causa→imagen y respaldo al desconocido; la pausa usa
///      la misma letra.
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
			CheckArt();
			await CheckDefeatLayout();
			await CheckDiaryLayout();
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

		// Sin causa hay dos frases propias, con las mismas reglas que el resto.
		IReadOnlyList<string> unknown = DeathTexts.PhrasesOf(DeathCause.Unknown);
		Require(unknown.Count == 2 && unknown.All(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 100) && unknown.All(all.Add), "Sin causa hay dos frases propias, cortas y distintas");
		Require(Enumerable.Range(-3, 9).All(seed => unknown.Contains(DeathTexts.PhraseFor(DeathCause.Unknown, seed))) && DeathTexts.NameOf(DeathCause.Unknown) == "Desconocido",
			"Sin causa se elige entre ellas y se llama «Desconocido»");

		// Las frases aprobadas, en primera persona, con sus tildes y signos.
		Dictionary<DeathCause, string[]> approved = new()
		{
			[DeathCause.Chaser] = new[] { "Hoy me abrazaron tan fuerte que me quedé sin aire. Qué cariñoso.", "El más sencillo de todos me ganó. Me arrullé yo solo con eso." },
			[DeathCause.Shooter] = new[] { "Hoy aprendí que sí hay quien apunta bien a una cabecita.", "Me dispararon desde lejos. Ni me dio tiempo de llorar." },
			[DeathCause.Projectile] = new[] { "Me llegó una lágrima ajena. Qué mal sabe el llanto del vecino.", "Algo salió volando y yo no lo vi venir." },
			[DeathCause.Exploder] = new[] { "Alguien me quiso tanto que explotó a mi lado.", "Me dieron un abrazo con mecha. Qué detalle." },
			[DeathCause.Flyer] = new[] { "Me arrulló un zumbido con alas. Los bebés confiamos en todo.", "Me picó el sueño, con alas." },
			[DeathCause.Runner] = new[] { "Me corrieron encima mientras miraba para otro lado.", "Ni lo vi venir: se hacía el tímido." },
			[DeathCause.Digger] = new[] { "Salió algo de debajo de la cuna. Siempre hay algo debajo de la cuna.", "Algo me jaló desde el suelo. Debí mirar debajo de la cuna." },
			[DeathCause.TheMother] = new[] { "Mamá me arropó para siempre. Las nanas siempre acaban así.", "Mamá cantó hasta que me dormí. Esta vez fue para siempre." },
			[DeathCause.Unknown] = new[] { "No sé qué pasó, pero fue un día largo.", "Algo me pasó y no sé qué. Fue un día muy largo." },
		};
		foreach ((DeathCause cause, string[] phrases) in approved)
		{
			Require(DeathTexts.PhrasesOf(cause).SequenceEqual(phrases), $"{cause}: sus dos frases aprobadas");
		}
		Require(DeathTexts.PhraseFor(DeathCause.Digger, int.MinValue).Length > 0, "Una semilla negativa también elige frase");
		Require(DeathTexts.Title == "Fin de la nana" && DeathTexts.KilledBy == "Te venció:", "Título y etiqueta aprobados");
		Require(DeathTexts.DiaryGreeting == "Querido diario," && DeathTexts.DiaryCause == "Morí a causa de:", "Saludo y línea de la causa del diario");
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
			DeathCause expectedProjectile = info.Kind switch { EnemyKind.Digger => DeathCause.Digger, EnemyKind.Shooter => DeathCause.Shooter, _ => DeathCause.Projectile };
			Require(projectile == expectedProjectile, $"{info.Name}: su proyectil da {projectile}");
			enemy.Free();
		}

		EnemyBase mother = GD.Load<PackedScene>("res://scenes/bosses/TheMother.tscn").Instantiate<EnemyBase>();
		Require(DeathCauses.FromEnemy(mother) == DeathCause.TheMother && DeathCauses.ForProjectile(mother) == DeathCause.TheMother, "The Mother, de contacto y con sus proyectiles");
		mother.Free();
		Require(DeathCauses.FromEnemy(null) == DeathCause.Unknown, "Sin enemigo, causa desconocida");
		Require(DeathCauses.ForProjectile(null) == DeathCause.Projectile, "Un proyectil sin dueño conocido es solo un proyectil");
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
			Require(defeat.CauseText == DeathTexts.DiaryCause, $"{cause}: «{defeat.CauseText}»");
			Require(defeat.DrawingPath == DeathArt.PathOf(cause) && defeat.Drawing.Visible && defeat.Drawing.Texture != null && defeat.Arrow.Visible,
				$"{cause}: se ve la flecha y su dibujo ({defeat.DrawingPath})");
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
		Require(screen.Drawing == null && screen.DrawingPath == null && screen.GetNode<Control>("Root/Center/Box/Title").GetThemeFont("font") != HandFont.Load(),
			"Sin hoja no hay dibujo ni letra manuscrita");
		screen.QueueFree();
		await tree.ToSignal(screen, Node.SignalName.TreeExited);
		DefeatScreen.SheetPath = PaperSheet.Path;
		DefeatScreen.RetryNotePath = DefeatScreen.DefaultRetryNotePath;
		GD.Print("Derrota con hoja y notas, sin notas y sin hoja: OK");
	}

	// Qué dibujo corresponde a cada causa, su respaldo y el ajuste a la caja (sin pantalla).
	private void CheckArt()
	{
		GD.Print("\n--- Dibujos por causa ---");
		Dictionary<DeathCause, string> expected = new()
		{
			[DeathCause.Chaser] = "basico", [DeathCause.Shooter] = "tirador", [DeathCause.Projectile] = "proyectil", [DeathCause.Exploder] = "explosivo",
			[DeathCause.Flyer] = "volador", [DeathCause.Runner] = "huidor", [DeathCause.Digger] = "excavador", [DeathCause.TheMother] = "mother",
			[DeathCause.Unknown] = "desconocido",
		};
		Require(expected.Keys.OrderBy(c => c).SequenceEqual(Enum.GetValues<DeathCause>().OrderBy(c => c)), "Cada causa tiene su dibujo previsto");
		foreach ((DeathCause cause, string name) in expected)
		{
			string path = DeathArt.PathOf(cause);
			Require(path == $"res://assets/ui/muerte/asesino_{name}.png" && ResourceLoader.Exists(path) && GD.Load<Texture2D>(path) != null, $"{cause}: {path}");
		}

		// Cada archivo muestra a su enemigo (los dibujos se confundieron una vez): la forma de cada uno se reconoce por su proporción.
		// cubo rosa = básico (512×441), esfera naranja con antena = explosivo (512×685), oso = huidor (512×621), tortuga con aros = tirador (512×344).
		Dictionary<string, Vector2I> shapes = new() { ["basico"] = new(512, 441), ["explosivo"] = new(512, 685), ["huidor"] = new(512, 621), ["tirador"] = new(512, 344) };
		foreach ((string name, Vector2I size) in shapes)
		{
			Require(GD.Load<Texture2D>($"res://assets/ui/muerte/asesino_{name}.png").GetSize() == (Vector2)size, $"asesino_{name}.png es el dibujo de su enemigo");
		}

		Require(ResourceLoader.Exists(DeathArt.ArrowPath) && DeathArt.ArrowPath.EndsWith("flecha_out.png"), "La flecha existe con su nombre real");
		Require(DeathArt.PathOf(DeathCause.Digger, p => p != "res://assets/ui/muerte/asesino_excavador.png") == DeathArt.UnknownPath, "Sin el archivo de una causa se usa el del desconocido");
		Require(DeathArt.PathOf((DeathCause)99) == DeathArt.UnknownPath, "Una causa que no existe usa el del desconocido");
		Require(DeathArt.PathOf(DeathCause.Chaser, _ => false) == null, "Sin ningún dibujo no hay ruta (y la pantalla no lo muestra)");

		// El proyectil es muy alto y estrecho, The Mother es ancha: los dos caben en la caja por alto y por ancho a la vez.
		foreach ((DeathCause cause, Vector2 size) in expected.Keys.Select(c => (c, GD.Load<Texture2D>(DeathArt.PathOf(c)).GetSize())))
		{
			Vector2 fit = DeathArt.Fit(size, DeathArt.MaxSize);
			Require(fit.X <= DeathArt.MaxSize.X + 0.01f && fit.Y <= DeathArt.MaxSize.Y + 0.01f, $"{cause}: cabe en la caja ({fit})");
			Require(Mathf.Abs(fit.X / fit.Y - size.X / size.Y) < 0.001f, $"{cause}: conserva su proporción");
			Require(Mathf.IsEqualApprox(fit.X, DeathArt.MaxSize.X) || Mathf.IsEqualApprox(fit.Y, DeathArt.MaxSize.Y), $"{cause}: aprovecha la caja ({fit})");
		}

		Require(Mathf.IsEqualApprox(DeathArt.Fit(new Vector2(512, 841), DeathArt.MaxSize).Y, DeathArt.MaxSize.Y), "El proyectil lo limita el alto");
		Require(DeathArt.Fit(new Vector2(512, 412), DeathArt.MaxSize).X <= DeathArt.MaxSize.X, "The Mother no pasa del ancho");
		Require(Mathf.IsEqualApprox(DeathArt.Fit(new Vector2(1000, 100), DeathArt.MaxSize).X, DeathArt.MaxSize.X), "Una imagen muy ancha la limita el ancho");
		Require(DeathArt.Fit(new Vector2(100, 50), DeathArt.MaxSize) == new Vector2(100, 50), "Una imagen pequeña no se amplía");
		GD.Print("Dibujos por causa, respaldo y ajuste: OK");
	}

	// La hoja de diario con cada frase de cada causa (en particular las largas del explosivo y del excavador): letra manuscrita, ninguna
	// línea se corta ni se sale de la hoja, el orden es el del diario y el dibujo cabe en la hoja sin tocar las notas.
	private async Task CheckDiaryLayout()
	{
		GD.Print("\n--- Hoja de diario con Patrick Hand (1152×648) ---");
		SceneTree tree = GetTree();
		Rect2 window = new(Vector2.Zero, DesignSize);
		Rect2 inner = PaperSheet.Inner;
		inner.Position += DesignSize / 2.0f;
		Font hand = HandFont.Load();
		Require(hand != null, "Patrick Hand carga");
		foreach (char letter in "áéíóúñÁÉÍÓÚÑ¿¡…:,.")
		{
			Require(hand.HasChar(letter), $"Patrick Hand dibuja «{letter}»");
		}

		int screens = 0;
		foreach (DeathCause cause in Enum.GetValues<DeathCause>())
		{
			foreach (string phrase in DeathTexts.PhrasesOf(cause))
			{
				DefeatScreen screen = await OpenDefeat();
				screen.Present(phrase, cause);
				await BossTestHarness.Frames(tree, 3);
				string what = $"{cause} «{phrase}»";
				Label title = screen.GetNode<Label>("Root/Center/Box/Title");
				Label message = screen.GetNode<Label>("Root/Center/Box/Message");
				Label causeLabel = screen.GetNode<Label>("Root/Center/Box/Cause");
				Require(title.Text == "Querido diario," && message.Text == phrase && causeLabel.Text == "Morí a causa de:", $"{what}: textos del diario");
				foreach (Label label in new[] { title, message, causeLabel })
				{
					Require(label.GetThemeFont("font") == hand, $"{what}: «{label.Name}» con Patrick Hand");
					Require(label.GetLineCount() == label.GetVisibleLineCount() && inner.Grow(1.0f).Encloses(label.GetGlobalRect()), $"{what}: «{label.Name}» no se corta ni sale de la hoja");
				}

				int size = causeLabel.GetThemeFontSize("font_size");
				Require(causeLabel.GetLineCount() == 1 && hand.GetStringSize(causeLabel.Text, HorizontalAlignment.Left, -1, size).X <= inner.Size.X, $"{what}: «Morí a causa de:» en una línea ({hand.GetStringSize(causeLabel.Text, HorizontalAlignment.Left, -1, size).X:0} de {inner.Size.X:0} px)");
				Require(title.GetLineCount() == 1, $"{what}: «Querido diario,» en una línea");
				int messageSize = message.GetThemeFontSize("font_size");
				float longestWord = phrase.Split(' ').Max(word => hand.GetStringSize(word, HorizontalAlignment.Left, -1, messageSize).X);
				Require(longestWord <= message.Size.X && message.GetLineCount() <= 4, $"{what}: la frase no parte palabras y ocupa {message.GetLineCount()} líneas");

				// Orden del diario y dibujo dentro de la hoja, sin tocar las notas.
				Control arrow = screen.Arrow, drawing = screen.Drawing;
				Require(title.GetGlobalRect().End.Y <= message.GetGlobalRect().Position.Y + 0.5f && message.GetGlobalRect().End.Y <= causeLabel.GetGlobalRect().Position.Y + 0.5f
					&& causeLabel.GetGlobalRect().End.Y <= arrow.GetGlobalRect().Position.Y + 0.5f && arrow.GetGlobalRect().End.Y <= drawing.GetGlobalRect().Position.Y + 0.5f,
					$"{what}: orden título, frase, causa, flecha y dibujo");
				Rect2 rect = drawing.GetGlobalRect();
				Texture2D texture = GD.Load<Texture2D>(DeathArt.PathOf(cause));
				Require(screen.DrawingPath == DeathArt.PathOf(cause) && drawing.Visible && arrow.Visible, $"{what}: flecha y dibujo de {cause}");
				Require(rect.Size.X <= DeathArt.MaxSize.X + 1.5f && rect.Size.Y <= DeathArt.MaxSize.Y + 1.5f, $"{what}: el dibujo respeta la caja máxima ({rect.Size})");
				Require(Mathf.Abs(drawing.CustomMinimumSize.X / drawing.CustomMinimumSize.Y - texture.GetWidth() / (float)texture.GetHeight()) < 0.005f, $"{what}: el dibujo conserva su proporción ({drawing.CustomMinimumSize})");
				Require(inner.Grow(1.0f).Encloses(rect) && inner.Grow(1.0f).Encloses(arrow.GetGlobalRect()) && window.Encloses(rect), $"{what}: flecha y dibujo caben en la hoja ({rect} en {inner})");
				Rect2 box = screen.GetNode<Control>("Root/Center/Box").GetGlobalRect();
				Require(inner.Grow(1.0f).Encloses(box), $"{what}: todo el diario cabe en la hoja ({box} en {inner})");
				foreach ((Button button, string texturePath) in new[] { (screen.RetryButton, DefeatScreen.DefaultRetryNotePath), (screen.MenuButton, DefeatScreen.DefaultMenuNotePath) })
				{
					Rect2 note = new(DesignSize / 2.0f + new Vector2(button.OffsetLeft, button.OffsetTop), button.Size);
					Rect2 visible = DefeatScreen.VisiblePart(GD.Load<Texture2D>(texturePath));
					Rect2 paper = new(note.Position + visible.Position, visible.Size);
					Require(!paper.Intersects(rect) && !paper.Intersects(arrow.GetGlobalRect()), $"{what}: el dibujo no toca la nota «{button.Text}»");
				}

				screen.QueueFree();
				await tree.ToSignal(screen, Node.SignalName.TreeExited);
				screens++;
			}
		}

		GD.Print($"Hoja de diario: {screens} pantallas (todas las frases de todas las causas): OK");
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

		Require(pause.GetNode<Label>("Root/Center/Box/Title").GetThemeFont("font") == HandFont.Load() && pause.ResumeButton.GetThemeFont("font") == HandFont.Load(), "La pausa usa la letra manuscrita");
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
