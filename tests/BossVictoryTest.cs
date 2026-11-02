using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el final de la partida al derrotar a The Mother y la nueva partida desde la pantalla de victoria, con el
/// código real del juego (TestLevel). Modo headless:
///   godot --headless --path . res://tests/BossVictoryTest.tscn
/// Casos:
///   1. Cadenas de victoria: son las tres del juego y la semilla elige siempre la misma.
///   2. Muerte del jefe en cada estado (entrada, aviso de disparo, salva con proyectiles, pausa previa, acercamiento, aviso
///      de embestida, embestida, recuperación y, en las fases 2 y 3, cambio de fase invulnerable, órbita, aviso del anillo y
///      embestidas): al instante la partida termina UNA vez, Noah queda quieto y sin disparar,
///      las transiciones se bloquean, no queda ningún proyectil enemigo ni nace otro, y la pantalla de victoria aparece
///      una sola vez con una de las cadenas.
///   3. Nueva partida en el mismo proceso (PrepareNewRun): semilla distinta de la actual y que manda sobre UseFixedSeed,
///      inventario vacío, mejoras y salud restauradas.
///   4. Nueva partida de punta a punta: el botón «Nueva partida» recarga la escena con otra semilla, sin objetos y con la
///      salud completa. (La prueba convierte TestLevel en la escena actual para que la recarga no reinicie la prueba.)
/// Solo usa APIs que existen también en Release: no depende de las teclas ni de los atajos de depuración. Noah tiene vida de
/// sobra salvo en los casos 3 y 4, donde el jefe muere en cuanto aparece. Sale con código 0 si todo pasa.
/// </summary>
public partial class BossVictoryTest : Node
{
	private const int FixedSeed = 777;

	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== BossVictoryTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckVictoryMessages();
			await CheckVictoryArt();
			await CheckDeathInEveryState();
			await CheckNewRunInProcess();
			await CheckNewRunReload();
			GD.Print($"BossVictoryTest: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			GetTree().Quit(1);
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private void CheckVictoryMessages()
	{
		Require(RunManager.VictoryMessages.Length == 3
			&& RunManager.VictoryMessages[0] == MotherTexts.VictoryDawn
			&& RunManager.VictoryMessages[1] == MotherTexts.VictoryFreedom
			&& RunManager.VictoryMessages[2] == MotherTexts.VictorySilence, "Las tres cadenas de victoria del juego");

		HashSet<string> chosen = new();
		for (int seed = 0; seed < 30; seed++)
		{
			string message = RunManager.VictoryMessageFor(seed);
			Require(Array.IndexOf(RunManager.VictoryMessages, message) >= 0, "La cadena elegida es una de las del juego");
			Require(message == RunManager.VictoryMessageFor(seed), "La misma semilla da la misma cadena");
			chosen.Add(message);
		}

		Require(chosen.Count == 3, "Las tres cadenas se usan según la semilla");
		Require(Array.IndexOf(RunManager.VictoryMessages, RunManager.VictoryMessageFor(-5)) >= 0 &&
			Array.IndexOf(RunManager.VictoryMessages, RunManager.VictoryMessageFor(int.MinValue)) >= 0,
			"Una semilla negativa (--seed=-5) también elige una cadena");
		GD.Print("Cadenas de victoria: OK");
	}

	// La pantalla de victoria usa pantalla_victoria.png de fondo (cubriendo sin deformar) con el texto en tinta oscura en la zona vacía de
	// arriba; sin la imagen cae al diseño de siempre. Se abre en una ventana de 1152×648 (la de diseño; la headless no la tiene).
	private async Task CheckVictoryArt()
	{
		SceneTree tree = GetTree();
		string original = VictoryScreen.ArtPath;
		Vector2I originalSize = tree.Root.Size;
		try
		{
			tree.Root.Size = new Vector2I(1152, 648);
			Rect2 window = new(Vector2.Zero, new Vector2(1152.0f, 648.0f));

			VictoryScreen screen = await OpenVictory();
			Require(screen.HasArt == ResourceLoader.Exists(VictoryScreen.DefaultArtPath) && screen.HasArt, "Está la imagen de la pantalla de victoria y se usa");
			screen.Present("Frase de prueba de la victoria, lo bastante larga para ocupar más de una línea de la pantalla.");
			await BossTestHarness.Frames(tree, 2);
			TextureRect art = screen.GetNode<TextureRect>("Root/Art");
			Require(art.Visible && art.Texture != null && art.StretchMode == TextureRect.StretchModeEnum.KeepAspectCovered
				&& art.ExpandMode == TextureRect.ExpandModeEnum.IgnoreSize, "El fondo cubre la ventana sin deformarse");
			Require(art.GetGlobalRect().IsEqualApprox(window), $"El fondo ocupa toda la ventana ({art.GetGlobalRect()})");
			Require(!screen.GetNode<Control>("Root/Background").Visible, "Con la imagen no hay velo oscuro encima");
			foreach (string name in new[] { "Title", "Message" })
			{
				Label label = screen.GetNode<Label>($"Root/Center/Box/{name}");
				Require(Luminance(label.GetThemeColor("font_color")) < 0.25f, $"«{name}» va en tinta oscura sobre el papel");
			}

			Rect2 box = screen.GetNode<Control>("Root/Center/Box").GetGlobalRect();
			Require(window.Encloses(box) && box.End.Y <= window.Size.Y * VictoryScreen.ArtTextArea + 1.0f,
				$"Título, frase y botón caben en la zona vacía de arriba ({box}), sin tapar la cuna");
			Require(screen.NewRunButton.IsVisibleInTree() && window.Encloses(screen.NewRunButton.GetGlobalRect()) && screen.NewRunButton.HasFocus(),
				"El botón «Nueva partida» se ve, cabe y tiene el foco");
			screen.QueueFree();
			await tree.ToSignal(screen, Node.SignalName.TreeExited);

			// Sin la imagen: el diseño de siempre (velo oscuro y todo centrado).
			VictoryScreen.ArtPath = "res://tests/fixtures/none/pantalla_victoria.png";
			VictoryScreen plain = await OpenVictory();
			plain.Present("Frase de prueba");
			await BossTestHarness.Frames(tree, 2);
			Require(!plain.HasArt && !plain.GetNode<Control>("Root/Art").Visible && plain.GetNode<Control>("Root/Background").Visible
				&& Mathf.IsEqualApprox(plain.GetNode<Control>("Root/Center").AnchorBottom, 1.0f), "Sin la imagen queda el diseño de siempre");
			Require(plain.IsPresented && plain.Message == "Frase de prueba" && plain.NewRunButton.IsVisibleInTree() && plain.NewRunButton.Text == "Nueva partida",
				"Sin la imagen la pantalla funciona igual");
			plain.QueueFree();
			await tree.ToSignal(plain, Node.SignalName.TreeExited);
		}
		finally
		{
			VictoryScreen.ArtPath = original;
			tree.Root.Size = originalSize;
		}

		GD.Print("Pantalla de victoria: imagen de fondo, tinta oscura, zona de texto y caída al diseño de siempre: OK");
	}

	private static float Luminance(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

	private async Task<VictoryScreen> OpenVictory()
	{
		VictoryScreen screen = GD.Load<PackedScene>("res://scenes/ui/VictoryScreen.tscn").Instantiate<VictoryScreen>();
		AddChild(screen);
		await BossTestHarness.Frames(GetTree(), 2);
		return screen;
	}

	private async Task CheckDeathInEveryState()
	{
		await CheckDeathIn("entrada", (b, r, e) => b.State == MotherBehavior.AttackState.Entrance);
		await CheckDeathIn("aviso de disparo", (b, r, e) => b.ShotWarningActive);
		await CheckDeathIn("salva con proyectiles en vuelo", (b, r, e) => b.State == MotherBehavior.AttackState.Volley
			&& !b.ShotWarningActive && r.ProjectilesRoot.GetChildCount() > 0);
		await CheckDeathIn("pausa antes de la embestida", (b, r, e) => b.State == MotherBehavior.AttackState.BeforeDash);
		await CheckDeathIn("acercamiento", (b, r, e) => b.State == MotherBehavior.AttackState.Approach);
		await CheckDeathIn("aviso de embestida", (b, r, e) => b.DashWarningActive);
		await CheckDeathIn("embestida en movimiento", (b, r, e) => b.State == MotherBehavior.AttackState.Dash
			&& !b.DashWarningActive && e.Velocity.Length() > 1.0f);
		await CheckDeathIn("recuperación", (b, r, e) => b.State == MotherBehavior.AttackState.Recovery);

		// Fases 2 y 3: antes de esperar el estado se le quita vida (70 la lleva a la fase 2; 130, a la 3). El cambio de fase es
		// invulnerable, pero Kill() lo ignora: la victoria no puede quedar bloqueada por la invulnerabilidad.
		await CheckDeathIn("cambio de fase invulnerable", (b, r, e) => b.State == MotherBehavior.AttackState.PhaseChange
			&& e.Health.Invulnerable, 70.0f);
		await CheckDeathIn("órbita de la fase 2", (b, r, e) => b.State == MotherBehavior.AttackState.Orbit, 70.0f);
		await CheckDeathIn("aviso del anillo", (b, r, e) => b.RingWarningActive, 70.0f);
		await CheckDeathIn("recuperación tras el anillo con proyectiles en vuelo", (b, r, e) => b.State == MotherBehavior.AttackState.Recovery
			&& r.ProjectilesRoot.GetChildCount() > 0, 70.0f);
		await CheckDeathIn("cambio a la fase 3", (b, r, e) => b.State == MotherBehavior.AttackState.PhaseChange && b.Phase == 3, 130.0f);
		await CheckDeathIn("aviso de embestida de la fase 3", (b, r, e) => b.DashWarningActive && b.Phase == 3, 130.0f);
		await CheckDeathIn("aviso de la espiral", (b, r, e) => b.SpiralWarningActive, 130.0f);
		await CheckDeathIn("espiral con proyectiles en vuelo", (b, r, e) => b.SpiralActive && r.ProjectilesRoot.GetChildCount() > 0, 130.0f);
	}

	private async Task CheckDeathIn(string name, Func<MotherBehavior, RoomController, EnemyBase, bool> inState, float preDamage = 0.0f)
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		int completed = 0, presented = 0, spawnedAfterDeath = 0;
		level.Run.RunCompleted += () => completed++;
		level.Victory.Presented += _ => presented++;

		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		EnemyBase boss = room.BossEnemy;
		MotherBehavior behavior = boss.Behavior<MotherBehavior>();
		PlayerShooter shooter = level.Player.GetNode<PlayerShooter>("Shooter");
		PlayerHealth playerHealth = level.Player.GetNode<PlayerHealth>("PlayerHealth");
		room.ProjectilesRoot.ChildEnteredTree += _ => spawnedAfterDeath++;
		if (preDamage > 0.0f) boss.Health.TakeDamage(preDamage);

		await BossTestHarness.WaitTicks(tree, () => inState(behavior, room, boss), 4000, $"el estado «{name}»");
		spawnedAfterDeath = 0;
		boss.Kill();

		// Todo esto ocurre en la misma pila de llamadas de la muerte: nada espera a un fotograma.
		Require(completed == 1 && level.Run.RunEnded && level.Rooms.RunEnded, $"[{name}] La partida termina al instante, una vez");
		Require(room.State == RoomState.Cleared, $"[{name}] La sala del jefe queda despejada");
		Require(!level.Player.IsPhysicsProcessing() && !shooter.IsPhysicsProcessing(), $"[{name}] Noah no se mueve ni dispara");
		Require(playerHealth.IsLocked, $"[{name}] Noah ya no puede recibir daño ni reiniciar la escena");

		foreach (DoorSide side in Enum.GetValues<DoorSide>())
		{
			room.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);
		}

		await BossTestHarness.Frames(tree, 3);
		Require(GodotObject.IsInstanceValid(room) && level.Rooms.CurrentRoom == room && !level.Player.IsPhysicsProcessing(),
			$"[{name}] Las transiciones están bloqueadas");
		Require(tree.GetNodesInGroup("enemy_projectiles").Count == 0 && room.ProjectilesRoot.GetChildCount() == 0,
			$"[{name}] No queda ningún proyectil enemigo");
		Require(!GodotObject.IsInstanceValid(boss), $"[{name}] El jefe desaparece");

		await BossTestHarness.Frames(tree, 120);
		Require(spawnedAfterDeath == 0, $"[{name}] Tras morir no nace ningún proyectil ({spawnedAfterDeath})");

		await BossTestHarness.WaitUntil(tree, () => level.Victory.IsPresented, 300, "la pantalla de victoria");
		await BossTestHarness.Frames(tree, 120);
		Require(presented == 1 && completed == 1, $"[{name}] Victoria una sola vez ({presented} pantallas, {completed} finales)");
		Require(level.Victory.Message == RunManager.VictoryMessageFor(level.Run.Seed)
			&& Array.IndexOf(RunManager.VictoryMessages, level.Victory.Message) >= 0,
			$"[{name}] La pantalla muestra una cadena de victoria del juego");
		Require(level.Victory.NewRunButton.IsVisibleInTree() && level.Victory.NewRunButton.Text == "Nueva partida",
			$"[{name}] El botón «Nueva partida» está a la vista");
		Require(!level.Player.IsPhysicsProcessing(), $"[{name}] Noah sigue quieto en la pantalla de victoria");

		GD.Print($"Muerte del jefe en «{name}»: fin de partida único, sin proyectiles y con victoria: OK");
		await BossTestHarness.Close(tree, level);
	}

