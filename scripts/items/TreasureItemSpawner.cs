using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Recompensas por partida y celda; el azar y el estado son independientes del nivel y del director.</summary>
public partial class TreasureItemSpawner : Node
{
	public sealed class Offer
	{
		public ItemDefinition Item { get; init; }
		public bool Collected { get; internal set; }
	}

	[Export] public bool Enabled { get; set; } = false;
	[Export] public RoomManager Rooms { get; set; }
	[Export] public PlayerItems Inventory { get; set; }
	[Export] public ItemCatalog Catalog { get; set; }
	[Export] public PackedScene PickupScene { get; set; }
	private readonly Dictionary<Vector2I, List<Offer>> _offers = new();
	private readonly List<ItemPickup> _live = new();
	private Node3D _root;
	private bool _active;

	// El modelo de un objeto ya queda ~0,25 m sobre el suelo: sube solo la altura del pedestal menos este margen (que lo deja justo encima).
	private const float PedestalOverlap = 0.2f;

	public bool Active => Enabled && _active;

	public override void _Ready()
	{
		if (Rooms != null) Rooms.RoomLoaded += OnRoomLoaded;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Rooms)) Rooms.RoomLoaded -= OnRoomLoaded;
	}

	public IReadOnlyList<Offer> OffersFor(Vector2I cell) => _offers.TryGetValue(cell, out List<Offer> offers)
		? offers.AsReadOnly() : Array.Empty<Offer>();

	public void BeginRun(int gameSeed, LevelMap map)
	{
		_active = false;
		ClearLive();
		_offers.Clear();
		if (!Enabled) return;
		if (map == null || Rooms == null || Inventory == null || Catalog == null || !Catalog.IsValid() || PickupScene == null)
		{
			GD.PushError("TreasureItemSpawner: faltan mapa, salas, inventario, catálogo válido o escena.");
			return;
		}
		ItemDefinition[] items = Catalog.Items.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
		foreach (RoomData room in map.Rooms.Where(room => room.Type == RoomType.Treasure))
		{
			// Cada celda tiene su propia secuencia: visitar otras salas o cambiar su orden no consume este azar.
			using RandomNumberGenerator random = new() { Seed = SeedFor(gameSeed, room.Cell) };
			List<ItemDefinition> remaining = new(items);
			List<Offer> offers = new();
			for (int slot = 0; slot < Catalog.ItemsPerTreasure; slot++)
			{
				if (remaining.Count == 0) remaining.AddRange(items);
				int chosen = random.RandiRange(0, remaining.Count - 1);
				offers.Add(new Offer { Item = remaining[chosen] });
				remaining.RemoveAt(chosen);
			}
			_offers.Add(room.Cell, offers);
		}
		Inventory.ResetForNewRun();
		_active = true;
	}

	private void OnRoomLoaded(RoomController room)
	{
		ClearLive();
		if (!Active || room.Data.Type != RoomType.Treasure || !_offers.TryGetValue(room.Data.Cell, out List<Offer> offers)) return;
		_root = new Node3D { Name = "TreasureItems" };
		room.AddChild(_root);
		for (int slot = 0; slot < offers.Count; slot++)
		{
			Offer offer = offers[slot];
			Vector3 position = PositionFor(slot, offers.Count);
			// Con la ambientación activa, cada objeto descansa sobre un pedestal (que se queda vacío al recogerlo). Es solo visual.
			float lift = room.DecorEnabled ? AddPedestal(position) : 0.0f;
			if (offer.Collected) continue;
			ItemPickup pickup = PickupScene.Instantiate<ItemPickup>();
			pickup.Monitoring = false;
			pickup.Definition = offer.Item;
			pickup.Position = position + new Vector3(0.0f, lift, 0.0f);
			pickup.Collected += _ => offer.Collected = true;
			_root.AddChild(pickup);
			_live.Add(pickup);
			EnableAfterPhysics(pickup);
		}
	}

	// Devuelve cuánto hay que subir el objeto (la altura del pedestal menos lo que ya flota sobre el suelo); 0 si falta la pieza.
	private float AddPedestal(Vector3 position)
	{
		string path = PropModels.Path(PropModels.Pedestal);
		Node3D pedestal = ModelLibrary.Create(path);
		if (pedestal == null) return 0.0f;
		pedestal.Name = "Pedestal";
		pedestal.Position = position;
		_root.AddChild(pedestal);
		return ModelLibrary.BoundsOf(path).Value.Size.Y - PedestalOverlap;
	}

	private async void EnableAfterPhysics(ItemPickup pickup)
	{
		// Sincronizar primero las posiciones físicas del jugador y de las nuevas áreas, también con fundido de duración cero.
		SceneTree tree = GetTree();
		await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		if (IsInstanceValid(this) && IsInsideTree() && Active && IsInstanceValid(pickup)
			&& !pickup.IsQueuedForDeletion() && !pickup.IsCollected)
			pickup.SetDeferred(Area3D.PropertyName.Monitoring, true);
	}

	private void ClearLive()
	{
		foreach (ItemPickup pickup in _live)
			if (IsInstanceValid(pickup) && !pickup.IsQueuedForDeletion()) pickup.QueueFree();
		_live.Clear();
		if (IsInstanceValid(_root) && !_root.IsQueuedForDeletion()) _root.QueueFree();
		_root = null;
	}

	private static Vector3 PositionFor(int slot, int count)
	{
		int columns = (int)Math.Ceiling(Math.Sqrt(count));
		int rows = (count + columns - 1) / columns;
		// Cuadrícula centrada de hasta cuatro metros de lado, lejos de paredes y llegadas del tesoro de 12x12.
		float spacing = Math.Min(2.6f, 4.0f / Math.Max(1, Math.Max(columns - 1, rows - 1)));
		return new Vector3((slot % columns - (columns - 1) / 2.0f) * spacing, 0.0f,
			(slot / columns - (rows - 1) / 2.0f) * spacing);
	}

	private static ulong SeedFor(int gameSeed, Vector2I cell) =>
		Mix(Mix(Mix(0x5452454153555245UL ^ (uint)gameSeed) ^ (uint)cell.X) ^ (uint)cell.Y);

	private static ulong Mix(ulong value)
	{
		unchecked
		{
			value += 0x9E3779B97F4A7C15UL;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}
	}
}
