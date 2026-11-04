using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de los sonidos del lote 2 enganchados al juego real. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/AudioHooksTest.tscn
/// Sin dispositivo de audio, AudioManager registra cada petición en Requests, que es lo que se mira aquí.
///
///   1. Catálogo: los 12 archivos nuevos existen y cada uno tiene su entrada, con una voz como mucho en los que se solapan
///      (puertas, curar, confirmar, jefe, victoria); jefe_golpe y jefe_fase pueden estar sin archivo.
///   2. Interfaz: un botón normal pide menu_click; Jugar, menu_confirmar (y no el clic); un botón con el metadato, el suyo.
///   3. Objetos: el biberón con vida que curar pide curar (y no objeto_recoger); sin nada que curar no suena; el resto, objeto_recoger.
///   4. Una partida hasta el jefe: las puertas se cierran al empezar el combate (una sola voz por evento, no una por puerta), el jefe pide
///      jefe_aparece una vez, 3D; al matarlo suenan jefe_muere (por su propio id), puerta_abre y, tras el fundido, victoria una sola vez.
///   5. Los ataques de The Mother: el anillo pide jefe_ataque_1 una vez y la espiral jefe_ataque_2 una vez (sin los disparos del tirador).
///   6. La victoria suena una sola vez al mostrar su pantalla.
/// Antes de salir recolecta los envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class AudioHooksTest : Node
{
	private static readonly string[] NewFiles =
	{
		"puerta_abre", "puerta_cierra", "objeto_recoger", "curar", "menu_click", "menu_confirmar",
		"jefe_aparece", "jefe_ataque_1", "jefe_ataque_2", "jefe_muere", "victoria", "derrota",
	};

	private int _checks;
	private AudioManager _audio;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== AudioHooksTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			_audio = AudioManager.Instance;
			CheckCatalog();
			await CheckButtons();
			await CheckItems();
			await CheckBossFlow();
			await CheckBossAttacks();
			await CheckVictoryScreen();
			await CheckPageTurn();
			GD.Print($"AudioHooksTest: {_checks} comprobaciones. RESULTADO: OK");
			await BossTestHarness.CollectWrappers(GetTree());
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
		if (!condition)
		{
			throw new InvalidDataException(message);
		}
	}

	private int Count(string id) => _audio.Requests.Count(request => request.Id == id);

	private AudioManager.Request Single(string id) => _audio.Requests.Single(request => request.Id == id);

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void CheckCatalog()
	{
		GD.Print("\n--- Catálogo ---");
		foreach (string id in NewFiles)
		{
			SoundCatalog.Sound sound = SoundCatalog.Get(id);
			Require(sound != null && ResourceLoader.Exists(sound.Path), $"{id}: está en el catálogo y su archivo existe");
			Require(sound.MaxVoices <= 4, $"{id}: respeta el límite de voces ({sound.MaxVoices})");
		}

		foreach (string id in new[] { "puerta_abre", "puerta_cierra", "curar", "menu_confirmar", "jefe_aparece", "jefe_ataque_1", "jefe_ataque_2", "victoria", "derrota" })
		{
			Require(SoundCatalog.Get(id).MaxVoices == 1, $"{id}: una sola voz");
		}

		Require(SoundCatalog.Get(SoundCatalog.BossAppears).Positional && SoundCatalog.Get(SoundCatalog.BossAttackRing).Positional && SoundCatalog.Get(SoundCatalog.BossAttackSpiral).Positional,
			"Los sonidos del jefe son 3D");
		Require(!SoundCatalog.Get(SoundCatalog.DoorOpens).Positional && !SoundCatalog.Get(SoundCatalog.Victory).Positional, "Puertas y victoria son planos");
		Require(SoundCatalog.Get(SoundCatalog.BossHit) != null && SoundCatalog.Get(SoundCatalog.BossPhase) != null, "jefe_golpe y jefe_fase están en el catálogo (con o sin archivo)");
		GD.Print("Catálogo de los sonidos nuevos: OK");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task CheckButtons()
	{
		GD.Print("\n--- Botones ---");
		_audio.ClearRequests();
		Control root = new();
		AddChild(root);
		Button plain = new(), confirm = new();
		confirm.SetMeta(AudioManager.UiSoundMeta, SoundCatalog.MenuConfirm);
		root.AddChild(plain);
		root.AddChild(confirm);
		AudioManager.HookButtons(root);
		plain.EmitSignal(BaseButton.SignalName.Pressed);
		Require(Count("menu_click") == 1 && Count("menu_confirmar") == 0 && Single("menu_click").Result != AudioManager.Outcome.Missing, "Un botón normal pide menu_click");
		confirm.EmitSignal(BaseButton.SignalName.Pressed);
		Require(Count("menu_click") == 1 && Count("menu_confirmar") == 1, "Un botón con el metadato pide el suyo y no el clic");
		root.QueueFree();
		await ToSignal(root, Node.SignalName.TreeExited);

		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		AddChild(menu);
		await BossTestHarness.Frames(GetTree(), 2);
		Require(menu.PlayButton.GetMeta(AudioManager.UiSoundMeta).AsString() == SoundCatalog.MenuConfirm, "Jugar lleva menu_confirmar");
		_audio.ClearRequests();
		menu.GetNode<Button>("Center/Box/ControlsButton").EmitSignal(BaseButton.SignalName.Pressed);
		Require(Count("menu_click") == 1 && Count("menu_confirmar") == 0, "Controles, en cambio, suena como un clic");
		menu.Controls.Close();
		menu.QueueFree();
		await ToSignal(menu, Node.SignalName.TreeExited);
		GD.Print("Clic de los botones y confirmar en Jugar: OK");
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task CheckItems()
	{
		GD.Print("\n--- Objetos ---");
		PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
		AddChild(player);
		await BossTestHarness.Frames(GetTree(), 2);
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		HealthComponent health = HealthComponent.FindIn(player);

		ItemPickup Make(ItemEffect effect)
		{
			ItemPickup pickup = GD.Load<PackedScene>("res://scenes/items/ItemPickup.tscn").Instantiate<ItemPickup>();
			pickup.Definition = catalog.Items.First(item => item.Effect == effect);
			AddChild(pickup);
			return pickup;
		}

		_audio.ClearRequests();
		ItemPickup bottle = Make(ItemEffect.Heal);
		Require(!bottle.TryCollect(player) && Count("curar") == 0 && Count("objeto_recoger") == 0, "Con la vida llena el biberón no se recoge y no suena nada");
		health.TakeDamage(2.0f);
		Require(bottle.TryCollect(player), "Herida, Noah recoge el biberón");
		Require(Count("curar") == 1 && Count("objeto_recoger") == 0 && Single("curar").Result != AudioManager.Outcome.Missing, "El biberón suena curar (y no objeto_recoger)");

		_audio.ClearRequests();
		ItemPickup damage = Make(ItemEffect.Damage);
		Require(damage.TryCollect(player) && Count("objeto_recoger") == 1 && Count("curar") == 0 && Single("objeto_recoger").Result != AudioManager.Outcome.Missing, "Un objeto de mejora suena objeto_recoger (y no curar)");
		player.QueueFree();
		await ToSignal(player, Node.SignalName.TreeExited);
		GD.Print("Recoger y curar: OK");
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task CheckBossFlow()
	{
		GD.Print("\n--- Partida hasta el jefe ---");
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await BossTestHarness.Frames(tree, 3);
		_audio.ClearRequests();
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		await BossTestHarness.Frames(tree, 2);
		Require(Count("puerta_cierra") >= 1, $"Las puertas se cierran al empezar el combate ({Count("puerta_cierra")})");
		Require(Count("jefe_aparece") == 1 && Single("jefe_aparece").Positional && Single("jefe_aparece").Result != AudioManager.Outcome.Missing, "El jefe suena al aparecer, una vez y en 3D");
		int doorSounds = Count("puerta_cierra");
		Require(doorSounds <= level.Rooms.Map.Rooms.Count(), "Una voz por evento de puertas, no una por puerta");

		_audio.ClearRequests();
		room.BossEnemy.Kill();
		await BossTestHarness.WaitUntil(tree, () => level.Victory.IsPresented, 300, "la pantalla de victoria");
		await BossTestHarness.Frames(tree, 2);
		Require(Count("jefe_muere") >= 1 && Single("jefe_muere").Result != AudioManager.Outcome.Fallback && Single("jefe_muere").Result != AudioManager.Outcome.Missing, "El jefe muere con su propio sonido");
		Require(Count("puerta_abre") == 1, $"Al despejar la sala se abren las puertas con una sola voz ({Count("puerta_abre")})");
		Require(Count("victoria") == 1, $"La victoria suena una sola vez ({Count("victoria")})");
		await BossTestHarness.Close(tree, level);
		GD.Print("Puertas, aparición, muerte del jefe y victoria: OK");
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task CheckBossAttacks()
	{
		GD.Print("\n--- Ataques de The Mother ---");
		SceneTree tree = GetTree();

		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		MotherBehavior behavior = room.BossEnemy.Behavior<MotherBehavior>();
		room.BossEnemy.Health.TakeDamage(70.0f);
		Require(behavior.Phase == 2, "En la fase 2");
		await BossTestHarness.WaitTicks(tree, () => behavior.RingWarningActive, 6000, "el aviso del anillo");
		_audio.ClearRequests();
		await BossTestHarness.WaitTicks(tree, () => behavior.State == MotherBehavior.AttackState.Recovery, 600, "el anillo disparado");
		Require(Count("jefe_ataque_1") == 1 && Single("jefe_ataque_1").Positional && Count("tirador_disparo") == 0 && Count("jefe_ataque_2") == 0,
			$"El anillo suena jefe_ataque_1 una vez en 3D y no como un disparo de tirador ({Count("jefe_ataque_1")}, {Count("tirador_disparo")})");
		await BossTestHarness.Close(tree, level);

		level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await BossTestHarness.GoToBoss(tree, level);
		room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => room.BossEnemy != null, 300, "la aparición de The Mother");
		behavior = room.BossEnemy.Behavior<MotherBehavior>();
		room.BossEnemy.Health.TakeDamage(130.0f);
		Require(behavior.Phase == 3, "En la fase 3");
		await BossTestHarness.WaitTicks(tree, () => behavior.SpiralWarningActive, 9000, "el aviso de la espiral");
		_audio.ClearRequests();
		await BossTestHarness.WaitTicks(tree, () => behavior.SpiralActive, 300, "la espiral");
		await BossTestHarness.WaitTicks(tree, () => behavior.State == MotherBehavior.AttackState.Recovery, 400, "el fin de la espiral");
		Require(Count("jefe_ataque_2") == 1 && Single("jefe_ataque_2").Positional && Count("tirador_disparo") == 0 && Count("jefe_ataque_1") == 0,
			$"La espiral suena jefe_ataque_2 una sola vez, sin disparos de tirador ({Count("jefe_ataque_2")}, {Count("tirador_disparo")})");
		await BossTestHarness.Close(tree, level);
		GD.Print("Anillo y espiral con su sonido: OK");
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task CheckVictoryScreen()
	{
		GD.Print("\n--- Pantalla de victoria ---");
		VictoryScreen screen = GD.Load<PackedScene>("res://scenes/ui/VictoryScreen.tscn").Instantiate<VictoryScreen>();
		AddChild(screen);
		await BossTestHarness.Frames(GetTree(), 2);
		_audio.ClearRequests();
		screen.Present("Frase");
		screen.Present("Otra");
		Require(Count("victoria") == 1 && Single("victoria").Result != AudioManager.Outcome.Missing, "Suena victoria una sola vez aunque se pida dos");
		screen.QueueFree();
		await ToSignal(screen, Node.SignalName.TreeExited);
		GD.Print("Victoria al mostrar la pantalla: OK");
	}

	// --- 7 ------------------------------------------------------------------------------------------------------------

	// pagina_pasa suena al aparecer la pantalla de carga y la de muerte (el diario), una vez cada una, y no en la intro.
	private async Task CheckPageTurn()
	{
		GD.Print("\n--- Pasar la página ---");
		SceneTree tree = GetTree();
		SoundCatalog.Sound page = SoundCatalog.Get(SoundCatalog.PageTurn);
		Require(page != null && ResourceLoader.Exists(page.Path) && !page.Positional && page.Bus == SoundCatalog.BusSfx && page.VolumeDb <= -6.0f, "pagina_pasa tiene archivo y es plana, discreta y del bus SFX");

		LoadingScreen loading = new() { MinVisibleSeconds = 0.1f, FadeSeconds = 0.05f };
		_audio.ClearRequests();
		AddChild(loading);
		await BossTestHarness.Frames(tree, 2);
		Require(Count("pagina_pasa") == 1 && Single("pagina_pasa").Result != AudioManager.Outcome.Missing && !Single("pagina_pasa").Positional, "Al aparecer la pantalla de carga suena pagina_pasa una vez");
		await loading.Complete();
		await BossTestHarness.Frames(tree, 2);
		Require(Count("pagina_pasa") == 1, "Y no vuelve a sonar al desvanecerse");

		OS.DelayMsec(400); // pasa el intervalo mínimo (reloj real) para que el limitador no descarte la siguiente
		DefeatScreen defeat = GD.Load<PackedScene>("res://scenes/ui/DefeatScreen.tscn").Instantiate<DefeatScreen>();
		AddChild(defeat);
		await BossTestHarness.Frames(tree, 2);
		_audio.ClearRequests();
		defeat.Present("Frase", DeathCause.Chaser);
		defeat.Present("Otra", DeathCause.Chaser);
		Require(Count("pagina_pasa") == 1 && Single("pagina_pasa").Result != AudioManager.Outcome.Missing && Count("derrota") == 1,
			"Al aparecer la pantalla de muerte suena pagina_pasa una vez, junto a derrota");
		defeat.QueueFree();
		await ToSignal(defeat, Node.SignalName.TreeExited);

		// La intro ya tiene su voz y su canto: no pasa páginas.
		IntroScene.Shown = false;
		IntroScene.ForcePlay = true;
		IntroScene.TimeScale = 0.05f;
		IntroScene intro = GD.Load<PackedScene>("res://scenes/ui/IntroScene.tscn").Instantiate<IntroScene>();
		intro.NextScene = null;
		_audio.ClearRequests();
		AddChild(intro);
		await BossTestHarness.Frames(tree, 20);
		bool played = intro.Playing;
		Require(Count("pagina_pasa") == 0, "La intro no usa pagina_pasa");
		intro.QueueFree();
		await ToSignal(intro, Node.SignalName.TreeExited);
		IntroScene.ForcePlay = false;
		IntroScene.TimeScale = 1.0f;
		IntroScene.Shown = false;
		GD.Print($"pagina_pasa: carga, muerte y no en la intro (la intro sonaba: {played}): OK");
	}
}
