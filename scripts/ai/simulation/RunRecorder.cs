using System.Collections.Generic;
using Godot;

/// <summary>Primera visita a una sala dentro de una partida del bot.</summary>
public sealed class RoomVisit
{
	public Vector2I Cell { get; init; }
	public float EnterTime { get; init; }

	/// <summary>-1 mientras no se haya despejado; igual a EnterTime si la sala ya nació despejada.</summary>
	public float ClearTime { get; set; } = -1.0f;

	public float Damage { get; set; }
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

	public void Damage(float amount)
	{
		Result.DamageTotal += amount;
		if (_current != null)
		{
			_current.Damage += amount;
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
