using Godot;

public partial class Tear : Area3D
{
	[Export] public float Speed { get; set; } = 12.0f;
	[Export] public float Lifetime { get; set; } = 1.2f;
	[Export] public float Damage { get; set; } = 1.0f;

	private Vector3 _direction = Vector3.Zero;
	private float _age;
	private bool _spent;

	public override void _Ready()
	{
		AddToGroup("tears");
		BodyEntered += OnBodyEntered;
	}

	public void Launch(Vector3 direction)
	{
		_direction = direction.Normalized();
		GetNodeOrNull<ProjectileModel>("Model")?.Orient(_direction);
	}

	public override void _PhysicsProcess(double delta)
	{
		GlobalPosition += _direction * Speed * (float)delta;

		_age += (float)delta;
		if (_age >= Lifetime)
		{
			QueueFree();
		}
	}

	// La máscara solo detecta World y Enemies: las paredes no tienen vida, los enemigos sí.
	private void OnBodyEntered(Node3D body)
	{
		if (_spent)
		{
			return;
		}

		_spent = true;
		// Primero el daño y la liberación; el sonido va después y nunca puede impedirlos.
		HealthComponent.FindIn(body)?.TakeDamage(Damage);
		QueueFree();
		AudioManager.Play(SoundCatalog.TearImpact);
	}
}
