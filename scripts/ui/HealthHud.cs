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
		_health.Damaged += OnDamaged;
		UpdateText();
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(_health))
		{
			_health.Damaged -= OnDamaged;
		}
	}

	private void OnDamaged(float amount, float current)
	{
		UpdateText();
	}

	private void UpdateText()
	{
		_label.Text = $"Vida: {Mathf.CeilToInt(_health.CurrentHealth)}/{Mathf.CeilToInt(_health.MaxHealth)}";
	}
}
