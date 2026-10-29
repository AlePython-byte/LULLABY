using Godot;

/// <summary>
/// Proyectil enemigo. Capa EnemyProjectiles, máscara World + Player: golpea al jugador y a las paredes (y puertas
/// cerradas), nunca a otros enemigos ni a las lágrimas. Al jugador le hace Damage con PlayerHealth.TakeHit, que respeta
/// su invulnerabilidad. Se mueve en línea recta y desaparece al chocar o al cumplir Lifetime segundos; las salas
/// liberan los que queden al despejarse.
/// </summary>
public partial class EnemyProjectile : Area3D
{
	[Signal] public delegate void ImpactEventHandler(bool hitPlayer);

	[Export] public float Speed { get; set; } = 5.0f;
	[Export] public float Damage { get; set; } = 1.0f;
	[Export] public float Lifetime { get; set; } = 3.0f;

	private Vector3 _direction = Vector3.Zero;
	private float _age;
	private bool _spent;

	public override void _Ready()
	{
		AddToGroup("enemy_projectiles");
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

	private void OnBodyEntered(Node3D body)
	{
		if (_spent)
		{
			return;
		}

		_spent = true;
		bool hitPlayer = body.IsInGroup("player");
		if (hitPlayer)
		{
			body.GetNodeOrNull<PlayerHealth>("PlayerHealth")?.TakeHit(Damage);
		}

		EmitSignal(SignalName.Impact, hitPlayer);
		QueueFree();
	}
}
