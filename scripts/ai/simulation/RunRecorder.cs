using System.Collections.Generic;
using Godot;

/// <summary>De dónde vino un golpe al jugador (diagnóstico de la prueba de fidelidad).</summary>
public enum DamageSource
{
	Contact,
	Projectile,
	Explosion,
}

/// <summary>Primera visita a una sala dentro de una partida del bot.</summary>
public sealed class RoomVisit
{
	public Vector2I Cell { get; init; }
	public float EnterTime { get; init; }

	/// <summary>-1 mientras no se haya despejado; igual a EnterTime si la sala ya nació despejada.</summary>
	public float ClearTime { get; set; } = -1.0f;

	public float Damage { get; set; }

	/// <summary>Daño de esta visita por fuente (índice = DamageSource).</summary>
	public float[] DamageBySource { get; } = new float[3];

	/// <summary>Daño por contacto de esta visita por tipo del enemigo que golpeó (índice = EnemyKind).</summary>
	public float[] ContactByKind { get; } = new float[4];
}

/// <summary>Medidas de una partida del bot (en el simulador o en el juego real).</summary>
public sealed class SimulationResult
{
	public float DamageTotal { get; set; }
	public float DamageCritical { get; set; }
	public float TimeTotal { get; set; }

	/// <summary>-1 si no llegó a la sala del jefe.</summary>
	public float TimeToBoss { get; set; } = -1.0f;

	public bool TimedOut { get; set; }

	/// <summary>Si con la vida máxima real del jugador habría muerto (la partida se mide con vida ilimitada).</summary>
	public bool WouldDie { get; set; }

	public List<RoomVisit> Visits { get; } = new();

	/// <summary>Daño y número de golpes con daño de toda la partida, por fuente (índice = DamageSource).</summary>
	public float[] DamageBySource { get; } = new float[3];
	public int[] HitsBySource { get; } = new int[3];
}

/// <summary>
/// Registra las medidas de una partida con las mismas reglas en el simulador y en el juego real.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class RunRecorder
{
	private readonly SimLevel _level;
	private readonly float _playerMaxHealth;
	private readonly Dictionary<Vector2I, RoomVisit> _visits = new();

	private Vector2I _cell;
	private RoomVisit _current;

	public RunRecorder(SimLevel level, float playerMaxHealth)
	{
		_level = level;
		_playerMaxHealth = playerMaxHealth;
	}

	public SimulationResult Result { get; } = new();

	public void EnterRoom(Vector2I cell, float time, bool alreadyCleared)
	{
		_cell = cell;
		if (!_visits.TryGetValue(cell, out _current))
		{
			_current = new RoomVisit { Cell = cell, EnterTime = time, ClearTime = alreadyCleared ? time : -1.0f };
			_visits[cell] = _current;
			Result.Visits.Add(_current);
		}

		if (cell == _level.BossCell && Result.TimeToBoss < 0.0f)
		{
			Result.TimeToBoss = time;
		}
	}

	public void RoomCleared(float time)
	{
		if (_current != null && _current.ClearTime < 0.0f)
		{
			_current.ClearTime = time;
		}
	}

	public void Damage(float amount, DamageSource source = DamageSource.Contact, EnemyKind contactKind = EnemyKind.Chaser)
	{
		Result.DamageTotal += amount;
		Result.DamageBySource[(int)source] += amount;
		Result.HitsBySource[(int)source]++;
		if (_current != null)
		{
			_current.Damage += amount;
			_current.DamageBySource[(int)source] += amount;
			if (source == DamageSource.Contact)
			{
				_current.ContactByKind[(int)contactKind] += amount;
			}
		}

		if (_level.CriticalPath.Contains(_cell))
		{
			Result.DamageCritical += amount;
		}
	}

	public void Finish(float time, bool timedOut)
	{
		Result.TimeTotal = time;
		Result.TimedOut = timedOut;
		Result.WouldDie = Result.DamageTotal >= _playerMaxHealth;
	}
}
