using Godot;

/// <summary>Una fase: pausa inicial, tres disparos, embestida y recuperación. Solo ejecuta un ataque a la vez.</summary>
public partial class MotherBehavior : EnemyBehavior
{
	public enum AttackState { Entrance, Volley, BeforeDash, Approach, Dash, Recovery }
	[Export] public float EntrancePause { get; set; } = 1.5f;
	[Export] public PackedScene ProjectileScene { get; set; }
	[Export] public int VolleyShots { get; set; } = 3;
	[Export] public float ShotWarning { get; set; } = 0.7f;
	[Export] public float ShotInterval { get; set; } = 1.25f;
	[Export] public float ShotSpawnDistance { get; set; } = 1.6f;
	[Export] public float BeforeDashPause { get; set; } = 1.0f;
	[Export] public float DashWarning { get; set; } = 1.0f;
	[Export] public float DashSpeed { get; set; } = 8.0f;
	[Export] public float DashDistance { get; set; } = 6.0f;
	[Export] public float DashTriggerDistance { get; set; } = 18.0f;
	[Export] public float RecoveryTime { get; set; } = 1.5f;
	[Export] public float ArenaMargin { get; set; } = 3.0f;
	[Export] public Vector2 FallbackRoomSize { get; set; } = new(26.0f, 26.0f);

	public AttackState State { get; private set; } = AttackState.Entrance;
	public Vector3 DashDirection { get; private set; }
	public bool ShotWarningActive => State == AttackState.Volley && _shotWarning;
	public bool DashWarningActive => State == AttackState.Dash && _dash?.State == DashBehavior.DashState.Warning;
	public Vector2 ArenaSize => _room?.Data.Size ?? FallbackRoomSize;

	private ShootBehavior _shoot;
	private DashBehavior _dash;
	private ChaseBehavior _chase;
	private RoomController _room;
	private float _timer;
	private int _shots;
	private bool _shotWarning;

	public override void _Ready()
	{
		_timer = EntrancePause;
		_chase = new ChaseBehavior();
		AddChild(_chase);
		for (Node node = GetParent(); node != null; node = node.GetParent())
			if (node is RoomController room) { _room = room; break; }
	}

	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		velocity = Vector3.Zero;
		if (enemy.Health.IsDead) return true;
		switch (State)
		{
			case AttackState.Entrance:
			case AttackState.BeforeDash:
			case AttackState.Recovery:
				_timer -= delta;
				if (_timer <= 0.0f)
				{
					if (State == AttackState.BeforeDash) State = AttackState.Approach;
					else BeginVolley();
				}
				break;
			case AttackState.Approach:
				if (PlanarToPlayer(enemy, player).Length() <= DashTriggerDistance) BeginDash();
				else _chase.TryGetVelocity(enemy, player, delta, out velocity);
				break;
			case AttackState.Dash:
				_dash.TryGetVelocity(enemy, player, delta, out velocity);
				break;
		}
		Vector3 local = _room != null ? _room.ToLocal(enemy.GlobalPosition) : enemy.Position;
		velocity = MotherArena.LimitVelocity(local, velocity, ArenaSize, ArenaMargin, delta);
		return true;
	}

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		if (enemy.Health.IsDead) return;
		if (State == AttackState.Volley) _shoot.AfterMove(enemy, player, delta);
		else if (State == AttackState.Dash) _dash.AfterMove(enemy, player, delta);
	}

	private void BeginVolley()
	{
		if (_shoot != null) { RemoveChild(_shoot); _shoot.QueueFree(); }
		_shots = 0;
		_shotWarning = false;
		_shoot = new ShootBehavior
		{
			ProjectileScene = ProjectileScene, WarnTime = ShotWarning,
			FireInterval = ShotInterval, FirstShotDelay = 0.0f, SpawnDistance = ShotSpawnDistance,
		};
		_shoot.WarningStarted += () => _shotWarning = true;
		_shoot.Fired += _ =>
		{
			_shotWarning = false;
			if (++_shots >= Mathf.Max(1, VolleyShots))
			{
				State = AttackState.BeforeDash;
				_timer = BeforeDashPause;
			}
		};
		AddChild(_shoot);
		State = AttackState.Volley;
	}

	private void BeginDash()
	{
		if (_dash != null) { RemoveChild(_dash); _dash.QueueFree(); }
		_dash = new DashBehavior
		{
			FirstDelay = 0.0f, WarnTime = DashWarning, DashSpeed = DashSpeed,
			DashTime = DashDistance / Mathf.Max(0.01f, DashSpeed), MinDistance = 0.0f,
			MaxDistance = float.MaxValue,
		};
		_dash.WarningStarted += direction => DashDirection = direction;
		_dash.DashEnded += _ => { State = AttackState.Recovery; _timer = RecoveryTime; };
		AddChild(_dash);
		State = AttackState.Dash;
	}
}
