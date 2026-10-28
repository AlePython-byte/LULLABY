using Godot;

public partial class BasicEnemy : CharacterBody3D
{
	[Export] public float Speed { get; set; } = 3.0f;
	[Export] public float ContactDamage { get; set; } = 1.0f;
	[Export] public float HitCooldown { get; set; } = 1.0f;

	// Tras un golpe que hace daño, el enemigo retrocede RecoilDistance metros en RecoilDuration segundos
	// (0 = sin retroceso). Durante el retroceso no persigue ni hace daño por contacto.
	// Existe para deshacer un atasco: si el jugador está en una esquina y el enemigo en la diagonal, ninguno de los
	// dos puede moverse y el jugador no puede apuntarle (solo dispara en 4 direcciones). Un empuje del jugador
	// "lejos del enemigo" no sirve: en la esquina apunta a las paredes (ver tests/CornerTrapTest.cs).
	[Export] public float RecoilDistance { get; set; } = 1.0f;
	[Export] public float RecoilDuration { get; set; } = 0.15f;

	private HealthComponent _health;
	private Node3D _player;
	private float _hitTimer;
	private Vector3 _recoilVelocity;
	private int _recoilFrames;

	public override void _Ready()
	{
		_health = HealthComponent.FindIn(this);
		_health.Died += OnDied;
	}

	public override void _PhysicsProcess(double delta)
	{
		_hitTimer = Mathf.Max(0.0f, _hitTimer - (float)delta);

		if (_recoilFrames > 0)
		{
			_recoilFrames--;
			Velocity = _recoilVelocity;
			MoveAndSlide();
			return;
		}

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

		bool damaged = playerHealth.TakeHit(ContactDamage);
		_hitTimer = HitCooldown;

		if (damaged && RecoilDistance > 0.0f)
		{
			StartRecoil(target.GlobalPosition);
		}
	}

	// La duración se redondea a un número entero de pasos de física, igual que en el simulador. Se mueve con
	// MoveAndSlide, así que respeta paredes, puertas cerradas y otros cuerpos.
	private void StartRecoil(Vector3 playerPosition)
	{
		Vector3 away = GlobalPosition - playerPosition;
		away.Y = 0.0f;
		if (away.LengthSquared() < 0.000001f)
		{
			away = Vector3.Right;
		}

		float step = 1.0f / Engine.PhysicsTicksPerSecond;
		_recoilFrames = Mathf.Max(1, Mathf.CeilToInt(RecoilDuration / step - 0.0001f));
		_recoilVelocity = away.Normalized() * (RecoilDistance / (_recoilFrames * step));
	}

	private void OnDied()
	{
		QueueFree();
	}
}
