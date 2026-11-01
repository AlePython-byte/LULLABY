using System;
using Godot;

/// <summary>
/// Una fase de combate: pausa inicial, tres disparos, embestida y recuperación. Solo ejecuta un ataque a la vez.
/// Con la vida baja pasa a la fase 2 (66 %) y a la 3 (33 %): órbita entre ataques, anillo de proyectiles tras la embestida
/// y, en la 3, embestidas encadenadas y una espiral (MotherSpiral) que alterna con el anillo. Cada cambio de fase la deja quieta e invulnerable PhaseChangeTime segundos.
/// </summary>
public partial class MotherBehavior : EnemyBehavior
{
	public enum AttackState { Entrance, Volley, BeforeDash, Approach, Dash, Recovery, Orbit, RingWarning, PhaseChange, SpiralWarning, Spiral }
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
	[Export] public float PhaseChangeTime { get; set; } = 1.2f;
	[Export] public float PhaseChangeRecovery { get; set; } = 0.5f;
	[Export] public float RingWarning { get; set; } = 0.6f;
	[Export] public float RingSpawnDistance { get; set; } = 2.4f;
	[Export] public float RingSafeDistance { get; set; } = 1.2f;
	[Export] public float OrbitRadius { get; set; } = 6.0f;
	[Export] public float OrbitSpeed { get; set; } = 2.5f;

	public AttackState State { get; private set; } = AttackState.Entrance;
	public int Phase { get; private set; } = 1;
	public Vector3 DashDirection { get; private set; }
	public Vector3 RingGapDirection { get; private set; } = Vector3.Forward;
	public bool ShotWarningActive => State == AttackState.Volley && _shotWarning;
	public bool DashWarningActive => State == AttackState.Dash && _dash?.State == DashBehavior.DashState.Warning;
	public bool RingWarningActive => State == AttackState.RingWarning;
	public bool PhaseChangeActive => State == AttackState.PhaseChange;
	public bool SpiralWarningActive => State == AttackState.SpiralWarning;
	public bool SpiralActive => State == AttackState.Spiral;

	/// <summary>Centro de uno de los tres huecos de la espiral en curso (fijado al empezar su aviso) y si gira en sentido contrario.</summary>
	public Vector3 SpiralGapDirection { get; private set; } = Vector3.Forward;
	public bool SpiralReverse { get; private set; }
	public Vector2 ArenaSize => _room?.Data.Size ?? FallbackRoomSize;

	/// <summary>Se emite al entrar en una fase nueva (2 o 3), al empezar su cambio.</summary>
	public event Action<int> PhaseChanged;

	private ShootBehavior _shoot;
	private DashBehavior _dash;
	private ChaseBehavior _chase;
	private RoomController _room;
	private HealthComponent _health;
	private float _timer;
	private int _shots;
	private int _dashesDone;
	private int _orbitDirection = -1;
	private int _ringSide = -1;
	private bool _ringGapSet;
	private int _attacksInPhase;
	private int _spiralSide = -1;
	private bool _spiralGapSet;
	private float _spiralTime;
	private int _spiralEmitted;
	private bool _shotWarning;
	private Vector3 _dashOrigin;

	private MotherPhases.Settings Settings => MotherPhases.For(Phase);

	public override void _Ready()
	{
		_timer = EntrancePause;
		_chase = new ChaseBehavior();
		AddChild(_chase);
		for (Node node = GetParent(); node != null; node = node.GetParent())
			if (node is RoomController room) { _room = room; break; }
		_health = HealthComponent.FindIn(GetParent());
		if (_health != null) _health.HealthChanged += OnHealthChanged;
	}

