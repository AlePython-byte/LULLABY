using System.Collections.Generic;
using Godot;

/// <summary>
/// Opción de equilibrio: al despejar una sala de combate (tipo Normal) puede caer un corazón (+1 de vida, resources/items/corazon.tres)
/// en el centro de la sala, con probabilidad DropChance (RunManager.HeartDropChance, 0.25 por defecto; 0 lo apaga). El sorteo es por
/// semilla de partida y celda, con su propio azar como los tesoros: no consume el azar global ni depende del orden de visita, y una sala
/// se sortea una sola vez. El corazón no está en el catálogo de objetos, así que los tesoros nunca lo ofrecen.
///
/// Solo actúa cuando las recompensas están activas (TreasureItemSpawner.Active): el bot y el simulador, que juegan sin objetos, no ven
/// corazones. Un corazón sin recoger (por ejemplo con la vida completa) sigue en el suelo y reaparece al volver a la sala; uno recogido no.
/// </summary>
public partial class HeartDropper : Node
{
	public const string HeartPath = "res://resources/items/corazon.tres";
	public const float DefaultChance = 0.25f;

	private sealed class Drop
	{
		public bool Collected;
	}

	/// <summary>Probabilidad de que una sala despejada suelte un corazón (de 0 a 1).</summary>
	public float DropChance { get; set; } = DefaultChance;

	private readonly Dictionary<Vector2I, Drop> _drops = new();
	private readonly HashSet<Vector2I> _rolled = new();
	private RoomManager _rooms;
	private TreasureItemSpawner _treasure;
	private ItemDefinition _heart;
	private RoomController _room;
	private Node3D _root;
	private int _gameSeed;

	public bool Active => DropChance > 0.0f && _heart != null && _treasure != null && _treasure.Active;

	public void Begin(RoomManager rooms, TreasureItemSpawner treasure, int gameSeed, float chance)
	{
		_rooms = rooms;
		_treasure = treasure;
		_gameSeed = gameSeed;
		DropChance = Mathf.Clamp(chance, 0.0f, 1.0f);
		_heart = GD.Load<ItemDefinition>(HeartPath);
		if (_heart == null || !_heart.IsValid)
		{
			GD.PushError($"HeartDropper: {HeartPath} no es un objeto válido; no caerán corazones.");
			_heart = null;
			return;
		}

		_rooms.RoomLoaded += OnRoomLoaded;
	}

	public override void _ExitTree()
	{
		if (_rooms != null)
		{
			_rooms.RoomLoaded -= OnRoomLoaded;
		}

		Detach();
	}

	/// <summary>Sorteo de una sala: determinista por semilla de partida y celda, con azar propio.</summary>
	public static bool Rolls(int gameSeed, Vector2I cell, float chance)
	{
		if (chance <= 0.0f) return false;
		if (chance >= 1.0f) return true;
		using RandomNumberGenerator random = new() { Seed = SeedFor(gameSeed, cell) };
		return random.Randf() < chance;
	}

	private void OnRoomLoaded(RoomController room)
	{
		Detach();
		_room = room;
		room.RoomCleared += OnRoomCleared;
		if (Active && _drops.TryGetValue(room.Data.Cell, out Drop drop) && !drop.Collected)
		{
			Spawn(room, drop);
		}
	}

	private void OnRoomCleared()
	{
		RoomController room = _room;
		if (!Active || room == null || room.Data.Type != RoomType.Normal || !_rolled.Add(room.Data.Cell)) return;
		if (!Rolls(_gameSeed, room.Data.Cell, DropChance)) return;

		Drop drop = new();
		_drops[room.Data.Cell] = drop;
		// La última muerte llega desde un callback de física: el área del corazón se añade ya fuera de él.
		Callable.From(() =>
		{
			if (IsInstanceValid(room) && room.IsInsideTree() && _room == room) Spawn(room, drop);
		}).CallDeferred();
	}

	private void Spawn(RoomController room, Drop drop)
	{
		if (_treasure.PickupScene == null) return;
		_root = new Node3D { Name = "HeartDrop" };
		room.AddChild(_root);
		ItemPickup pickup = _treasure.PickupScene.Instantiate<ItemPickup>();
		pickup.Monitoring = false;
		pickup.Definition = _heart;
		pickup.Collected += _ => drop.Collected = true;
		_root.AddChild(pickup);
		EnableAfterPhysics(pickup);
	}

	private async void EnableAfterPhysics(ItemPickup pickup)
	{
		SceneTree tree = GetTree();
		await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		if (IsInstanceValid(this) && IsInsideTree() && IsInstanceValid(pickup) && !pickup.IsQueuedForDeletion() && !pickup.IsCollected)
		{
			pickup.SetDeferred(Area3D.PropertyName.Monitoring, true);
		}
	}

	private void Detach()
	{
		if (IsInstanceValid(_room))
		{
			_room.RoomCleared -= OnRoomCleared;
		}

		_room = null;
		if (IsInstanceValid(_root) && !_root.IsQueuedForDeletion())
		{
			_root.QueueFree();
		}

		_root = null;
	}

	private static ulong SeedFor(int gameSeed, Vector2I cell) =>
		Mix(Mix(Mix(0x4845415254445250UL ^ (uint)gameSeed) ^ (uint)cell.X) ^ (uint)cell.Y);

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
