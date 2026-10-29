using Godot;

/// <summary>
/// Un comportamiento de enemigo: un nodo hijo de EnemyBase con su propio script. EnemyBase los llama en orden de
/// árbol, una vez por fotograma de física; los comportamientos no tienen _PhysicsProcess propio, así el orden de
/// ejecución es fijo y determinista.
///   - TryGetVelocity: el primer comportamiento que devuelve true decide la velocidad del fotograma.
///   - AfterMove: se llama a todos después de MoveAndSlide (disparar, mecha, comprobar si está bloqueado...).
/// </summary>
public abstract partial class EnemyBehavior : Node
{
	public virtual bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		velocity = Vector3.Zero;
		return false;
	}

	public virtual void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
	}

	/// <summary>Vector en el plano XZ del enemigo al jugador.</summary>
	protected static Vector3 PlanarToPlayer(EnemyBase enemy, Node3D player)
	{
		Vector3 toPlayer = player.GlobalPosition - enemy.GlobalPosition;
		toPlayer.Y = 0.0f;
		return toPlayer;
	}
}
