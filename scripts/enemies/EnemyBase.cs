using System.Collections.Generic;
using Godot;

/// <summary>
/// Base de todos los enemigos. Reúne lo común:
///   - vida: un HealthComponent hijo; al morir, el enemigo desaparece (las salas cuentan las muertes con su señal Died);
///   - daño por contacto (ContactDamage, con espera HitCooldown entre golpes; 0 = sin daño por contacto);
///   - retroceso: tras un golpe de contacto que hace daño, retrocede RecoilDistance metros en RecoilDuration segundos,
///     sin perseguir ni golpear (deshace el atasco en esquinas, ver tests/CornerTrapTest.cs);
///   - parte gráfica en el nodo hijo "Visual" (EnemyVisual), que la lógica nunca toca directamente.
/// El movimiento y las acciones salen de los nodos hijo EnemyBehavior, en orden de árbol (ver EnemyBehavior).
///
/// Orden de cada fotograma de física (el mismo que tenía BasicEnemy, del que depende el simulador de desarrollo):
///   1. descuenta la espera entre golpes; 2. si está retrocediendo, se mueve y termina; 3. busca al jugador;
///   4. velocidad del primer comportamiento que la dé; MoveAndSlide; 5. daño por contacto; 6. AfterMove.
/// Todos los números son [Export]. SpeedScale (no exportado) lo usan los comportamientos para frenar al enemigo.
/// </summary>
public partial class EnemyBase : CharacterBody3D
{
	[Export] public EnemyKind Kind { get; set; } = EnemyKind.Chaser;
	[Export] public float Speed { get; set; } = 3.0f;
	[Export] public float ContactDamage { get; set; } = 1.0f;
	[Export] public float HitCooldown { get; set; } = 1.0f;

	// Tras un golpe que hace daño, el enemigo retrocede RecoilDistance metros en RecoilDuration segundos
	// (0 = sin retroceso). Durante el retroceso no persigue ni hace daño por contacto.
	// Existe para deshacer un atasco: si el jugador está en una esquina y el enemigo en la diagonal, ninguno de los
	// dos puede moverse y el jugador no puede apuntarle (solo dispara en 4 direcciones). Un empuje del jugador
	// "lejos del enemigo" no sirve: en la esquina apunta a las paredes (ver tests/CornerTrapTest.cs).
	[Export] public float RecoilDistance { get; set; } = 1.0f;
	[Export] public float RecoilDuration { get; set; } = 0.15f;

	private readonly List<EnemyBehavior> _behaviors = new();
	private Node3D _player;
	private float _hitTimer;
	private Vector3 _recoilVelocity;
	private int _recoilFrames;

	/// <summary>Multiplicador de la velocidad que aplican los comportamientos de movimiento (1 = normal).</summary>
	public float SpeedScale { get; set; } = 1.0f;

	/// <summary>
	/// Familia de sonidos de golpe y muerte: "{SoundSet}_golpe" y "{SoundSet}_muere" (SoundCatalog). "enemigo" por defecto; The Mother usa
	/// "jefe", que mientras no tenga sus archivos suena como los enemigos con el tono más grave.
	/// </summary>
	[Export] public string SoundSet { get; set; } = "enemigo";

	/// <summary>Su muerte no suena (el explosivo ya suena como explosión).</summary>
	public bool MuteDeathSound { get; set; }

	public HealthComponent Health { get; private set; }
	public EnemyVisual Visual { get; private set; }

	public override void _Ready()
	{
		Health = HealthComponent.FindIn(this);
		Health.Died += OnDied;
		Health.Damaged += OnDamagedSound;

		Visual = GetNodeOrNull<EnemyVisual>("Visual");
		Visual?.ApplyKind(Kind);

		foreach (Node child in GetChildren())
		{
			if (child is EnemyBehavior behavior)
			{
				_behaviors.Add(behavior);
			}
		}
	}

	public T Behavior<T>() where T : EnemyBehavior
	{
		foreach (EnemyBehavior behavior in _behaviors)
		{
			if (behavior is T match)
			{
				return match;
			}
		}

		return null;
	}

	/// <summary>Muere por sus propios medios (por ejemplo, al explotar): pasa por la vida, así se emite Died.</summary>
	public void Kill()
	{
		if (!Health.IsDead)
		{
			Health.TakeDamage(Health.CurrentHealth);
		}
	}

	/// <summary>Dónde colgar sus proyectiles: el nodo de proyectiles de la sala, o el padre del enemigo fuera de una sala.</summary>
	public Node ProjectileParent()
	{
		for (Node node = GetParent(); node != null; node = node.GetParent())
		{
			if (node is RoomController room)
			{
				return room.ProjectilesRoot;
			}
		}

		return GetParent();
	}

	public override void _PhysicsProcess(double delta)
	{
		float step = (float)delta;
		_hitTimer = Mathf.Max(0.0f, _hitTimer - step);

		if (_recoilFrames > 0)
		{
			_recoilFrames--;
			Velocity = _recoilVelocity;
			MoveAndSlide();
			return;
		}

		if (!IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
			if (_player == null)
			{
				return;
			}
		}

		Velocity = DesiredVelocity(step);
		MoveAndSlide();

		if (ContactDamage > 0.0f)
		{
			for (int i = 0; i < GetSlideCollisionCount(); i++)
			{
				if (GetSlideCollision(i).GetCollider() is Node3D collider && collider.IsInGroup("player"))
				{
					TryHit(collider);
				}
			}
		}

		foreach (EnemyBehavior behavior in _behaviors)
		{
			behavior.AfterMove(this, _player, step);
		}
	}

	private Vector3 DesiredVelocity(float step)
	{
		foreach (EnemyBehavior behavior in _behaviors)
		{
			if (behavior.TryGetVelocity(this, _player, step, out Vector3 velocity))
			{
				return velocity;
			}
		}

		return Vector3.Zero;
	}

	private void TryHit(Node3D target)
	{
		if (_hitTimer > 0.0f)
		{
			return;
		}

		PlayerHealth playerHealth = target.GetNodeOrNull<PlayerHealth>("PlayerHealth");
		if (playerHealth == null)
		{
			return;
		}

		bool damaged = playerHealth.TakeHit(ContactDamage);
		_hitTimer = HitCooldown;

		if (damaged && RecoilDistance > 0.0f)
		{
			StartRecoil(target.GlobalPosition);
		}
	}

	// La duración se redondea a un número entero de pasos de física, igual que en el simulador. Se mueve con
	// MoveAndSlide, así que respeta paredes, puertas cerradas y otros cuerpos.
	private void StartRecoil(Vector3 playerPosition)
	{
		Vector3 away = GlobalPosition - playerPosition;
		away.Y = 0.0f;
		if (away.LengthSquared() < 0.000001f)
		{
			away = Vector3.Right;
		}

		float step = 1.0f / Engine.PhysicsTicksPerSecond;
		_recoilFrames = Mathf.Max(1, Mathf.CeilToInt(RecoilDuration / step - 0.0001f));
		_recoilVelocity = away.Normalized() * (RecoilDistance / (_recoilFrames * step));
	}

	// Un golpe que no mata suena como golpe; el que mata, solo como muerte (la posición es la del enemigo: él mismo desaparece).
	private void OnDamagedSound(float amount, float current)
	{
		if (current > 0.0f)
		{
			AudioManager.PlayAt($"{SoundSet}_golpe", GlobalPosition);
		}
	}

	private void OnDied()
	{
		if (!MuteDeathSound)
		{
			AudioManager.PlayAt($"{SoundSet}_muere", GlobalPosition);
		}

		QueueFree();
	}
}
