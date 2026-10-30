using System.Linq;
using Godot;

/// <summary>J sin modificadores crea el siguiente objeto del catálogo. No usa azar ni funciona en Release.</summary>
public partial class DebugItemSpawner : Node
{
	private const string SpawnAction = "debug_spawn_item";
	[Export] public PlayerController Player { get; set; }
	[Export] public RoomManager Rooms { get; set; }
	[Export] public ItemCatalog Catalog { get; set; }
	[Export] public PackedScene PickupScene { get; set; }
	private int _nextItem;

	public override void _Ready()
	{
		if (!OS.IsDebugBuild())
		{
			SetProcessUnhandledInput(false);
			return;
		}
		if (Player == null || Catalog == null || !Catalog.IsValid() || PickupScene == null)
		{
			GD.PushError("DebugItemSpawner: faltan jugador, catálogo válido o escena de recogida.");
			SetProcessUnhandledInput(false);
			return;
		}
		InputActions.Register(SpawnAction, Key.J);
		GD.Print("Depuración de objetos: J crea el siguiente junto a Noah: " +
			string.Join(" → ", Catalog.Items.Select(item => item.DisplayName)) + ".");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!OS.IsDebugBuild() || @event is not InputEventKey key || key.Echo || key.CtrlPressed || key.AltPressed
			|| key.ShiftPressed || key.MetaPressed || !@event.IsActionPressed(SpawnAction, false, true)) return;
		if (SpawnNext() != null) GetViewport().SetInputAsHandled();
	}

	public ItemPickup SpawnNext()
	{
		if (!OS.IsDebugBuild() || !IsInsideTree() || Player == null || Catalog == null || !Catalog.IsValid()
			|| PickupScene == null || HealthComponent.FindIn(Player).IsDead) return null;
		RoomController room = Rooms?.CurrentRoom;
		if (!FindPosition(room, out Vector3 position)) return null;
		ItemDefinition definition = Catalog.Items[_nextItem];
		ItemPickup pickup = PickupScene.Instantiate<ItemPickup>();
		pickup.Definition = definition;
		Node parent = room != null ? room : Player.GetParent();
		pickup.Position = parent is Node3D space ? space.ToLocal(position) : position;
		pickup.Collected += item =>
		{
			PlayerShooter shooter = Player.GetNode<PlayerShooter>("Shooter");
			HealthComponent health = HealthComponent.FindIn(Player);
			GD.Print($"Recogido {item.DisplayName}: {item.Description} " +
				$"Vida {health.CurrentHealth:0.#}/{health.EffectiveMaxHealth:0.#}; daño ×{shooter.DamageMultiplier:0.##}; " +
				$"disparos/s {shooter.EffectiveShotsPerSecond:0.##}; daño recibido " +
				$"×{Player.GetNode<PlayerHealth>("PlayerHealth").DamageTakenMultiplier:0.##}.");
		};
		parent.AddChild(pickup);
		_nextItem = (_nextItem + 1) % Catalog.Items.Count;
		GD.Print($"Objeto de depuración: {definition.DisplayName}. Próximo con J: {Catalog.Items[_nextItem].DisplayName}.");
		return pickup;
	}

	private bool FindPosition(RoomController room, out Vector3 position)
	{
		Vector3 localPlayer = room != null ? room.ToLocal(Player.GlobalPosition) : Player.GlobalPosition;
		foreach (Vector3 offset in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
		{
			Vector3 candidate = localPlayer + offset * 2.0f;
			candidate.Y = room != null ? 0.0f : localPlayer.Y - 0.5f;
			if (room != null)
			{
				Vector2 limit = room.Data.Size / 2.0f - Vector2.One;
				candidate.X = Mathf.Clamp(candidate.X, -limit.X, limit.X);
				candidate.Z = Mathf.Clamp(candidate.Z, -limit.Y, limit.Y);
			}
			if (new Vector2(candidate.X - localPlayer.X, candidate.Z - localPlayer.Z).Length() < 1.5f) continue;
			position = room != null ? room.ToGlobal(candidate) : candidate;
			return true;
		}
		position = default;
		return false;
	}
}