	public override void _ExitTree()
	{
		if (!IsInstanceValid(_health)) return;
		_health.HealthChanged -= OnHealthChanged;
		_health.Invulnerable = false;
	}

	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		velocity = Vector3.Zero;
		if (enemy.Health.IsDead) return true;
		Vector3 local = _room != null ? _room.ToLocal(enemy.GlobalPosition) : enemy.Position;
		switch (State)
		{
			case AttackState.Entrance:
			case AttackState.BeforeDash:
			case AttackState.Recovery:
				_timer -= delta;
				if (_timer <= 0.0f)
				{
					if (State == AttackState.BeforeDash) BeginApproach();
					else BeginVolley();
				}
				break;
			case AttackState.PhaseChange:
				_timer -= delta;
				if (_timer <= 0.0f) EndPhaseChange();
				break;
			case AttackState.Orbit:
				_timer -= delta;
				if (_timer <= 0.0f) BeginApproach();
				else velocity = MotherArena.OrbitVelocity(local, OrbitRadius, OrbitSpeed, _orbitDirection);
				break;
			case AttackState.RingWarning:
				if (!_ringGapSet)
				{
					RingGapDirection = MotherRing.GapCenter(PlanarToPlayer(enemy, player), Settings.RingGapOffsetDegrees * _ringSide);
					_ringGapSet = true;
				}
				_timer -= delta;
				if (_timer <= 0.0f) FireRing(enemy, player);
				break;
			case AttackState.SpiralWarning:
				if (!_spiralGapSet)
				{
					SpiralGapDirection = MotherSpiral.GapCenter(PlanarToPlayer(enemy, player), _spiralSide);
					SpiralReverse = _spiralSide < 0;
					_spiralGapSet = true;
				}
				_timer -= delta;
				if (_timer <= 0.0f) BeginSpiral();
				break;
			case AttackState.Spiral:
				_spiralTime += delta;
				FireSpiral(enemy, player);
				if (_spiralEmitted >= MotherSpiral.Emissions) EndSpiral();
				break;
			case AttackState.Approach:
				if (PlanarToPlayer(enemy, player).Length() <= DashTriggerDistance) BeginDash();
				else _chase.TryGetVelocity(enemy, player, delta, out velocity);
				break;
			case AttackState.Dash:
				_dash.TryGetVelocity(enemy, player, delta, out velocity);
				if (_dash.State == DashBehavior.DashState.Dashing && delta > 0.0f)
				{
					float remaining = Mathf.Max(0.0f, DashDistance - enemy.GlobalPosition.DistanceTo(_dashOrigin));
					velocity = velocity.LimitLength(remaining / delta);
				}
				break;
		}
		velocity = MotherArena.LimitVelocity(local, velocity, ArenaSize, ArenaMargin, delta);
		return true;
	}

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		if (enemy.Health.IsDead) return;
		if (State == AttackState.Volley) _shoot?.AfterMove(enemy, player, delta);
		else if (State == AttackState.Dash) _dash?.AfterMove(enemy, player, delta);
	}

	/// <summary>Valor exportado en la fase 1 y el de la fase en curso en las demás.</summary>
	private float Pick(float firstPhaseValue, float laterPhaseValue) => Phase == 1 ? firstPhaseValue : laterPhaseValue;

	private void OnHealthChanged(float current, float maximum)
	{
		if (current <= 0.0f || State == AttackState.PhaseChange) return;
		int target = MotherPhases.PhaseFor(current, maximum);
		if (target > Phase) BeginPhaseChange(target);
	}

	private void BeginPhaseChange(int phase)
	{
		EnemyBase enemy = GetParent<EnemyBase>();
		CancelAttacks(enemy);
		Phase = phase;
		_attacksInPhase = 0;
		State = AttackState.PhaseChange;
		_timer = PhaseChangeTime;
		_health.Invulnerable = true;
		PhaseChanged?.Invoke(phase);
		try { AudioManager.PlayFrom(SoundCatalog.BossPhase, enemy); }
		catch (Exception exception) { GD.PushWarning($"The Mother: sonido de fase: {exception.Message}"); }
	}

	private void EndPhaseChange()
	{
		_health.Invulnerable = false;
		State = AttackState.Recovery;
		_timer = PhaseChangeRecovery;
	}

	// Corta el ataque en curso: el aviso y los proyectiles ya lanzados no se deshacen (los lanzados siguen siendo esquivables).
	private void CancelAttacks(EnemyBase enemy)
	{
		if (_shoot != null) { RemoveChild(_shoot); _shoot.QueueFree(); _shoot = null; }
		if (_dash != null) { RemoveChild(_dash); _dash.QueueFree(); _dash = null; }
		_shotWarning = false;
		_ringGapSet = false;
		_spiralGapSet = false;
		enemy.Visual?.SetHighlight(false);
	}

	private void BeginVolley()
	{
		if (_shoot != null) { RemoveChild(_shoot); _shoot.QueueFree(); }
		_shots = 0;
		_shotWarning = false;
		_shoot = new ShootBehavior
		{
			ProjectileScene = ProjectileScene, WarnTime = ShotWarning,
			FireInterval = Pick(ShotInterval, Settings.ShotInterval), FirstShotDelay = 0.0f, SpawnDistance = ShotSpawnDistance,
		};
		_shoot.WarningStarted += () => _shotWarning = true;
		_shoot.Fired += _ =>
		{
			_shotWarning = false;
			if (++_shots >= Mathf.Max(1, VolleyShots)) EndVolley();
		};
		AddChild(_shoot);
		State = AttackState.Volley;
	}

	private void EndVolley()
	{
		if (Phase >= 2)
		{
			State = AttackState.Orbit;
			_timer = Settings.OrbitTime;
			_orbitDirection = -_orbitDirection;
		}
		else
		{
			State = AttackState.BeforeDash;
			_timer = BeforeDashPause;
		}
	}

	private void BeginApproach()
	{
		_dashesDone = 0;
		State = AttackState.Approach;
	}

	private void BeginDash()
	{
		if (_dash != null) { RemoveChild(_dash); _dash.QueueFree(); }
		float warning = Phase == 1 ? DashWarning : _dashesDone == 0 ? Settings.DashWarning : Settings.ChainDashWarning;
		_dash = new DashBehavior
		{
			FirstDelay = 0.0f, WarnTime = warning, DashSpeed = DashSpeed,
			DashTime = DashDistance / Mathf.Max(0.01f, DashSpeed), MinDistance = 0.0f,
			MaxDistance = float.MaxValue,
		};
		_dash.WarningStarted += direction => DashDirection = direction;
		_dash.DashStarted += _ => _dashOrigin = GetParent<Node3D>().GlobalPosition;
		_dash.DashEnded += _ => OnDashEnded();
		AddChild(_dash);
		State = AttackState.Dash;
	}

	private void OnDashEnded()
	{
		_dashesDone++;
		if (_dashesDone < Settings.DashCount)
		{
			// Embestida encadenada: otra vez Approach (con Noah a tiro ataca de inmediato) y un aviso nuevo.
			State = AttackState.Approach;
		}
		else if (Phase >= 2)
		{
			// En la fase 3 el anillo y la espiral se alternan: primero el anillo tras el cambio de fase, luego la espiral, y así.
			bool spiral = Phase >= 3 && _attacksInPhase % 2 == 1;
			_attacksInPhase++;
			if (spiral)
			{
				State = AttackState.SpiralWarning;
				_timer = MotherSpiral.Warning;
				_spiralSide = -_spiralSide;
				_spiralGapSet = false;
				return;
			}

			State = AttackState.RingWarning;
			_timer = RingWarning;
			_ringSide = -_ringSide;
			_ringGapSet = false;
		}
		else
		{
			State = AttackState.Recovery;
			_timer = RecoveryTime;
		}
	}

	private void BeginSpiral()
	{
		_spiralTime = 0.0f;
		_spiralEmitted = 0;
		State = AttackState.Spiral;
	}

	// Emite las tandas que ya tocan (una por brazo cada MotherSpiral.Interval). Como el anillo, no dispara lo que nacería sobre Noah.
	private void FireSpiral(EnemyBase enemy, Node3D player)
	{
		while (_spiralEmitted < MotherSpiral.Emissions && _spiralTime >= _spiralEmitted * MotherSpiral.Interval)
		{
			if (ProjectileScene != null)
			{
				Vector3 origin = enemy.GlobalPosition;
				Vector3 target = player.GlobalPosition;
				foreach (Vector3 direction in MotherSpiral.Directions(_spiralEmitted, SpiralGapDirection, SpiralReverse))
				{
					if (MotherRing.SpawnsOnTarget(origin, direction, RingSpawnDistance, target, RingSafeDistance)) continue;
					EnemyProjectile.FireFrom(enemy, ProjectileScene, direction, RingSpawnDistance, MotherSpiral.ProjectileSpeed);
				}
			}

			if (_spiralEmitted % 2 == 0) AudioManager.PlayFrom(SoundCatalog.ShooterShot, this);
			_spiralEmitted++;
		}
	}

	private void EndSpiral()
	{
		_spiralGapSet = false;
		State = AttackState.Recovery;
		_timer = Settings.RecoveryTime;
	}

	// Dispara el anillo. Los proyectiles que nacerían encima de Noah no se disparan, así que nada aparece sobre él.
	private void FireRing(EnemyBase enemy, Node3D player)
	{
		Vector3 origin = enemy.GlobalPosition;
		Vector3 target = player.GlobalPosition;
		MotherPhases.Settings settings = Settings;
		if (ProjectileScene != null)
		{
			foreach (Vector3 direction in MotherRing.Directions(settings.RingProjectiles, RingGapDirection, settings.RingGapDegrees))
			{
				if (MotherRing.SpawnsOnTarget(origin, direction, RingSpawnDistance, target, RingSafeDistance)) continue;
				EnemyProjectile.FireFrom(enemy, ProjectileScene, direction, RingSpawnDistance);
			}
		}
		AudioManager.PlayFrom(SoundCatalog.ShooterShot, this);
		_ringGapSet = false;
		State = AttackState.Recovery;
		_timer = settings.RecoveryTime;
	}
}
