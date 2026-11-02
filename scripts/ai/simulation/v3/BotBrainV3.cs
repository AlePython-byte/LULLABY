using System.Collections.Generic;
using Godot;

/// <summary>Lo que el bot ve en un fotograma. Coordenadas en el plano (X, Z) de la sala actual.</summary>
public readonly struct BotObservationV3
{
	public Vector2I Cell { get; init; }
	public RoomState State { get; init; }
	public Vector2 RoomSize { get; init; }
	public Vector2 Player { get; init; }
	public IReadOnlyList<Vector2> Enemies { get; init; }

	/// <summary>Tipo de cada enemigo, en el orden de Enemies. Null = todos básicos.</summary>
	public IReadOnlyList<EnemyKind> EnemyKinds { get; init; }

	/// <summary>Si cada enemigo tiene la mecha encendida, en el orden de Enemies. Null = ninguno.</summary>
	public IReadOnlyList<bool> FuseLit { get; init; }

	/// <summary>Estado visible del cuerpo y del aviso; sin direccion interna de la embestida.</summary>
	public IReadOnlyList<bool> Vulnerable { get; init; }
	public IReadOnlyList<bool> DashWarning { get; init; }

	/// <summary>Proyectiles enemigos en vuelo: posición y velocidad (m/s). Null = ninguno.</summary>
	public IReadOnlyList<Vector2> Projectiles { get; init; }
	public IReadOnlyList<Vector2> ProjectileVelocities { get; init; }
}

