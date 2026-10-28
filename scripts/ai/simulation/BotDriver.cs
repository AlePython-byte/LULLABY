using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Conecta un BotBrain al jugador real del juego: cada fotograma de física construye la observación con los nodos
/// reales, le pide una decisión y la aplica con BotInputApplier. Se procesa antes que el jugador
/// (ProcessPhysicsPriority muy baja) para que las teclas valgan en ese mismo fotograma.
/// Mide la partida con RunRecorder, igual que el simulador. Necesita vida ilimitada en el jugador (la pone quien
/// lo usa antes de añadir el nivel al árbol).
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public partial class BotDriver : Node
{
	private readonly BotInputApplier _input = new();
	private readonly List<Vector2> _enemies = new();

	private RoomManager _manager;
	private PlayerController _player;
	private HealthComponent _health;
	private SimLevel _level;
	private BotBrain _brain;
	private RunRecorder _recorder;
	private RoomController _room;
	private float _maxSeconds;
	private int _frame;
	private bool _running;

	public event Action<SimulationResult> Finished;

	public override void _Ready()
	{
		ProcessPhysicsPriority = -1000;
	}

	public void Begin(RoomManager manager, SimLevel level, BotBrain brain, float playerMaxHealth, float maxSeconds)
	{
		_manager = manager;
		_player = manager.Player;
		_health = HealthComponent.FindIn(_player);
		_health.Damaged += OnDamaged;
		_level = level;
		_brain = brain;
		_recorder = new RunRecorder(level, playerMaxHealth);
		_maxSeconds = maxSeconds;
		_running = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_running)
		{
			return;
		}

		_frame++;
		float time = _frame * (float)delta;

		RoomController room = _manager.CurrentRoom;
		if (room == null || !IsInstanceValid(room))
		{
			return;
		}

		if (room != _room)
		{
			_room = room;
			_recorder.EnterRoom(room.Data.Cell, time, room.State == RoomState.Cleared);
		}

		if (room.State == RoomState.Cleared)
		{
			_recorder.RoomCleared(time);
			if (room.Data.Cell == _level.BossCell)
			{
				Stop(time, false);
				return;
			}
		}

		if (time >= _maxSeconds)
		{
			Stop(time, true);
			return;
		}

		_enemies.Clear();
		foreach (Node child in room.GetNode("Enemies").GetChildren())
		{
			if (child is Node3D enemy && IsInstanceValid(enemy) && !enemy.IsQueuedForDeletion())
			{
				_enemies.Add(new Vector2(enemy.GlobalPosition.X, enemy.GlobalPosition.Z));
			}
		}

		_input.Apply(_brain.Decide(new BotObservation
		{
			Cell = room.Data.Cell,
			State = room.State,
			RoomSize = room.Data.Size,
			Player = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z),
			Enemies = _enemies,
		}));
	}

	private void OnDamaged(float amount, float current)
	{
		_recorder.Damage(amount);
	}

	private void Stop(float time, bool timedOut)
	{
		_running = false;
		_health.Damaged -= OnDamaged;
		_input.ReleaseAll();
		_recorder.Finish(time, timedOut);
		Finished?.Invoke(_recorder.Result);
	}
}
