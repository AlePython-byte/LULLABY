using Godot;

/// <summary>
/// Nombre y vida del jefe actual. Sigue la sala cargada y no mantiene referencias a salas anteriores.
/// Si el jefe tiene fases (The Mother), añade dos marcas en la barra (66 % y 33 %, donde cambia de fase) y la etiqueta de la fase
/// en curso junto al nombre (Fase I, II o III).
/// </summary>
public partial class BossHud : CanvasLayer
{
	public const string MarkNamePrefix = "PhaseMark";

	[Export] public RoomManager Rooms { get; set; }
	private Control _panel;
	private ProgressBar _bar;
	private Label _name;
	private RoomController _room;
	private HealthComponent _health;
	private MotherBehavior _phases;
	private int _phase = 1;

	public override void _Ready()
	{
		_panel = GetNode<Control>("Panel");
		_bar = GetNode<ProgressBar>("Panel/Bar");
		_name = GetNode<Label>("Panel/Name");
		BuildMarks();
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

	// Marcas finas sobre la barra, ancladas a su ancho: x = fracción de vida a la que cambia de fase.
	private void BuildMarks()
	{
		float[] fractions = { MotherPhases.SecondPhaseFraction, MotherPhases.ThirdPhaseFraction };
		for (int i = 0; i < fractions.Length; i++)
		{
			_bar.AddChild(new ColorRect
			{
				Name = $"{MarkNamePrefix}{i + 1}",
				Color = new Color(0.97f, 0.9f, 0.8f, 0.95f),
				MouseFilter = Control.MouseFilterEnum.Ignore,
				AnchorLeft = fractions[i],
				AnchorRight = fractions[i],
				AnchorTop = 0.0f,
				AnchorBottom = 1.0f,
				OffsetLeft = -1.5f,
				OffsetRight = 1.5f,
				Visible = false,
			});
		}
	}

	private void ShowMarks(bool visible)
	{
		foreach (Node child in _bar.GetChildren())
		{
			if (child is ColorRect mark && mark.Name.ToString().StartsWith(MarkNamePrefix)) mark.Visible = visible;
		}
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
		_phases = boss.Behavior<MotherBehavior>();
		_phase = _phases?.Phase ?? 1;
		if (_phases != null) _phases.PhaseChanged += OnPhaseChanged;
		ShowMarks(_phases != null);
		OnHealthChanged(_health.CurrentHealth, _health.EffectiveMaxHealth);
	}

	private void OnPhaseChanged(int phase)
	{
		_phase = phase;
		if (IsInstanceValid(_health)) OnHealthChanged(_health.CurrentHealth, _health.EffectiveMaxHealth);
	}

	private void OnHealthChanged(float current, float maximum)
	{
		_bar.MaxValue = maximum;
		_bar.Value = current;
		string label = _phases != null ? $"  {MotherTexts.PhaseLabel(_phase)}" : "";
		_name.Text = $"The Mother{label}  {current:0.#}/{maximum:0.#}";
		_panel.Visible = current > 0.0f;
	}

	private void OnCleared() => _panel.Hide();

	private void DisconnectHealth()
	{
		if (IsInstanceValid(_health)) _health.HealthChanged -= OnHealthChanged;
		if (IsInstanceValid(_phases)) _phases.PhaseChanged -= OnPhaseChanged;
		_health = null;
		_phases = null;
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
