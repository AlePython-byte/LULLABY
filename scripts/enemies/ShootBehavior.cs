using Godot;

/// <summary>
/// Dispara proyectiles al jugador. Ciclo determinista: espera, aviso visual de WarnTime segundos (Visual.SetHighlight)
/// y disparo hacia la posición del jugador en ese instante. FireInterval es el tiempo entre disparos (incluye el
/// aviso); el primer aviso empieza tras FirstShotDelay. El proyectil (ProjectileScene) define su velocidad, daño y vida.
/// </summary>
public partial class ShootBehavior : EnemyBehavior
{
	[Signal] public delegate void WarningStartedEventHandler();
	[Signal] public delegate void FiredEventHandler(Vector3 direction);

	[Export] public PackedScene ProjectileScene { get; set; }
	[Export] public float FireInterval { get; set; } = 2.0f;
	[Export] public float WarnTime { get; set; } = 0.5f;
	[Export] public float FirstShotDelay { get; set; } = 1.0f;
	[Export] public float SpawnDistance { get; set; } = 0.7f;

	private float _cooldown = float.NaN;
	private float _warnTimer;
	private bool _warning;

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		if (float.IsNaN(_cooldown))
		{
			_cooldown = FirstShotDelay;
		}

		if (_warning)
		{
			_warnTimer -= delta;
			if (_warnTimer <= 0.0f)
			{
				_warning = false;
				enemy.Visual?.SetHighlight(false);
				Fire(enemy, player);
				_cooldown = Mathf.Max(0.0f, FireInterval - WarnTime);
			}

			return;
		}

		_cooldown -= delta;
		if (_cooldown <= 0.0f)
		{
			_warning = true;
			_warnTimer = WarnTime;
			enemy.Visual?.SetHighlight(true);
			EmitSignal(SignalName.WarningStarted);
		}
	}

	private void Fire(EnemyBase enemy, Node3D player)
	{
		Vector3 toPlayer = PlanarToPlayer(enemy, player);
		Vector3 direction = toPlayer.LengthSquared() > 0.0001f ? toPlayer.Normalized() : Vector3.Forward;

		EnemyProjectile.FireFrom(enemy, ProjectileScene, direction, SpawnDistance);
		EmitSignal(SignalName.Fired, direction);
	}
}
