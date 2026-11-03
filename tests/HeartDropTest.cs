using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el corazón que suelta un enemigo (HeartDropper), con el nivel real (TestLevel) y semillas fijas. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/HeartDropTest.tscn
/// Casos:
///   1. El sorteo por sala (Rolls): determinista por semilla y celda, 0 nunca y 1 siempre, frecuencia cercana a la pedida (0,25, 0,40 y
///      0,20), distinto según la semilla de partida y sin consumir el azar global.
///   2. El portador (CarrierFor): -1 sin corazón, un índice válido con él, estable, repartido entre todos los enemigos y coherente con Rolls.
///   3. El objeto: curación de 1, válido y fuera del catálogo; la tabla de RunManager vale 0,40 / 0,20 / 0 y cada dificultad la aplica.
///   4. Con probabilidad 1: solo el enemigo portador suelta el corazón, donde muere (no al morir los demás, y solo uno por sala); con la
///      vida completa no se consume y sigue en el suelo; con daño cura 1 y desaparece; al volver no reaparece.
///   5. Un corazón sin recoger reaparece donde cayó al volver a la sala.
///   6. Con probabilidad 0 no cae nada; con 0,25 cae justo cuando lo dice el sorteo de esa semilla y celda.
///   7. Sin recompensas activas (modo del bot y del simulador) no cae nada aunque la probabilidad sea 1.
///   8. La sala del jefe no tiene corazón aunque la probabilidad sea 1.
/// Antes de salir recolecta los envoltorios de C# (BossTestHarness.CollectWrappers). Sale con código 0 si todo pasa.
/// </summary>
public partial class HeartDropTest : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== HeartDropTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			CheckRolls();
			CheckCarrier();
			CheckHeartItem();
			await CheckDifficultyTable();
			await CheckCarrierDropsHeart();
			await CheckPendingHeartReappears();
			await CheckChanceZeroAndSeeded();
			await CheckInertWithoutRewards();
			await CheckNoHeartInBossRoom();
			GD.Print($"HeartDropTest: {_checks} comprobaciones. RESULTADO: OK");
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
		if (!condition) throw new InvalidDataException(message);
	}

	private static Vector2I[] Grid() =>
		Enumerable.Range(0, 64).SelectMany(x => Enumerable.Range(0, 64).Select(y => new Vector2I(x - 32, y - 32))).ToArray();

	private void CheckRolls()
	{
		Vector2I[] cells = Grid();
		Require(cells.All(cell => !HeartDropper.Rolls(7, cell, 0.0f)) && cells.All(cell => HeartDropper.Rolls(7, cell, 1.0f)),
			"Probabilidad 0 nunca y 1 siempre");
		bool[] first = cells.Select(cell => HeartDropper.Rolls(7, cell, 0.25f)).ToArray();
		Require(first.SequenceEqual(cells.Select(cell => HeartDropper.Rolls(7, cell, 0.25f))), "El sorteo es determinista por semilla y celda");
		float rate = first.Count(hit => hit) / (float)first.Length;
		Require(rate > 0.21f && rate < 0.29f, $"Con 0,25 cae en torno a una de cada cuatro salas ({rate:0.000})");
		foreach ((float chance, float low, float high) in new[] { (0.40f, 0.37f, 0.43f), (0.20f, 0.17f, 0.23f) })
		{
			float hit = cells.Count(cell => HeartDropper.Rolls(2024, cell, chance)) / (float)cells.Length;
			Require(hit > low && hit < high, $"Con {chance:0.00} cae en torno a esa fracción de las salas ({hit:0.000})");
		}

		Require(!first.SequenceEqual(cells.Select(cell => HeartDropper.Rolls(8, cell, 0.25f))), "Otra semilla de partida da otro reparto");
		bool[] half = cells.Select(cell => HeartDropper.Rolls(7, cell, 0.5f)).ToArray();
		Require(first.Select((hit, i) => !hit || half[i]).All(ok => ok) && half.Count(hit => hit) > first.Count(hit => hit),
			"Subir la probabilidad conserva las salas con corazón y añade otras");

		GD.Seed(123456UL);
		uint expected = GD.Randi();
		GD.Seed(123456UL);
		foreach (Vector2I cell in cells.Take(500)) HeartDropper.Rolls(7, cell, 0.25f);
		Require(GD.Randi() == expected, "El sorteo no consume el azar global");
		GD.Print("Sorteo: determinista, 0 y 1, frecuencias, semilla y azar propio: OK");
	}

	private void CheckCarrier()
	{
		Vector2I[] cells = Grid();
		Require(cells.All(cell => HeartDropper.CarrierFor(7, cell, 0.0f, 4) == -1), "Sin probabilidad no hay portador");
		Require(cells.All(cell => HeartDropper.CarrierFor(7, cell, 1.0f, 0) == -1), "Sin enemigos tampoco");
		int[] first = cells.Select(cell => HeartDropper.CarrierFor(7, cell, 0.4f, 4)).ToArray();
		Require(first.SequenceEqual(cells.Select(cell => HeartDropper.CarrierFor(7, cell, 0.4f, 4))), "El portador es estable para una semilla y una celda");
		Require(first.Select((carrier, i) => (carrier >= 0) == HeartDropper.Rolls(7, cells[i], 0.4f)).All(ok => ok), "Hay portador justo en las salas que dice el sorteo");
		Require(first.All(carrier => carrier >= -1 && carrier < 4), "El portador es un índice válido");
		float expectedPerIndex = first.Count(carrier => carrier >= 0) / 4.0f;
		for (int index = 0; index < 4; index++)
		{
			int count = first.Count(carrier => carrier == index);
			Require(count > expectedPerIndex * 0.8f && count < expectedPerIndex * 1.2f, $"El portador {index} sale en una cuarta parte de las salas con corazón ({count} de {expectedPerIndex * 4})");
		}

		Require(cells.All(cell => HeartDropper.CarrierFor(7, cell, 1.0f, 2) is 0 or 1) && cells.Any(cell => HeartDropper.CarrierFor(7, cell, 1.0f, 2) == 1),
			"Con dos enemigos el portador es el primero o el segundo");
		GD.Seed(123456UL);
		uint expected = GD.Randi();
		GD.Seed(123456UL);
		foreach (Vector2I cell in cells.Take(500)) HeartDropper.CarrierFor(7, cell, 0.4f, 4);
		Require(GD.Randi() == expected, "Elegir portador no consume el azar global");
		GD.Print("Portador: estable, en rango, repartido, coherente con el sorteo y con azar propio: OK");
	}

	private void CheckHeartItem()
	{
		ItemDefinition heart = GD.Load<ItemDefinition>(HeartDropper.HeartPath);
		Require(heart != null && heart.IsValid && heart.Id == "corazon" && heart.Effect == ItemEffect.Heal && Mathf.IsEqualApprox(heart.Amount, 1.0f),
			"El corazón cura 1 punto de vida");
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		Require(catalog.Items.All(item => item.Id != "corazon"), "Los tesoros nunca ofrecen el corazón");
		Require(Mathf.IsEqualApprox(RunManager.HeartChanceFor(DifficultyTarget.Easy), 0.40f)
			&& Mathf.IsEqualApprox(RunManager.HeartChanceFor(DifficultyTarget.Normal), 0.20f)
			&& Mathf.IsEqualApprox(RunManager.HeartChanceFor(DifficultyTarget.Hard), 0.0f),
			"La tabla por dificultad es 0,40 / 0,20 / 0");
		GD.Print("Objeto: curación de 1, fuera del catálogo y tabla 0,40 / 0,20 / 0: OK");
	}

	private BossTestHarness.Level Open(float chance, bool rewards = true)
	{
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, true, false, rewards);
		level.Run.GetNode<HeartDropper>("HeartDropper").DropChance = chance;
		return level;
	}

	// Cruza puertas por el camino al jefe hasta la primera sala Normal y devuelve la puerta por la que se entró.
	private async Task<(RoomController Room, DoorSide Entered)> GoToCombatRoom(BossTestHarness.Level level)
	{
		foreach (DoorSide side in BossTestHarness.PathToBoss(level.Rooms.Map))
		{
			await BossTestHarness.Cross(GetTree(), level.Rooms, side);
			if (level.Rooms.CurrentRoom.Data.Type == RoomType.Normal) return (level.Rooms.CurrentRoom, side);
		}

		throw new InvalidDataException("El camino al jefe no tiene ninguna sala de combate.");
	}

	private static List<EnemyBase> Enemies(RoomController room) => room.GetNode<Node3D>("Enemies").GetChildren().OfType<EnemyBase>().ToList();

	private async Task ClearRoom(RoomController room)
	{
		for (int i = 0; i < 400 && room.State != RoomState.Cleared; i++)
		{
			foreach (EnemyBase enemy in Enemies(room)) enemy.Kill();
			await BossTestHarness.Frames(GetTree(), 1);
		}

		Require(room.State == RoomState.Cleared, "La sala de combate se despeja");
		await BossTestHarness.Frames(GetTree(), 3);
	}

	private static ItemPickup Heart(RoomController room) =>
		room.GetNodeOrNull("HeartDrop")?.GetChildren().OfType<ItemPickup>().FirstOrDefault(pickup => !pickup.IsQueuedForDeletion());

	private static int HeartCount(RoomController room) =>
		room.GetNodeOrNull("HeartDrop")?.GetChildren().OfType<ItemPickup>().Count(pickup => !pickup.IsQueuedForDeletion()) ?? 0;

	private async Task CheckDifficultyTable()
	{
		SceneTree tree = GetTree();
		DifficultyTarget? previous = GameSettings.Difficulty;
		try
		{
			foreach (DifficultyTarget target in new[] { DifficultyTarget.Easy, DifficultyTarget.Normal, DifficultyTarget.Hard })
			{
				GameSettings.Difficulty = target;
				BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, true, false, true);
				await BossTestHarness.Frames(tree, 2);
				float chance = level.Run.GetNode<HeartDropper>("HeartDropper").DropChance;
				Require(level.Run.CurrentDifficulty == target && Mathf.IsEqualApprox(chance, RunManager.HeartChanceFor(target)), $"{target} usa la probabilidad de la tabla ({chance})");
				await BossTestHarness.Close(tree, level);
			}
		}
		finally
		{
			GameSettings.Difficulty = previous;
		}

		GD.Print("Cada dificultad aplica su probabilidad de la tabla: OK");
	}

	private async Task CheckCarrierDropsHeart()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = Open(1.0f);
		HeartDropper dropper = level.Run.GetNode<HeartDropper>("HeartDropper");
		(RoomController room, DoorSide entered) = await GoToCombatRoom(level);
		Vector2I cell = room.Data.Cell;
		int points = room.Data.SpawnPoints.Length;
		int expectedCarrier = HeartDropper.CarrierFor(level.Run.Seed, cell, 1.0f, points);
		Require(points >= 2 && dropper.Carrier == expectedCarrier && expectedCarrier >= 0, $"La sala tiene {points} enemigos y su portador es el {dropper.Carrier} (esperado {expectedCarrier})");
		await BossTestHarness.WaitUntil(tree, () => Enemies(room).Count == points, 200, "la aparición de los enemigos");
		Require(HeartCount(room) == 0, "Antes de que muera nadie no hay corazón");

		List<EnemyBase> enemies = Enemies(room);
		EnemyBase carrier = enemies[expectedCarrier];
		foreach (EnemyBase other in enemies.Where(enemy => enemy != carrier))
		{
			other.Kill();
			await BossTestHarness.Frames(tree, 3);
			Require(HeartCount(room) == 0, "Cuando muere un enemigo que no es el portador no cae corazón");
		}

		Require(room.State == RoomState.Combat, "Con el portador vivo la sala sigue en combate");
		Vector3 deathPoint = carrier.GlobalPosition;
		carrier.Kill();
		await BossTestHarness.Frames(tree, 4);
		ItemPickup heart = Heart(room);
		Require(heart != null && heart.Definition.Id == "corazon", "Al morir el portador cae un corazón");
		Require(HeartCount(room) == 1, "Y solo uno");
		Vector3 global = heart.GlobalPosition;
		Require(Mathf.Abs(global.X - deathPoint.X) < 0.05f && Mathf.Abs(global.Z - deathPoint.Z) < 0.05f && Mathf.Abs(heart.Position.Y) < 0.05f,
			$"Cae donde murió el portador ({global} frente a {deathPoint})");
		Require(room.State == RoomState.Cleared, "La sala queda despejada");
		bool hasModel = ResourceLoader.Exists(ItemPickup.ModelPathFor("corazon"));
		Require(hasModel ? heart.Model != null && !heart.GetNode<MeshInstance3D>("Visual").Visible : heart.Model == null && heart.GetNode<MeshInstance3D>("Visual").Visible,
			hasModel ? "Con corazon.glb disponible se ve el modelo" : "Sin corazon.glb se ve la esfera roja");

		HealthComponent health = level.Health;
		float full = health.EffectiveMaxHealth;
		Require(!heart.TryCollect(level.Player) && !heart.IsCollected && Heart(room) == heart, "Con la vida completa no se consume y sigue en el suelo");
		health.TakeDamage(2.0f);
		Require(Mathf.IsEqualApprox(health.CurrentHealth, full - 2.0f), "Noah recibe daño");
		Require(heart.TryCollect(level.Player) && heart.IsCollected, "Con daño se recoge");
		Require(Mathf.IsEqualApprox(health.CurrentHealth, full - 1.0f), "Cura exactamente 1 punto de vida");
		Require(level.Player.GetNode<PlayerItems>("Items").CollectedItems.Any(item => item.Id == "corazon"), "Queda en el inventario");
		await BossTestHarness.Frames(tree, 3);
		Require(Heart(room) == null, "El corazón recogido desaparece");

		DoorSide back = (DoorSide)(((int)entered + 2) % 4);
		await BossTestHarness.Cross(tree, level.Rooms, back);
		await BossTestHarness.Cross(tree, level.Rooms, entered);
		Require(level.Rooms.CurrentRoom.Data.Cell == cell && HeartCount(level.Rooms.CurrentRoom) == 0, "Un corazón recogido no reaparece al volver");
		await BossTestHarness.Close(tree, level);
		GD.Print("Portador: solo él suelta el corazón, donde muere; no se gasta con la vida completa, cura 1 y no reaparece: OK");
	}

	private async Task CheckPendingHeartReappears()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = Open(1.0f);
		(RoomController room, DoorSide entered) = await GoToCombatRoom(level);
		await BossTestHarness.WaitUntil(tree, () => Enemies(room).Count == room.Data.SpawnPoints.Length, 200, "la aparición de los enemigos");
		await ClearRoom(room);
		Require(Heart(room) != null, "Cae el corazón");
		Vector3 fallenAt = Heart(room).GlobalPosition;

		DoorSide back = (DoorSide)(((int)entered + 2) % 4);
		await BossTestHarness.Cross(tree, level.Rooms, back);
		Require(level.Rooms.CurrentRoom.Data.Cell != room.Data.Cell, "Se sale de la sala sin recogerlo");
		await BossTestHarness.Cross(tree, level.Rooms, entered);
		ItemPickup again = Heart(level.Rooms.CurrentRoom);
		Require(again != null && !again.IsCollected && again.Definition.Id == "corazon", "Un corazón sin recoger reaparece al volver");
		Require(again.GlobalPosition.IsEqualApprox(fallenAt), $"Y reaparece donde cayó ({again.GlobalPosition} frente a {fallenAt})");
		await BossTestHarness.Close(tree, level);
		GD.Print("Corazón pendiente: reaparece donde cayó al volver a la sala: OK");
	}

	private async Task CheckChanceZeroAndSeeded()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level none = Open(0.0f);
		(RoomController room, _) = await GoToCombatRoom(none);
		await BossTestHarness.WaitUntil(tree, () => Enemies(room).Count == room.Data.SpawnPoints.Length, 200, "la aparición de los enemigos");
		await ClearRoom(room);
		Require(Heart(room) == null && room.GetNodeOrNull("HeartDrop") == null, "Con probabilidad 0 no cae nada");
		await BossTestHarness.Close(tree, none);

		BossTestHarness.Level seeded = Open(0.25f);
		(room, _) = await GoToCombatRoom(seeded);
		bool expected = HeartDropper.CarrierFor(seeded.Run.Seed, room.Data.Cell, 0.25f, room.Data.SpawnPoints.Length) >= 0;
		await BossTestHarness.WaitUntil(tree, () => Enemies(room).Count == room.Data.SpawnPoints.Length, 200, "la aparición de los enemigos");
		await ClearRoom(room);
		Require((Heart(room) != null) == expected, $"Con 0,25 cae justo cuando lo dice el sorteo de la semilla y la celda (esperado: {expected})");
		await BossTestHarness.Close(tree, seeded);
		GD.Print("Probabilidad 0 y sorteo por semilla y celda en el nivel real: OK");
	}

	private async Task CheckInertWithoutRewards()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = Open(1.0f, rewards: false);
		HeartDropper dropper = level.Run.GetNode<HeartDropper>("HeartDropper");
		Require(!dropper.Active, "Sin recompensas activas el corazón está inactivo");
		(RoomController room, _) = await GoToCombatRoom(level);
		await BossTestHarness.WaitUntil(tree, () => Enemies(room).Count == room.Data.SpawnPoints.Length, 200, "la aparición de los enemigos");
		await ClearRoom(room);
		Require(room.GetNodeOrNull("HeartDrop") == null, "El bot y el simulador (sin objetos) no ven corazones, ni con probabilidad 1");
		await BossTestHarness.Close(tree, level);
		GD.Print("Sin recompensas activas: no cae nada: OK");
	}

	private async Task CheckNoHeartInBossRoom()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, withMother: false, unlimitedHealth: false, treasure: true);
		level.Run.GetNode<HeartDropper>("HeartDropper").DropChance = 1.0f;
		await BossTestHarness.GoToBoss(tree, level);
		RoomController room = level.Rooms.CurrentRoom;
		await BossTestHarness.WaitUntil(tree, () => Enemies(room).Count == room.Data.SpawnPoints.Length, 200, "los seis básicos del jefe");
		await ClearRoom(room);
		Require(room.Data.Type == RoomType.Boss && room.GetNodeOrNull("HeartDrop") == null, "La sala del jefe no suelta corazón, ni con probabilidad 1");
		await BossTestHarness.Close(tree, level);
		GD.Print("Sala del jefe: sin corazón: OK");
	}
}
