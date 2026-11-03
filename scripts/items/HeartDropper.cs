using System.Collections.Generic;
using Godot;

/// <summary>
/// Opción de equilibrio: un corazón (+1 de vida, resources/items/corazon.tres) que suelta un enemigo al morir. La probabilidad se aplica POR
/// SALA, no por enemigo: al cargar una sala de combate normal se sortea (semilla de partida y celda, con azar propio como los tesoros) si
/// habrá corazón y, si lo hay, cuál de sus enemigos lo lleva; ese enemigo lo suelta donde muere. Con 3-4 enemigos por sala, un sorteo por
/// muerte daría de media más de un corazón por sala y volvería trivial el nivel fácil (ver docs/LULLABY.md). La probabilidad por
/// dificultad está en RunManager.HeartChanceByDifficulty. No hay corazón en la sala del jefe, ni pista visual en el portador.
///
/// El sorteo no consume el azar global ni depende del orden de visita; una sala siempre da el mismo resultado. El corazón no está en el
/// catálogo de objetos, así que los tesoros nunca lo ofrecen. Con la vida completa no se consume. Uno sin recoger sigue en el suelo y
/// reaparece donde cayó al volver a la sala; uno recogido no.
///
/// Solo actúa cuando las recompensas están activas (TreasureItemSpawner.Active): el bot y el simulador, que juegan sin objetos, no ven
/// corazones.
/// </summary>
public partial class HeartDropper : Node
{
	public const string HeartPath = "res://resources/items/corazon.tres";
	public const float DefaultChance = 0.25f;

	private sealed class Drop
	{
		public Vector3 Position; // en coordenadas de la sala (suelo y = 0)
		public bool Collected;
	}

	/// <summary>Probabilidad de que una sala de combate tenga un enemigo con corazón (de 0 a 1).</summary>
	public float DropChance { get; set; } = DefaultChance;

	/// <summary>Índice del punto de aparición cuyo enemigo lleva el corazón en la sala actual (-1 si ninguno).</summary>
	public int Carrier { get; private set; } = -1;

	private readonly Dictionary<Vector2I, Drop> _drops = new();
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

	/// <summary>Sorteo de una sala: ¿tendrá corazón? Determinista por semilla de partida y celda, con azar propio.</summary>
	public static bool Rolls(int gameSeed, Vector2I cell, float chance)
	{
		if (chance <= 0.0f) return false;
		if (chance >= 1.0f) return true;
		using RandomNumberGenerator random = new() { Seed = SeedFor(gameSeed, cell) };
		return random.Randf() < chance;
	}

	/// <summary>
	/// Qué enemigo lleva el corazón: el índice de su punto de aparición (de 0 a enemyCount - 1), o -1 si la sala no tiene corazón. El
	/// sorteo de Rolls y la elección del portador salen del mismo generador, de modo que ambos son estables para una semilla y una celda.
	/// </summary>
	public static int CarrierFor(int gameSeed, Vector2I cell, float chance, int enemyCount)
	{
		if (enemyCount <= 0 || !Rolls(gameSeed, cell, chance)) return -1;
		using RandomNumberGenerator random = new() { Seed = SeedFor(gameSeed, cell) };
		random.Randf(); // la primera extracción es la de Rolls
		return random.RandiRange(0, enemyCount - 1);
	}

	private void OnRoomLoaded(RoomController room)
	{
		Detach();
		_room = room;
		Carrier = -1;
		if (!Active) return;

		room.EnemyKilled += OnEnemyKilled;
		if (room.Data.Type == RoomType.Normal && room.State != RoomState.Cleared)
		{
			Carrier = CarrierFor(_gameSeed, room.Data.Cell, DropChance, room.Data.SpawnPoints.Length);
		}

		if (_drops.TryGetValue(room.Data.Cell, out Drop drop) && !drop.Collected)
		{
			Spawn(room, drop);
		}
	}

	private void OnEnemyKilled(int index, Vector3 position)
	{
		RoomController room = _room;
		if (!Active || room == null || index != Carrier || _drops.ContainsKey(room.Data.Cell)) return;

		Vector3 local = room.ToLocal(position);
		Drop drop = new() { Position = new Vector3(local.X, 0.0f, local.Z) };
		_drops[room.Data.Cell] = drop;
		// La muerte llega desde un callback de física: el área del corazón se añade ya fuera de él.
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
		pickup.Position = drop.Position;
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
			_room.EnemyKilled -= OnEnemyKilled;
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
