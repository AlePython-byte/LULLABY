using Godot;

/// <summary>
/// Vuelo errático y determinista: velocidad hacia el jugador (Speed) más una oscilación lateral senoidal
///   v = adelante · Speed + lado · Amplitude · sin(2π · Frequency · t + fase)
/// donde "adelante" es la dirección al jugador en el plano, "lado" su perpendicular, t el tiempo de vuelo acumulado y
/// la fase sale de la posición de aparición (PhaseFor). No hay azar.
///
/// El Volador choca con las paredes y con el jugador, pero no con otros enemigos (su máscara es World + Player). Cuando
/// existan obstáculos dentro de las salas (rocas, pozos...) estarán en otra capa y los ignorará: vuela por encima.
/// </summary>
public partial class FlyPatternBehavior : EnemyBehavior
{
	[Export] public float Amplitude { get; set; } = 3.0f;
	[Export] public float Frequency { get; set; } = 1.2f;

	private bool _started;

	public float Time { get; private set; }
	public float Phase { get; private set; }

	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		if (!_started)
		{
			_started = true;
			Phase = PhaseFor(enemy.GlobalPosition);
		}

		velocity = PatternVelocity(PlanarToPlayer(enemy, player), Time, enemy.Speed * enemy.SpeedScale, Amplitude, Frequency, Phase);
		Time += delta;
		return true;
	}

	/// <summary>La velocidad del patrón; pública para poder reproducirla fuera (pruebas, futuro simulador).</summary>
	public static Vector3 PatternVelocity(Vector3 toPlayer, float time, float speed, float amplitude, float frequency, float phase)
	{
		toPlayer.Y = 0.0f;
		if (toPlayer.LengthSquared() < 0.0001f)
		{
			return Vector3.Zero;
		}

		Vector3 forward = toPlayer.Normalized();
		Vector3 side = new(-forward.Z, 0.0f, forward.X);
		return forward * speed + side * (amplitude * Mathf.Sin(Mathf.Tau * frequency * time + phase));
	}

	/// <summary>Fase de la oscilación a partir de la posición de aparición: dos voladores en sitios distintos no van a la par.</summary>
	public static float PhaseFor(Vector3 spawn)
	{
		return Mathf.PosMod(spawn.X * 12.9898f + spawn.Z * 78.233f, Mathf.Tau);
	}
}
