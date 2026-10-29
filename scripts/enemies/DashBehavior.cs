using Godot;

/// <summary>
/// Embestida telegrafiada (la usa el Huidor para hostigar desde lejos). Ciclo determinista:
///   1. Espera: tras FirstDelay (la primera vez) o Interval (después), en cuanto el jugador está entre MinDistance y
///      MaxDistance empieza el aviso.
///   2. Aviso: WarnTime segundos quieto y parpadeando. La DIRECCIÓN se fija al empezar el aviso (hacia donde estaba el
///      jugador en ese instante), así que la embestida se puede esquivar apartándose durante el aviso.
///   3. Embestida: en línea recta a DashSpeed durante DashTime como mucho. Termina antes si choca con el jugador (el daño
///      y el retroceso son los del contacto de EnemyBase) o si algo la frena (pared, otro enemigo).
/// Debe ir ANTES del comportamiento de movimiento en el árbol: mientras avisa o embiste, decide la velocidad; el resto
/// del tiempo devuelve false y manda el siguiente (KeepDistanceBehavior en el Huidor).
/// </summary>
public partial class DashBehavior : EnemyBehavior
{
	[Signal] public delegate void WarningStartedEventHandler(Vector3 direction);
	[Signal] public delegate void DashStartedEventHandler(Vector3 direction);
	[Signal] public delegate void DashEndedEventHandler(bool hitPlayer);

	public enum DashState
	{
		Waiting,
		Warning,
		Dashing,
	}

	[Export] public float FirstDelay { get; set; } = 1.5f;
	[Export] public float Interval { get; set; } = 2.5f;
	[Export] public float WarnTime { get; set; } = 0.35f;
	[Export] public float DashSpeed { get; set; } = 9.0f;
	[Export] public float DashTime { get; set; } = 0.45f;
	[Export] public float MinDistance { get; set; } = 3.0f;
	[Export] public float MaxDistance { get; set; } = 6.5f;
	[Export] public float BlockedFraction { get; set; } = 0.25f;

	private float _cooldown = float.NaN;
	private float _timer;
	private Vector3 _direction;

	public DashState State { get; private set; } = DashState.Waiting;

	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		velocity = Vector3.Zero;
		if (float.IsNaN(_cooldown))
		{
			_cooldown = FirstDelay;
		}

		switch (State)
		{
			case DashState.Waiting:
				_cooldown -= delta;
				Vector3 toPlayer = PlanarToPlayer(enemy, player);
				float distance = toPlayer.Length();
				if (_cooldown > 0.0f || distance < MinDistance || distance > MaxDistance || distance < 0.0001f)
				{
					return false;
				}

				State = DashState.Warning;
				_timer = WarnTime;
				_direction = toPlayer / distance;
				enemy.Visual?.SetHighlight(true);
				EmitSignal(SignalName.WarningStarted, _direction);
				return true;

			case DashState.Warning:
				_timer -= delta;
				if (_timer <= 0.0f)
				{
					// La embestida ya se mueve en este fotograma (si no, AfterMove la tomaría por frenada).
					State = DashState.Dashing;
					_timer = DashTime;
					enemy.Visual?.SetHighlight(false);
					EmitSignal(SignalName.DashStarted, _direction);
					velocity = _direction * DashSpeed;
				}

				return true;

			default:
				_timer -= delta;
				velocity = _direction * DashSpeed;
				return true;
		}
	}

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		if (State != DashState.Dashing)
		{
			return;
		}

		bool hitPlayer = false;
		for (int i = 0; i < enemy.GetSlideCollisionCount(); i++)
		{
			hitPlayer |= enemy.GetSlideCollision(i).GetCollider() is Node3D collider && collider.IsInGroup("player");
		}

		Vector3 real = enemy.GetRealVelocity();
		real.Y = 0.0f;
		bool blocked = real.Length() < DashSpeed * BlockedFraction;
		if (hitPlayer || blocked || _timer <= 0.0f)
		{
			State = DashState.Waiting;
			_cooldown = Interval;
			EmitSignal(SignalName.DashEnded, hitPlayer);
		}
	}
}
