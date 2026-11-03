using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba el corazón que puede caer al despejar una sala de combate (HeartDropper), con el nivel real (TestLevel). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/HeartDropTest.tscn
/// Casos:
///   1. El sorteo (Rolls): determinista por semilla y celda, 0 nunca y 1 siempre, frecuencia cercana a la pedida, distinto según la
///      semilla de partida y sin consumir el azar global.
///   2. El objeto: curación de 1, válido y fuera del catálogo de los tesoros; la probabilidad por defecto es 0,25.
///   3. Con probabilidad 1: al despejar una sala Normal aparece el corazón en el centro; con la vida completa no se consume y sigue en
///      el suelo; con daño cura 1 y desaparece; al salir y volver no reaparece ni se vuelve a sortear la sala.
///   4. Un corazón sin recoger reaparece al volver a la sala.
///   5. Con probabilidad 0 no cae nada; con probabilidad 0,25 cae justo cuando lo dice el sorteo de esa semilla y celda.
///   6. Sin recompensas activas (modo del bot y del simulador) no cae nada aunque la probabilidad sea 1.
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
			CheckHeartItem();
			await CheckDropCollectAndRevisit();
			await CheckPendingHeartReappears();
			await CheckChanceZeroAndSeeded();
			await CheckInertWithoutRewards();
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

	private void CheckRolls()
	{
		Vector2I[] cells = Enumerable.Range(0, 64).SelectMany(x => Enumerable.Range(0, 64).Select(y => new Vector2I(x - 32, y - 32))).ToArray();
		Require(cells.All(cell => !HeartDropper.Rolls(7, cell, 0.0f)) && cells.All(cell => HeartDropper.Rolls(7, cell, 1.0f)),
			"Probabilidad 0 nunca y 1 siempre");
		bool[] first = cells.Select(cell => HeartDropper.Rolls(7, cell, 0.25f)).ToArray();
		Require(first.SequenceEqual(cells.Select(cell => HeartDropper.Rolls(7, cell, 0.25f))), "El sorteo es determinista por semilla y celda");
		float rate = first.Count(hit => hit) / (float)first.Length;
		Require(rate > 0.21f && rate < 0.29f, $"Con 0,25 cae en torno a una de cada cuatro salas ({rate:0.000})");
		Require(!first.SequenceEqual(cells.Select(cell => HeartDropper.Rolls(8, cell, 0.25f))), "Otra semilla de partida da otro reparto");
		bool[] half = cells.Select(cell => HeartDropper.Rolls(7, cell, 0.5f)).ToArray();
		Require(first.Select((hit, i) => !hit || half[i]).All(ok => ok) && half.Count(hit => hit) > first.Count(hit => hit),
			"Subir la probabilidad conserva las salas con corazón y añade otras");

		GD.Seed(123456UL);
		uint expected = GD.Randi();
		GD.Seed(123456UL);
		foreach (Vector2I cell in cells.Take(500)) HeartDropper.Rolls(7, cell, 0.25f);
		Require(GD.Randi() == expected, "El sorteo no consume el azar global");
		GD.Print("Sorteo: determinista, 0 y 1, frecuencia, semilla y azar propio: OK");
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

	private async Task ClearRoom(RoomController room)
	{
		Node3D enemies = room.GetNode<Node3D>("Enemies");
		for (int i = 0; i < 400 && room.State != RoomState.Cleared; i++)
		{
			foreach (EnemyBase enemy in enemies.GetChildren().OfType<EnemyBase>().ToArray()) enemy.Kill();
			await BossTestHarness.Frames(GetTree(), 1);
		}

		Require(room.State == RoomState.Cleared, "La sala de combate se despeja");
		await BossTestHarness.Frames(GetTree(), 3);
	}

	private static ItemPickup Heart(RoomController room) =>
		room.GetNodeOrNull("HeartDrop")?.GetChildren().OfType<ItemPickup>().FirstOrDefault(pickup => !pickup.IsQueuedForDeletion());

	private async Task CheckDropCollectAndRevisit()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = Open(1.0f);
		(RoomController room, DoorSide entered) = await GoToCombatRoom(level);
		Vector2I cell = room.Data.Cell;
		Require(Heart(room) == null, "Antes de despejar no hay corazón");
		await ClearRoom(room);

		ItemPickup heart = Heart(room);
		Require(heart != null && heart.Definition.Id == "corazon", "Al despejar la sala cae un corazón");
		Require(heart.Position.IsEqualApprox(Vector3.Zero), "Cae en el centro de la sala");
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
		Require(level.Rooms.CurrentRoom.Data.Cell == cell && Heart(level.Rooms.CurrentRoom) == null, "Un corazón recogido no reaparece al volver");
		await BossTestHarness.Close(tree, level);
		GD.Print("Corazón: cae al despejar, no se gasta con la vida completa, cura 1 y no reaparece: OK");
	}

	private async Task CheckPendingHeartReappears()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level level = Open(1.0f);
		(RoomController room, DoorSide entered) = await GoToCombatRoom(level);
		await ClearRoom(room);
		Require(Heart(room) != null, "Cae el corazón");

		DoorSide back = (DoorSide)(((int)entered + 2) % 4);
		await BossTestHarness.Cross(tree, level.Rooms, back);
		Require(level.Rooms.CurrentRoom.Data.Cell != room.Data.Cell, "Se sale de la sala sin recogerlo");
		await BossTestHarness.Cross(tree, level.Rooms, entered);
		ItemPickup again = Heart(level.Rooms.CurrentRoom);
		Require(again != null && !again.IsCollected && again.Definition.Id == "corazon", "Un corazón sin recoger reaparece al volver");
		await BossTestHarness.Close(tree, level);
		GD.Print("Corazón pendiente: reaparece al volver a la sala: OK");
	}

	private async Task CheckChanceZeroAndSeeded()
	{
		SceneTree tree = GetTree();
		BossTestHarness.Level none = Open(0.0f);
		(RoomController room, _) = await GoToCombatRoom(none);
		await ClearRoom(room);
		Require(Heart(room) == null && room.GetNodeOrNull("HeartDrop") == null, "Con probabilidad 0 no cae nada");
		await BossTestHarness.Close(tree, none);

		BossTestHarness.Level seeded = Open(0.25f);
		(room, _) = await GoToCombatRoom(seeded);
		bool expected = HeartDropper.Rolls(seeded.Run.Seed, room.Data.Cell, 0.25f);
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
		await ClearRoom(room);
		Require(room.GetNodeOrNull("HeartDrop") == null, "El bot y el simulador (sin objetos) no ven corazones, ni con probabilidad 1");
		await BossTestHarness.Close(tree, level);
		GD.Print("Sin recompensas activas: no cae nada: OK");
	}
}
