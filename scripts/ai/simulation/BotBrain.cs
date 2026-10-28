using System.Collections.Generic;
using Godot;

/// <summary>Parámetros de habilidad del bot. Fijos durante el etiquetado (se guardan en el meta.json).</summary>
public sealed class BotSettings
{
	/// <summary>Retraso de reacción: ve a los enemigos con este número de fotogramas de retraso (±ReactionJitter).</summary>
	public int ReactionFrames { get; init; } = 6;
	public int ReactionJitter { get; init; } = 3;

	/// <summary>Cada cuántos fotogramas se vuelven a sortear el retraso y el error de puntería.</summary>
	public int ResampleFrames { get; init; } = 30;

	/// <summary>Error de puntería máximo (metros) sobre la posición que percibe de cada enemigo.</summary>
	public float AimNoise { get; init; } = 0.15f;

	/// <summary>Distancia a la que intenta colocarse, alineado con su objetivo.</summary>
	public float PreferredDistance { get; init; } = 5.0f;

	/// <summary>Radio dentro del cual se aparta de cualquier enemigo.</summary>
	public float AvoidRadius { get; init; } = 3.0f;

	/// <summary>Distancia mínima a las paredes de los puntos a los que intenta ir en combate.</summary>
	public float WallMargin { get; init; } = 2.0f;

	/// <summary>En combate, por debajo de esta distancia a una pared se aparta de ella (evita que lo arrinconen).</summary>
	public float WallAvoidRange { get; init; } = 2.0f;
	public float WallAvoidStrength { get; init; } = 1.5f;

	/// <summary>Por debajo de esta diferencia en un eje, no pulsa esa dirección (evita oscilar).</summary>
	public float MoveDeadband { get; init; } = 0.15f;

	/// <summary>Desalineación lateral máxima con el centro de una puerta antes de cruzarla en línea recta.</summary>
	public float DoorAlignTolerance { get; init; } = 0.25f;

	public SortedDictionary<string, double> ToDictionary()
	{
		return new SortedDictionary<string, double>
		{
			["reaction_frames"] = ReactionFrames,
			["reaction_jitter"] = ReactionJitter,
			["resample_frames"] = ResampleFrames,
			["aim_noise"] = AimNoise,
			["preferred_distance"] = PreferredDistance,
			["avoid_radius"] = AvoidRadius,
			["wall_margin"] = WallMargin,
			["wall_avoid_range"] = WallAvoidRange,
			["wall_avoid_strength"] = WallAvoidStrength,
			["move_deadband"] = MoveDeadband,
			["door_align_tolerance"] = DoorAlignTolerance,
		};
	}
}

/// <summary>Lo que el bot ve en un fotograma. Coordenadas en el plano (X, Z) de la sala actual.</summary>
public readonly struct BotObservation
{
	public Vector2I Cell { get; init; }
	public RoomState State { get; init; }
	public Vector2 RoomSize { get; init; }
	public Vector2 Player { get; init; }
	public IReadOnlyList<Vector2> Enemies { get; init; }
}

/// <summary>
/// Teclas que pulsa el bot. Move: cada eje vale -1, 0 o 1 (X: A/D, Z: W/S; -Z es arriba).
/// Shoot: -1 ninguna, o el índice en PlayerShooter.Actions (0 izquierda, 1 derecha, 2 arriba, 3 abajo).
/// </summary>
public readonly struct BotAction
{
	public Vector2 Move { get; init; }
	public int Shoot { get; init; }
}

/// <summary>
/// Bot con habilidad fija y reproducible. El mismo cerebro controla al jugador del simulador y al del juego real
/// (BotDriver), así que la prueba de fidelidad mide el simulador y no diferencias entre bots.
///
/// Comportamiento:
///   - Fuera de combate, sigue la ruta de SimLevel (todas las salas, la rama del jefe al final): se alinea con el
///     centro de la puerta siguiente y la cruza en línea recta.
///   - En combate, sin enemigos visibles (antes de que aparezcan), se queda quieto.
///   - Con enemigos, elige el más cercano, se coloca alineado con él en X o en Z (el eje que pida menos
///     movimiento) a PreferredDistance, se aparta de cualquier enemigo a menos de AvoidRadius y de las paredes a
///     menos de WallAvoidRange (para no acabar arrinconado), y dispara cuando
///     algún enemigo queda alineado en una de las 4 direcciones (el juego solo dispara en cardinales).
///   - Ruido: ve a los enemigos con un retraso de reacción y apunta con un pequeño error, ambos sorteados cada
///     ResampleFrames fotogramas con su propio PCG32.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class BotBrain
{
	public const int ShootNone = -1;
	public const int ShootLeft = 0;
	public const int ShootRight = 1;
	public const int ShootUp = 2;
	public const int ShootDown = 3;

	private const int HistorySize = 32;

	private static readonly BotAction Idle = new() { Move = Vector2.Zero, Shoot = ShootNone };

	private readonly SimLevel _level;
	private readonly BotSettings _settings;
	private readonly Pcg32 _random;
	private readonly Vector2[][] _history = new Vector2[HistorySize][];

	private int _routeIndex;
	private Vector2I _cell;
	private int _historyCount;
	private int _historyHead;
	private int _delay;
	private Vector2 _aimError;
	private int _framesToResample;

	public BotBrain(SimLevel level, BotSettings settings, ulong seed)
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

	public BotAction Decide(in BotObservation observation)
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
		Remember(observation.Enemies);

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

	private void Remember(IReadOnlyList<Vector2> enemies)
	{
		Vector2[] copy = new Vector2[enemies.Count];
		for (int i = 0; i < copy.Length; i++)
		{
			copy[i] = enemies[i];
		}

		_historyHead = (_historyHead + 1) % HistorySize;
		_history[_historyHead] = copy;
		_historyCount = Mathf.Min(_historyCount + 1, HistorySize);
	}

	private Vector2[] Perceived()
	{
		int delay = Mathf.Min(_delay, _historyCount - 1);
		return _history[(_historyHead - delay + HistorySize) % HistorySize];
	}

	private BotAction GoThroughDoor(in BotObservation observation, DoorSide side)
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

	private BotAction Fight(in BotObservation observation, Vector2[] enemies)
	{
		if (enemies.Length == 0)
		{
			return Idle;
		}

		Vector2 player = observation.Player;
		int targetIndex = 0;
		for (int i = 1; i < enemies.Length; i++)
		{
			if (player.DistanceSquaredTo(enemies[i]) < player.DistanceSquaredTo(enemies[targetIndex]))
			{
				targetIndex = i;
			}
		}

		Vector2 target = enemies[targetIndex];
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

		return new BotAction { Move = Quantize(steer), Shoot = Aim(player, enemies, targetIndex) };
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

	private int Aim(Vector2 player, Vector2[] enemies, int targetIndex)
	{
		int shot = AimAt(player, enemies[targetIndex]);
		for (int i = 0; shot == ShootNone && i < enemies.Length; i++)
		{
			shot = AimAt(player, enemies[i]);
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
