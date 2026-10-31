using Godot;

/// <summary>
/// Vida del jugador como una fila de corazones (HeartsRow) arriba a la izquierda, con el HUD de objetos debajo. Solo muestra la vida
/// (HealthComponent.CurrentHealth y EffectiveMaxHealth): no cambia ningún valor.
/// </summary>
public partial class HealthHud : CanvasLayer
{
	private HeartsRow _hearts;
	private HealthComponent _health;

	public HeartsRow Hearts => _hearts;

	public override void _Ready()
	{
		_hearts = GetNode<HeartsRow>("Hearts");

		Node player = GetTree().GetFirstNodeInGroup(GameNames.PlayerGroup);
		if (player == null)
		{
			return;
		}

		_health = HealthComponent.FindIn(player);
		_health.HealthChanged += OnHealthChanged;
		_hearts.SetHealth(_health.CurrentHealth, _health.EffectiveMaxHealth);
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(_health))
		{
			_health.HealthChanged -= OnHealthChanged;
		}
	}

	private void OnHealthChanged(float current, float maximum)
	{
		_hearts.SetHealth(current, maximum);
	}
}
