using Godot;

public partial class PlayerShooter : Node3D
{
	public const string ShootLeft = "shoot_left";
	public const string ShootRight = "shoot_right";
	public const string ShootUp = "shoot_up";
	public const string ShootDown = "shoot_down";

	// StringName propios: pasar el texto a Input.IsActionPressed crearía uno nuevo en cada fotograma.
	private static readonly StringName ShootLeftName = new(ShootLeft);
	private static readonly StringName ShootRightName = new(ShootRight);
	private static readonly StringName ShootUpName = new(ShootUp);
	private static readonly StringName ShootDownName = new(ShootDown);

	// Orden fijo: izquierda, derecha, arriba (-Z), abajo (+Z).
	public static readonly StringName[] Actions = { ShootLeftName, ShootRightName, ShootUpName, ShootDownName };

	[Export] public PackedScene TearScene { get; set; }
	[Export] public float ShotsPerSecond { get; set; } = 3.0f;
	[Export] public float SpawnDistance { get; set; } = 0.6f;

	public float DamageMultiplier { get; private set; } = 1.0f;
	public float CadenceMultiplier { get; private set; } = 1.0f;
	public float EffectiveShotsPerSecond => ShotsPerSecond * CadenceMultiplier;

	private Node3D _player;
	private StringName _activeAction;
	private float _cooldown;

	public override void _Ready()
	{
		_player = GetParent<Node3D>();

		InputActions.Register(ShootLeft, Key.Left);
		InputActions.Register(ShootRight, Key.Right);
		InputActions.Register(ShootUp, Key.Up);
		InputActions.Register(ShootDown, Key.Down);
	}

	public override void _PhysicsProcess(double delta)
	{
		_cooldown = Mathf.Max(0.0f, _cooldown - (float)delta);

		UpdateActiveAction();

		if (_activeAction != null && _cooldown <= 0.0f)
		{
			Fire(DirectionOf(_activeAction));
			_cooldown = 1.0f / EffectiveShotsPerSecond;
		}
	}

	/// <summary>Modificadores de esta instancia; ShotsPerSecond y TearScene conservan los datos base.</summary>
	public void SetItemModifiers(float damage, float cadence)
	{
		if (!float.IsFinite(damage) || damage <= 0.0f) throw new System.ArgumentOutOfRangeException(nameof(damage));
		if (!float.IsFinite(cadence) || cadence <= 0.0f) throw new System.ArgumentOutOfRangeException(nameof(cadence));
		DamageMultiplier = damage;
		CadenceMultiplier = cadence;
	}

	public void ResetItemModifiers()
	{
		SetItemModifiers(1.0f, 1.0f);
		_cooldown = 0.0f;
		_activeAction = null;
	}

	// Con varias flechas pulsadas manda la última; al soltarla vuelve a otra que siga pulsada.
	private void UpdateActiveAction()
	{
		foreach (StringName action in Actions)
		{
			if (Input.IsActionJustPressed(action))
			{
				_activeAction = action;
			}
		}

		if (_activeAction != null && !Input.IsActionPressed(_activeAction))
		{
			_activeAction = null;
			foreach (StringName action in Actions)
			{
				if (Input.IsActionPressed(action))
				{
					_activeAction = action;
					break;
				}
			}
		}
	}

	private void Fire(Vector3 direction)
	{
		Tear tear = TearScene.Instantiate<Tear>();
		tear.Damage *= DamageMultiplier;
		_player.GetParent().AddChild(tear);
		tear.GlobalPosition = _player.GlobalPosition + direction * SpawnDistance;
		tear.Launch(direction);
		AudioManager.Play(SoundCatalog.TearShot);
	}

	private static Vector3 DirectionOf(StringName action)
	{
		if (action == ShootLeftName)
		{
			return Vector3.Left;
		}

		if (action == ShootRightName)
		{
			return Vector3.Right;
		}

		if (action == ShootUpName)
		{
			return Vector3.Forward;
		}

		return Vector3.Back;
	}
}
