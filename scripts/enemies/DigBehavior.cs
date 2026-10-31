using Godot;

/// <summary>
/// Ciclo del Excavador, determinista y con todos los tiempos en [Export]:
///   Fuera        OutTime segundos quieto y VULNERABLE. A los FireDelay segundos dispara un proyectil lento al jugador
///                (ProjectileSpeed), con un aviso de WarnTime antes (parpadeo).
///   Hundiéndose  SinkTime segundos; sigue siendo vulnerable (el cuerpo aún se ve).
///   Bajo tierra  INVULNERABLE y sin hacer daño: apaga su capa de colisión (ni las lágrimas ni el jugador lo tocan) y solo
///                choca con las paredes. Se desplaza a Speed (la de EnemyBase) rodeando al jugador: apunta a un punto
///                girado OrbitDegrees respecto a su posición actual y a EmergeDistance / cos(OrbitDegrees) del jugador,
///                con lo que se estabiliza a EmergeDistance de él mientras lo rodea (apuntando a EmergeDistance a secas
///                se quedaría a EmergeDistance · cos(OrbitDegrees)). El punto se recorta al interior de la sala.
///                Emerge cuando lleva al menos UndergroundTime y está a MinEmergeDistance o más del jugador. Si pasan
///                MaxUndergroundTime sin poder emerger (un jugador pegado al montículo), emerge en el punto de la sala más
///                lejano del jugador, a WallMargin de las paredes: así una sala con un solo Excavador siempre se despeja.
///   Emergiendo   EmergeTime segundos; vuelve a ser vulnerable desde el primer instante.
/// La parte gráfica (cuerpo que se hunde y montículo) la hace DiggerVisual con SetBurial.
/// </summary>
public partial class DigBehavior : EnemyBehavior
{
	[Signal] public delegate void WarningStartedEventHandler();
	[Signal] public delegate void FiredEventHandler(Vector3 direction);
	[Signal] public delegate void BuriedEventHandler();
	[Signal] public delegate void SurfacedEventHandler(float distanceToPlayer, bool forced);

	public enum DigPhase
	{
		Out,
		Sinking,
		Underground,
		Emerging,
	}

	[ExportGroup("Fuera")]
	[Export] public float OutTime { get; set; } = 2.6f;
	[Export] public float FireDelay { get; set; } = 1.0f;
	[Export] public float WarnTime { get; set; } = 0.4f;
	[Export] public PackedScene ProjectileScene { get; set; }
	[Export] public float ProjectileSpeed { get; set; } = 3.5f;
	[Export] public float SpawnDistance { get; set; } = 0.7f;

	public override void _Ready()
	{
		Fired += _ => AudioManager.PlayFrom(SoundCatalog.ShooterShot, this);
	}

	[ExportGroup("Bajo tierra")]
	[Export] public float SinkTime { get; set; } = 0.5f;
	[Export] public float UndergroundTime { get; set; } = 1.8f;
	[Export] public float MaxUndergroundTime { get; set; } = 4.0f;
	[Export] public float EmergeTime { get; set; } = 0.5f;
	[Export] public float EmergeDistance { get; set; } = 4.5f;
	[Export] public float MinEmergeDistance { get; set; } = 3.0f;
	[Export] public float OrbitDegrees { get; set; } = 30.0f;
	[Export] public float WallMargin { get; set; } = 1.5f;

	private bool _started;
	private uint _layer;
	private uint _mask;
	private bool _warned;
	private bool _fired;

	public DigPhase Phase { get; private set; } = DigPhase.Out;
	public float TimeInPhase { get; private set; }

	/// <summary>Si ahora mismo puede recibir daño (todas las fases menos bajo tierra).</summary>
	public bool Vulnerable => Phase != DigPhase.Underground;

	public override bool TryGetVelocity(EnemyBase enemy, Node3D player, float delta, out Vector3 velocity)
	{
		velocity = Vector3.Zero;
		if (!_started)
		{
			_started = true;
			_layer = enemy.CollisionLayer;
			_mask = enemy.CollisionMask;
		}

		if (Phase != DigPhase.Underground)
		{
			return true;
		}

		Vector3 toTarget = TunnelTarget(enemy, player) - enemy.GlobalPosition;
		toTarget.Y = 0.0f;
		float distance = toTarget.Length();
		if (distance > 0.05f)
		{
			velocity = toTarget / distance * Mathf.Min(enemy.Speed, distance / Mathf.Max(delta, 0.0001f));
		}

		return true;
	}

