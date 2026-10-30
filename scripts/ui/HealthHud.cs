using Godot;

public partial class HealthHud : CanvasLayer
{
	private Label _label;
	private HealthComponent _health;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");

		Node player = GetTree().GetFirstNodeInGroup("player");
		if (player == null)
		{
			return;
		}

		_health = HealthComponent.FindIn(player);
		_health.HealthChanged += OnHealthChanged;
		UpdateText();
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
		UpdateText();
	}

	private void UpdateText()
	{
		_label.Text = $"Vida: {_health.CurrentHealth:0.#}/{_health.EffectiveMaxHealth:0.#}";
	}
}
