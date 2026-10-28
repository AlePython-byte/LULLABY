using Godot;

public partial class Tear : Area3D
{
	[Export] public float Speed { get; set; } = 12.0f;
	[Export] public float Lifetime { get; set; } = 1.2f;
	[Export] public float Damage { get; set; } = 1.0f;

	private Vector3 _direction = Vector3.Zero;
	private float _age;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	public void Launch(Vector3 direction)
	{
		_direction = direction.Normalized();
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

	// La máscara solo detecta World y Enemies; aplicar Damage a los enemigos llegará con ellos.
	private void OnBodyEntered(Node3D body)
	{
		QueueFree();
	}
}
