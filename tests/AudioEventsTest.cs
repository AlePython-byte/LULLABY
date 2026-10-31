using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba del audio, sin reproducir nada real (AudioManager en modo Silent, que es también lo que hace --headless). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/AudioEventsTest.tscn
///
///   1. Buses (Master, SFX, Musica) y catálogo: los archivos de Luis existen y los que faltan (objetos, interfaz, jefe) no.
///   2. Cada evento real pide su sonido, plano o posicional, con su bus: disparo e impacto de lágrima, daño y muerte de Noah, golpe y
///      muerte de enemigos, mecha y explosión del explosivo (que no suena además como muerte), disparo del tirador y del excavador y
///      el bucle del aleteo mientras haya voladores vivos (uno solo para varios, y se detiene al morir todos).
///   3. Los faltantes no rompen: objeto recogido, clic de interfaz, música e ids desconocidos solo quedan registrados como Missing o
///      Unknown. The Mother usa los sonidos de los enemigos con el tono -20 % mientras no existan los suyos.
///   4. Tono ±5 % en los repetidos con azar propio (el global no cambia); límites de voces, intervalo mínimo y prioridad.
///   5. StopAll corta todo, también al reiniciar o volver al menú desde RunManager.
///   5b. Un fallo de audio (inyectado) no altera el juego: el daño de lágrimas y proyectiles, la muerte y liberación de enemigos, la
///      derrota de Noah, la recogida de objetos y la salva de The Mother (un ShootBehavior colgado de otro comportamiento) funcionan
///      igual; el fallo se captura, se cuenta y tras MaxFailures el audio se desactiva solo.
///   6. Volúmenes: buses, guardado en settings.cfg, valores por defecto con el archivo ausente o dañado, deslizadores y menús.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class AudioEventsTest : Node
{
	private const string TestSettings = "user://audio_test_settings.cfg";

	private readonly List<string> _failures = new();
	private AudioManager _audio;

	public override async void _Ready()
	{
		GD.Print("=== AudioEventsTest ===");
		string originalPath = GameSettings.SettingsPath;
		try
		{
			_audio = AudioManager.Instance;
			Check("el AudioManager está registrado como autoload", _audio != null, "sin AudioManager");
			_audio.Silent = true;
			GameSettings.SettingsPath = TestSettings;
			BusesAndCatalog();
			await Events();
			MissingSounds();
			Variation();
			Voices();
			await StopAll();
			Volumes();
			await FaultTolerance();
			await Sliders();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}
		finally
		{
			GameSettings.SettingsPath = originalPath;
			DirAccess.RemoveAbsolute(TestSettings);
			GameSettings.MasterVolume = GameSettings.DefaultMasterVolume;
			GameSettings.SfxVolume = GameSettings.DefaultSfxVolume;
			AudioManager.ApplyVolumes();
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void BusesAndCatalog()
	{
		GD.Print("\n--- Buses y catálogo ---");
		int master = AudioServer.GetBusIndex("Master"), sfx = AudioServer.GetBusIndex("SFX"), music = AudioServer.GetBusIndex("Musica");
		Check("existen los buses Master, SFX y Musica", master >= 0 && sfx >= 0 && music >= 0, $"{master} {sfx} {music}");
		Check("SFX y Musica envían a Master", sfx >= 0 && music >= 0 && AudioServer.GetBusSend(sfx) == "Master" && AudioServer.GetBusSend(music) == "Master", "envío");
		string[] present = { "lagrima_disparo", "lagrima_impacto", "noah_dano", "noah_muere", "enemigo_golpe", "enemigo_muere", "mecha_explosivo", "explosion", "tirador_disparo", "volador_aleteo" };
		Check("los 10 archivos de efectos existen y están en el catálogo", present.All(id => SoundCatalog.Get(id) != null && ResourceLoader.Exists(SoundCatalog.Get(id).Path)), string.Join(",", present.Where(id => SoundCatalog.Get(id) == null || !ResourceLoader.Exists(SoundCatalog.Get(id).Path))));
		string[] missing = { SoundCatalog.ItemPickedUp, SoundCatalog.UiClick, SoundCatalog.BossHit, SoundCatalog.BossDies };
		Check("los sonidos de objetos, interfaz y jefe están en el catálogo pero aún sin archivo", missing.All(id => SoundCatalog.Get(id) != null && !ResourceLoader.Exists(SoundCatalog.Get(id).Path)), "alguno existe ya (¡bien!) o falta del catálogo");
		Check("el aleteo se importa sin bucle (se activa por código)", ResourceLoader.Load<AudioStreamOggVorbis>(SoundCatalog.Get("volador_aleteo").Path).Loop == false, "la importación activó el bucle");
		Check("los enemigos son posicionales y Noah plano",
			SoundCatalog.Get("enemigo_golpe").Positional && SoundCatalog.Get("explosion").Positional && SoundCatalog.Get("tirador_disparo").Positional
			&& !SoundCatalog.Get("noah_dano").Positional && !SoundCatalog.Get("lagrima_disparo").Positional && !SoundCatalog.Get("lagrima_impacto").Positional, "tipo incorrecto");
		Check("los repetidos llevan ±5 % de variación y 3 o 4 voces como mucho", SoundCatalog.Get("lagrima_disparo").PitchVariation == 0.05f
			&& SoundCatalog.Get("lagrima_impacto").PitchVariation == 0.05f && SoundCatalog.Get("enemigo_golpe").PitchVariation == 0.05f
			&& SoundCatalog.All.All(s => s.MaxVoices <= 4), "variación o voces");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task Events()
	{
		GD.Print("\n--- Eventos ---");
		Node3D world = new();
		AddChild(world);
		PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
		world.AddChild(player);
		await Frames(3);
		_audio.ClearRequests();

		// Noah
		typeof(PlayerShooter).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player.GetNode<PlayerShooter>("Shooter"), new object[] { Vector3.Right });
		AudioManager.Request shot = Single("lagrima_disparo");
		Check("disparar una lágrima pide lagrima_disparo, plano y en el bus SFX", shot.Id != null && !shot.Positional && shot.Bus == "SFX", "sin petición");

		Tear tear = GD.Load<PackedScene>("res://scenes/combat/Tear.tscn").Instantiate<Tear>();
		world.AddChild(tear);
		StaticBody3D wall = new();
		tear.EmitSignal(Area3D.SignalName.BodyEntered, wall);
		wall.Free();
		Check("el impacto de una lágrima pide lagrima_impacto, plano", Single("lagrima_impacto") is { Id: not null, Positional: false }, "sin petición");

		PlayerHealth health = player.GetNode<PlayerHealth>("PlayerHealth");
		_audio.ClearRequests();
		health.TakeHit(1.0f);
		Check("un golpe que no mata a Noah pide noah_dano, plano", Count("noah_dano") == 1 && Count("noah_muere") == 0 && !Single("noah_dano").Positional, "sonidos de daño");
		_audio.ClearRequests();
		health.Defeated += () => { };
		HealthComponent.FindIn(player).TakeDamage(100.0f);
		Check("el golpe que mata a Noah pide solo noah_muere", Count("noah_muere") == 1 && Count("noah_dano") == 0, "sonidos de muerte");
		player.Free();

		// Enemigos
		EnemyBase basic = NewEnemy(world, "res://scenes/enemies/BasicEnemy.tscn", new Vector3(3, 0.5f, 2));
		_audio.ClearRequests();
		HealthComponent.FindIn(basic).TakeDamage(1.0f);
		AudioManager.Request hit = Single("enemigo_golpe");
		Check("un golpe que no mata a un enemigo pide enemigo_golpe, 3D y en su posición", hit.Id != null && hit.Positional && hit.Position.IsEqualApprox(new Vector3(3, 0.5f, 2)) && Count("enemigo_muere") == 0, "golpe");
		_audio.ClearRequests();
		HealthComponent.FindIn(basic).TakeDamage(100.0f);
		Check("matar a un enemigo pide solo enemigo_muere, 3D y en su posición", Count("enemigo_muere") == 1 && Count("enemigo_golpe") == 0 && Single("enemigo_muere").Position.IsEqualApprox(new Vector3(3, 0.5f, 2)), "muerte");
		await Frames(2);

		// The Mother (y cualquier jefe): sin jefe_golpe ni jefe_muere usa los de los enemigos con -20 % de tono.
		EnemyBase boss = NewEnemy(world, "res://scenes/enemies/BasicEnemy.tscn", new Vector3(0, 0.5f, 0));
		boss.SoundSet = "jefe";
		OS.DelayMsec(80); // pasa el intervalo mínimo del golpe anterior (comparten límites de voces con el enemigo)
		_audio.ClearRequests();
		HealthComponent.FindIn(boss).TakeDamage(1.0f);
		AudioManager.Request bossHit = Single("jefe_golpe");
		Check("un jefe sin sonidos propios usa el golpe de los enemigos con el tono un 20 % más grave", bossHit.Result == AudioManager.Outcome.Fallback && bossHit.PitchScale is > 0.75f and < 0.85f, $"{bossHit.Result} {bossHit.PitchScale}");
		_audio.ClearRequests();
		OS.DelayMsec(80);
		HealthComponent.FindIn(boss).TakeDamage(100.0f);
		AudioManager.Request bossDies = Single("jefe_muere");
		Check("y su muerte usa la de los enemigos con el tono un 20 % más grave", bossDies.Result == AudioManager.Outcome.Fallback && Mathf.Abs(bossDies.PitchScale - 0.8f) < 0.001f, $"{bossDies.Result} {bossDies.PitchScale}");
		Check("la escena de The Mother usa la familia \"jefe\"", GD.Load<PackedScene>("res://scenes/bosses/TheMother.tscn").Instantiate<EnemyBase>() is { SoundSet: "jefe" } mother && FreeAndTrue(mother), "SoundSet");
		await Frames(2);

		// Explosivo
		EnemyBase exploder = NewEnemy(world, "res://scenes/enemies/ExploderEnemy.tscn", new Vector3(-4, 0.5f, 1));
		FuseBehavior fuse = exploder.Behavior<FuseBehavior>();
		_audio.ClearRequests();
		fuse.EmitSignal(FuseBehavior.SignalName.FuseLit);
		Check("encender la mecha pide mecha_explosivo, 3D en el explosivo", Single("mecha_explosivo") is { Positional: true } fusePing && fusePing.Position.IsEqualApprox(new Vector3(-4, 0.5f, 1)), "mecha");
		Node3D target = new() { Position = new Vector3(40, 0.5f, 0) };
		world.AddChild(target);
		_audio.ClearRequests();
		typeof(FuseBehavior).GetMethod("Explode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fuse, new object[] { exploder, target, 99.0f });
		Check("explotar pide explosion y la muerte del explosivo no suena además", Count("explosion") == 1 && Count("enemigo_muere") == 0 && Single("explosion").Positional, "explosión");
		await Frames(2);

		// Tirador y excavador
		EnemyBase shooter = NewEnemy(world, "res://scenes/enemies/ShooterEnemy.tscn", new Vector3(5, 0.5f, 5));
		_audio.ClearRequests();
		shooter.Behavior<ShootBehavior>().EmitSignal(ShootBehavior.SignalName.Fired, Vector3.Left);
		Check("el tirador al disparar pide tirador_disparo, 3D en su posición", Single("tirador_disparo") is { Positional: true } shooterPing && shooterPing.Position.IsEqualApprox(new Vector3(5, 0.5f, 5)), "tirador");
		EnemyBase digger = NewEnemy(world, "res://scenes/enemies/DiggerEnemy.tscn", new Vector3(-5, 0.5f, 5));
		_audio.ClearRequests();
		digger.Behavior<DigBehavior>().EmitSignal(DigBehavior.SignalName.Fired, Vector3.Left);
		Check("el excavador al disparar usa también tirador_disparo", Single("tirador_disparo") is { Positional: true }, "excavador");

		// Bucle del volador
		AudioManager.StopAll();
		_audio.ClearRequests();
		EnemyBase flyerA = NewEnemy(world, "res://scenes/enemies/FlyerEnemy.tscn", new Vector3(2, 0.5f, 2));
		EnemyBase flyerB = NewEnemy(world, "res://scenes/enemies/FlyerEnemy.tscn", new Vector3(4, 0.5f, 2));
		flyerA.SetPhysicsProcess(false);
		flyerB.SetPhysicsProcess(false);
		await Frames(30);
		Check("con voladores vivos hay un único bucle de aleteo, en su posición media", _audio.FlyerLoopActive && Count("volador_aleteo:loop") == 1 && _audio.FlyerLoopPosition.DistanceTo(new Vector3(3, 0.5f, 2)) < 0.1f, $"activo {_audio.FlyerLoopActive}, {Count("volador_aleteo:loop")} bucles, en {_audio.FlyerLoopPosition}");
		flyerA.Kill();
		await Frames(30);
		Check("sigue mientras quede un volador vivo", _audio.FlyerLoopActive && Count("volador_aleteo:stop") == 0, "se detuvo antes");
		flyerB.Kill();
		await Frames(30);
		Check("al morir todos los voladores el bucle se detiene", !_audio.FlyerLoopActive && Count("volador_aleteo:stop") == 1, "no se detuvo");

		world.Free();
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private void MissingSounds()
	{
		GD.Print("\n--- Faltantes ---");
		AudioManager.StopAll();
		_audio.ClearRequests();
		PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
		AddChild(player);
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		ItemPickup pickup = GD.Load<PackedScene>("res://scenes/items/ItemPickup.tscn").Instantiate<ItemPickup>();
		pickup.Definition = catalog.Items.First(i => i.Effect == ItemEffect.Damage);
		AddChild(pickup);
		bool collected = pickup.TryCollect(player);
		Check("recoger un objeto pide objeto_recogido y, sin archivo, queda como faltante sin romper nada", collected && Single("objeto_recogido").Result == AudioManager.Outcome.Missing, "objeto");
		Button button = new();
		AddChild(button);
		AudioManager.HookButtons(this);
		button.EmitSignal(BaseButton.SignalName.Pressed);
		Check("pulsar un botón pide ui_click y, sin archivo, queda como faltante", Single("ui_click").Result == AudioManager.Outcome.Missing, "clic");
		AudioManager.PlayMusic("menu");
		Check("la música sin archivo no suena ni rompe (gancho listo)", _audio.CurrentMusic == null && Single("musica:menu").Result == AudioManager.Outcome.Missing, "música");
		AudioManager.StopMusic();
		AudioManager.Play("no_existe");
		AudioManager.PlayAt("tampoco", Vector3.One);
		Check("un id desconocido queda registrado como desconocido", Single("no_existe").Result == AudioManager.Outcome.Unknown && Single("tampoco").Result == AudioManager.Outcome.Unknown, "desconocido");
		Check("ningún faltante deja voces sonando", _audio.ActiveVoiceCount == 0, $"{_audio.ActiveVoiceCount} voces");
		button.Free();
		pickup.Free();
		player.Free();
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private void Variation()
	{
		GD.Print("\n--- Tono y azar ---");
		_audio.ClearRequests();
		GD.Seed(2024);
		uint before = GD.Randi();
		GD.Seed(2024);
		for (int i = 0; i < 300; i++)
		{
			AudioManager.Play("lagrima_disparo");
			AudioManager.Play("lagrima_impacto");
			AudioManager.PlayAt("enemigo_golpe", Vector3.Zero);
			AudioManager.Play("noah_dano");
		}

		Check("el azar global no cambia al variar el tono", GD.Randi() == before, "azar global consumido");
		foreach (string id in new[] { "lagrima_disparo", "lagrima_impacto", "enemigo_golpe" })
		{
			float[] pitches = _audio.Requests.Where(r => r.Id == id).Select(r => r.PitchScale).ToArray();
			Check($"{id}: el tono varía dentro de ±5 % ({pitches.Min():F3} a {pitches.Max():F3})", pitches.Min() >= 0.95f && pitches.Max() <= 1.05f && pitches.Max() - pitches.Min() > 0.05f, "fuera de rango o sin variación");
		}

		Check("noah_dano no varía de tono", _audio.Requests.Where(r => r.Id == "noah_dano").All(r => Mathf.IsEqualApprox(r.PitchScale, 1.0f)), "tono variable");
		AudioManager.StopAll();
	}

	private void Voices()
	{
		GD.Print("\n--- Voces ---");
		AudioManager.StopAll();
		_audio.ClearRequests();
		for (int i = 0; i < 10; i++)
		{
			AudioManager.PlayAt("mecha_explosivo", Vector3.Zero);
		}

		int accepted = _audio.Requests.Count(r => r.Result == AudioManager.Outcome.Silent);
		int dropped = _audio.Requests.Count(r => r.Result == AudioManager.Outcome.Dropped);
		Check($"mecha_explosivo admite {SoundCatalog.Get("mecha_explosivo").MaxVoices} voces a la vez y descarta el resto", accepted == SoundCatalog.Get("mecha_explosivo").MaxVoices && dropped == 10 - accepted, $"{accepted} aceptadas, {dropped} descartadas");

		AudioManager.StopAll();
		_audio.ClearRequests();
		AudioManager.PlayAt("enemigo_golpe", Vector3.Zero);
		AudioManager.PlayAt("enemigo_golpe", Vector3.Zero);
		Check("dos golpes en el mismo instante: el segundo cae por el intervalo mínimo", _audio.Requests[0].Result == AudioManager.Outcome.Silent && _audio.Requests[1].Result == AudioManager.Outcome.Dropped, $"{_audio.Requests[0].Result}, {_audio.Requests[1].Result}");

		AudioManager.StopAll();
		_audio.ClearRequests();
		for (int i = 0; i < 8; i++)
		{
			AudioManager.PlayAt("explosion", Vector3.Zero);
		}

		Check("un sonido prioritario (explosion) roba la voz más antigua en vez de descartarse", _audio.Requests.All(r => r.Result == AudioManager.Outcome.Silent) && _audio.ActiveVoiceCount <= SoundCatalog.Get("explosion").MaxVoices, $"{_audio.ActiveVoiceCount} voces");
		Check("las voces nunca superan la capacidad", _audio.ActiveVoiceCount <= AudioManager.FlatVoices + AudioManager.SpatialVoices, $"{_audio.ActiveVoiceCount}");
		AudioManager.StopAll();
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task StopAll()
	{
		GD.Print("\n--- Cortar al reiniciar o volver al menú ---");
		AudioManager.Play("noah_muere");
		Check("noah_muere dura casi 7 s y queda sonando", _audio.ActiveVoiceCount == 1, $"{_audio.ActiveVoiceCount}");
		AudioManager.StopAll();
		Check("StopAll corta todas las voces", _audio.ActiveVoiceCount == 0, $"{_audio.ActiveVoiceCount}");

		SceneTree tree = GetTree();
		foreach (bool restart in new[] { true, false })
		{
			BossTestHarness.Level level = BossTestHarness.OpenAsCurrent(tree, BossTestHarness.Seed);
			await BossTestHarness.Frames(tree, 3);
			AudioManager.Play("noah_muere");
			Check($"{(restart ? "reiniciar la partida" : "volver al menú")}: noah_muere suena antes", _audio.ActiveVoiceCount == 1, $"{_audio.ActiveVoiceCount}");
			if (restart)
			{
				level.Run.StartNewRun();
			}
			else
			{
				level.Run.ReturnToMenu();
			}

			Check($"{(restart ? "StartNewRun" : "ReturnToMenu")} corta noah_muere", _audio.ActiveVoiceCount == 0, $"{_audio.ActiveVoiceCount}");
			Node next = await BossTestHarness.WaitForScene(tree, level.Root, restart ? GameSettings.LevelScene : GameSettings.MenuScene);
			next.QueueFree();
			await tree.ToSignal(next, Node.SignalName.TreeExited);
			tree.Paused = false;
			tree.CurrentScene = this;
			await Frames(2);
		}
	}

	// --- 5b -----------------------------------------------------------------------------------------------------------

	private async Task FaultTolerance()
	{
		GD.Print("\n--- Un fallo de audio no altera el juego ---");
		AudioManager.StopAll();
		_audio.ResetFailures();
		int injected = 0;
		AudioManager.FaultInjection = id =>
		{
			injected++;
			throw new InvalidOperationException("fallo de audio simulado: " + id);
		};
		try
		{
			Node3D world = new();
			AddChild(world);
			PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
			world.AddChild(player);
			player.GetNode<PlayerHealth>("PlayerHealth").Defeated += () => { };
			HealthComponent playerHealth = HealthComponent.FindIn(player);
			await Frames(3);

			// Lágrima contra enemigo: el daño se aplica y la lágrima se libera aunque el sonido explote.
			EnemyBase enemy = NewEnemy(world, "res://scenes/enemies/BasicEnemy.tscn", new Vector3(6, 0.5f, 0));
			HealthComponent enemyHealth = HealthComponent.FindIn(enemy);
			float before = enemyHealth.CurrentHealth;
			Tear tear = GD.Load<PackedScene>("res://scenes/combat/Tear.tscn").Instantiate<Tear>();
			world.AddChild(tear);
			tear.EmitSignal(Area3D.SignalName.BodyEntered, enemy);
			Check("con el audio roto, una lágrima sigue dañando al enemigo", enemyHealth.CurrentHealth < before, $"vida {enemyHealth.CurrentHealth}");
			Check("y la lágrima se libera", tear.IsQueuedForDeletion(), "la lágrima no se liberó");

			// Proyectil enemigo contra Noah.
			float playerBefore = playerHealth.CurrentHealth;
			EnemyProjectile projectile = GD.Load<PackedScene>("res://scenes/combat/EnemyProjectile.tscn").Instantiate<EnemyProjectile>();
			world.AddChild(projectile);
			projectile.EmitSignal(Area3D.SignalName.BodyEntered, player);
			Check("con el audio roto, un proyectil enemigo sigue dañando a Noah", playerHealth.CurrentHealth < playerBefore, $"vida {playerHealth.CurrentHealth}");

			// Muerte de enemigo: se libera.
			enemyHealth.TakeDamage(100.0f);
			Check("con el audio roto, el enemigo muerto se libera", enemy.IsQueuedForDeletion(), "no se liberó");

			// Objeto recogido.
			ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
			ItemPickup pickup = GD.Load<PackedScene>("res://scenes/items/ItemPickup.tscn").Instantiate<ItemPickup>();
			pickup.Definition = catalog.Items.First(i => i.Effect == ItemEffect.Damage);
			world.AddChild(pickup);
			bool collected = pickup.TryCollect(player);
			Check("con el audio roto, el objeto se recoge y se libera", collected && pickup.IsQueuedForDeletion(), "no se recogió");

			// Salva de The Mother: un ShootBehavior colgado de otro comportamiento (no del enemigo) también pide su sonido.
			AudioManager.StopAll();
			_audio.ResetFailures();
			AudioManager.FaultInjection = null;
			_audio.ClearRequests();
			EnemyBase host = NewEnemy(world, "res://scenes/enemies/BasicEnemy.tscn", new Vector3(-6, 0.5f, 3));
			Node wrapper = new Node { Name = "MotherLike" };
			host.AddChild(wrapper);
			ShootBehavior nested = new();
			wrapper.AddChild(nested);
			nested.EmitSignal(ShootBehavior.SignalName.Fired, Vector3.Left);
			Check("un ShootBehavior colgado de otro nodo (como el de The Mother) pide tirador_disparo en su enemigo, sin excepción",
				Single("tirador_disparo") is { Positional: true } nestedPing && nestedPing.Position.IsEqualApprox(new Vector3(-6, 0.5f, 3)), "sin petición");

			// Tras varios fallos seguidos el audio se desactiva solo y deja de llamar a nadie.
			_audio.ResetFailures();
			int calls = 0;
			AudioManager.FaultInjection = id =>
			{
				calls++;
				throw new InvalidOperationException("fallo " + id);
			};
			for (int i = 0; i < AudioManager.MaxFailures + 5; i++)
			{
				AudioManager.Play("lagrima_disparo");
				AudioManager.PlayAt("enemigo_golpe", Vector3.Zero);
			}

			Check($"tras {AudioManager.MaxFailures} fallos el audio se desactiva solo", !_audio.Enabled && _audio.FailureCount == AudioManager.MaxFailures, $"activo {_audio.Enabled}, {_audio.FailureCount} fallos");
			Check("y desactivado ya no se le llama más", calls == AudioManager.MaxFailures, $"{calls} llamadas");
			AudioManager.FaultInjection = null;
			AudioManager.PlayMusic("menu");
			AudioManager.StopAll();
			Check("PlayMusic, StopMusic y StopAll tampoco lanzan con el audio desactivado", true, "");

			world.Free();
		}
		finally
		{
			AudioManager.FaultInjection = null;
			_audio.ResetFailures();
			AudioManager.StopAll();
		}
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private void Volumes()
	{
		GD.Print("\n--- Volúmenes ---");
		Check("por defecto: general 80 % y efectos 100 %", Mathf.IsEqualApprox(GameSettings.DefaultMasterVolume, 0.8f) && Mathf.IsEqualApprox(GameSettings.DefaultSfxVolume, 1.0f), "valores por defecto");
		int master = AudioServer.GetBusIndex("Master"), sfx = AudioServer.GetBusIndex("SFX");
		AudioManager.SetMasterVolume(0.5f);
		Check("el volumen general se aplica al bus Master", Mathf.Abs(AudioServer.GetBusVolumeDb(master) - Mathf.LinearToDb(0.5f)) < 0.01f, $"{AudioServer.GetBusVolumeDb(master)}");
		AudioManager.SetSfxVolume(0.0f);
		Check("efectos a 0 silencia el bus SFX", AudioServer.GetBusVolumeDb(sfx) <= -79.0f, $"{AudioServer.GetBusVolumeDb(sfx)}");
		AudioManager.SetMasterVolume(7.0f);
		Check("los valores se limitan entre 0 y 1", GameSettings.MasterVolume == 1.0f, $"{GameSettings.MasterVolume}");

		AudioManager.SetMasterVolume(0.35f);
		AudioManager.SetSfxVolume(0.6f);
		GameSettings.MasterVolume = 0.1f;
		GameSettings.SfxVolume = 0.1f;
		bool read = GameSettings.LoadSettings();
		Check("los volúmenes se guardan en settings.cfg y se recuperan", read && Mathf.IsEqualApprox(GameSettings.MasterVolume, 0.35f) && Mathf.IsEqualApprox(GameSettings.SfxVolume, 0.6f), $"{GameSettings.MasterVolume} {GameSettings.SfxVolume}");

		DirAccess.RemoveAbsolute(TestSettings);
		Check("sin archivo: valores por defecto", !GameSettings.LoadSettings() && GameSettings.MasterVolume == GameSettings.DefaultMasterVolume && GameSettings.SfxVolume == GameSettings.DefaultSfxVolume, "sin archivo");
		WriteSettings("esto no es un archivo de configuración [[[ ===");
		Check("archivo dañado: valores por defecto, sin lanzar nada", !GameSettings.LoadSettings() && GameSettings.MasterVolume == GameSettings.DefaultMasterVolume && GameSettings.SfxVolume == GameSettings.DefaultSfxVolume, "archivo dañado");
		WriteSettings("[audio]\nmaster=5.0\nsfx=-3.0\n");
		GameSettings.LoadSettings();
		Check("valores fuera de rango: se limitan a 0..1", GameSettings.MasterVolume == 1.0f && GameSettings.SfxVolume == 0.0f, $"{GameSettings.MasterVolume} {GameSettings.SfxVolume}");
		WriteSettings("[audio]\nmaster=\"alto\"\n");
		bool whole = GameSettings.LoadSettings();
		Check("valor de tipo erróneo o ausente: ese volumen vuelve al de por defecto", !whole && GameSettings.MasterVolume == GameSettings.DefaultMasterVolume && GameSettings.SfxVolume == GameSettings.DefaultSfxVolume, $"{GameSettings.MasterVolume} {GameSettings.SfxVolume}");
		WriteSettings("[audio]\nmaster=0.25\n");
		GameSettings.LoadSettings();
		Check("con solo una clave, la otra usa su valor por defecto", Mathf.IsEqualApprox(GameSettings.MasterVolume, 0.25f) && GameSettings.SfxVolume == GameSettings.DefaultSfxVolume, $"{GameSettings.MasterVolume} {GameSettings.SfxVolume}");
		GameSettings.MasterVolume = GameSettings.DefaultMasterVolume;
		GameSettings.SfxVolume = GameSettings.DefaultSfxVolume;
		AudioManager.ApplyVolumes();
	}

	private async Task Sliders()
	{
		GD.Print("\n--- Deslizadores y menús ---");
		GameSettings.MasterVolume = 0.8f;
		GameSettings.SfxVolume = 1.0f;
		VolumeControls controls = GD.Load<PackedScene>("res://scenes/ui/VolumeControls.tscn").Instantiate<VolumeControls>();
		AddChild(controls);
		await Frames(2);
		Check("los deslizadores muestran 80 y 100 por defecto", controls.MasterSlider.Value == 80.0 && controls.SfxSlider.Value == 100.0, $"{controls.MasterSlider.Value} {controls.SfxSlider.Value}");
		controls.MasterSlider.Value = 40.0;
		controls.SfxSlider.Value = 55.0;
		Check("moverlos cambia GameSettings y los buses", Mathf.IsEqualApprox(GameSettings.MasterVolume, 0.4f) && Mathf.IsEqualApprox(GameSettings.SfxVolume, 0.55f)
			&& Mathf.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master")) - Mathf.LinearToDb(0.4f)) < 0.01f, $"{GameSettings.MasterVolume} {GameSettings.SfxVolume}");
		controls.Free();
		VolumeControls again = GD.Load<PackedScene>("res://scenes/ui/VolumeControls.tscn").Instantiate<VolumeControls>();
		AddChild(again);
		await Frames(2);
		Check("al volver a abrir el menú el valor se recuerda durante la sesión", again.MasterSlider.Value == 40.0 && again.SfxSlider.Value == 55.0, $"{again.MasterSlider.Value} {again.SfxSlider.Value}");
		again.Free();

		MainMenu menu = GD.Load<PackedScene>(GameSettings.MenuScene).Instantiate<MainMenu>();
		PauseMenu pause = GD.Load<PackedScene>("res://scenes/ui/PauseMenu.tscn").Instantiate<PauseMenu>();
		Check("el menú principal y el de pausa traen los deslizadores", menu.GetNodeOrNull<VolumeControls>("VolumeControls") != null && pause.GetNodeOrNull<VolumeControls>("Root/Center/Box/VolumeControls") != null, "sin deslizadores");
		menu.Free();
		pause.Free();
		Node level = GD.Load<PackedScene>(GameSettings.LevelScene).Instantiate();
		Check("el nivel trae un oyente de audio en el soporte de la cámara", level.GetNodeOrNull<AudioListener3D>("CameraRig/AudioListener3D") != null, "sin oyente");
		level.Free();
	}

	// --- ayudas -------------------------------------------------------------------------------------------------------

	private static void WriteSettings(string text)
	{
		using Godot.FileAccess file = Godot.FileAccess.Open(TestSettings, Godot.FileAccess.ModeFlags.Write);
		file.StoreString(text);
	}

	private EnemyBase NewEnemy(Node parent, string scene, Vector3 position)
	{
		EnemyBase enemy = GD.Load<PackedScene>(scene).Instantiate<EnemyBase>();
		parent.AddChild(enemy);
		enemy.Position = position;
		enemy.SetPhysicsProcess(false);
		return enemy;
	}

	private static bool FreeAndTrue(Node node)
	{
		node.Free();
		return true;
	}

	private int Count(string id) => _audio.Requests.Count(r => r.Id == id);

	private AudioManager.Request Single(string id) => _audio.Requests.LastOrDefault(r => r.Id == id);

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