	private static ItemDefinition ItemWith(ItemCatalog catalog, ItemEffect effect)
	{
		foreach (ItemDefinition item in catalog.Items)
		{
			if (item.Effect == effect) return item;
		}

		throw new InvalidDataException($"El catálogo no tiene un objeto con el efecto {effect}.");
	}

	// Noah con dos mejoras (daño y vida máxima) y herido, y el jefe derrotado en cuanto aparece (en su pausa de entrada,
	// sin ataques), para que la salud inicial dependa solo de lo que hace la prueba.
	private async Task<BossTestHarness.Level> PlayUntilVictory(Node parent)
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(parent, FixedSeed, withMother: true, unlimitedHealth: false);
		Require(level.Run.Seed == FixedSeed, "La partida de prueba arranca con la semilla fija");

		PlayerItems items = level.Player.GetNode<PlayerItems>("Items");
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		Require(items.TryCollect(ItemWith(catalog, ItemEffect.Damage)) && items.TryCollect(ItemWith(catalog, ItemEffect.MaxHealth)),
			"Noah recoge una mejora de daño y el oso");
		level.Health.TakeDamage(2.0f);
		Require(items.CollectedItems.Count == 2 && level.Health.MaxHealthBonus > 0.0f
			&& level.Health.CurrentHealth < level.Health.EffectiveMaxHealth, "Noah llega a la victoria con mejoras y herido");

		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		room.BossEnemy.Kill();
		await BossTestHarness.WaitUntil(tree, () => level.Victory.IsPresented, 300, "la pantalla de victoria");
		Require(level.Run.RunEnded, "La partida terminó");
		return level;
	}

	private void RequireFreshRun(BossTestHarness.Level level, string where)
	{
		PlayerItems items = level.Player.GetNode<PlayerItems>("Items");
		PlayerShooter shooter = level.Player.GetNode<PlayerShooter>("Shooter");
		Require(items.CollectedItems.Count == 0, $"[{where}] Inventario vacío");
		Require(level.Health.MaxHealthBonus == 0.0f && Mathf.IsEqualApprox(level.Health.CurrentHealth, level.Health.MaxHealth),
			$"[{where}] Salud restaurada ({level.Health.CurrentHealth}/{level.Health.EffectiveMaxHealth})");
		Require(shooter.DamageMultiplier == 1.0f && shooter.CadenceMultiplier == 1.0f
			&& level.Player.GetNode<PlayerHealth>("PlayerHealth").DamageTakenMultiplier == 1.0f, $"[{where}] Mejoras quitadas");
		Require(!level.Player.GetNode<PlayerHealth>("PlayerHealth").IsLocked, $"[{where}] Noah vuelve a poder recibir daño");
	}

	private async Task CheckNewRunInProcess()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level old = await PlayUntilVictory(this);
		int oldSeed = old.Run.Seed;

		int next = old.Run.PrepareNewRun();
		Require(next != oldSeed && next >= 0, $"La nueva partida usa otra semilla ({oldSeed} → {next})");
		RequireFreshRun(old, "tras PrepareNewRun");
		await BossTestHarness.Close(tree, old);

		// La nueva escena arranca con UseFixedSeed = true y FixedSeed = 777, igual que una partida con semilla fija:
		// la semilla pendiente de la nueva partida debe mandar.
		BossTestHarness.Level fresh = BossTestHarness.Open(this, FixedSeed, withMother: true, unlimitedHealth: false);
		Require(fresh.Run.UseFixedSeed && fresh.Run.FixedSeed == FixedSeed, "La escena nueva pide la semilla fija");
		Require(fresh.Run.Seed == next && fresh.Run.Seed != FixedSeed && fresh.Run.Seed != oldSeed,
			$"La semilla de la nueva partida ({fresh.Run.Seed}) manda sobre la fija ({FixedSeed})");
		RequireFreshRun(fresh, "partida nueva");
		Require(!fresh.Run.RunEnded && !fresh.Rooms.RunEnded && !fresh.Victory.IsPresented, "La partida nueva empieza sin terminar");
		GD.Print($"Nueva partida en el mismo proceso: semilla {oldSeed} → {next}, inventario vacío y salud completa: OK");
		await BossTestHarness.Close(tree, fresh);
	}

	private async Task CheckNewRunReload()
	{
		SceneTree tree = GetTree();
		Node testScene = tree.CurrentScene;

		// ReloadCurrentScene recarga la escena ACTUAL: se hace actual a TestLevel para que la recarga lo reemplace a él y no
		// reinicie esta prueba.
		BossTestHarness.Level old = await PlayUntilVictory(tree.Root);
		tree.CurrentScene = old.Root;
		Node oldRoot = old.Root;
		int oldSeed = old.Run.Seed;

		old.Victory.NewRunButton.EmitSignal(BaseButton.SignalName.Pressed);
		RequireFreshRun(old, "al pulsar el botón");

		await BossTestHarness.WaitUntil(tree, () => !GodotObject.IsInstanceValid(oldRoot), 300, "la recarga de la escena");
		await BossTestHarness.WaitUntil(tree, () => tree.CurrentScene != null && tree.CurrentScene != oldRoot
			&& tree.CurrentScene.SceneFilePath == BossTestHarness.LevelPath, 300, "la escena nueva");
		Node freshRoot = tree.CurrentScene;
		await BossTestHarness.Frames(tree, 5);

		BossTestHarness.Level fresh = new()
		{
			Root = freshRoot,
			Rooms = freshRoot.GetNode<RoomManager>("RoomManager"),
			Run = freshRoot.GetNode<RunManager>("RunManager"),
			Victory = freshRoot.GetNode<VictoryScreen>("VictoryScreen"),
			Player = freshRoot.GetNode<PlayerController>("Player"),
		};
		fresh.Health = HealthComponent.FindIn(fresh.Player);

		Require(fresh.Run.Seed != oldSeed, $"La escena recargada usa otra semilla ({oldSeed} → {fresh.Run.Seed})");
		RequireFreshRun(fresh, "escena recargada");
		Require(!fresh.Run.RunEnded && !fresh.Victory.IsPresented && fresh.Rooms.CurrentRoom != null
			&& fresh.Rooms.CurrentRoom.Data.Type == RoomType.Start, "La partida recargada empieza en la sala inicial");
		GD.Print($"Botón «Nueva partida»: escena recargada con la semilla {fresh.Run.Seed}: OK");

		freshRoot.QueueFree();
		await tree.ToSignal(freshRoot, Node.SignalName.TreeExited);
		tree.CurrentScene = testScene;
	}
}