	public override void AfterMove(EnemyBase enemy, Node3D player, float delta)
	{
		TimeInPhase += delta;
		DiggerVisual visual = enemy.Visual as DiggerVisual;

		switch (Phase)
		{
			case DigPhase.Out:
				if (!_fired && !_warned && TimeInPhase >= FireDelay - WarnTime)
				{
					_warned = true;
					enemy.Visual?.SetHighlight(true);
					EmitSignal(SignalName.WarningStarted);
				}

				if (!_fired && TimeInPhase >= FireDelay)
				{
					_fired = true;
					enemy.Visual?.SetHighlight(false);
					Fire(enemy, player);
				}

				if (TimeInPhase >= OutTime)
				{
					Enter(DigPhase.Sinking);
				}

				break;

			case DigPhase.Sinking:
				visual?.SetBurial(TimeInPhase / SinkTime);
				if (TimeInPhase >= SinkTime)
				{
					Enter(DigPhase.Underground);
					enemy.CollisionLayer = 0;
					enemy.CollisionMask = CollisionLayers.World;
					visual?.SetBurial(1.0f);
					EmitSignal(SignalName.Buried);
				}

				break;

			case DigPhase.Underground:
				float distance = PlanarToPlayer(enemy, player).Length();
				if (TimeInPhase >= UndergroundTime && distance >= MinEmergeDistance)
				{
					Emerge(enemy, player, false);
				}
				else if (TimeInPhase >= MaxUndergroundTime)
				{
					Vector3 farthest = FarthestPoint(enemy, player);
					enemy.GlobalPosition = new Vector3(farthest.X, enemy.GlobalPosition.Y, farthest.Z);
					Emerge(enemy, player, true);
				}

				break;

			case DigPhase.Emerging:
				visual?.SetBurial(1.0f - TimeInPhase / EmergeTime);
				if (TimeInPhase >= EmergeTime)
				{
					visual?.SetBurial(0.0f);
					Enter(DigPhase.Out);
					_warned = false;
					_fired = false;
				}

				break;
		}
	}

	private void Enter(DigPhase phase)
	{
		Phase = phase;
		TimeInPhase = 0.0f;
	}

	private void Emerge(EnemyBase enemy, Node3D player, bool forced)
	{
		enemy.CollisionLayer = _layer;
		enemy.CollisionMask = _mask;
		Enter(DigPhase.Emerging);
		EmitSignal(SignalName.Surfaced, PlanarToPlayer(enemy, player).Length(), forced);
	}

	private void Fire(EnemyBase enemy, Node3D player)
	{
		if (ProjectileScene == null)
		{
			return;
		}

		Vector3 toPlayer = PlanarToPlayer(enemy, player);
		Vector3 direction = toPlayer.LengthSquared() > 0.0001f ? toPlayer.Normalized() : Vector3.Forward;
		EnemyProjectile.FireFrom(enemy, ProjectileScene, direction, SpawnDistance, ProjectileSpeed);
		EmitSignal(SignalName.Fired, direction);
	}

	// Punto hacia el que avanza bajo tierra (ver el resumen de la clase).
	private Vector3 TunnelTarget(EnemyBase enemy, Node3D player)
	{
		Vector3 away = -PlanarToPlayer(enemy, player);
		Vector3 direction = away.LengthSquared() > 0.0001f ? away.Normalized() : Vector3.Right;
		float orbit = Mathf.DegToRad(OrbitDegrees);
		direction = direction.Rotated(Vector3.Up, orbit);
		Vector3 target = player.GlobalPosition + direction * (EmergeDistance / Mathf.Max(Mathf.Cos(orbit), 0.2f));
		return Inner(enemy, out Rect2 inner) ? Clamp(target, inner) : target;
	}

	// La esquina del interior de la sala (con WallMargin) más lejana del jugador; sin sala, a EmergeDistance alejándose.
	private Vector3 FarthestPoint(EnemyBase enemy, Node3D player)
	{
		Vector3 from = player.GlobalPosition;
		if (!Inner(enemy, out Rect2 inner))
		{
			Vector3 away = -PlanarToPlayer(enemy, player);
			Vector3 direction = away.LengthSquared() > 0.0001f ? away.Normalized() : Vector3.Right;
			return from + direction * Mathf.Max(EmergeDistance, MinEmergeDistance);
		}

		Vector2[] corners = { inner.Position, new(inner.End.X, inner.Position.Y), inner.End, new(inner.Position.X, inner.End.Y) };
		Vector2 best = corners[0];
		float bestDistance = -1.0f;
		foreach (Vector2 corner in corners)
		{
			float distance = corner.DistanceSquaredTo(new Vector2(from.X, from.Z));
			if (distance > bestDistance)
			{
				best = corner;
				bestDistance = distance;
			}
		}

		return new Vector3(best.X, 0.0f, best.Y);
	}

	// Interior útil de la sala que contiene al enemigo (a WallMargin de las paredes), si hay sala.
	private bool Inner(EnemyBase enemy, out Rect2 inner)
	{
		for (Node node = enemy.GetParent(); node != null; node = node.GetParent())
		{
			if (node is RoomController room && room.Data != null)
			{
				Vector2 half = (room.Data.Size / 2.0f - new Vector2(WallMargin, WallMargin)).Max(Vector2.Zero);
				Vector2 center = new(room.GlobalPosition.X, room.GlobalPosition.Z);
				inner = new Rect2(center - half, half * 2.0f);
				return true;
			}
		}

		inner = default;
		return false;
	}

	private static Vector3 Clamp(Vector3 point, Rect2 inner)
	{
		return new Vector3(Mathf.Clamp(point.X, inner.Position.X, inner.End.X), point.Y,
			Mathf.Clamp(point.Z, inner.Position.Y, inner.End.Y));
	}
}
