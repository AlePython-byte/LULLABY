using Godot;

public partial class BasicEnemy : CharacterBody3D
{
	[Export] public float Speed { get; set; } = 3.0f;
	[Export] public float ContactDamage { get; set; } = 1.0f;
	[Export] public float HitCooldown { get; set; } = 1.0f;

	private HealthComponent _health;
	private Node3D _player;
	private float _hitTimer;

	public override void _Ready()
	{
		_health = HealthComponent.FindIn(this);
		_health.Died += OnDied;
	}

	public override void _PhysicsProcess(double delta)
	{
		_hitTimer = Mathf.Max(0.0f, _hitTimer - (float)delta);

		if (!IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
			if (_player == null)
			{
				return;
			}
		}

		Vector3 toPlayer = _player.GlobalPosition - GlobalPosition;
		toPlayer.Y = 0.0f;
		Velocity = toPlayer.LengthSquared() > 0.0001f ? toPlayer.Normalized() * Speed : Vector3.Zero;
		MoveAndSlide();

		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			if (GetSlideCollision(i).GetCollider() is Node3D collider && collider.IsInGroup("player"))
			{
				TryHit(collider);
			}
		}
	}

	private void TryHit(Node3D target)
	{
		if (_hitTimer > 0.0f)
		{
			return;
		}

		PlayerHealth playerHealth = target.GetNodeOrNull<PlayerHealth>("PlayerHealth");
		if (playerHealth == null)
		{
			return;
		}

		playerHealth.TakeHit(ContactDamage);
		_hitTimer = HitCooldown;
	}

	private void OnDied()
	{
		QueueFree();
	}
}
