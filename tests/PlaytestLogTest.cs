using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el registro de partida (PlaytestLog) con el código real del juego (TestLevel y el menú). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/PlaytestLogTest.tscn
/// Todo se escribe en una carpeta temporal (PlaytestLog.Directory); nunca en user://playtest/. Casos:
///   1. Apagado por defecto: TestLevel sin el menú no crea el registro ni escribe nada.
///   2. Menú en headless: abrirlo apaga el registro y «Jugar» no lo enciende (la suite no escribe), aunque una prueba lo hubiera dejado
///      encendido.
///   3. Menú con ventana (simulada con PlaytestNeedsWindow = false): «Jugar» lo enciende, la partida crea su CSV y, al liberar el nivel,
///      la fila FIN dice abandono y el archivo queda cerrado.
///   4. Victoria: filas SALA con vida de entrada y salida, objeto, curación y daño por causa; fila FIN con vida al llegar al jefe y
///      duración del jefe; las cuentas cuadran (vida final = vida máxima − daño + curación); el archivo se cierra al terminar y nada
///      se añade después.
///   5. Muerte: resultado y causa; archivo cerrado.
///   6. Cierre de ventana: la notificación escribe FIN con abandono y cierra el archivo; liberar el nivel después no añade otra fila.
///   7. Volver al menú a mitad de partida: FIN con abandono, archivo cerrado y registro apagado.
/// «Cerrado» se comprueba abriendo el archivo en exclusiva desde .NET (falla mientras Godot lo tenga abierto). Antes de salir recolecta los
/// envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class PlaytestLogTest : Node
{
	private int _checks;
	private string _directory;

	public override async void _Ready()
	{
		string originalDirectory = PlaytestLog.Directory;
		try
		{
			GD.Print($"=== PlaytestLogTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			_directory = Path.Combine(Path.GetTempPath(), "lullaby-playtest-test-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
			PlaytestLog.Directory = _directory;
			GameSettings.PlaytestLogEnabled = false;

			await CheckOffByDefault();
			await CheckMenuHeadless();
			await CheckMenuWithWindow();
			await CheckVictory();
			await CheckDeath();
			await CheckWindowClose();
			await CheckReturnToMenu();
			Cleanup(originalDirectory);
			GD.Print($"PlaytestLogTest: {_checks} comprobaciones. RESULTADO: OK");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			Cleanup(originalDirectory);
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
	}

	private void Cleanup(string originalDirectory)
	{
		PlaytestLog.Directory = originalDirectory;
		GameSettings.PlaytestLogEnabled = false;
		GameSettings.PlaytestNeedsWindow = true;
		GameSettings.Difficulty = null;
		try
		{
			if (_directory != null && System.IO.Directory.Exists(_directory)) System.IO.Directory.Delete(_directory, true);
		}
		catch (IOException)
		{
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private string[] Files() => System.IO.Directory.Exists(_directory) ? System.IO.Directory.GetFiles(_directory, "*.csv") : Array.Empty<string>();

	// Abre el archivo en exclusiva: solo se puede si nadie más (Godot) lo tiene abierto.
	private static bool IsOpenElsewhere(string path)
	{
		try
		{
			using FileStream stream = new(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
			return false;
		}
		catch (IOException)
		{
			return true;
		}
	}

	private List<Dictionary<string, string>> ReadRows(string path)
	{
		string[] lines = File.ReadAllLines(path);
		string[] header = lines[0].Split(',');
		List<Dictionary<string, string>> rows = new();
		foreach (string line in lines.Skip(1))
		{
			if (line.Length == 0) continue;
			string[] fields = line.Split(',');
			Require(fields.Length == header.Length, $"Cada fila trae {header.Length} columnas (trae {fields.Length}): {line}");
			Dictionary<string, string> row = new();
			for (int i = 0; i < header.Length; i++) row[header[i]] = fields[i];
			rows.Add(row);
		}

		return rows;
	}

	private static float F(Dictionary<string, string> row, string column) => float.Parse(row[column], System.Globalization.CultureInfo.InvariantCulture);

	private static float Damage(Dictionary<string, string> row) => row.Where(pair => pair.Key.StartsWith("dano_")).Sum(pair => F(row, pair.Key));

	private async Task<MainMenu> OpenMenu()
	{
		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		GetTree().Root.AddChild(menu);
		GetTree().CurrentScene = menu;
		await BossTestHarness.Frames(GetTree(), 2);
		return menu;
	}

	private static BossTestHarness.Level OpenLogged(Node parent, bool asCurrent, SceneTree tree)
	{
		GameSettings.PlaytestLogEnabled = true;
		return asCurrent
			? BossTestHarness.OpenAsCurrent(tree, BossTestHarness.Seed, true, false)
			: BossTestHarness.Open(parent, BossTestHarness.Seed, true, false);
	}

	private async Task CheckOffByDefault()
	{
		Require(!GameSettings.PlaytestLogEnabled, "El registro está apagado por defecto");
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, true, false);
		Require(level.Run.GetNodeOrNull("PlaytestLog") == null, "TestLevel sin menú no crea el registro");
		PlayerHealth health = level.Player.GetNode<PlayerHealth>("PlayerHealth");
		health.TakeHit(1.0f, DeathCause.Chaser);
		await BossTestHarness.GoToBoss(GetTree(), level);
		await BossTestHarness.Close(GetTree(), level);
		Require(Files().Length == 0 && !System.IO.Directory.Exists(_directory), "Sin el menú no se escribe ningún archivo");
		GD.Print("Apagado por defecto: TestLevel y las pruebas no escriben: OK");
	}

	private async Task CheckMenuHeadless()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		bool headless = DisplayServer.GetName() == "headless";
		if (headless) Require(!GameSettings.CanRecordPlaytest, "En headless no se puede registrar");

		GameSettings.PlaytestLogEnabled = true;
		MainMenu menu = await OpenMenu();
		Require(!GameSettings.PlaytestLogEnabled, "Abrir el menú apaga el registro");
		if (headless)
		{
			menu.StartGame();
			Require(!GameSettings.PlaytestLogEnabled, "En headless «Jugar» no enciende el registro");
			Node level = await BossTestHarness.WaitForScene(tree, menu, GameSettings.LevelScene);
			Require(level.GetNode<RunManager>("RunManager").GetNodeOrNull("PlaytestLog") == null, "La partida desde el menú no crea el registro en headless");
			await BossTestHarness.Restore(tree, testScene, level);
		}
		else
		{
			await BossTestHarness.Restore(tree, testScene, menu);
		}

		GameSettings.Difficulty = null;
		Require(Files().Length == 0, "El menú en headless no escribe ningún archivo");
		GD.Print("Menú en headless: abrirlo apaga el registro y «Jugar» no lo enciende: OK");
	}

	private async Task CheckMenuWithWindow()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		GameSettings.PlaytestNeedsWindow = false;
		Require(GameSettings.CanRecordPlaytest, "Con la ventana simulada se puede registrar");

		MainMenu menu = await OpenMenu();
		Require(!GameSettings.PlaytestLogEnabled, "Con el menú abierto el registro sigue apagado");
		menu.Select(DifficultyTarget.Hard);
		menu.StartGame();
		Require(GameSettings.PlaytestLogEnabled, "«Jugar» con ventana enciende el registro");

		Node level = await BossTestHarness.WaitForScene(tree, menu, GameSettings.LevelScene);
		PlaytestLog log = level.GetNode<RunManager>("RunManager").GetNodeOrNull<PlaytestLog>("PlaytestLog");
		Require(log != null && log.FilePath != "" && File.Exists(log.FilePath), "La partida desde el menú crea su CSV");
		string path = log.FilePath;
		Require(path.StartsWith(_directory), "Escribe en la carpeta configurada, no en user://");
		Require(IsOpenElsewhere(path), "Mientras se juega el archivo está abierto");

		await BossTestHarness.Restore(tree, testScene, level);
		Require(log.IsFinished && !IsOpenElsewhere(path), "Al liberar el nivel el archivo se cierra");
		List<Dictionary<string, string>> rows = ReadRows(path);
		Dictionary<string, string> end = rows.Last();
		Require(rows.Count(r => r["fila"] == "FIN") == 1 && end["fila"] == "FIN" && end["resultado"] == "abandono",
			"Liberar el nivel escribe una sola fila FIN con abandono");
		Require(end["dificultad"] == "Hard" && rows.All(r => r["dificultad"] == "Hard"), "Cada fila lleva la dificultad elegida en el menú");
		Require(end["vida_al_jefe"] == "" && end["segundos_jefe"] == "" && end["causa_muerte"] == "", "Sin llegar al jefe esos campos van vacíos");
		GameSettings.PlaytestNeedsWindow = true;
		GameSettings.Difficulty = null;
		GD.Print("Menú con ventana: «Jugar» enciende el registro, escribe en la carpeta y cierra con abandono: OK");
	}

	private async Task CheckVictory()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = OpenLogged(this, false, tree);
		PlaytestLog log = level.Run.GetNode<PlaytestLog>("PlaytestLog");
		string path = log.FilePath;
		Require(File.Exists(path), "Se crea el CSV de la partida");
		Require(IsOpenElsewhere(path), "Mientras se juega el archivo está abierto (la comprobación de «cerrado» es válida)");

		float maxHealth = level.Health.EffectiveMaxHealth;
		PlayerHealth health = level.Player.GetNode<PlayerHealth>("PlayerHealth");
		PlayerItems items = level.Player.GetNode<PlayerItems>("Items");
		await BossTestHarness.Frames(tree, 10);
		health.TakeHit(1.0f, DeathCause.Chaser);
		health.ResetItemModifiers();
		health.TakeHit(2.0f, DeathCause.Exploder);
		health.ResetItemModifiers();
		Require(items.TryCollect(GD.Load<ItemDefinition>("res://resources/items/biberon.tres")), "El biberón cura");

		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		float healthAtBoss = level.Health.CurrentHealth;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		health.ResetItemModifiers();
		health.TakeHit(1.0f, DeathCause.TheMother);
		await BossTestHarness.Frames(tree, 20);
		float finalHealth = level.Health.CurrentHealth;
		room.BossEnemy.Kill();

		Require(log.IsFinished && !IsOpenElsewhere(path), "Al terminar la partida el archivo se cierra");
		List<Dictionary<string, string>> rows = ReadRows(path);
		List<Dictionary<string, string>> rooms = rows.Where(r => r["fila"] == "SALA").ToList();
		Dictionary<string, string> end = rows.Last();
		Require(rows.Count(r => r["fila"] == "FIN") == 1 && end["fila"] == "FIN", "Una sola fila FIN, la última");
		Require(rooms.Count == BossTestHarness.PathToBoss(level.Rooms.Map).Count + 1, "Una fila SALA por cada sala visitada");
		Require(rooms[0]["tipo_sala"] == "Start" && rooms[^1]["tipo_sala"] == "Boss", "Primera sala Start, última Boss");
		Require(rooms[0]["objeto"] == "biberon" && Mathf.IsEqualApprox(F(rooms[0], "curacion"), 2.0f), "La sala con el biberón anota objeto y curación 2");
		Require(F(rooms[0], "dano_Chaser") >= 1.0f && F(rooms[0], "dano_Exploder") >= 2.0f, "Daño por causa en la sala de inicio");
		Require(Mathf.IsEqualApprox(F(rooms[0], "vida_entrada"), maxHealth), "Entra a la primera sala con la vida completa");
		Require(F(rooms[^1], "dano_TheMother") >= 1.0f, "El golpe de The Mother se anota en la sala del jefe");
		Require(Mathf.IsEqualApprox(F(rooms[^1], "vida_entrada"), healthAtBoss), "La vida de entrada del jefe es la real");
		Require(end["resultado"] == "victoria" && end["causa_muerte"] == "", "Resultado victoria sin causa de muerte");
		Require(Mathf.IsEqualApprox(F(end, "vida_al_jefe"), healthAtBoss), "La vida al llegar al jefe coincide");
		Require(F(end, "segundos_jefe") > 0.1f && F(end, "segundos") >= F(end, "segundos_jefe"), "Duración del jefe y total, con el total mayor");
		Require(Mathf.IsEqualApprox(F(end, "vida_salida"), finalHealth), "La fila FIN lleva la vida final");
		Require(Mathf.IsEqualApprox(F(end, "vida_salida"), maxHealth - Damage(end) + F(end, "curacion")),
			"Las cuentas cuadran: vida final = vida máxima − daño + curación");
		Require(Mathf.IsEqualApprox(rooms.Sum(Damage), Damage(end)) && Mathf.IsEqualApprox(rooms.Sum(r => F(r, "curacion")), F(end, "curacion")),
			"El total de FIN es la suma de las salas");
		Require(end["dificultad"] == level.Run.CurrentDifficulty.ToString() && end["semilla"] == level.Run.Seed.ToString(), "Dificultad y semilla");

		int rowsAtEnd = File.ReadAllLines(path).Length;
		await BossTestHarness.Frames(tree, 5);
		health.ResetItemModifiers();
		await BossTestHarness.Close(tree, level);
		Require(File.ReadAllLines(path).Length == rowsAtEnd, "Después de terminar no se añade nada, ni al liberar el nivel");
		GameSettings.PlaytestLogEnabled = false;
		GD.Print("Victoria: salas, objeto, curación, daño por causa, vida al jefe, duración y cuentas que cuadran: OK");
	}

	private async Task CheckDeath()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = OpenLogged(this, false, tree);
		PlaytestLog log = level.Run.GetNode<PlaytestLog>("PlaytestLog");
		string path = log.FilePath;
		PlayerHealth health = level.Player.GetNode<PlayerHealth>("PlayerHealth");
		await BossTestHarness.Frames(tree, 5);
		health.TakeHit(level.Health.EffectiveMaxHealth, DeathCause.Shooter);

		Require(level.Health.IsDead && log.IsFinished && !IsOpenElsewhere(path), "Al morir el archivo se cierra");
		List<Dictionary<string, string>> rows = ReadRows(path);
		Dictionary<string, string> end = rows.Last();
		Require(end["fila"] == "FIN" && end["resultado"] == "muerte" && end["causa_muerte"] == "Shooter", "Muerte por el tirador");
		Require(Mathf.IsEqualApprox(F(end, "dano_Shooter"), level.Health.EffectiveMaxHealth) && F(end, "vida_salida") == 0.0f, "Daño de la causa y vida final 0");
		Require(rows[^2]["fila"] == "SALA" && rows[^2]["tipo_sala"] == "Start", "La sala en curso se escribe antes de FIN");
		Require(end["vida_al_jefe"] == "" && end["segundos_jefe"] == "", "Sin llegar al jefe esos campos van vacíos");
		await BossTestHarness.Close(tree, level);
		Require(File.ReadAllLines(path).Length == rows.Count + 1, "Liberar el nivel tras morir no añade filas");
		GameSettings.PlaytestLogEnabled = false;
		GD.Print("Muerte: resultado, causa y archivo cerrado: OK");
	}

	private async Task CheckWindowClose()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = OpenLogged(this, false, tree);
		PlaytestLog log = level.Run.GetNode<PlaytestLog>("PlaytestLog");
		string path = log.FilePath;
		level.Player.GetNode<PlayerHealth>("PlayerHealth").TakeHit(1.0f, DeathCause.Flyer);
		await BossTestHarness.Frames(tree, 5);

		level.Root.PropagateNotification((int)Node.NotificationWMCloseRequest);
		Require(log.IsFinished && !IsOpenElsewhere(path), "La notificación de cierre de ventana cierra el archivo");
		List<Dictionary<string, string>> rows = ReadRows(path);
		Require(rows.Last()["fila"] == "FIN" && rows.Last()["resultado"] == "abandono", "Cierre de ventana: FIN con abandono");
		Require(F(rows.Last(), "dano_Flyer") >= 1.0f, "El daño recibido hasta el cierre queda anotado");
		await BossTestHarness.Close(tree, level);
		Require(ReadRows(path).Count(r => r["fila"] == "FIN") == 1, "Liberar el nivel después no escribe otra fila FIN");
		GameSettings.PlaytestLogEnabled = false;
		GD.Print("Cierre de ventana: FIN con abandono y archivo cerrado, una sola vez: OK");
	}

	private async Task CheckReturnToMenu()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;
		BossTestHarness.Level level = OpenLogged(this, true, tree);
		PlaytestLog log = level.Run.GetNode<PlaytestLog>("PlaytestLog");
		string path = log.FilePath;
		await BossTestHarness.Frames(tree, 5);
		level.Run.ReturnToMenu();
		Node menu = await BossTestHarness.WaitForScene(tree, level.Root, GameSettings.MenuScene);

		Require(log.IsFinished && !IsOpenElsewhere(path), "Al volver al menú el archivo se cierra");
		List<Dictionary<string, string>> rows = ReadRows(path);
		Require(rows.Last()["fila"] == "FIN" && rows.Last()["resultado"] == "abandono", "Volver al menú a mitad de partida: FIN con abandono");
		Require(!GameSettings.PlaytestLogEnabled, "El menú deja el registro apagado");
		await BossTestHarness.Restore(tree, testScene, menu);
		GD.Print("Volver al menú: FIN con abandono, archivo cerrado y registro apagado: OK");
	}
}