/// <summary>
/// Bot con habilidad fija y reproducible. El mismo cerebro controla al jugador del simulador y al del juego real
/// (BotDriverV3), así que la prueba de fidelidad mide el simulador y no diferencias entre bots.
///
/// Comportamiento:
///   - Fuera de combate, sigue la ruta de SimLevelV3 (todas las salas, la rama del jefe al final): se alinea con el
///     centro de la puerta siguiente y la cruza en línea recta.
///   - En combate, sin enemigos visibles (antes de que aparezcan), se queda quieto.
///   - Con enemigos, elige el más cercano, se coloca alineado con él en X o en Z (el eje que pida menos
///     movimiento) a PreferredDistance, se aparta de cualquier enemigo a menos de AvoidRadius y de las paredes a
///     menos de WallAvoidRange (para no acabar arrinconado), y dispara cuando
///     algún enemigo queda alineado en una de las 4 direcciones (el juego solo dispara en cardinales).
///   - Ruido: ve a los enemigos con un retraso de reacción y apunta con un pequeño error, ambos sorteados cada
///     ResampleFrames fotogramas con su propio PCG32.
///
/// Politica v3: explosivo cercano, tirador o excavador vulnerable, huidor, volador, resto.
/// Los empates se resuelven por distancia y orden de aparicion. El monticulo sigue visible,
/// pero no se apunta a el cuando se percibe enterrado. Solo se observa el aviso del huidor,
/// nunca su direccion interna; la esquiva perpendicular tambien usa la historia retrasada.
///   - Huye de los explosivos con la mecha encendida a menos de FleeRadius (empuje que crece al acercarse).
///   - Esquiva los proyectiles cuya trayectoria pasa a menos de DodgeRadius dentro de DodgeHorizon segundos, apartándose
///     en perpendicular a ella.
///   - Huida y esquiva usan lo MISMO que percibe para todo lo demás: la observación retrasada ReactionFrames
///     (±ReactionJitter) fotogramas. Ve la mecha encendida y los proyectiles tarde, así que no es perfecto.
///
/// El bot es la vara de medir: "difícil" significa "difícil para ESTE bot". Su habilidad (reacción, puntería, distancias,
/// cuánto esquiva) decide qué se mide como dificultad. Un bot que esquivara todo haría inofensivo al tirador; uno que no
/// esquivara nada lo haría el enemigo más peligroso. Por eso sus parámetros son fijos y se guardan en el meta.json.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class BotBrainV3
{
	public const int PolicyVersion = 1;
	public const int ShootNone = -1;
	public const int ShootLeft = 0;
	public const int ShootRight = 1;
	public const int ShootUp = 2;
	public const int ShootDown = 3;

	private const int HistorySize = 32;

	// Lo que el bot recuerda de un fotograma (para percibirlo con retraso).
	private sealed class Snapshot
	{
		public Vector2[] Enemies;
		public EnemyKind[] Kinds;
		public bool[] Lit;
		public bool[] Vulnerable;
		public bool[] DashWarning;
		public Vector2[] Projectiles;
		public Vector2[] ProjectileVelocities;
	}

	private static readonly BotAction Idle = new() { Move = Vector2.Zero, Shoot = ShootNone };

	private readonly SimLevelV3 _level;
	private readonly BotSettings _settings;
	private readonly Pcg32 _random;
	private readonly Snapshot[] _history = new Snapshot[HistorySize];

	private int _routeIndex;
	private Vector2I _cell;
	private int _historyCount;
	private int _historyHead;
	private int _delay;
	private Vector2 _aimError;
	private int _framesToResample;

	public BotBrainV3(SimLevelV3 level, BotSettings settings, ulong seed)
	{
		_level = level;
		_settings = settings;
		_random = new Pcg32(seed);
		_cell = level.StartCell;
	}

	/// <summary>Semilla del bot para una repetición de un nivel.</summary>
	public static ulong SeedFor(int levelSeed, int run)
	{
		return unchecked((ulong)(uint)levelSeed * 0x9E3779B97F4A7C15UL + (ulong)run + 1UL);
	}

	public BotAction Decide(in BotObservationV3 observation)
	{
		if (observation.Cell != _cell)
		{
			if (_routeIndex + 1 < _level.Route.Count && observation.Cell == _level.Route[_routeIndex + 1])
			{
				_routeIndex++;
			}

			_cell = observation.Cell;
			_historyCount = 0;
		}

		Resample();
		Remember(observation);

		if (observation.State == RoomState.Combat)
		{
			return Fight(observation, Perceived());
		}

		// Si se desvió de la ruta, primero vuelve a la sala de la ruta en la que debería estar.
		bool onRoute = _cell == _level.Route[_routeIndex];
		Vector2I target = onRoute && _routeIndex + 1 < _level.Route.Count
			? _level.Route[_routeIndex + 1]
			: _level.Route[_routeIndex];

		return target == _cell ? Idle : GoThroughDoor(observation, _level.FirstStep(_cell, target));
	}

	private void Resample()
	{
		if (--_framesToResample > 0)
		{
			return;
		}

		_framesToResample = _settings.ResampleFrames;
		_delay = Mathf.Clamp(_settings.ReactionFrames + _random.Range(-_settings.ReactionJitter, _settings.ReactionJitter),
			0, HistorySize - 1);
		_aimError = new Vector2((_random.NextFloat() * 2.0f - 1.0f) * _settings.AimNoise,
			(_random.NextFloat() * 2.0f - 1.0f) * _settings.AimNoise);
	}

	private void Remember(in BotObservationV3 observation)
	{
		int count = observation.Enemies.Count;
		Snapshot snapshot = new()
		{
			Enemies = new Vector2[count],
			Kinds = new EnemyKind[count],
			Lit = new bool[count],
			Vulnerable = new bool[count],
			DashWarning = new bool[count],
			Projectiles = Copy(observation.Projectiles),
			ProjectileVelocities = Copy(observation.ProjectileVelocities),
		};

		for (int i = 0; i < count; i++)
		{
			snapshot.Enemies[i] = observation.Enemies[i];
			snapshot.Kinds[i] = observation.EnemyKinds?[i] ?? EnemyKind.Chaser;
			snapshot.Lit[i] = observation.FuseLit?[i] ?? false;
			snapshot.Vulnerable[i] = observation.Vulnerable?[i] ?? true;
			snapshot.DashWarning[i] = observation.DashWarning?[i] ?? false;
		}

		_historyHead = (_historyHead + 1) % HistorySize;
		_history[_historyHead] = snapshot;
		_historyCount = Mathf.Min(_historyCount + 1, HistorySize);
	}

	private static Vector2[] Copy(IReadOnlyList<Vector2> values)
	{
		if (values == null || values.Count == 0)
		{
			return System.Array.Empty<Vector2>();
		}

		Vector2[] copy = new Vector2[values.Count];
		for (int i = 0; i < copy.Length; i++)
		{
			copy[i] = values[i];
		}

		return copy;
	}

	private Snapshot Perceived()
	{
		int delay = Mathf.Min(_delay, _historyCount - 1);
		return _history[(_historyHead - delay + HistorySize) % HistorySize];
	}

	private BotAction GoThroughDoor(in BotObservationV3 observation, DoorSide side)
	{
		Vector3 direction3 = side.Direction();
		Vector2 direction = new(direction3.X, direction3.Z);
		bool alongX = direction.X != 0.0f;
		float half = alongX ? observation.RoomSize.X / 2.0f : observation.RoomSize.Y / 2.0f;
		float lateral = alongX ? observation.Player.Y : observation.Player.X;

		// Primero se alinea con el centro del hueco (dentro de la sala) y después cruza en línea recta.
		Vector2 target = Mathf.Abs(lateral) > _settings.DoorAlignTolerance
			? direction * (half - 1.0f)
			: direction * (half + 2.0f);

		return new BotAction { Move = Quantize(target - observation.Player), Shoot = ShootNone };
	}

	private BotAction Fight(in BotObservationV3 observation, Snapshot seen)
	{
		Vector2[] enemies = seen.Enemies;
		if (enemies.Length == 0)
		{
			return Idle;
		}

		Vector2 player = observation.Player;
		int targetIndex = -1;
		for (int i = 0; i < enemies.Length; i++)
		{
			if (!seen.Vulnerable[i]) continue;
			if (targetIndex < 0) { targetIndex = i; continue; }
			int priority = Priority(seen, i, player);
			int best = Priority(seen, targetIndex, player);
			if (priority < best
				|| (priority == best && player.DistanceSquaredTo(enemies[i]) < player.DistanceSquaredTo(enemies[targetIndex])))
			{
				targetIndex = i;
			}
		}

		// Si solo quedan monticulos, sigue su posicion visible para alinearse cuando emerjan.
		// No dispara ni inventa su ubicacion futura.
		Vector2 target = enemies[targetIndex >= 0 ? targetIndex : 0];
		float wallLimit = 0.5f + _settings.WallMargin;
		Vector2 limit = new(observation.RoomSize.X / 2.0f - wallLimit, observation.RoomSize.Y / 2.0f - wallLimit);
		Vector2 steer = ChooseSpot(player, target, limit) - player;
		if (steer.Length() > 1.0f)
		{
			steer = steer.Normalized();
		}

		foreach (Vector2 enemy in enemies)
		{
			Vector2 away = player - enemy;
			float distance = away.Length();
			if (distance > 0.0001f && distance < _settings.AvoidRadius)
			{
				steer += away / distance * ((_settings.AvoidRadius - distance) / _settings.AvoidRadius) * 2.0f;
			}
		}

		// Las paredes también empujan: sin esto, huir de los enemigos acaba en una esquina, donde un enemigo en
		// diagonal puede encajarlo sin que pueda moverse ni dispararle (las lágrimas solo van en 4 direcciones).
		Vector2 inner = observation.RoomSize / 2.0f - new Vector2(0.5f, 0.5f);
		steer.X += WallPush(player.X + inner.X) - WallPush(inner.X - player.X);
		steer.Y += WallPush(player.Y + inner.Y) - WallPush(inner.Y - player.Y);

		// Solo suman algo con explosivos encendidos o proyectiles a la vista (con solo básicos, las decisiones de v1).
		steer += FleeLitExploders(player, seen) + DodgeProjectiles(player, seen) + DodgeDashWarnings(player, seen);

		return new BotAction { Move = Quantize(steer), Shoot = Aim(player, seen, targetIndex) };
	}

	// 0 = la más alta. Con solo básicos todos tienen la misma y gana el más cercano, como en la versión 1.
	private int Priority(Snapshot seen, int index, Vector2 player)
	{
		return seen.Kinds[index] switch
		{
			EnemyKind.Exploder when player.DistanceTo(seen.Enemies[index]) <= _settings.ExploderPriorityRadius => 0,
			EnemyKind.Shooter or EnemyKind.Digger => 1,
			EnemyKind.Runner => 2,
			EnemyKind.Flyer => 3,
			_ => 4,
		};
	}

	private Vector2 FleeLitExploders(Vector2 player, Snapshot seen)
	{
		Vector2 push = Vector2.Zero;
		for (int i = 0; i < seen.Enemies.Length; i++)
		{
			if (!seen.Lit[i])
			{
				continue;
			}

			Vector2 away = player - seen.Enemies[i];
			float distance = away.Length();
			if (distance > 0.0001f && distance < _settings.FleeRadius)
			{
				push += away / distance * ((_settings.FleeRadius - distance) / _settings.FleeRadius) * _settings.FleeStrength;
			}
		}

		return push;
	}

	// Para cada proyectil percibido: momento de máximo acercamiento a lo largo de su recta; si llega pronto y pasa cerca,
	// se aparta en perpendicular (hacia el lado en que ya está).
	private Vector2 DodgeProjectiles(Vector2 player, Snapshot seen)
	{
		Vector2 push = Vector2.Zero;
		for (int i = 0; i < seen.Projectiles.Length; i++)
		{
			Vector2 velocity = seen.ProjectileVelocities[i];
			float speedSquared = velocity.LengthSquared();
			if (speedSquared < 0.0001f)
			{
				continue;
			}

			Vector2 relative = player - seen.Projectiles[i];
			float time = relative.Dot(velocity) / speedSquared;
			if (time <= 0.0f || time > _settings.DodgeHorizon)
			{
				continue;
			}

			Vector2 offset = relative - velocity * time;
			float miss = offset.Length();
			if (miss >= _settings.DodgeRadius)
			{
				continue;
			}

			Vector2 side = miss > 0.0001f ? offset / miss : new Vector2(-velocity.Y, velocity.X).Normalized();
			push += side * _settings.DodgeStrength;
		}

		return push;
	}

	// Solo reacciona al aviso visual retrasado. No lee DashDirection ni temporizadores internos.
	private Vector2 DodgeDashWarnings(Vector2 player, Snapshot seen)
	{
		Vector2 push = Vector2.Zero;
		for (int i = 0; i < seen.Enemies.Length; i++)
		{
			if (!seen.DashWarning[i]) continue;
			Vector2 away = player - seen.Enemies[i];
			if (away.LengthSquared() > 0.0001f)
				push += new Vector2(-away.Y, away.X).Normalized() * _settings.DodgeStrength;
		}
		return push;
	}

	private float WallPush(float distance)
	{
		float range = _settings.WallAvoidRange;
		return distance < range ? (range - distance) / range * _settings.WallAvoidStrength : 0.0f;
	}

	// Punto alineado con el objetivo en el eje que pida menos movimiento, a PreferredDistance y dentro de la sala.
	private Vector2 ChooseSpot(Vector2 player, Vector2 target, Vector2 limit)
	{
		float distance = _settings.PreferredDistance;
		bool shareX = Mathf.Abs(target.X - player.X) <= Mathf.Abs(target.Y - player.Y);

		if (shareX)
		{
			float side = player.Y >= target.Y ? 1.0f : -1.0f;
			float z = target.Y + side * distance;
			if (Mathf.Abs(z) > limit.Y && Mathf.Abs(target.Y - side * distance) <= limit.Y)
			{
				z = target.Y - side * distance;
			}

			return new Vector2(Mathf.Clamp(target.X, -limit.X, limit.X), Mathf.Clamp(z, -limit.Y, limit.Y));
		}

		float sideX = player.X >= target.X ? 1.0f : -1.0f;
		float x = target.X + sideX * distance;
		if (Mathf.Abs(x) > limit.X && Mathf.Abs(target.X - sideX * distance) <= limit.X)
		{
			x = target.X - sideX * distance;
		}

		return new Vector2(Mathf.Clamp(x, -limit.X, limit.X), Mathf.Clamp(target.Y, -limit.Y, limit.Y));
	}

	private int Aim(Vector2 player, Snapshot seen, int targetIndex)
	{
		if (targetIndex < 0) return ShootNone;
		Vector2[] enemies = seen.Enemies;
		int shot = AimAt(player, enemies[targetIndex]);
		for (int i = 0; shot == ShootNone && i < enemies.Length; i++)
		{
			if (seen.Vulnerable[i]) shot = AimAt(player, enemies[i]);
		}

		return shot;
	}

	private int AimAt(Vector2 player, Vector2 enemy)
	{
		Vector2 seen = enemy + _aimError;
		if (Mathf.Abs(seen.X - player.X) < _level.AimTolerance)
		{
			return seen.Y < player.Y ? ShootUp : ShootDown;
		}

		if (Mathf.Abs(seen.Y - player.Y) < _level.AimTolerance)
		{
			return seen.X < player.X ? ShootLeft : ShootRight;
		}

		return ShootNone;
	}

	private Vector2 Quantize(Vector2 steer)
	{
		float deadband = _settings.MoveDeadband;
		return new Vector2(
			Mathf.Abs(steer.X) > deadband ? Mathf.Sign(steer.X) : 0.0f,
			Mathf.Abs(steer.Y) > deadband ? Mathf.Sign(steer.Y) : 0.0f);
	}
}
