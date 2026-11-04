using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba la aparición de los enemigos de una sala con el código real del juego (TestLevel). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/EnemySpawnTest.tscn
/// Durante la espera de SpawnDelay (0,7 s):
///   1. Cada punto tiene su anillo (SpawnMarker) y los anillos crecen.
///   2. Los enemigos se instancian ocultos, repartidos en varios fotogramas, en un nodo aparte: Enemies sigue vacío. Están invisibles, sin
///      procesar y con las formas de colisión desactivadas; sus capas, máscaras y tamaños de colisión son los de la escena.
/// A los 0,7 s:
///   3. Pasan a Enemies (el mismo número que puntos), el nodo de preparación desaparece y suena enemigo_aparece una vez por enemigo, en su
///      punto y escalonado (nunca antes de salir).
///   4. Durante los 0,25 s de aparición inerte (Spawning) no se mueven, no hacen daño por contacto, no reciben daño y las lágrimas de Noah
///      los atraviesan sin desaparecer; los anillos se desvanecen y desaparecen.
/// Después:
///   5. Vuelven a actuar: la colisión se reactiva con las mismas capas y tamaños, una lágrima los daña, hacen daño por contacto y
///      matarlos a todos despeja la sala y abre las puertas.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class EnemySpawnTest : Node
{
	private const string TearPath = "res://scenes/combat/Tear.tscn";

	private readonly record struct CollisionProfile(uint Layer, uint Mask, Vector3[] ShapeSizes);

	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== EnemySpawnTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			foreach (int seed in new[] { 1, BossTestHarness.Seed })
			{
				await CheckWave(seed);
			}

			GD.Print($"EnemySpawnTest: {_checks} comprobaciones. RESULTADO: OK");
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
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private static CollisionProfile ProfileOf(CollisionObject3D body)
	{
		Vector3[] sizes = body.GetChildren().OfType<CollisionShape3D>().Select(shape => shape.Shape is BoxShape3D box ? box.Size : Vector3.Zero).ToArray();
		return new CollisionProfile(body.CollisionLayer, body.CollisionMask, sizes);
	}

	private static bool Same(CollisionProfile a, CollisionProfile b)
	{
		return a.Layer == b.Layer && a.Mask == b.Mask && a.ShapeSizes.SequenceEqual(b.ShapeSizes);
	}

	// Entra en la primera sala del camino al jefe con al menos tres enemigos y devuelve cuando acaba de empezar su combate.
	private async Task<RoomController> EnterWaveRoom(SceneTree tree, BossTestHarness.Level level)
	{
		foreach (DoorSide side in BossTestHarness.PathToBoss(level.Rooms.Map))
		{
			Vector2I expected = level.Rooms.CurrentRoom.Data.Cell + side.GridOffset();
			level.Rooms.CurrentRoom.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);
			await BossTestHarness.WaitTicks(tree, () => GodotObject.IsInstanceValid(level.Rooms.CurrentRoom) && level.Rooms.CurrentRoom.Data.Cell == expected
				&& level.Rooms.CurrentRoom.State != RoomState.Unvisited, 400, $"entrar en la sala {expected}");

			RoomController room = level.Rooms.CurrentRoom;
			if (room.State == RoomState.Combat && room.Data.SpawnPoints.Length >= 3)
			{
				return room;
			}

			// Una sala sin la oleada buscada se despeja para poder seguir (sus enemigos aún pueden estar por salir).
			await BossTestHarness.WaitTicks(tree, () => room.GetNode("Enemies").GetChildCount() > 0 || room.State == RoomState.Cleared, 200, "salir de una sala intermedia");
			foreach (EnemyBase enemy in room.GetNode("Enemies").GetChildren().OfType<EnemyBase>().ToList())
			{
				enemy.Kill();
			}

			await BossTestHarness.WaitTicks(tree, () => room.State == RoomState.Cleared, 200, "despejar una sala intermedia");
		}

		throw new InvalidDataException("El camino al jefe no tiene ninguna sala con una oleada de tres enemigos o más.");
	}

	private async Task CheckWave(int seed)
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, seed, withMother: false, unlimitedHealth: false);
		await BossTestHarness.Frames(tree, 2);
		RoomController room = await EnterWaveRoom(tree, level);
		string tag = $"[semilla {seed}, sala {room.Data.Cell}]";
		AudioManager audio = AudioManager.Instance;
		// Las voces de la sala anterior siguen contando en el reloj real (0,41 s) aunque la prueba vaya más rápido que el tiempo del juego:
		// se vacían para que el límite de voces no descarte los sonidos de esta.
		AudioManager.StopAll();
		audio.ClearRequests();

		Node enemiesRoot = room.GetNode("Enemies");
		int total = room.Data.SpawnPoints.Length;
		EnemyKind[] kinds = Enumerable.Range(0, total)
			.Select(i => room.Data.SpawnKinds.Length == total ? (EnemyKind)room.Data.SpawnKinds[i] : EnemyKinds.KindOfScene(room.Data.EnemyScene)).ToArray();
		Dictionary<EnemyKind, CollisionProfile> reference = new();
		Dictionary<EnemyKind, Vector3> visualScale = new();
		foreach (EnemyKind kind in kinds.Distinct())
		{
			EnemyBase fresh = EnemyKinds.Scene(kind).Instantiate<EnemyBase>();
			reference[kind] = ProfileOf(fresh);
			visualScale[kind] = fresh.GetNodeOrNull<Node3D>("Visual")?.Scale ?? Vector3.One;
			fresh.Free();
		}

		// --- La espera: anillos y enemigos preparados ---
		Node markersRoot = room.GetNodeOrNull("SpawnMarkers");
		Require(markersRoot != null && markersRoot.GetChildren().OfType<SpawnMarker>().Count() == total, $"{tag} Hay un anillo por punto de aparición ({total})");
		List<SpawnMarker> markers = markersRoot.GetChildren().OfType<SpawnMarker>().ToList();
		Require(enemiesRoot.GetChildCount() == 0, $"{tag} Enemies empieza vacío");

		int combatTick = 0;
		int voicesBefore = 0;
		int stagedPrevious = 0;
		int stagedFirst = -1;
		int stagedMaxJump = 0;
		float progressPrevious = -1.0f;
		bool revealed = false;
		for (int tick = 1; tick <= 120 && !revealed; tick++)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			combatTick = tick;
			Node staging = room.GetNodeOrNull("EnemyStaging");
			int staged = staging?.GetChildCount() ?? 0;

			if (enemiesRoot.GetChildCount() > 0)
			{
				revealed = true;
				break;
			}

			Require(!audio.Requests.Any(request => request.Id == SoundCatalog.EnemySpawns), $"{tag} Ningún enemigo suena antes de salir de su marcador (paso {tick})");
			voicesBefore = audio.ActiveVoiceCount; // las voces que ya había (puertas) no cuentan para la aparición

			if (stagedFirst < 0)
			{
				stagedFirst = staged;
			}

			stagedMaxJump = Math.Max(stagedMaxJump, staged - stagedPrevious);
			stagedPrevious = staged;

			float progress = markers.Where(GodotObject.IsInstanceValid).Min(marker => marker.Progress);
			Require(progress >= progressPrevious, $"{tag} Los anillos solo crecen (paso {tick})");
			progressPrevious = progress;

			if (staging != null)
			{
				foreach (EnemyBase staged3 in staging.GetChildren().OfType<EnemyBase>())
				{
					Require(staged3.Staged && !staged3.Visible && staged3.ProcessMode == Node.ProcessModeEnum.Disabled && !staged3.Spawning, $"{tag} Un enemigo preparado está oculto y sin procesar");
					Require(staged3.GetChildren().OfType<CollisionShape3D>().All(shape => shape.Disabled), $"{tag} Un enemigo preparado tiene las formas de colisión desactivadas");
					Require(Same(ProfileOf(staged3), reference[staged3.Kind]), $"{tag} Un enemigo preparado conserva capas, máscaras y tamaños de colisión de su escena");
				}
			}
		}

		Require(revealed, $"{tag} Los enemigos acaban saliendo");
		int expectedTicks = Mathf.RoundToInt(room.SpawnDelay * Engine.PhysicsTicksPerSecond);
		Require(Mathf.Abs(combatTick - expectedTicks) <= 6, $"{tag} Salen a los {combatTick} pasos del combate (esperado ~{expectedTicks}, 0,7 s)");
		Require(stagedFirst < total, $"{tag} Se instancian repartidos: tras el primer paso hay {stagedFirst} de {total}");
		Require(stagedMaxJump <= 2, $"{tag} Como mucho 2 instancias por paso (salto máximo {stagedMaxJump})");
		Require(stagedPrevious == total, $"{tag} Antes de salir ya están los {total} preparados ({stagedPrevious})");
		Require(markers.All(marker => marker.Progress > 0.9f), $"{tag} Los anillos llegaron casi a su tamaño completo");
		GD.Print($"{tag} Espera: {total} anillos que crecen, enemigos preparados repartidos (primer paso {stagedFirst}/{total}), salen a los {combatTick} pasos: OK");

		// --- La salida: pasan a Enemies, una sola petición de sonido ---
		List<EnemyBase> enemies = enemiesRoot.GetChildren().OfType<EnemyBase>().ToList();
		Require(enemies.Count == total && enemiesRoot.GetChildCount() == total, $"{tag} Salen {enemies.Count} enemigos, uno por punto ({total})");
		Require(room.GetNodeOrNull("EnemyStaging") == null || room.GetNode("EnemyStaging").IsQueuedForDeletion(), $"{tag} El nodo de preparación desaparece");
		Vector3[] spots = enemies.Select(enemy => enemy.GlobalPosition).ToArray();
		Require(enemies.All(enemy => enemy.Spawning && enemy.Visible && !enemy.Staged), $"{tag} Todos están en aparición inerte y visibles");
		Require(enemies.All(enemy => HealthComponent.FindIn(enemy).Invulnerable), $"{tag} Y son invulnerables mientras aparecen");
		Require(enemies.All(enemy => enemy.GetChildren().OfType<CollisionShape3D>().All(shape => shape.Disabled)), $"{tag} Con la colisión aún desactivada");
		Require(enemies.All(enemy => Same(ProfileOf(enemy), reference[enemy.Kind])), $"{tag} Sin cambiar capas, máscaras ni tamaños de colisión");
		Require(enemies.All(enemy => enemy.Visual == null || enemy.Visual.Scale.X < visualScale[enemy.Kind].X), $"{tag} Salen más pequeños que su tamaño y crecen");
		Require(markers.All(marker => !GodotObject.IsInstanceValid(marker) || marker.Finishing), $"{tag} Los anillos empiezan a desvanecerse");

		// Una recolección de basura justo tras salir (como la que puede caer en una partida real) no debe hacer perder ningún sonido escalonado.
		SceneChangeGuard.Collect();

		// El sonido se muestrea en el mismo bucle que mide la aparición inerte (más abajo): cuántos suenan en cada paso y cuántas voces hay.
		int[] arrivals = new int[64];
		int heard = audio.Requests.Count(request => request.Id == SoundCatalog.EnemySpawns);
		arrivals[0] = heard;
		int maxVoices = audio.ActiveVoiceCount;

		// --- La aparición inerte: nada se mueve ni daña, y las lágrimas atraviesan ---
		EnemyBase target = enemies.OrderByDescending(enemy => enemy.GlobalPosition.DistanceTo(Vector3.Zero)).First();
		Dictionary<EnemyBase, Vector3> positions = enemies.ToDictionary(enemy => enemy, enemy => enemy.GlobalPosition);
		float noahHealth = level.Health.CurrentHealth;
		level.Player.GlobalPosition = target.GlobalPosition;

		Vector3 toCenter = new Vector3(-target.GlobalPosition.X, 0.0f, -target.GlobalPosition.Z);
		toCenter = toCenter.LengthSquared() < 0.01f ? Vector3.Right : toCenter.Normalized();
		Tear tear = GD.Load<PackedScene>(TearPath).Instantiate<Tear>();
		level.Root.AddChild(tear);
		tear.GlobalPosition = target.GlobalPosition - toCenter * 1.2f;
		tear.Launch(toCenter);
		float targetHealth = HealthComponent.FindIn(target).CurrentHealth;

		int spawningTicks = 0;
		while (enemies.All(enemy => enemy.Spawning) && spawningTicks < 60)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			spawningTicks++;
			int now = audio.Requests.Count(request => request.Id == SoundCatalog.EnemySpawns);
			if (spawningTicks < arrivals.Length) arrivals[spawningTicks] = now - heard;
			heard = now;
			maxVoices = Math.Max(maxVoices, audio.ActiveVoiceCount);
			if (enemies.All(enemy => enemy.Spawning))
			{
				Require(level.Health.CurrentHealth >= noahHealth, $"{tag} Sin daño por contacto durante la aparición (paso {spawningTicks})");
				Require(enemies.All(enemy => enemy.GlobalPosition.IsEqualApprox(positions[enemy])), $"{tag} Nadie se mueve durante la aparición (paso {spawningTicks})");
			}
		}

		AudioManager.Request[] sounds = audio.Requests.Where(request => request.Id == SoundCatalog.EnemySpawns).ToArray();
		Require(sounds.Length == total, $"{tag} enemigo_aparece suena una vez por enemigo ({sounds.Length} de {total})");
		Require(sounds.All(request => request.Positional && request.Bus == SoundCatalog.BusSfx), $"{tag} Cada uno en 3D y por el bus SFX");
		Require(sounds.All(request => request.Result is AudioManager.Outcome.Played or AudioManager.Outcome.Silent), $"{tag} Todos suenan: con su archivo, ninguno descartado ni faltante ({string.Join(",", sounds.Select(request => request.Result))})");
		Require(spots.All(spot => sounds.Any(request => request.Position.IsEqualApprox(spot))), $"{tag} Cada sonido sale del punto de su enemigo");
		Require(sounds.All(request => request.PitchScale is > 0.94f and < 1.06f), $"{tag} Con el tono casi intacto (±5 %)");
		Require(arrivals.Max() <= 2, $"{tag} Escalonados: como mucho 2 por paso de física (máximo {arrivals.Max()})");
		int lastStep = Array.FindLastIndex(arrivals, count => count > 0);
		Require(arrivals[0] + arrivals[1] >= 1 && lastStep <= (total - 1) * 3 + 3, $"{tag} El primero suena al salir y el último a los {lastStep} pasos (≤ {(total - 1) * 3 + 3})");
		Require(maxVoices - voicesBefore <= SoundCatalog.Get(SoundCatalog.EnemySpawns).MaxVoices, $"{tag} Sin saturar: {maxVoices - voicesBefore} voces de aparición a la vez como mucho");
		GD.Print($"{tag} Sonido: {total} enemigos, uno cada ~0,04 s (máx. {arrivals.Max()} por paso), último a los {lastStep} pasos, sin pasar del límite de voces: OK");

		Require(spawningTicks >= 12 && spawningTicks <= 18, $"{tag} La aparición dura {spawningTicks} pasos (~0,25 s = 15)");
		Require(GodotObject.IsInstanceValid(tear) && !tear.IsQueuedForDeletion(), $"{tag} La lágrima atravesó al enemigo sin desaparecer");
		Require(HealthComponent.FindIn(target).CurrentHealth == targetHealth, $"{tag} Y no le hizo daño");
		float actingAt = (combatTick + spawningTicks) / (float)Engine.PhysicsTicksPerSecond;
		GD.Print($"{tag} Aparición inerte: {spawningTicks} pasos, sin contacto, sin movimiento, lágrima atraviesa; actúan hacia {actingAt:F2} s: OK");
		if (GodotObject.IsInstanceValid(tear))
		{
			tear.QueueFree();
		}

		// --- Después: todo vuelve a funcionar ---
		await BossTestHarness.Frames(tree, 1);
		Require(enemies.All(enemy => !enemy.Spawning && !HealthComponent.FindIn(enemy).Invulnerable), $"{tag} Tras la aparición ya no son invulnerables");
		Require(enemies.All(enemy => enemy.GetChildren().OfType<CollisionShape3D>().All(shape => !shape.Disabled)), $"{tag} La colisión se reactiva");
		Require(enemies.All(enemy => Same(ProfileOf(enemy), reference[enemy.Kind])), $"{tag} Con las mismas capas, máscaras y tamaños");
		Require(enemies.All(enemy => enemy.Visual == null || enemy.Visual.Scale.IsEqualApprox(visualScale[enemy.Kind])), $"{tag} Recuperan su tamaño");
		Require(markersRoot.GetChildren().OfType<SpawnMarker>().All(marker => marker.IsQueuedForDeletion() || marker.Finishing), $"{tag} Los anillos siguen desvaneciéndose");

		// Una lágrima ahora sí daña (el enemigo se queda quieto con su comportamiento apagado para que no la esquive).
		target.SetPhysicsProcess(false);
		HealthComponent targetHp = HealthComponent.FindIn(target);
		Tear second = GD.Load<PackedScene>(TearPath).Instantiate<Tear>();
		level.Root.AddChild(second);
		second.GlobalPosition = target.GlobalPosition - toCenter * 1.2f;
		second.Launch(toCenter);
		await BossTestHarness.WaitTicks(tree, () => targetHp.CurrentHealth < targetHp.EffectiveMaxHealth || !GodotObject.IsInstanceValid(target), 60, "daño de una lágrima tras la aparición");
		Require(!GodotObject.IsInstanceValid(second) || second.IsQueuedForDeletion(), $"{tag} Tras la aparición la lágrima sí se detiene en el enemigo");
		target.SetPhysicsProcess(true);

		// Daño por contacto: Noah sigue encima de un enemigo y, en cuanto actúan, alguno la toca.
		await BossTestHarness.WaitTicks(tree, () => level.Health.CurrentHealth < noahHealth, 240, "daño por contacto tras la aparición");
		Require(level.Health.CurrentHealth < noahHealth, $"{tag} Tras la aparición hacen daño por contacto");

		await BossTestHarness.WaitTicks(tree, () => markersRoot.GetChildren().OfType<SpawnMarker>().All(marker => !GodotObject.IsInstanceValid(marker)) || markersRoot.GetChildCount() == 0, 120, "que desaparezcan los anillos");
		Require(markersRoot.GetChildCount() == 0, $"{tag} Los anillos desaparecen");

		// --- Conteo y sala despejada ---
		Require(room.State == RoomState.Combat, $"{tag} La sala sigue en combate con los enemigos vivos");
		List<EnemyBase> alive = enemiesRoot.GetChildren().OfType<EnemyBase>().Where(enemy => !enemy.IsQueuedForDeletion()).ToList();
		// La lágrima de antes pudo matar al objetivo (un volador tiene poca vida): esa muerte cuenta, y la sala sigue en combate.
		int killedByTear = GodotObject.IsInstanceValid(target) && !target.IsQueuedForDeletion() ? 0 : 1;
		Require(alive.Count + killedByTear == total, $"{tag} Cuadra el conteo: {alive.Count} vivos + {killedByTear} abatido por la lágrima = {total}");
		foreach (EnemyBase enemy in alive)
		{
			enemy.Kill();
		}

		await BossTestHarness.WaitTicks(tree, () => room.State == RoomState.Cleared, 120, "sala despejada");
		Require(room.State == RoomState.Cleared, $"{tag} Matar a todos despeja la sala");
		GD.Print($"{tag} Después: lágrima daña, contacto daña, mismas capas y tamaños, conteo {total}, sala despejada: OK");

		await BossTestHarness.Close(tree, level);
	}
}
