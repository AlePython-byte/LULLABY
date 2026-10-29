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
	private readonly List<EnemyKind> _kinds = new();
	private readonly List<bool> _lit = new();
	private readonly List<Vector2> _projectiles = new();
	private readonly List<Vector2> _projectileVelocities = new();

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
		_kinds.Clear();
		_lit.Clear();
		foreach (Node child in room.GetNode("Enemies").GetChildren())
		{
			if (child is Node3D enemy && IsInstanceValid(enemy) && !enemy.IsQueuedForDeletion())
			{
				_enemies.Add(new Vector2(enemy.GlobalPosition.X, enemy.GlobalPosition.Z));
				EnemyBase typed = enemy as EnemyBase;
				_kinds.Add(typed?.Kind ?? EnemyKind.Chaser);
				_lit.Add(typed?.Behavior<FuseBehavior>()?.IsLit ?? false);
			}
		}

		_projectiles.Clear();
		_projectileVelocities.Clear();
		foreach (Node child in room.ProjectilesRoot.GetChildren())
		{
			if (child is EnemyProjectile projectile && IsInstanceValid(projectile) && !projectile.IsQueuedForDeletion())
			{
				_projectiles.Add(new Vector2(projectile.GlobalPosition.X, projectile.GlobalPosition.Z));
				_projectileVelocities.Add(new Vector2(projectile.Velocity.X, projectile.Velocity.Z));
			}
		}

		_input.Apply(_brain.Decide(new BotObservation
		{
			Cell = room.Data.Cell,
			State = room.State,
			RoomSize = room.Data.Size,
			Player = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z),
			Enemies = _enemies,
			EnemyKinds = _kinds,
			FuseLit = _lit,
			Projectiles = _projectiles,
			ProjectileVelocities = _projectileVelocities,
		}));
	}

	private void OnDamaged(float amount, float current)
	{
		DamageSource source = SourceOfCurrentHit();
		_recorder.Damage(amount, source, source == DamageSource.Contact ? NearestEnemyKind() : EnemyKind.Chaser);
	}

	// Diagnóstico: en un golpe por contacto el enemigo que golpea está tocando al jugador; se toma el más cercano.
	private EnemyKind NearestEnemyKind()
	{
		EnemyKind kind = EnemyKind.Chaser;
		float best = float.MaxValue;
		if (_room == null || !IsInstanceValid(_room))
		{
			return kind;
		}

		foreach (Node child in _room.GetNode("Enemies").GetChildren())
		{
			if (child is EnemyBase enemy && IsInstanceValid(enemy))
			{
				float distance = enemy.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition);
				if (distance < best)
				{
					best = distance;
					kind = enemy.Kind;
				}
			}
		}

		return kind;
	}

	// Diagnóstico: quién llamó a PlayerHealth.TakeHit, mirando la pila (la señal Damaged se emite dentro de esa llamada).
	// Solo para el desglose por fuente de la prueba de fidelidad; no cambia ninguna medida.
	private static DamageSource SourceOfCurrentHit()
	{
		foreach (System.Diagnostics.StackFrame frame in new System.Diagnostics.StackTrace(false).GetFrames())
		{
			Type type = frame.GetMethod()?.DeclaringType;
			for (; type != null; type = type.DeclaringType)
			{
				if (type == typeof(EnemyProjectile))
				{
					return DamageSource.Projectile;
				}

				if (type == typeof(FuseBehavior))
				{
					return DamageSource.Explosion;
				}
			}
		}

		return DamageSource.Contact;
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
