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

	/// <summary>
	/// Segundos que dura la aparición inerte (ver BeginSpawn). Sumados a la espera de la sala (RoomController.SpawnDelay, 0,7 s), el
	/// enemigo empieza a actuar hacia los 0,95 s: casi el 1,0 s que espera el simulador.
	/// </summary>
	[Export] public float SpawnDuration { get; set; } = 0.25f;

	private const float SpawnStartScale = 0.2f;

	private readonly List<EnemyBehavior> _behaviors = new();
	private readonly List<CollisionShape3D> _shapes = new();
	private Node3D _player;
	private float _hitTimer;
	private Vector3 _recoilVelocity;
	private int _recoilFrames;
	private float _spawnLeft;
	private Vector3 _visualScale = Vector3.One;
	private bool _madeInvulnerable;

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

	/// <summary>
	/// Preparado de antemano (Stage) y aún sin revelar: invisible, sin procesar y sin colisión. La sala lo instancia así durante su espera
	/// para que el momento de aparecer no cueste una instanciación.
	/// </summary>
	public bool Staged { get; private set; }

	/// <summary>
	/// En aparición inerte (BeginSpawn): ya se ve y crece, pero no se mueve, no ataca, no hace daño por contacto, no recibe daño y las
	/// lágrimas de Noah lo atraviesan. Termina solo a los SpawnDuration segundos.
	/// </summary>
	public bool Spawning { get; private set; }

	public override void _Ready()
	{
		SetProcess(false);
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

	/// <summary>
	/// Lo deja preparado sin que se vea ni actúe: invisible, sin procesar (ProcessMode desactivado) y con las formas de colisión
	/// desactivadas. No toca capas, máscaras ni tamaños de colisión. Se llama antes de añadirlo al árbol o poco después, y BeginSpawn lo revela.
	/// </summary>
	public void Stage()
	{
		Staged = true;
		ProcessMode = ProcessModeEnum.Disabled;
		Visible = false;
		SetShapesDisabled(true);
	}

	/// <summary>
	/// Lo revela y empieza la aparición inerte: crece desde SpawnStartScale hasta su tamaño en SpawnDuration segundos y, mientras tanto,
	/// no actúa, no hace daño y no recibe daño. Las lágrimas lo atraviesan porque su colisión sigue desactivada (el área de la lágrima no lo
	/// detecta); las capas y los tamaños no cambian. Llamar después de añadirlo a su sala (HealthComponent se reinicia al entrar al árbol).
	/// </summary>
	public void BeginSpawn()
	{
		Staged = false;
		ProcessMode = ProcessModeEnum.Inherit;
		Visible = true;
		SetShapesDisabled(true);

		if (SpawnDuration <= 0.0f)
		{
			EndSpawn();
			return;
		}

		Spawning = true;
		_spawnLeft = SpawnDuration;
		_madeInvulnerable = !Health.Invulnerable;
		Health.Invulnerable = true;
		if (Visual != null)
		{
			_visualScale = Visual.Scale;
			Visual.Scale = _visualScale * SpawnStartScale;
			SetProcess(true);
		}
	}

	private void EndSpawn()
	{
		Spawning = false;
		_spawnLeft = 0.0f;
		SetShapesDisabled(false);
		if (_madeInvulnerable)
		{
			Health.Invulnerable = false;
			_madeInvulnerable = false;
		}

		if (Visual != null)
		{
			Visual.Scale = _visualScale;
		}

		SetProcess(false);
	}

	private void SetShapesDisabled(bool disabled)
	{
		if (_shapes.Count == 0)
		{
			foreach (Node child in GetChildren())
			{
				if (child is CollisionShape3D shape)
				{
					_shapes.Add(shape);
				}
			}
		}

		foreach (CollisionShape3D shape in _shapes)
		{
			shape.Disabled = disabled;
		}
	}

	// Solo corre durante la aparición (se enciende en BeginSpawn): el crecimiento sigue el mismo reloj de pasos de física que su final.
	public override void _Process(double delta)
	{
		if (!Spawning || Visual == null)
		{
			return;
		}

		float progress = Mathf.Clamp(1.0f - _spawnLeft / SpawnDuration, 0.0f, 1.0f);
		float eased = 1.0f - Mathf.Pow(1.0f - progress, 3.0f);
		Visual.Scale = _visualScale * Mathf.Lerp(SpawnStartScale, 1.0f, eased);
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
			Health.Invulnerable = false;
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

		if (Spawning)
		{
			_spawnLeft -= step;
			if (_spawnLeft <= 0.0f)
			{
				EndSpawn();
			}

			return;
		}

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
			_player = GetTree().GetFirstNodeInGroup(GameNames.PlayerGroup) as Node3D;
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
				if (GetSlideCollision(i).GetCollider() is Node3D collider && collider.IsInGroup(GameNames.PlayerGroup))
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

		bool damaged = playerHealth.TakeHit(ContactDamage, DeathCauses.FromEnemy(this));
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
			AudioManager.PlayFrom($"{SoundSet}_golpe", this);
		}
	}

	private void OnDied()
	{
		// Primero se libera (la lógica no depende del audio); el sonido va después y nunca puede lanzar. El nodo sigue en el árbol
		// hasta el final del fotograma, así que PlayFrom aún encuentra su posición.
		QueueFree();
		if (!MuteDeathSound)
		{
			AudioManager.PlayFrom($"{SoundSet}_muere", this);
		}
	}
}
