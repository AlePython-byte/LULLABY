using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de los corazones de vida (HeartsRow y HealthHud). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/HeartsHudTest.tscn
///
///   1. La regla de redondeo con casos de vida: máxima, 5,2/6, 0,5, 0,1, 0, vida máxima distinta de 6 (5, 3, 7,5), los umbrales
///      exactos, la garantía de no mostrar 0 corazones con vida y el tope de 24.
///   2. El HUD real: sigue a la vida de Noah (golpe, curación, Oso de peluche, vida máxima distinta), sacude el corazón que baja de
///      estado y solo ese, y reiniciar con ResetForNewRun lo deja completo.
///   3. La Manta: tras un golpe de contacto, el HUD muestra medio corazón menos; ningún golpe quita menos de 0,5; la explosión (2)
///      con Manta quita 1; los efectos de resistencia apilados quedan acotados por el mínimo.
///   4. Dibujo: escala entera x3 con filtro Nearest, dibujo por código si faltan las texturas, sin un nodo por corazón.
///   5. Posición: arriba a la izquierda sin chocar con el HUD de objetos ni con el minimapa.
///   6. Reintentar: no deja nodos huérfanos ni acumulados, y el HUD nuevo empieza con la vida completa.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class HeartsHudTest : Node
{
	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== HeartsHudTest ===");
		try
		{
			Rule();
			await RealHud();
			await Blanket();
			await Drawing();
			await Placement();
			await Retry();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		await BossTestHarness.CollectWrappers(GetTree());
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	// F = lleno, M = medio, E = vacío.
	private static string Text(IEnumerable<HeartState> states) => string.Concat(states.Select(s => s switch { HeartState.Full => 'F', HeartState.Half => 'M', _ => 'E' }));

	private static string Rule(float current, float max) => Text(HeartsRow.StatesFor(current, max));

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void Rule()
	{
		GD.Print("\n--- Regla de redondeo ---");
		(float current, float max, string expected, string name)[] cases =
		{
			(5.0f, 5.0f, "FFFFF", "vida máxima (5/5)"),
			(5.2f, 6.0f, "FFFFFE", "5,2/6: 5 llenos y 1 vacío"),
			(4.5f, 6.0f, "FFFFME", "4,5/6: 4 llenos, 1 medio y 1 vacío"),
			(0.5f, 5.0f, "MEEEE", "0,5: un medio corazón"),
			(0.1f, 5.0f, "MEEEE", "0,1: nunca 0 corazones, mínimo medio"),
			(0.0f, 5.0f, "EEEEE", "0: todos vacíos"),
			(3.0f, 3.0f, "FFF", "vida máxima 3"),
			(7.5f, 7.5f, "FFFFFFFM", "vida máxima 7,5: 8 corazones (el último, medio)"),
			(7.0f, 7.5f, "FFFFFFFE", "7/7,5: el último, vacío"),
			(6.0f, 6.0f, "FFFFFF", "vida máxima 6 (con el Oso de peluche)"),
			(4.75f, 6.0f, "FFFFFE", "umbral 0,75: lleno"),
			(4.74f, 6.0f, "FFFFME", "justo bajo 0,75: medio"),
			(4.25f, 6.0f, "FFFFME", "umbral 0,25: medio"),
			(4.24f, 6.0f, "FFFFEE", "justo bajo 0,25: vacío"),
			(5.0000001f, 5.0000001f, "FFFFF", "un máximo con ruido de coma flotante no añade un corazón"),
		};
		foreach ((float current, float max, string expected, string name) in cases)
		{
			Check($"{name}: {expected}", Rule(current, max) == expected, Rule(current, max));
		}

		bool neverZero = true;
		for (float health = 0.001f; health <= 5.0f; health += 0.0371f)
		{
			neverZero &= HeartsRow.StatesFor(health, 5.0f).Any(state => state != HeartState.Empty);
		}

		Check("con cualquier vida mayor que 0 se muestra al menos medio corazón", neverZero, "alguna vida mostró 0 corazones");
		Check("la vida máxima enorme se limita a 24 corazones", HeartsRow.StatesFor(1000.0f, 1000.0f).Length == HeartsRow.MaxHearts && HeartsRow.StatesFor(1.0e9f, 1.0e9f).Length == 24, "tope");
		Check("valores no válidos no rompen ni dibujan corazones", HeartsRow.StatesFor(float.NaN, float.NaN).Length == 0 && HeartsRow.StatesFor(float.NaN, 5.0f).All(s => s == HeartState.Empty), "NaN");
		Check("1 corazón = 1 punto: la cantidad es la vida máxima redondeada hacia arriba", HeartsRow.StatesFor(1.0f, 4.2f).Length == 5 && HeartsRow.StatesFor(1.0f, 4.0f).Length == 4, "cantidad");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task RealHud()
	{
		GD.Print("\n--- HUD real ---");
		SceneTree tree = GetTree();
		(PlayerController player, HealthHud hud, HealthComponent health) = await NewPlayerAndHud();
		HeartsRow hearts = hud.Hearts;
		Check("Noah empieza con sus 5 corazones llenos", Text(hearts.States) == "FFFFF" && hearts.HeartCount == 5, Text(hearts.States));
		Check("al empezar ningún corazón se sacude", Enumerable.Range(0, 5).All(i => !hearts.IsShaking(i)), "sacudida inicial");

		health.TakeDamage(1.0f);
		Check("un golpe de 1 deja 4 llenos y 1 vacío", Text(hearts.States) == "FFFFE", Text(hearts.States));
		Check("se sacude el corazón que se vació, y solo ese", hearts.IsShaking(4) && Enumerable.Range(0, 4).All(i => !hearts.IsShaking(i)), "sacudida");
		await WaitSeconds(0.5f);
		Check("la sacudida es breve (acaba sola)", Enumerable.Range(0, 5).All(i => !hearts.IsShaking(i)), "sigue sacudiéndose");

		health.TakeDamage(0.5f);
		Check("medio punto más: el corazón pasa de lleno a medio (y se sacude)", Text(hearts.States) == "FFFME" && hearts.IsShaking(3), Text(hearts.States));
		health.Heal(1.0f);
		Check("curar sube los corazones (de FFFME a FFFFM) sin sacudirlos", Text(hearts.States) == "FFFFM" && Enumerable.Range(0, 5).All(i => !hearts.IsShaking(i)), Text(hearts.States));

		health.TakeDamage(4.4f);
		Check("con 0,1 de vida se ve medio corazón", Text(hearts.States) == "MEEEE", Text(hearts.States));
		health.ResetForNewRun();
		Check("reiniciar la partida lo deja completo y sin sacudidas", Text(hearts.States) == "FFFFF" && Enumerable.Range(0, 5).All(i => !hearts.IsShaking(i)), Text(hearts.States));

		// Oso de peluche: +1 de vida máxima y actual.
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		player.GetNode<PlayerItems>("Items").TryCollect(catalog.Find("oso_peluche"));
		Check("el Oso de peluche añade un corazón (6, todos llenos)", Text(hearts.States) == "FFFFFF", Text(hearts.States));

		// Vida máxima distinta de 6.
		health.MaxHealth = 7.5f;
		health.ResetForNewRun();
		Check("con vida máxima 7,5 y la vida completa hay 8 corazones: 7 llenos y el último medio", Text(hearts.States) == "FFFFFFFM", Text(hearts.States));
		health.TakeDamage(1.0f);
		Check("y con 6,5 de 7,5: 6 llenos, 1 medio y 1 vacío", Text(hearts.States) == "FFFFFFME", Text(hearts.States));
		health.MaxHealth = 3.0f;
		health.ResetForNewRun();
		Check("con vida máxima 3 hay 3 corazones", Text(hearts.States) == "FFF", Text(hearts.States));

		Free(player, hud);
		await BossTestHarness.Frames(tree, 1);
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task Blanket()
	{
		GD.Print("\n--- La Manta vieja ---");
		(PlayerController player, HealthHud hud, HealthComponent health) = await NewPlayerAndHud();
		PlayerHealth protection = player.GetNode<PlayerHealth>("PlayerHealth");
		PlayerItems items = player.GetNode<PlayerItems>("Items");
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		EnemyBase contactEnemy = GD.Load<PackedScene>("res://scenes/enemies/BasicEnemy.tscn").Instantiate<EnemyBase>();
		AddChild(contactEnemy);
		HeartsRow hearts = hud.Hearts;

		Check("sin Manta, un golpe de contacto quita 1 corazón entero", protection.TakeHit(contactEnemy.ContactDamage) && Text(hearts.States) == "FFFFE", Text(hearts.States));
		health.ResetForNewRun();
		protection.ResetItemModifiers();

		Check("la Manta se recoge y reduce el daño un 50 %", items.TryCollect(catalog.Find("manta_vieja")) && Mathf.IsEqualApprox(protection.DamageTakenMultiplier, 0.5f), $"{protection.DamageTakenMultiplier}");
		string before = Text(hearts.States);
		bool hit = protection.TakeHit(contactEnemy.ContactDamage);
		Check("con Manta, un golpe de contacto quita medio corazón: de FFFFF a FFFFM", hit && before == "FFFFF" && Text(hearts.States) == "FFFFM" && Mathf.IsEqualApprox(health.CurrentHealth, 4.5f), $"{before} → {Text(hearts.States)} ({health.CurrentHealth})");
		Check("el corazón afectado se sacude", hearts.IsShaking(4), "sin sacudida");

		health.ResetForNewRun();
		protection.ResetItemModifiers();
		items.ResetForNewRun();
		items.TryCollect(catalog.Find("manta_vieja"));
		EnemyBase exploder = GD.Load<PackedScene>("res://scenes/enemies/ExploderEnemy.tscn").Instantiate<EnemyBase>();
		AddChild(exploder);
		float explosion = exploder.Behavior<FuseBehavior>().ExplosionDamage;
		exploder.Free();
		Check($"con Manta, la explosión del explosivo ({explosion}) quita 1 corazón entero", Mathf.IsEqualApprox(explosion, 2.0f) && protection.TakeHit(explosion) && Text(hearts.States) == "FFFFE", Text(hearts.States));

		// Mínimo por golpe y efectos apilados.
		Check("ningún golpe baja de 0,5: 1×0,5, 2×0,5, 1×0,25 y 2×0,25", Mathf.IsEqualApprox(PlayerHealth.EffectiveHitDamage(1.0f, 0.5f), 0.5f) && Mathf.IsEqualApprox(PlayerHealth.EffectiveHitDamage(2.0f, 0.5f), 1.0f)
			&& Mathf.IsEqualApprox(PlayerHealth.EffectiveHitDamage(1.0f, 0.25f), 0.5f) && Mathf.IsEqualApprox(PlayerHealth.EffectiveHitDamage(2.0f, 0.25f), 0.5f), "mínimo");
		Check("un golpe menor que el mínimo conserva su valor y sin resistencia no cambia nada", Mathf.IsEqualApprox(PlayerHealth.EffectiveHitDamage(0.3f, 0.5f), 0.3f) && Mathf.IsEqualApprox(PlayerHealth.EffectiveHitDamage(1.0f, 1.0f), 1.0f), "casos límite");

		health.ResetForNewRun();
		protection.ResetItemModifiers();
		items.ResetForNewRun();
		ItemDefinition second = (ItemDefinition)catalog.Find("manta_vieja").Duplicate();
		second.Id = "manta_extra";
		Check("dos resistencias de 50 % se combinan multiplicándose (×0,25)", items.TryCollect(catalog.Find("manta_vieja")) && items.TryCollect(second) && Mathf.IsEqualApprox(protection.DamageTakenMultiplier, 0.25f), $"{protection.DamageTakenMultiplier}");
		protection.TakeHit(1.0f);
		Check("apiladas, un golpe de 1 sigue quitando como mínimo medio corazón (no 0,25)", Mathf.IsEqualApprox(health.CurrentHealth, 4.5f) && Text(hearts.States) == "FFFFM", $"{health.CurrentHealth}");

		contactEnemy.Free();
		Free(player, hud);
		await BossTestHarness.Frames(GetTree(), 1);
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task Drawing()
	{
		GD.Print("\n--- Dibujo ---");
		HeartsRow row = new();
		AddChild(row);
		row.SetHealth(2.5f, 3.0f);
		await BossTestHarness.Frames(GetTree(), 2);
		Check("escala entera x3 (48 px por corazón) y filtro Nearest", HeartsRow.IconScale == 3 && HeartsRow.IconSize * HeartsRow.IconScale == 48 && row.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, $"x{HeartsRow.IconScale}");
		Check("la fila mide lo justo para sus corazones y 48 px de alto", row.Size == new Vector2(3 * HeartsRow.Pitch - 4, 48), $"{row.Size}");
		Check("con los PNG de assets/ui/ se usan las tres texturas", row.HasTexture(HeartsRow.FullIcon) && row.HasTexture(HeartsRow.HalfIcon) && row.HasTexture(HeartsRow.EmptyIcon), "falta algún PNG");
		Check("no hay un nodo por corazón (sin residuos)", row.GetChildCount() == 0, $"{row.GetChildCount()} hijos");
		row.Free();

		string original = HeartsRow.IconFolder;
		try
		{
			HeartsRow.IconFolder = "res://tests/fixtures/none/";
			HeartsRow fallback = new();
			AddChild(fallback);
			fallback.SetHealth(2.5f, 3.0f);
			fallback.QueueRedraw();
			await BossTestHarness.Frames(GetTree(), 2);
			Check("sin texturas usa el dibujo por código y no falla", !fallback.HasTexture(HeartsRow.FullIcon) && !fallback.HasTexture(HeartsRow.HalfIcon) && !fallback.HasTexture(HeartsRow.EmptyIcon) && fallback.HeartCount == 3, "respaldo");
			fallback.Free();
		}
		finally
		{
			HeartsRow.IconFolder = original;
		}
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task Placement()
	{
		GD.Print("\n--- Posición ---");
		SceneTree tree = GetTree();
		GetWindow().Size = new Vector2I(1152, 648);
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		await BossTestHarness.Frames(tree, 4);
		HeartsRow hearts = level.Root.GetNode<HealthHud>("HealthHud").Hearts;
		Rect2 rect = hearts.GetGlobalRect();
		Check($"los corazones están arriba a la izquierda ({rect.Position}, {rect.Size})", rect.Position == new Vector2(16, 16) && rect.Size.Y == 48, $"{rect}");
		Rect2 items = level.Root.GetNode<Control>("ItemsHud/Label").GetGlobalRect();
		Check($"no chocan con el HUD de objetos que va debajo ({items.Position})", !rect.Intersects(items) && rect.End.Y < items.Position.Y, $"{rect} con {items}");
		Check("ni con el minimapa pequeño de arriba a la derecha", !rect.Intersects(level.Root.GetNode<Minimap>("Minimap").View.GetGlobalRect()), "minimapa");
		Check("el mensaje de objeto y la barra del jefe quedan a la derecha de la fila", rect.End.X < level.Root.GetNode<Control>("BossHud/Panel").GetGlobalRect().Position.X, "jefe");
		await BossTestHarness.Close(tree, level);
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task Retry()
	{
		GD.Print("\n--- Reintentar ---");
		SceneTree tree = GetTree();
		Node testScene = this;
		BossTestHarness.Level level = BossTestHarness.OpenAsCurrent(tree, BossTestHarness.Seed, unlimitedHealth: false);
		await BossTestHarness.Frames(tree, 4);
		level.Health.TakeDamage(4.5f);
		HealthHud first = level.Root.GetNode<HealthHud>("HealthHud");
		Check("antes de reintentar, la vida está baja (medio corazón mostrado)", Text(first.Hearts.States) == "MEEEE", Text(first.Hearts.States));
		int nodesBefore = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		long orphansBefore = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

		for (int round = 1; round <= 3; round++)
		{
			level.Run.StartNewRun();
			Node next = await BossTestHarness.WaitForScene(tree, level.Root, GameSettings.LevelScene);
			HealthHud hud = next.GetNode<HealthHud>("HealthHud");
			Check($"reintento {round}: el HUD viejo se liberó y el nuevo empieza con 5 corazones llenos y sin sacudidas", !GodotObject.IsInstanceValid(first) && Text(hud.Hearts.States) == "FFFFF" && Enumerable.Range(0, 5).All(i => !hud.Hearts.IsShaking(i)) && hud.GetChildCount() == 1 && hud.Hearts.GetChildCount() == 0, Text(hud.Hearts.States));
			first = hud;
			level = new BossTestHarness.Level { Root = next, Rooms = next.GetNode<RoomManager>("RoomManager"), Run = next.GetNode<RunManager>("RunManager"), Player = next.GetNode<PlayerController>("Player") };
			level.Health = HealthComponent.FindIn(level.Player);
			await BossTestHarness.Frames(tree, 4);
		}

		int nodesAfter = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		long orphansAfter = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
		Check($"tras tres reintentos no se acumulan nodos ({nodesBefore} → {nodesAfter}) ni huérfanos ({orphansBefore} → {orphansAfter})", Mathf.Abs(nodesAfter - nodesBefore) <= 40 && orphansAfter <= orphansBefore, "acumulación");

		level.Root.QueueFree();
		await tree.ToSignal(level.Root, Node.SignalName.TreeExited);
		tree.Paused = false;
		tree.CurrentScene = testScene;
		await BossTestHarness.Frames(tree, 2);
	}

	// --- ayudas -------------------------------------------------------------------------------------------------------

	private async Task<(PlayerController, HealthHud, HealthComponent)> NewPlayerAndHud()
	{
		PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
		AddChild(player);
		HealthHud hud = GD.Load<PackedScene>("res://scenes/ui/HealthHud.tscn").Instantiate<HealthHud>();
		AddChild(hud);
		await BossTestHarness.Frames(GetTree(), 2);
		return (player, hud, HealthComponent.FindIn(player));
	}

	private static void Free(Node player, Node hud)
	{
		hud.Free();
		player.Free();
	}

	private async Task WaitSeconds(float seconds)
	{
		for (int i = 0; i < Mathf.CeilToInt(seconds * 60.0f); i++)
		{
			await BossTestHarness.Frames(GetTree(), 1);
		}
	}

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  {(ok ? "OK   " : "FALLO")} {name}" + (ok ? "" : $" ({detail})"));
		if (!ok)
		{
			_failures.Add(name);
		}
	}
}
