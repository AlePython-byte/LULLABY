using Godot;

/// <summary>
/// Qué hizo el último daño que recibió Noah (y, por tanto, qué la mató). Sirve solo para mostrarlo en la pantalla de derrota: no
/// cambia ningún daño ni ninguna regla del juego, y el simulador no lo usa.
/// Los valores se añaden al final; Unknown es el de los golpes que no dicen quién los da (por ejemplo, el daño directo de las pruebas).
/// </summary>
public enum DeathCause
{
	Unknown = 0,
	Chaser,
	Shooter,
	Projectile,
	Exploder,
	Flyer,
	Runner,
	Digger,
	TheMother,
}

/// <summary>Cómo se averigua la causa de un golpe a partir de quien lo da.</summary>
public static class DeathCauses
{
	private static readonly StringName BossesGroup = "bosses";

	/// <summary>Causa de un golpe de contacto o explosión de este enemigo: The Mother si es un jefe; si no, su tipo.</summary>
	public static DeathCause FromEnemy(EnemyBase enemy)
	{
		if (enemy == null)
		{
			return DeathCause.Unknown;
		}

		if (enemy.IsInGroup(BossesGroup))
		{
			return DeathCause.TheMother;
		}

		return enemy.Kind switch
		{
			EnemyKind.Chaser => DeathCause.Chaser,
			EnemyKind.Shooter => DeathCause.Shooter,
			EnemyKind.Exploder => DeathCause.Exploder,
			EnemyKind.Flyer => DeathCause.Flyer,
			EnemyKind.Runner => DeathCause.Runner,
			EnemyKind.Digger => DeathCause.Digger,
			_ => DeathCause.Unknown,
		};
	}

	/// <summary>Causa de un proyectil disparado por este enemigo: The Mother, el excavador o el tirador; un proyectil sin dueño conocido es solo un proyectil.</summary>
	public static DeathCause ForProjectile(EnemyBase owner)
	{
		DeathCause cause = FromEnemy(owner);
		return cause is DeathCause.TheMother or DeathCause.Digger or DeathCause.Shooter ? cause : DeathCause.Projectile;
	}
}
