using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Reproduce el atasco en esquinas (jugador pegado a una esquina y un enemigo en la diagonal, sin ninguna dirección
/// libre) y comprueba qué reglas lo resuelven. Ejecutar en headless a velocidad fija:
///   godot --headless --fixed-fps 60 --path . res://tests/CornerTrapTest.tscn
///
/// Variantes: sin reglas (línea base: debe reproducir el atasco) y con el retroceso del enemigo, activados con el
/// [Export] RecoilDistance del propio juego (0 = desactivado), así que se prueba el código real.
/// Casos: 4 esquinas × 3 configuraciones de enemigos × BotRuns semillas de bot.
///
/// Comparación original que decidió la regla (36 casos por variante; empuje del jugador ya retirado del juego):
///   ninguna            0/36   10.00 golpes de media   atasco máx 9.00 s
///   solo empuje        0/36   10.00 golpes            atasco máx 9.00 s   (idéntico a la base: empujar "lejos del
///                                                                          enemigo" en una esquina apunta a las
///                                                                          paredes y no mueve al jugador)
///   solo retroceso    36/36    1.92 golpes            atasco máx 0.53 s
///   ambos             36/36    2.67 golpes            atasco máx 0.53 s
/// Se eligió solo el retroceso: es la regla más simple que supera todos los casos.
///   Configuración 1: un enemigo en la diagonal.
///   Configuración 2: ese enemigo y otro pegado a la pared lateral en el eje Z.
///   Configuración 3: ese enemigo y otro pegado a la pared lateral en el eje X.
///
/// El jugador está quieto SettleSeconds (para que el enemigo lo encaje) y después lo controla el bot ControlSeconds.
/// Éxito de un caso: en los últimos ControlSeconds - LateFrom segundos recibe como mucho MaxLateHits golpes (no un
/// golpe por segundo) y no pasa más de MaxStuckSeconds seguidos encajado (casi sin moverse, con un enemigo vivo
/// pegado a él). Sale con código 0 si la variante con retroceso supera todos los casos.
/// </summary>
public partial class CornerTrapTest : Node
{
	private const string PlayerScene = "res://scenes/player/Player.tscn";
	private const string EnemyScene = "res://scenes/enemies/BasicEnemy.tscn";

	[Export] public float RoomSide { get; set; } = 20.0f;
	[Export] public float SettleSeconds { get; set; } = 1.5f;
	[Export] public float ControlSeconds { get; set; } = 10.0f;
	[Export] public float LateFrom { get; set; } = 5.0f;
	[Export] public int MaxLateHits { get; set; } = 1;
	[Export] public float MaxStuckSeconds { get; set; } = 3.0f;
	[Export] public int BotRuns { get; set; } = 3;

	private sealed record Variant(string Name, bool Recoil);

	private sealed class CaseResult
	{
		public Variant Variant;
		public string Description;
		public int TotalHits;
		public int LateHits;
		public int Kills;
		public int Enemies;
		public float MaxStuck;
		public bool Success;
	}

	private static readonly Variant[] Variants =
	{
		new("sin reglas (base)", false),
		new("retroceso enemigo", true),
	};

	private static readonly Vector2[] Corners = { new(1, 1), new(-1, 1), new(1, -1), new(-1, -1) };

	private readonly BotInputApplier _input = new();
	private readonly List<Vector2> _enemyPositions = new();
	private readonly Queue<Vector2> _trail = new();

	private SimLevel _level;
	private BotSettings _settings;
	private BotBrain _brain;
	private PlayerController _player;
	private List<Node3D> _enemies;
	private TaskCompletionSource _done;
	private int _frame;
	private int _settleFrames;
	private int _totalFrames;
	private List<float> _hitTimes;
	private float _stuckRun;
	private float _maxStuck;
	private float _step;
	private bool _running;

	public override void _Ready()
	{
		ProcessPhysicsPriority = -1000;
		Run();
	}

	private async void Run()
	{
		GameplayParameters parameters = GameplayParameters.FromScenes();
		PackedScene enemyScene = GD.Load<PackedScene>(EnemyScene);
		LevelMap map = new() { StartCell = Vector2I.Zero };
		map.Rooms.Add(new RoomData
		{
			Cell = Vector2I.Zero, Type = RoomType.Start, Size = new Vector2(RoomSide, RoomSide), EnemyScene = enemyScene,
		});
		_level = SimLevel.From(map, parameters);
		_settings = new BotSettings();
		_step = parameters.PhysicsDelta;
		_settleFrames = Mathf.RoundToInt(SettleSeconds / _step);
		_totalFrames = _settleFrames + Mathf.RoundToInt(ControlSeconds / _step);

		List<CaseResult> results = new();
		foreach (Variant variant in Variants)
		{
			foreach ((Vector2 corner, int cornerIndex) in Corners.Select((corner, index) => (corner, index)))
			{
				for (int config = 1; config <= 3; config++)
				{
					for (int run = 0; run < BotRuns; run++)
					{
						results.Add(await RunCase(parameters, variant, corner, config, cornerIndex * 100 + config * 10 + run));
					}
				}
			}
		}

		bool anyPasses = Report(results);
		GetTree().Quit(anyPasses ? 0 : 1);
	}

	private async Task<CaseResult> RunCase(GameplayParameters parameters, Variant variant, Vector2 corner, int config,
		int caseSeed)
	{
		Node3D world = new();
		AddChild(world);
		BuildRoom(world, parameters);

		float limit = RoomSide / 2.0f - 0.5f;
		Vector2 playerPosition = corner * limit;

		_player = GD.Load<PackedScene>(PlayerScene).Instantiate<PlayerController>();
		HealthComponent.FindIn(_player).MaxHealth = 1.0e9f;
		_player.Position = new Vector3(playerPosition.X, 0.5f, playerPosition.Y);
		world.AddChild(_player);

		List<Vector2> starts = new() { playerPosition - corner * 3.0f };
		if (config == 2)
		{
			starts.Add(new Vector2(playerPosition.X - corner.X * 3.0f, corner.Y * (RoomSide / 2.0f - 0.45f)));
		}
		else if (config == 3)
		{
			starts.Add(new Vector2(corner.X * (RoomSide / 2.0f - 0.45f), playerPosition.Y - corner.Y * 3.0f));
		}

		PackedScene enemyScene = GD.Load<PackedScene>(EnemyScene);
		_enemies = new List<Node3D>();
		int kills = 0;
		foreach (Vector2 start in starts)
		{
			BasicEnemy enemy = enemyScene.Instantiate<BasicEnemy>();
			enemy.RecoilDistance = variant.Recoil ? enemy.RecoilDistance : 0.0f;
			enemy.Position = new Vector3(start.X, 0.5f, start.Y);
			world.AddChild(enemy);
			HealthComponent.FindIn(enemy).Died += () => kills++;
			_enemies.Add(enemy);
		}

		_hitTimes = new List<float>();
		HealthComponent.FindIn(_player).Damaged += (amount, current) => _hitTimes.Add((_frame - _settleFrames) * _step);

		_brain = new BotBrain(_level, _settings, BotBrain.SeedFor(caseSeed, 0));
		_trail.Clear();
		_stuckRun = 0.0f;
		_maxStuck = 0.0f;
		_frame = 0;
		_done = new TaskCompletionSource();
		_running = true;

		await _done.Task;

		_input.ReleaseAll();
		world.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		int late = _hitTimes.Count(time => time >= LateFrom && time < ControlSeconds);
		return new CaseResult
		{
			Variant = variant,
			Description = $"esquina ({corner.X:+0;-0},{corner.Y:+0;-0}) enemigos {config} bot {caseSeed % 10}",
			TotalHits = _hitTimes.Count(time => time >= 0.0f),
			LateHits = late,
			Kills = kills,
			Enemies = starts.Count,
			MaxStuck = _maxStuck,
			Success = late <= MaxLateHits && _maxStuck < MaxStuckSeconds,
		};
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_running)
		{
			return;
		}

		_frame++;

		_enemyPositions.Clear();
		bool enemyClose = false;
		Vector2 player = new(_player.GlobalPosition.X, _player.GlobalPosition.Z);
		foreach (Node3D enemy in _enemies)
		{
			if (IsInstanceValid(enemy) && !enemy.IsQueuedForDeletion())
			{
				Vector2 position = new(enemy.GlobalPosition.X, enemy.GlobalPosition.Z);
				_enemyPositions.Add(position);
				enemyClose |= position.DistanceTo(player) < 1.4f;
			}
		}

		if (_frame <= _settleFrames)
		{
			_input.ReleaseAll();
		}
		else
		{
			_input.Apply(_brain.Decide(new BotObservation
			{
				Cell = Vector2I.Zero,
				State = RoomState.Combat,
				RoomSize = new Vector2(RoomSide, RoomSide),
				Player = player,
				Enemies = _enemyPositions,
			}));

			// Encajado = casi no se ha movido en el último segundo con un enemigo vivo pegado.
			_trail.Enqueue(player);
			if (_trail.Count > 60)
			{
				bool stuck = enemyClose && _trail.Dequeue().DistanceTo(player) < 0.3f;
				_stuckRun = stuck ? _stuckRun + _step : 0.0f;
				_maxStuck = Mathf.Max(_maxStuck, _stuckRun);
			}
		}

		if (_frame >= _totalFrames)
		{
			_running = false;
			_done.TrySetResult();
		}
	}

	private void BuildRoom(Node3D world, GameplayParameters parameters)
	{
		RoomLayout layout = RoomLayout.Build(new Vector2(RoomSide, RoomSide), Array.Empty<DoorSide>(),
			parameters.WallHeight, parameters.WallThickness, parameters.DoorWidth, parameters.EntryMargin);

		foreach (RoomLayout.Box box in layout.Walls.Append(layout.Floor))
		{
			StaticBody3D body = new() { Position = box.Center, CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
			body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size } });
			world.AddChild(body);
		}
	}

	private bool Report(List<CaseResult> results)
	{
		GD.Print("");
		GD.Print($"=== CornerTrapTest: {results.Count / Variants.Length} casos por variante ({BotRuns} semillas de bot) ===");
		GD.Print($"Éxito = como mucho {MaxLateHits} golpe(s) entre {LateFrom:0}-{ControlSeconds:0} s de control y menos de " +
			$"{MaxStuckSeconds:0.0} s seguidos encajado.");
		GD.Print($"{"variante",-18}{"superados",12}{"golpes (media)",16}{"golpes tardíos",16}{"atasco máx (s)",16}{"enemigos muertos",18}");

		bool anyRulesPass = false;
		foreach (Variant variant in Variants)
		{
			List<CaseResult> own = results.Where(result => result.Variant == variant).ToList();
			int passed = own.Count(result => result.Success);
			GD.Print($"{variant.Name,-18}{passed + "/" + own.Count,12}{F(own.Average(r => r.TotalHits)),16}" +
				$"{F(own.Average(r => r.LateHits)),16}{F(own.Max(r => r.MaxStuck)),16}" +
				$"{own.Sum(r => r.Kills) + "/" + own.Sum(r => r.Enemies),18}");
			anyRulesPass |= variant.Recoil && passed == own.Count;
		}

		foreach (Variant variant in Variants)
		{
			List<CaseResult> failures = results.Where(result => result.Variant == variant && !result.Success).ToList();
			if (failures.Count == 0)
			{
				continue;
			}

			GD.Print("");
			GD.Print($"Casos que fallan en '{variant.Name}' ({failures.Count}):");
			foreach (CaseResult failure in failures.Take(8))
			{
				GD.Print($"  {failure.Description}: golpes {failure.TotalHits}, tardíos {failure.LateHits}, " +
					$"atasco máx {F(failure.MaxStuck)} s, enemigos muertos {failure.Kills}/{failure.Enemies}");
			}
		}

		return anyRulesPass;
	}

	private static string F(double value)
	{
		return value.ToString("0.00", CultureInfo.InvariantCulture);
	}
}
