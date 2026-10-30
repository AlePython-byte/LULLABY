using Godot;

/// <summary>Nombre y vida del jefe actual. Sigue la sala cargada y no mantiene referencias a salas anteriores.</summary>
public partial class BossHud : CanvasLayer
{
	[Export] public RoomManager Rooms { get; set; }
	private Control _panel;
	private ProgressBar _bar;
	private Label _name;
	private RoomController _room;
	private HealthComponent _health;

	public override void _Ready()
	{
		_panel = GetNode<Control>("Panel");
		_bar = GetNode<ProgressBar>("Panel/Bar");
		_name = GetNode<Label>("Panel/Name");
		_panel.Hide();
		if (Rooms == null) return;
		Rooms.RoomLoaded += OnRoomLoaded;
		OnRoomLoaded(Rooms.CurrentRoom);
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Rooms)) Rooms.RoomLoaded -= OnRoomLoaded;
		DisconnectRoom();
	}

	private void OnRoomLoaded(RoomController room)
	{
		DisconnectRoom();
		_panel.Hide();
		_room = room;
		if (!IsInstanceValid(_room)) return;
		_room.BossSpawned += OnBossSpawned;
		_room.RoomCleared += OnCleared;
		if (IsInstanceValid(_room.BossEnemy)) OnBossSpawned(_room.BossEnemy);
	}

	private void OnBossSpawned(EnemyBase boss)
	{
		DisconnectHealth();
		_health = boss.Health;
		_health.HealthChanged += OnHealthChanged;
		OnHealthChanged(_health.CurrentHealth, _health.EffectiveMaxHealth);
	}

	private void OnHealthChanged(float current, float maximum)
	{
		_bar.MaxValue = maximum;
		_bar.Value = current;
		_name.Text = $"The Mother  {current:0.#}/{maximum:0.#}";
		_panel.Visible = current > 0.0f;
	}

	private void OnCleared() => _panel.Hide();

	private void DisconnectHealth()
	{
		if (IsInstanceValid(_health)) _health.HealthChanged -= OnHealthChanged;
		_health = null;
	}

	private void DisconnectRoom()
	{
		DisconnectHealth();
		if (IsInstanceValid(_room))
		{
			_room.BossSpawned -= OnBossSpawned;
			_room.RoomCleared -= OnCleared;
		}
		_room = null;
	}
}
