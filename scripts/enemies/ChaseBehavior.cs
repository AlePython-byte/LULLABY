using Godot;

/// <summary>Persigue al jugador en línea recta por el plano XZ a Speed × SpeedScale.</summary>
public partial class ChaseBehavior : EnemyBehavior
{
	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		Vector3 toPlayer = PlanarToPlayer(enemy, player);
		velocity = toPlayer.LengthSquared() > 0.0001f
			? toPlayer.Normalized() * (enemy.Speed * enemy.SpeedScale)
			: Vector3.Zero;
		return true;
	}
}
