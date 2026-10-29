using Godot;

/// <summary>
/// Mecha y explosión. Cuando el jugador está a TriggerRadius o menos, enciende la mecha: el enemigo se mueve a
/// FuseSpeedScale de su velocidad y su parte gráfica parpadea. A los FuseTime segundos explota: si el jugador está a
/// ExplosionRadius o menos recibe ExplosionDamage (respetando su invulnerabilidad), y el enemigo muere (emite Died, así
/// la sala lo cuenta). La explosión solo daña al jugador. Si lo matan antes, no explota ni hace daño.
/// </summary>
public partial class FuseBehavior : EnemyBehavior
{
	[Signal] public delegate void FuseLitEventHandler();
	[Signal] public delegate void ExplodedEventHandler(bool hitPlayer);

	[Export] public float TriggerRadius { get; set; } = 2.0f;
	[Export] public float FuseTime { get; set; } = 0.8f;
	[Export] public float FuseSpeedScale { get; set; } = 0.3f;
	[Export] public float ExplosionRadius { get; set; } = 2.5f;
	[Export] public float ExplosionDamage { get; set; } = 2.0f;
	[Export] public float BlinkInterval { get; set; } = 0.1f;
	[Export] public PackedScene ExplosionEffect { get; set; }

	private bool _lit;
	private bool _exploded;
	private float _timer;

	public bool IsLit => _lit;

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		if (_exploded)
		{
			return;
		}

		float distance = PlanarToPlayer(enemy, player).Length();
		if (!_lit)
		{
			if (distance <= TriggerRadius)
			{
				_lit = true;
				_timer = FuseTime;
				enemy.SpeedScale = FuseSpeedScale;
				enemy.Visual?.SetHighlight(true); // destello al encender; el parpadeo sigue desde el fotograma siguiente
				EmitSignal(SignalName.FuseLit);
			}

			return;
		}

		_timer -= delta;
		enemy.Visual?.SetHighlight((int)(_timer / BlinkInterval) % 2 == 0);
		if (_timer <= 0.0f)
		{
			Explode(enemy, player, distance);
		}
	}

	private void Explode(EnemyBase enemy, Node3D player, float distance)
	{
		_exploded = true;
		bool hitPlayer = false;
		if (distance <= ExplosionRadius)
		{
			hitPlayer = player.GetNodeOrNull<PlayerHealth>("PlayerHealth")?.TakeHit(ExplosionDamage) ?? false;
		}

		if (ExplosionEffect != null)
		{
			ExplosionEffect effect = ExplosionEffect.Instantiate<ExplosionEffect>();
			effect.Radius = ExplosionRadius;
			enemy.GetParent().AddChild(effect);
			effect.GlobalPosition = enemy.GlobalPosition;
		}

		EmitSignal(SignalName.Exploded, hitPlayer);
		enemy.Kill();
	}
}
