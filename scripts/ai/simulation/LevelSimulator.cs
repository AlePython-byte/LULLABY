using System.Collections.Generic;
using Godot;

/// <summary>
/// Simulador determinista de una partida del bot en un nivel, en C# puro (sin escenas ni física de Godot).
/// Replica las reglas del juego con los valores de GameplayParameters y la geometría de RoomLayout.
///
/// Paso fijo de PhysicsDelta. Orden de cada paso (el del juego: scripts de física y después el paso de física):
///   1. El bot decide con lo que ve (BotBrain, el mismo cerebro que en el juego real).
///   2. El jugador se mueve y dispara (salvo durante un fundido). Velocidad = dirección normalizada · PlayerSpeed.
///      El disparo: la primera lágrima sale al instante y luego cada 1 / ShotsPerSecond; nace a TearSpawnDistance.
///   3. Cada enemigo, en orden, va en línea recta hacia el jugador; si su movimiento lo bloquea el jugador y su
///      espera entre golpes terminó, golpea (ContactDamage) y reinicia la espera (HitCooldown). Si el golpe hizo
///      daño (el jugador no estaba invulnerable), el enemigo retrocede RecoilDistance metros alejándose del jugador
///      durante ceil(RecoilDuration / paso) fotogramas, a velocidad constante, empezando en el fotograma siguiente.
///      Durante el retroceso no persigue ni golpea, pero se mueve con las mismas colisiones.
///   4. Las lágrimas avanzan; caducan a TearLifetime; golpean el primer cuerpo que solapan (pared, puerta cerrada o
///      enemigo). Las recién disparadas se comprueban en su punto de salida (muere si nace dentro de una pared).
///   5. Los enemigos sin vida desaparecen; al morir el último, la sala queda despejada y se abren las puertas.
///   6. Temporizadores: invulnerabilidad del jugador y StartDelay hasta que aparecen los enemigos.
///   7. Áreas: entrada de la sala (empieza el combate si está activa) y disparadores de puertas abiertas.
///   Transición: tras cruzar una puerta el jugador da un paso más, queda congelado ceil(FadeDuration / paso)
///   fotogramas, cambia de sala (desaparecen las lágrimas y aparece a ArrivalOffset de la puerta opuesta), sigue
///   congelado otros tantos y la sala se activa.
///
/// Aproximaciones (documentadas para la prueba de fidelidad):
///   - Todo ocurre en el plano XZ: las cajas del jugador y los enemigos no giran (en el juego solo gira la malla) y
///     la altura no influye porque todo está a y = 0.5.
///   - El movimiento resuelve cada eje por separado contra cajas alineadas (paredes, puertas cerradas, jugador y
///     enemigos). Se parece a MoveAndSlide con cajas alineadas, pero no reproduce el margen de seguridad de Jolt,
///     sus correcciones de penetración ni el resultado exacto en esquinas; un cuerpo que ya solapa a otro lo ignora.
///   - En Godot las señales de áreas llegan al fotograma siguiente; aquí se aplican en el mismo paso (daño de las
///     lágrimas, entrada en la sala y cruce de puertas adelantados un fotograma).
///   - Si una lágrima solapa varios cuerpos a la vez gana el de centro más cercano (en Godot el orden no está
///     definido).
///   - Los enemigos muertos desaparecen al final del paso (en Godot, al final del fotograma siguiente).
///   - El disparo replica PlayerShooter suponiendo que el bot pulsa como mucho una flecha a la vez (siempre es así).
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class LevelSimulator
{
	/// <summary>2: añade el retroceso del enemigo tras un golpe que hace daño (v1 no lo tenía).</summary>
	public const int Version = 2;

	private const float Epsilon = 0.0001f;
	private const int StaticTag = -1;
	private const int PlayerTag = -2;
	private const int NoTag = int.MinValue;

	private static readonly Vector2[] ShotDirections =
	{
		new(-1.0f, 0.0f), new(1.0f, 0.0f), new(0.0f, -1.0f), new(0.0f, 1.0f),
	};

	private struct Box2
	{
		public Vector2 Min;
		public Vector2 Max;

		public static Box2 Around(Vector2 center, Vector2 half)
		{
			return new Box2 { Min = center - half, Max = center + half };
		}

		public static Box2 From(RoomLayout.Box box)
		{
			return Around(new Vector2(box.Center.X, box.Center.Z), new Vector2(box.Size.X, box.Size.Z) / 2.0f);
		}

		public readonly Vector2 Center => (Min + Max) / 2.0f;

		public readonly bool Overlaps(Box2 other)
		{
			return Min.X < other.Max.X - Epsilon && Max.X > other.Min.X + Epsilon
				&& Min.Y < other.Max.Y - Epsilon && Max.Y > other.Min.Y + Epsilon;
		}

		public readonly bool Overlaps(Vector2 center, float radius)
		{
			float dx = Mathf.Max(Mathf.Max(Min.X - center.X, 0.0f), center.X - Max.X);
			float dy = Mathf.Max(Mathf.Max(Min.Y - center.Y, 0.0f), center.Y - Max.Y);
			return dx * dx + dy * dy < radius * radius;
		}
	}

	private sealed class Enemy
	{
		public Vector2 Position;
		public float Health;
		public float HitTimer;
		public int RecoilFrames;
		public Vector2 RecoilVelocity;
		public GameplayParameters.EnemyStats Stats;
		public bool Dead;
	}

	private sealed class Tear
	{
		public Vector2 Position;
		public Vector2 Direction;
		public float Age;
	}

	private enum Phase
	{
		None,
		Pending,
		FadeOut,
		FadeIn,
	}

	private readonly GameplayParameters _parameters;
	private readonly SimLevel _level;
	private readonly BotBrain _bot;
	private readonly RunRecorder _recorder;
	private readonly float _dt;
	private readonly int _fadeFrames;

	private readonly HashSet<Vector2I> _cleared = new();
	private readonly List<Box2> _walls = new();
	private readonly List<(DoorSide Side, Box2 Door, Box2 Trigger)> _doors = new();
	private readonly List<bool> _insideTrigger = new();
	private readonly List<Enemy> _enemies = new();
	private readonly List<Tear> _tears = new();
	private readonly List<Tear> _newTears = new();
	private readonly List<Box2> _obstacles = new();
	private readonly List<int> _tags = new();
	private readonly List<Vector2> _visibleEnemies = new();

	private Vector2I _cell;
	private SimLevel.Room _room;
	private Box2 _entry;
	private RoomState _state;
	private bool _doorsOpen;
	private bool _active;
	private bool _spawnPending;
	private double _spawnTimer;
	private int _enemiesAlive;

	private Vector2 _player;
	private float _invulnerable;
	private float _shotCooldown;

	private Phase _phase;
	private int _phaseFrames;
	private DoorSide _exitSide;
	private int _frame;

	public LevelSimulator(GameplayParameters parameters, SimLevel level, BotBrain bot)
	{
		_parameters = parameters;
		_level = level;
		_bot = bot;
		_recorder = new RunRecorder(level, parameters.PlayerMaxHealth);
		_dt = parameters.PhysicsDelta;
		_fadeFrames = Mathf.CeilToInt(parameters.FadeDuration / _dt - Epsilon);
	}

	private float Time => _frame * _dt;

	public SimulationResult Run(float maxSeconds)
	{
		_player = _parameters.PlayerStart;
		LoadRoom(_level.StartCell);
		_active = true;

		while (true)
		{
			Step();

			if (_cell == _level.BossCell && _state == RoomState.Cleared)
			{
				_recorder.Finish(Time, false);
				break;
			}

			if (Time >= maxSeconds)
			{
				_recorder.Finish(Time, true);
				break;
			}
		}

		return _recorder.Result;
	}

	private void Step()
	{
		_frame++;

		_visibleEnemies.Clear();
		foreach (Enemy enemy in _enemies)
		{
			_visibleEnemies.Add(enemy.Position);
		}

		BotAction action = _bot.Decide(new BotObservation
		{
			Cell = _cell,
			State = _state,
			RoomSize = _room.Size,
			Player = _player,
			Enemies = _visibleEnemies,
		});

		if (_phase != Phase.FadeOut && _phase != Phase.FadeIn)
		{
			MovePlayer(action.Move);
			UpdateShooter(action.Shoot);
		}

		MoveEnemies();
		UpdateTears();
		RemoveDeadEnemies();

		_invulnerable = Mathf.Max(0.0f, _invulnerable - _dt);
		UpdateSpawnTimer();
		AdvanceTransition();
		CheckAreas();
	}

	private void MovePlayer(Vector2 move)
	{
		if (move == Vector2.Zero)
		{
			return;
		}

		Vector2 direction = move.Length() > 1.0f ? move.Normalized() : move;
		BuildObstacles(includePlayer: false, excludedEnemy: -1);
		Move(ref _player, _parameters.PlayerSize / 2.0f, direction * _parameters.PlayerSpeed * _dt, NoTag);
	}

	private void UpdateShooter(int shoot)
	{
		_shotCooldown = Mathf.Max(0.0f, _shotCooldown - _dt);
		if (shoot == BotBrain.ShootNone || _shotCooldown > 0.0f)
		{
			return;
		}

		Vector2 direction = ShotDirections[shoot];
		_newTears.Add(new Tear { Position = _player + direction * _parameters.TearSpawnDistance, Direction = direction });
		_shotCooldown = 1.0f / _parameters.ShotsPerSecond;
	}

	private void MoveEnemies()
	{
		for (int i = 0; i < _enemies.Count; i++)
		{
			Enemy enemy = _enemies[i];
			enemy.HitTimer = Mathf.Max(0.0f, enemy.HitTimer - _dt);

			if (enemy.RecoilFrames > 0)
			{
				enemy.RecoilFrames--;
				BuildObstacles(includePlayer: true, excludedEnemy: i);
				Move(ref enemy.Position, enemy.Stats.Size / 2.0f, enemy.RecoilVelocity * _dt, NoTag);
				continue;
			}

			Vector2 toPlayer = _player - enemy.Position;
			Vector2 velocity = toPlayer.LengthSquared() > 0.0001f ? toPlayer.Normalized() * enemy.Stats.Speed : Vector2.Zero;
			if (velocity == Vector2.Zero)
			{
				continue;
			}

			BuildObstacles(includePlayer: true, excludedEnemy: i);
			bool blockedByPlayer = Move(ref enemy.Position, enemy.Stats.Size / 2.0f, velocity * _dt, PlayerTag);

			if (blockedByPlayer && enemy.HitTimer <= 0.0f)
			{
				bool damaged = PlayerTakeHit(enemy.Stats.ContactDamage);
				enemy.HitTimer = enemy.Stats.HitCooldown;

				if (damaged && enemy.Stats.RecoilDistance > 0.0f)
				{
					StartRecoil(enemy);
				}
			}
		}
	}

	private void StartRecoil(Enemy enemy)
	{
		Vector2 away = enemy.Position - _player;
		if (away.LengthSquared() < 0.000001f)
		{
			away = new Vector2(1.0f, 0.0f);
		}

		enemy.RecoilFrames = Mathf.Max(1, Mathf.CeilToInt(enemy.Stats.RecoilDuration / _dt - 0.0001f));
		enemy.RecoilVelocity = away.Normalized() * (enemy.Stats.RecoilDistance / (enemy.RecoilFrames * _dt));
	}

	private void UpdateTears()
	{
		for (int i = _tears.Count - 1; i >= 0; i--)
		{
			Tear tear = _tears[i];
			tear.Position += tear.Direction * _parameters.TearSpeed * _dt;
			tear.Age += _dt;

			if (tear.Age >= _parameters.TearLifetime || Hit(tear))
			{
				_tears.RemoveAt(i);
			}
		}

		foreach (Tear tear in _newTears)
		{
			if (!Hit(tear))
			{
				_tears.Add(tear);
			}
		}

		_newTears.Clear();
	}

	// Golpea el cuerpo solapado de centro más cercano. Devuelve true si la lágrima se consume.
	private bool Hit(Tear tear)
	{
		float radius = _parameters.TearRadius;
		float best = float.MaxValue;
		int bestEnemy = -1;
		bool hit = false;

		foreach (Box2 wall in _walls)
		{
			Consider(wall, -1);
		}

		if (!_doorsOpen)
		{
			foreach ((DoorSide _, Box2 door, Box2 _) in _doors)
			{
				Consider(door, -1);
			}
		}

		for (int i = 0; i < _enemies.Count; i++)
		{
			Enemy enemy = _enemies[i];
			if (!enemy.Dead)
			{
				Consider(Box2.Around(enemy.Position, enemy.Stats.Size / 2.0f), i);
			}
		}

		if (hit && bestEnemy >= 0)
		{
			Enemy enemy = _enemies[bestEnemy];
			enemy.Health -= _parameters.TearDamage;
			enemy.Dead = enemy.Health <= 0.0f;
		}

		return hit;

		void Consider(Box2 box, int enemyIndex)
		{
			if (!box.Overlaps(tear.Position, radius))
			{
				return;
			}

			float distance = tear.Position.DistanceSquaredTo(box.Center);
			if (distance < best)
			{
				best = distance;
				bestEnemy = enemyIndex;
				hit = true;
			}
		}
	}

	private void RemoveDeadEnemies()
	{
		for (int i = _enemies.Count - 1; i >= 0; i--)
		{
			if (!_enemies[i].Dead)
			{
				continue;
			}

			_enemies.RemoveAt(i);
			_enemiesAlive--;

			if (_enemiesAlive <= 0 && _state == RoomState.Combat)
			{
				_state = RoomState.Cleared;
				_doorsOpen = true;
				_cleared.Add(_cell);
				_recorder.RoomCleared(Time);
			}
		}
	}

	private bool PlayerTakeHit(float damage)
	{
		if (_invulnerable > 0.0f)
		{
			return false;
		}

		_recorder.Damage(damage);
		_invulnerable = _parameters.PlayerInvulnerability;
		return true;
	}

	private void UpdateSpawnTimer()
	{
		if (!_spawnPending)
		{
			return;
		}

		_spawnTimer -= _dt;
		if (_spawnTimer <= 0.0)
		{
			SpawnEnemies();
		}
	}

	private void SpawnEnemies()
	{
		_spawnPending = false;
		foreach (Vector2 point in _room.SpawnPoints)
		{
			_enemies.Add(new Enemy { Position = point, Health = _room.Enemy.MaxHealth, Stats = _room.Enemy });
		}
	}

	private void AdvanceTransition()
	{
		switch (_phase)
		{
			case Phase.Pending:
				_phase = Phase.FadeOut;
				_phaseFrames = _fadeFrames;
				break;

			case Phase.FadeOut:
				if (--_phaseFrames <= 0)
				{
					LoadRoom(_cell + _exitSide.GridOffset());
					PlacePlayer(_exitSide);
					_phase = Phase.FadeIn;
					_phaseFrames = _fadeFrames;
				}

				break;

			case Phase.FadeIn:
				if (--_phaseFrames <= 0)
				{
					_phase = Phase.None;
					_active = true;
				}

				break;
		}
	}

	private void CheckAreas()
	{
		Box2 playerBox = Box2.Around(_player, _parameters.PlayerSize / 2.0f);

		if (playerBox.Overlaps(_entry) && _active && _state == RoomState.Unvisited)
		{
			StartCombat();
		}

		for (int i = 0; i < _doors.Count; i++)
		{
			bool inside = playerBox.Overlaps(_doors[i].Trigger);
			bool entered = inside && !_insideTrigger[i];
			_insideTrigger[i] = inside;

			if (entered && _doorsOpen && _phase == Phase.None)
			{
				_phase = Phase.Pending;
				_exitSide = _doors[i].Side;
			}
		}
	}

	private void StartCombat()
	{
		_state = RoomState.Combat;
		_doorsOpen = false;
		_enemiesAlive = _room.SpawnPoints.Length;

		if (_parameters.StartDelay > 0.0f)
		{
			_spawnPending = true;
			_spawnTimer = _parameters.StartDelay;
		}
		else
		{
			SpawnEnemies();
		}
	}

	private void LoadRoom(Vector2I cell)
	{
		_cell = cell;
		_room = _level[cell];

		RoomLayout layout = RoomLayout.Build(_room.Size, _room.Doors, _parameters.WallHeight, _parameters.WallThickness,
			_parameters.DoorWidth, _parameters.EntryMargin);

		_walls.Clear();
		foreach (RoomLayout.Box wall in layout.Walls)
		{
			_walls.Add(Box2.From(wall));
		}

		_doors.Clear();
		_insideTrigger.Clear();
		foreach (RoomLayout.DoorLayout door in layout.Doors)
		{
			_doors.Add((door.Side, Box2.From(door.Door), Box2.From(door.Trigger)));
			_insideTrigger.Add(false);
		}

		_entry = Box2.From(layout.EntryArea);
		_doorsOpen = true;
		_state = _cleared.Contains(cell) || _room.StartsCleared ? RoomState.Cleared : RoomState.Unvisited;
		_active = false;
		_spawnPending = false;
		_enemies.Clear();
		_tears.Clear();
		_newTears.Clear();

		_recorder.EnterRoom(cell, Time, _state == RoomState.Cleared);
	}

	private void PlacePlayer(DoorSide exitSide)
	{
		Vector3 direction3 = exitSide.Direction();
		Vector2 direction = new(direction3.X, direction3.Z);
		float halfExtent = direction.X != 0.0f ? _room.Size.X / 2.0f : _room.Size.Y / 2.0f;
		_player = -direction * (halfExtent - _parameters.ArrivalOffset);
	}

	private void BuildObstacles(bool includePlayer, int excludedEnemy)
	{
		_obstacles.Clear();
		_tags.Clear();

		foreach (Box2 wall in _walls)
		{
			_obstacles.Add(wall);
			_tags.Add(StaticTag);
		}

		if (!_doorsOpen)
		{
			foreach ((DoorSide _, Box2 door, Box2 _) in _doors)
			{
				_obstacles.Add(door);
				_tags.Add(StaticTag);
			}
		}

		if (includePlayer)
		{
			_obstacles.Add(Box2.Around(_player, _parameters.PlayerSize / 2.0f));
			_tags.Add(PlayerTag);
		}

		for (int i = 0; i < _enemies.Count; i++)
		{
			if (i != excludedEnemy && !_enemies[i].Dead)
			{
				_obstacles.Add(Box2.Around(_enemies[i].Position, _enemies[i].Stats.Size / 2.0f));
				_tags.Add(i);
			}
		}
	}

	// Mueve una caja eje por eje contra los obstáculos. Devuelve true si el obstáculo con watchTag la frenó.
	private bool Move(ref Vector2 position, Vector2 half, Vector2 motion, int watchTag)
	{
		bool blocked = false;

		if (motion.X != 0.0f)
		{
			float target = position.X + motion.X;
			int limiter = NoTag;
			for (int i = 0; i < _obstacles.Count; i++)
			{
				Box2 obstacle = _obstacles[i];
				if (!(position.Y - half.Y < obstacle.Max.Y - Epsilon && position.Y + half.Y > obstacle.Min.Y + Epsilon))
				{
					continue;
				}

				if (motion.X > 0.0f)
				{
					if (obstacle.Min.X < position.X + half.X - Epsilon)
					{
						continue;
					}

					float limit = obstacle.Min.X - half.X;
					if (limit < target)
					{
						target = limit;
						limiter = _tags[i];
					}
				}
				else
				{
					if (obstacle.Max.X > position.X - half.X + Epsilon)
					{
						continue;
					}

					float limit = obstacle.Max.X + half.X;
					if (limit > target)
					{
						target = limit;
						limiter = _tags[i];
					}
				}
			}

			position.X = target;
			blocked |= limiter == watchTag;
		}

		if (motion.Y != 0.0f)
		{
			float target = position.Y + motion.Y;
			int limiter = NoTag;
			for (int i = 0; i < _obstacles.Count; i++)
			{
				Box2 obstacle = _obstacles[i];
				if (!(position.X - half.X < obstacle.Max.X - Epsilon && position.X + half.X > obstacle.Min.X + Epsilon))
				{
					continue;
				}

				if (motion.Y > 0.0f)
				{
					if (obstacle.Min.Y < position.Y + half.Y - Epsilon)
					{
						continue;
					}

					float limit = obstacle.Min.Y - half.Y;
					if (limit < target)
					{
						target = limit;
						limiter = _tags[i];
					}
				}
				else
				{
					if (obstacle.Max.Y > position.Y - half.Y + Epsilon)
					{
						continue;
					}

					float limit = obstacle.Max.Y + half.Y;
					if (limit > target)
					{
						target = limit;
						limiter = _tags[i];
					}
				}
			}

			position.Y = target;
			blocked |= limiter == watchTag;
		}

		return blocked;
	}
}
