using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>Integración de recompensas, visitas, reinicios y aislamiento de la composición base.</summary>
public partial class TreasureItemsTest : Node
{
	private int _checks;
	private static readonly Vector2I TreasureCell = new(1, 0);

	public override async void _Ready()
	{
		try
		{
			await CheckVisitsAndReset();
			await CheckMultipleOffers();
			await CheckVisitOrder();
			await CheckDisabled();
			await CheckHud();
			CheckRandomIsolation();
			GD.Print($"TreasureItemsTest: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private static LevelMap Fixture(bool extraTreasure = false)
	{
		LevelMap map = new();
		map.Rooms.Add(new RoomData { Cell = Vector2I.Zero, Type = RoomType.Start, Size = new Vector2(12, 12), TemplateId = "inicio" });
		map.Rooms.Add(new RoomData { Cell = TreasureCell, Type = RoomType.Treasure, Size = new Vector2(12, 12), TemplateId = "tesoro" });
		map.Rooms.Add(new RoomData { Cell = new Vector2I(2, 0), Type = RoomType.Boss, Size = new Vector2(12, 12), TemplateId = "jefe" });
		if (extraTreasure) map.Rooms.Add(new RoomData { Cell = new Vector2I(0, 1), Type = RoomType.Treasure, Size = new Vector2(12, 12), TemplateId = "tesoro_extra" });
		return map;
	}

	private Node Open(int seed, LevelMap map, bool enabled = true, ItemCatalog catalog = null, bool unlimitedHealth = false)
	{
		RunManager.SetNextSeed(seed);
		RunManager.SetNextMap(map);
		Node level = GD.Load<PackedScene>("res://scenes/levels/TestLevel.tscn").Instantiate();
		TreasureItemSpawner spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		spawner.Enabled = enabled;
		if (catalog != null) spawner.Catalog = catalog;
		if (unlimitedHealth) HealthComponent.FindIn(level.GetNode("Player")).MaxHealth = 1.0e9f;
		level.GetNode<RoomManager>("RoomManager").FadeDuration = 0.0f;
		AddChild(level);
		return level;
	}

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private async Task Cross(Node level, DoorSide side, Vector2I expected)
	{
		RoomManager rooms = level.GetNode<RoomManager>("RoomManager");
		rooms.CurrentRoom.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);
		for (int i = 0; i < 120; i++)
		{
			await Frames(1);
			if (GodotObject.IsInstanceValid(rooms.CurrentRoom) && rooms.CurrentRoom.Data.Cell == expected
				&& rooms.Player.IsPhysicsProcessing())
			{
				await Frames(2);
				return;
			}
		}
		throw new InvalidDataException("No terminó la transición a " + expected);
	}

	private static ItemPickup[] Pickups(Node level)
	{
		Node root = level.GetNode<RoomManager>("RoomManager").CurrentRoom.GetNodeOrNull("TreasureItems");
		return root == null ? Array.Empty<ItemPickup>() : root.GetChildren().OfType<ItemPickup>().ToArray();
	}

	private async Task CheckVisitsAndReset()
	{
		LevelMap map = Fixture();
		string original = DungeonGenerator.Describe(map);
		Node level = Open(42, map);
		TreasureItemSpawner spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		Require(spawner.Active && spawner.OffersFor(TreasureCell).Count == 1, "Una recompensa por defecto");
		string offered = spawner.OffersFor(TreasureCell)[0].Item.Id;
		Require(Pickups(level).Length == 0, "No aparece una recompensa en inicio");
		await Cross(level, DoorSide.East, TreasureCell);
		Require(Pickups(level).Length == 1 && Pickups(level)[0].Definition.Id == offered,
			$"Recompensa asignada en el tesoro: jugador={level.GetNode<PlayerController>("Player").GlobalPosition}, recogida={spawner.OffersFor(TreasureCell)[0].Collected}, nodos={Pickups(level).Length}");
		await Cross(level, DoorSide.West, Vector2I.Zero);
		await Cross(level, DoorSide.East, TreasureCell);
		Require(Pickups(level).Length == 1 && Pickups(level)[0].Definition.Id == offered, "Volver no cambia el objeto pendiente");
		PlayerController player = level.GetNode<PlayerController>("Player");
		HealthComponent.FindIn(player).TakeDamage(2.0f);
		player.GlobalPosition = new Vector3(0.0f, 0.5f, 0.0f);
		await Frames(3);
		Require(spawner.OffersFor(TreasureCell)[0].Collected && Pickups(level).Length == 0, "Contacto consume y registra la oferta");
		Require(player.GetNode<PlayerItems>("Items").CollectedItems.Count == 1, "Inventario registra la recompensa");
		Require(level.GetNode<Label>("ItemsHud/Label").Text.Contains(spawner.OffersFor(TreasureCell)[0].Item.DisplayName),
			"HUD refleja la recogida real en el tesoro");
		await Cross(level, DoorSide.East, new Vector2I(2, 0));
		await Cross(level, DoorSide.West, TreasureCell);
		Require(Pickups(level).Length == 0 && player.GetNode<PlayerItems>("Items").CollectedItems.Count == 1,
			"Recogida no reaparece y el inventario persiste al cambiar de habitación");
		Require(level.GetNode<Label>("ItemsHud/Label").Text.Contains(spawner.OffersFor(TreasureCell)[0].Item.DisplayName),
			"HUD conserva el objeto entre habitaciones");
		Require(DungeonGenerator.Describe(map) == original, "Las recompensas no cambian RoomData");
		level.Free();
		level = Open(42, Fixture());
		spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		Require(spawner.OffersFor(TreasureCell)[0].Item.Id == offered && !spawner.OffersFor(TreasureCell)[0].Collected,
			"Nueva instancia de partida repite recompensa y reinicia su consumo");
		Require(level.GetNode<PlayerItems>("Player/Items").CollectedItems.Count == 0, "Nueva partida reinicia inventario");
		Require(level.GetNode<Label>("ItemsHud/Label").Text == "Objetos: —", "Nueva partida limpia el HUD");
		Require(HealthComponent.FindIn(level.GetNode("Player")).CurrentHealth == 5.0f, "Nueva partida reinicia salud");
		level.Free();
		GD.Print("Visitas reales, recogida, persistencia y nueva partida: OK");
	}

	private async Task CheckMultipleOffers()
	{
		ItemCatalog shared = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		ItemCatalog local = new() { Items = new Godot.Collections.Array<ItemDefinition>(shared.Items), ItemsPerTreasure = 5 };
		Node level = Open(42, Fixture(), catalog: local);
		await Cross(level, DoorSide.East, TreasureCell);
		ItemPickup[] offers = Pickups(level);
		Require(offers.Length == 5 && offers.Select(item => item.Definition.Id).Distinct().Count() == 5,
			$"Cantidad parametrizable sin repetir hasta agotar catálogo: presentes={offers.Length}, inventario={level.GetNode<PlayerItems>("Player/Items").CollectedItems.Count}, " +
			$"asignadas={level.GetNode<TreasureItemSpawner>("TreasureItemSpawner").OffersFor(TreasureCell).Count}, " +
			$"posiciones={string.Join(";", offers.Select(item => item.Position.ToString()))}");
		Require(offers.Select(item => item.Position).Distinct().Count() == 5, "Cada oferta tiene su posición");
		PlayerController player = level.GetNode<PlayerController>("Player");
		foreach (ItemPickup offer in offers)
		{
			if (offer.Definition.Effect == ItemEffect.Heal) HealthComponent.FindIn(player).TakeDamage(2.0f);
			Require(offer.TryCollect(player), "Cada oferta se puede recoger independientemente");
		}
		await Frames(2);
		Require(player.GetNode<PlayerItems>("Items").CollectedItems.Count == 5, "No se ha implementado elegir uno de N");
		Require(shared.ItemsPerTreasure == 1, "Configuración compartida intacta");
		level.Free();
		GD.Print("Cantidad configurable y ofertas independientes: OK");
	}

	private async Task CheckVisitOrder()
	{
		Node level = Open(42, Fixture(extraTreasure: true));
		TreasureItemSpawner spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		Vector2I other = new(0, 1);
		string first = spawner.OffersFor(TreasureCell)[0].Item.Id;
		string second = spawner.OffersFor(other)[0].Item.Id;
		await Cross(level, DoorSide.South, other);
		Require(Pickups(level).Single().Definition.Id == second, "Visitar primero otro tesoro conserva su recompensa");
		await Cross(level, DoorSide.North, Vector2I.Zero);
		await Cross(level, DoorSide.East, TreasureCell);
		Require(Pickups(level).Single().Definition.Id == first, "El orden de visita no cambia la selección");
		level.Free();

		ItemCatalog shared = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		ItemCatalog local = new()
		{
			Items = new Godot.Collections.Array<ItemDefinition>(Enumerable.Reverse(shared.Items)), ItemsPerTreasure = 8,
		};
		LevelMap reversed = Fixture(extraTreasure: true);
		reversed.Rooms = new Godot.Collections.Array<RoomData>(Enumerable.Reverse(reversed.Rooms));
		level = Open(42, reversed, catalog: local);
		spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		Require(spawner.OffersFor(TreasureCell).Count == 8, "Se respeta una cantidad mayor que el catálogo");
		Require(spawner.OffersFor(TreasureCell)[0].Item.Id == first && spawner.OffersFor(other)[0].Item.Id == second,
			"Orden del mapa, orden del catálogo y cantidad no cambian la primera oferta");
		Require(spawner.OffersFor(TreasureCell).Take(5).Select(offer => offer.Item.Id).Distinct().Count() == 5,
			"Se agota cada ciclo del catálogo antes de repetir");
		await Cross(level, DoorSide.East, TreasureCell);
		Require(Pickups(level).Length == 8, "Ocho ofertas aparecen en la sala");
		Require(Pickups(level).All(item => Mathf.Abs(item.Position.X) <= 2 && Mathf.Abs(item.Position.Z) <= 2),
			"Ofertas dentro de la zona central");
		level.Free();
		GD.Print("Orden de visita, catálogo estable y cantidades mayores: OK");
	}

	private async Task CheckDisabled()
	{
		TreasureItemSpawner defaults = new();
		Require(!defaults.Enabled, "Recompensas desactivadas por defecto en código");
		defaults.Free();
		Node level = Open(42, Fixture(), enabled: false, unlimitedHealth: true);
		TreasureItemSpawner spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		Require(!spawner.Active && spawner.OffersFor(TreasureCell).Count == 0, "Modo histórico no asigna objetos");
		Require(HealthComponent.FindIn(level.GetNode("Player")).CurrentHealth == 1.0e9f, "Modo histórico conserva la vida del bot");
		await Cross(level, DoorSide.East, TreasureCell);
		Require(Pickups(level).Length == 0 && level.GetNode<PlayerItems>("Player/Items").CollectedItems.Count == 0,
			"Modo histórico no crea ni recoge objetos");
		level.Free();
		GD.Print("Bot sin objetos y salud ilimitada intacta: OK");
	}

	private void CheckRandomIsolation()
	{
		Node level = Open(1, Fixture());
		TreasureItemSpawner spawner = level.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		RoomCatalog catalog = GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres");
		DifficultyModel model = DifficultyModel.Load(DifficultyModel.PathV2);
		AIDungeonDirector director = new(new DungeonGenerator(catalog), model,
			new DungeonGenerator.Settings { GeneratorVersion = 2 }) { PreferEnemyVariety = true };
		System.Collections.Generic.HashSet<string> found = new();
		for (int seed = 1; seed <= 200; seed++)
		{
			AIDungeonDirector.Result before = director.Select(seed, DifficultyTarget.Normal);
			string description = DungeonGenerator.Describe(before.Map);
			Vector2I cell = before.Map.Rooms.First(room => room.Type == RoomType.Treasure).Cell;
			GD.Seed(123456UL);
			uint expected = GD.Randi();
			GD.Seed(123456UL);
			spawner.BeginRun(seed, before.Map);
			Require(GD.Randi() == expected, "El azar de objetos no consume azar global");
			string first = spawner.OffersFor(cell)[0].Item.Id;
			found.Add(first);
			spawner.BeginRun(seed, before.Map);
			Require(spawner.OffersFor(cell)[0].Item.Id == first, "Recompensa determinista por semilla y celda");
			AIDungeonDirector.Result after = director.Select(seed, DifficultyTarget.Normal);
			Require(after.Chosen.EffectiveSeed == before.Chosen.EffectiveSeed && DungeonGenerator.Describe(after.Map) == description,
				"El nivel elegido no cambia al generar recompensas");
			Require(DungeonGenerator.Describe(before.Map) == description, "Mapa original intacto");
		}
		Require(found.Count == 5, "Las 200 semillas permiten los cinco objetos");
		level.Free();
		GD.Print("200 semillas: determinismo, RNG global y nivel elegido independientes: OK");
	}

	private async Task CheckHud()
	{
		Node level = Open(42, Fixture());
		PlayerItems inventory = level.GetNode<PlayerItems>("Player/Items");
		Label label = level.GetNode<Label>("ItemsHud/Label");
		Require(label.Text == "Objetos: —", "HUD empieza vacío");
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		Require(inventory.TryCollect(catalog.Find("fotografia_rota")), "Mejora para el HUD");
		Require(label.Text.Contains("Fotografía rota") && !label.Text.Contains("usado"), "HUD muestra la mejora");
		string previous = label.Text;
		Require(!inventory.TryCollect(catalog.Find("fotografia_rota")) && label.Text == previous, "HUD no duplica mejoras");
		HealthComponent health = HealthComponent.FindIn(level.GetNode("Player"));
		health.TakeDamage(2.0f);
		Require(inventory.TryCollect(catalog.Find("biberon")) && label.Text.Contains("Biberón (usado)"), "Curación marcada como usada");
		health.TakeDamage(2.0f);
		Require(inventory.TryCollect(catalog.Find("biberon")) && label.Text.Contains("Biberón (usado) ×2"), "HUD agrupa consumibles usados");
		foreach (ItemDefinition item in catalog.Items.Where(item => item.Effect != ItemEffect.Heal)) inventory.TryCollect(item);
		await Frames(2);
		Require(label.GetMinimumSize().X <= label.Size.X && label.GetMinimumSize().Y <= label.Size.Y,
			$"El listado completo cabe en su espacio de HUD: mínimo={label.GetMinimumSize()}, espacio={label.Size}");
		inventory.ResetForNewRun();
		Require(label.Text == "Objetos: —", "HUD se limpia al reiniciar");
		level.Free();
		GD.Print("HUD: mejoras, consumibles, duplicados y reinicio: OK");
	}
}
