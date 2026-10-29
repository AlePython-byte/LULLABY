using Godot;

/// <summary>
/// Mantiene una distancia al jugador: se acerca si está a más de PreferredDistance + Tolerance, se aleja si está a menos
/// de PreferredDistance - Tolerance y se queda quieto dentro de esa banda.
///
/// Si una pared o una esquina no le deja moverse (su velocidad real queda por debajo de BlockedFraction de la deseada),
/// se desplaza en perpendicular a la dirección del jugador: elige el lado que se aleja de la pared con la que chocó
/// (normal de la colisión; si empatan, la izquierda) y mantiene ese sentido al menos StrafeTime segundos y mientras siga
/// demasiado cerca del jugador, hasta MaxStrafeTime. Si también queda bloqueado de lado, invierte el sentido. Así sale
/// de las esquinas en vez de volver a meterse en ellas al intentar alejarse. Todo es determinista.
///
/// Si otro comportamiento anterior decide la velocidad de un fotograma (la embestida del Huidor), AfterMove no evalúa el
/// bloqueo en ese fotograma: no fue su movimiento. Con el Tirador, que solo tiene este, no cambia nada.
/// </summary>
public partial class KeepDistanceBehavior : EnemyBehavior
{
	[Export] public float PreferredDistance { get; set; } = 6.0f;
	[Export] public float Tolerance { get; set; } = 1.0f;
	[Export] public float BlockedFraction { get; set; } = 0.25f;
	[Export] public float StrafeTime { get; set; } = 0.6f;
	[Export] public float MaxStrafeTime { get; set; } = 3.0f;

	private Vector3 _wanted;
	private Vector3 _strafeDirection;
	private bool _strafing;
	private float _strafeElapsed;
	private bool _decidedThisFrame;

	/// <summary>Cuántas veces ha empezado a desplazarse en perpendicular por estar bloqueado (diagnóstico y pruebas).</summary>
	public int StrafeCount { get; private set; }

	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		_decidedThisFrame = true;
		float speed = enemy.Speed * enemy.SpeedScale;
		Vector3 toPlayer = PlanarToPlayer(enemy, player);
		float distance = toPlayer.Length();
		bool tooClose = distance < PreferredDistance - Tolerance;

		if (_strafing)
		{
			_strafeElapsed += delta;
			if (_strafeElapsed < StrafeTime || (tooClose && _strafeElapsed < MaxStrafeTime))
			{
				_wanted = velocity = _strafeDirection * speed;
				return true;
			}

			_strafing = false;
		}

		if (distance < 0.0001f)
		{
			_wanted = velocity = Vector3.Zero;
			return true;
		}

		Vector3 direction = toPlayer / distance;
		if (tooClose)
		{
			velocity = -direction * speed;
		}
		else if (distance > PreferredDistance + Tolerance)
		{
			velocity = direction * speed;
		}
		else
		{
			velocity = Vector3.Zero;
		}

		_wanted = velocity;
		return true;
	}

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		bool decided = _decidedThisFrame;
		_decidedThisFrame = false;
		if (!decided || _wanted == Vector3.Zero)
		{
			return;
		}

		Vector3 real = enemy.GetRealVelocity();
		real.Y = 0.0f;
		if (real.Length() >= _wanted.Length() * BlockedFraction)
		{
			return;
		}

		if (_strafing)
		{
			_strafeDirection = -_strafeDirection;
			return;
		}

		Vector3 toPlayer = PlanarToPlayer(enemy, player);
		if (toPlayer.LengthSquared() < 0.0001f)
		{
			return;
		}

		Vector3 forward = toPlayer.Normalized();
		Vector3 left = new(-forward.Z, 0.0f, forward.X);
		KinematicCollision3D collision = enemy.GetLastSlideCollision();
		Vector3 normal = collision?.GetNormal() ?? Vector3.Zero;
		normal.Y = 0.0f;

		_strafeDirection = left.Dot(normal) >= 0.0f ? left : -left;
		_strafing = true;
		_strafeElapsed = 0.0f;
		StrafeCount++;
	}
}
