using Godot;

public partial class PlayerHealth : Node
{
	[Export] public float InvulnerabilityTime { get; set; } = 1.0f;

	private HealthComponent _health;
	private Node3D _mesh;
	private float _invulnerableTimer;

	public override void _Ready()
	{
		Node player = GetParent();
		_health = HealthComponent.FindIn(player);
		_mesh = player.GetNode<Node3D>("Mesh");
		_health.Died += OnDied;
	}

	public override void _Process(double delta)
	{
		if (_invulnerableTimer <= 0.0f)
		{
			return;
		}

		_invulnerableTimer -= (float)delta;

		// Parpadeo mientras dura la invulnerabilidad.
		_mesh.Visible = _invulnerableTimer <= 0.0f || (int)(_invulnerableTimer * 10.0f) % 2 == 0;
	}

	public void TakeHit(float damage)
	{
		if (_invulnerableTimer > 0.0f || _health.IsDead)
		{
			return;
		}

		_health.TakeDamage(damage);
		_invulnerableTimer = InvulnerabilityTime;
	}

	private void OnDied()
	{
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}
}
