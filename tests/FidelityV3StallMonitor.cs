using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Alarma conservadora de ejecucion, no criterio estadistico ni cambio de combate.</summary>
public sealed class FidelityV3StallMonitor
{
	public readonly record struct Actor(ulong Id, Vector2 Position, float Health);
	private readonly double _seconds;
	private readonly float _tolerance;
	private Vector2I? _cell;
	private Vector2 _player;
	private Dictionary<ulong, Actor> _actors = new();
	private double _since;

	public FidelityV3StallMonitor(double seconds, float tolerance)
	{
		_seconds = seconds;
		_tolerance = tolerance;
	}

	public bool Observe(Vector2I cell, Vector2 player, Actor[] actors, double time)
	{
		bool changed = _cell != cell || player.DistanceTo(_player) > _tolerance || actors.Length != _actors.Count
			|| actors.Any(a => !_actors.TryGetValue(a.Id, out Actor previous) || a.Health != previous.Health
				|| a.Position.DistanceTo(previous.Position) > _tolerance);
		if (changed)
		{
			_cell = cell;
			_player = player;
			_actors = actors.ToDictionary(a => a.Id);
			_since = time;
		}
		return time - _since >= _seconds;
	}
}
