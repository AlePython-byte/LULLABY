using System.Collections.Generic;
using Godot;

/// <summary>
/// Datos que definen una sala (contrato que usa el generador de niveles). Vienen de un RoomData:
///
///   Size                               interior jugable en metros (X, Z). Suelo y paredes se construyen por código.
///   SpawnPoints                        un enemigo por punto (X, Z relativo al centro de la sala).
///   EnemyScene                         escena del enemigo. Sin ella o sin puntos, la sala nace despejada.
///   SpawnKinds                         tipo de enemigo de cada punto (generador v2); vacío = todos EnemyScene.
///   Type                               Start, Normal, Boss o Treasure; por ahora solo cambia el color del suelo.
///
/// Y de quien la carga (RoomManager), mediante Setup:
///
///   puertas conectadas                 solo existen puertas hacia las salas vecinas; el resto es pared completa.
///   ya despejada                       si es así, no hay enemigos y las puertas están abiertas.
///
/// Parámetros propios (inspector de Room.tscn): WallHeight, WallThickness, DoorWidth, EntryMargin,
/// StartDelay, SpawnDelay, DoorScene, FloorColor, WallColor.
///
/// Estados: Unvisited → Combat → Cleared. Señales: RoomCleared (murió el último enemigo) y
/// DoorCrossed (el jugador cruzó una puerta abierta; el lado va como int de DoorSide).
/// La sala no reacciona al jugador hasta que se llama a Activate (al terminar el fundido de entrada).
/// </summary>
public enum RoomState
{
	Unvisited,
	Combat,
	Cleared,
}

public enum DoorSide
{
	North,
	East,
	South,
	West,
}

public partial class RoomController : Node3D
{
	[Signal] public delegate void RoomClearedEventHandler();
	[Signal] public delegate void DoorCrossedEventHandler(int side);
	public event System.Action<EnemyBase> BossSpawned;

	/// <summary>
	/// Murió un enemigo de la oleada: índice de su punto de aparición (el mismo orden que RoomData.SpawnPoints) y la posición donde murió.
	/// Se emite justo antes de contar la muerte para despejar la sala. El jefe no pasa por aquí.
	/// </summary>
	public event System.Action<int, Vector3> EnemyKilled;
	// Override exclusivo de la instancia jugable. Nunca se escribe en RoomData ni en las plantillas.
	public PackedScene BossScene { get; set; }
	public EnemyBase BossEnemy { get; private set; }
	private bool UsesBossOverride => _data.Type == RoomType.Boss && BossScene != null;

	[Export] public float WallHeight { get; set; } = 3.0f;
	[Export] public float WallThickness { get; set; } = 1.0f;
	[Export] public float DoorWidth { get; set; } = 3.0f;
	[Export] public float EntryMargin { get; set; } = 1.5f;
	/// <summary>
	/// Retraso de inicio de combate que REPLICA el simulador (LevelSimulator lo lee de Room.tscn con GameplayParameters y los datasets y
	/// modelos de dificultad se generaron con él: 1 s). El juego real no lo usa: lo que se espera de verdad es SpawnDelay. No se cambia
	/// sin regenerar las etiquetas y volver a entrenar.
	/// </summary>
	[Export] public float StartDelay { get; set; }

	/// <summary>
	/// Segundos que tardan en aparecer los enemigos en el juego real tras empezar el combate (la pausa los detiene). Es más corto que
	/// StartDelay a propósito: el simulador espera 0,3 s más que el juego, una diferencia conocida (ver docs/LULLABY.md). Tras la espera, cada
	/// enemigo tiene aún EnemyBase.SpawnDuration (0,25 s) de aparición inerte: empieza a actuar hacia los 0,95 s, casi el 1,0 s del simulador.
	/// Durante la espera se ven anillos en los puntos y los enemigos se instancian ocultos (EnemyBase.Stage) en un nodo aparte.
	/// </summary>
	[Export] public float SpawnDelay { get; set; } = 0.7f;

	// Altura sobre el suelo (cuya cara superior está en y = 0) a la que se dibuja el anillo de aparición.
	private const float MarkerHeight = 0.03f;

	[Export] public PackedScene DoorScene { get; set; }

	[Export] public Color FloorColor { get; set; } = new Color(0.18f, 0.15f, 0.15f, 1.0f);
	[Export] public Color WallColor { get; set; } = new Color(0.25f, 0.2f, 0.22f, 1.0f);

	// Aspecto provisional de los tipos especiales hasta que tengan contenido propio.
	[Export] public Color StartFloorColor { get; set; } = new Color(0.13f, 0.15f, 0.22f, 1.0f);
	[Export] public Color BossFloorColor { get; set; } = new Color(0.32f, 0.07f, 0.07f, 1.0f);
	[Export] public Color TreasureFloorColor { get; set; } = new Color(0.32f, 0.26f, 0.08f, 1.0f);

	/// <summary>
	/// Ambientación visual (RoomDecorator y RoomSurfaces): paredes y suelo con texturas opcionales o colores pastel, marcos y
	/// barrotes de puerta, columnas. Solo visual. Apagada por defecto: tests y simulador ven la sala de siempre. La activa
	/// RoomManager (antes de añadir la sala al árbol) cuando su propio DecorEnabled está activo.
	/// </summary>
	public bool DecorEnabled { get; set; }

	/// <summary>Semilla de la partida para decorar (con la celda, siembra el azar propio de la decoración; ver SeedMix).</summary>
	public int DecorSeed { get; set; }

	// Aspecto de la sala decorada (colores pastel apagados, que se usan cuando no hay textura de ese tipo).
	[ExportGroup("Decoración")]
	[Export] public Color DecorFloorColor { get; set; } = new Color(0.56f, 0.46f, 0.40f, 1.0f);
	[Export] public Color DecorWallColor { get; set; } = new Color(0.68f, 0.60f, 0.64f, 1.0f);
	[Export] public Color DecorStartFloorColor { get; set; } = new Color(0.50f, 0.53f, 0.62f, 1.0f);
	[Export] public Color DecorBossFloorColor { get; set; } = new Color(0.58f, 0.40f, 0.40f, 1.0f);
	[Export] public Color DecorTreasureFloorColor { get; set; } = new Color(0.66f, 0.58f, 0.36f, 1.0f);

	/// <summary>Metros que cubre una repetición de las texturas opcionales de suelo y paredes.</summary>
	[Export] public float TextureTileMeters { get; set; } = 4.0f;

	public RoomState State { get; private set; } = RoomState.Unvisited;
	public RoomData Data => _data;

	private readonly Dictionary<DoorSide, Door> _doors = new();
	private readonly List<Marker3D> _spawnPoints = new();
	private readonly List<Node3D> _staged = new();
	private readonly List<Node3D> _revealedEnemies = new(); // los enemigos ya salidos, por índice de su punto de aparición
	private readonly List<SpawnMarker> _markers = new();

	private RoomData _data;
	private HashSet<DoorSide> _connectedDoors = new();
	private bool _alreadyCleared;

	private Node3D _geometry;
	private Node3D _doorsRoot;
	private Node3D _enemiesRoot;
	private Node3D _projectilesRoot;
	private Node3D _staging;
	private Node3D _markersRoot;
	private bool _revealed;
	private int _enemiesAlive;

	/// <summary>Donde cuelgan los proyectiles enemigos de esta sala (desaparecen con ella o al despejarla).</summary>
	public Node3D ProjectilesRoot => _projectilesRoot ?? _enemiesRoot;
	private bool _active;
	private bool _playerInside;

	// Debe llamarse antes de añadir la sala al árbol.
	public void Setup(RoomData data, IEnumerable<DoorSide> connectedDoors, bool alreadyCleared)
	{
		_data = data;
		_connectedDoors = new HashSet<DoorSide>(connectedDoors);
		_alreadyCleared = alreadyCleared;
	}

	public override void _Ready()
	{
		if (_data == null)
		{
			GD.PushError("RoomController: falta llamar a Setup antes de añadir la sala al árbol.");
			return;
		}

		_geometry = GetNode<Node3D>("Geometry");
		_doorsRoot = GetNode<Node3D>("Doors");
		_enemiesRoot = GetNode<Node3D>("Enemies");
		_projectilesRoot = GetNodeOrNull<Node3D>("Projectiles");
		Node spawnRoot = GetNode("SpawnPoints");

		for (int i = 0; i < _data.SpawnPoints.Length; i++)
		{
			Vector2 point = _data.SpawnPoints[i];
			Marker3D marker = new() { Name = $"Spawn{i + 1}", Position = new Vector3(point.X, 0.5f, point.Y) };
			spawnRoot.AddChild(marker);
			_spawnPoints.Add(marker);
		}

		BuildGeometry();

		foreach (Door door in _doors.Values)
		{
			door.SetOpen(true);
		}

		if (_alreadyCleared || (!UsesBossOverride && (_data.EnemyScene == null || _spawnPoints.Count == 0)))
		{
			State = RoomState.Cleared;
		}
	}

	public void Activate()
	{
		_active = true;
		TryStartCombat();
	}

	private void BuildGeometry()
	{
		Color floorColor = _data.Type switch
		{
			RoomType.Start => StartFloorColor,
			RoomType.Boss => BossFloorColor,
			RoomType.Treasure => TreasureFloorColor,
			_ => FloorColor,
		};
		StandardMaterial3D floorMaterial = new() { AlbedoColor = floorColor };
		StandardMaterial3D wallMaterial = new() { AlbedoColor = WallColor };
		if (DecorEnabled)
		{
			Color decorFloor = _data.Type switch
			{
				RoomType.Start => DecorStartFloorColor,
				RoomType.Boss => DecorBossFloorColor,
				RoomType.Treasure => DecorTreasureFloorColor,
				_ => DecorFloorColor,
			};
			floorMaterial = RoomLook.Floor(_data.Type, decorFloor, TextureTileMeters, DecorSeed, _data.Cell);
			wallMaterial = RoomLook.Wall(_data.Type, DecorWallColor, TextureTileMeters, DecorSeed, _data.Cell);
		}

		RoomLayout layout = RoomLayout.Build(_data.Size, _connectedDoors, WallHeight, WallThickness, DoorWidth,
			EntryMargin);

		CreateBox(_geometry, layout.Floor, floorMaterial);
		foreach (RoomLayout.Box wall in layout.Walls)
		{
			CreateBox(_geometry, wall, wallMaterial);
		}

		foreach (RoomLayout.DoorLayout door in layout.Doors)
		{
			AddDoor(door);
		}

		BuildEntryArea(layout.EntryArea);

		if (DecorEnabled)
		{
			RoomDecorator.Build(this, layout, _data.Size, _doors, _data.Type, _data.Cell, DecorSeed);
		}
	}

	private void AddDoor(RoomLayout.DoorLayout layout)
	{
		DoorSide side = layout.Side;
		Door door = DoorScene.Instantiate<Door>();
		door.Name = layout.Door.Name;
		_doorsRoot.AddChild(door);
		door.Position = layout.Door.Center;
		door.Configure(layout.Door.Size);
		_doors[side] = door;

		Area3D trigger = new()
		{
			Name = layout.Trigger.Name,
			Position = layout.Trigger.Center,
			CollisionLayer = 0,
			CollisionMask = CollisionLayers.Player,
		};
		trigger.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = layout.Trigger.Size } });
		trigger.BodyEntered += body => OnDoorTriggerEntered(side, body);
		_doorsRoot.AddChild(trigger);
	}

	private static StaticBody3D CreateBox(Node parent, RoomLayout.Box box, StandardMaterial3D material)
	{
		StaticBody3D body = new()
		{
			Name = box.Name,
			Position = box.Center,
			CollisionLayer = CollisionLayers.World,
			CollisionMask = 0,
		};
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size } });
		body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = box.Size }, MaterialOverride = material });

		parent.AddChild(body);
		return body;
	}

	private void BuildEntryArea(RoomLayout.Box entry)
	{
		Area3D area = GetNode<Area3D>("EntryArea");
		CollisionShape3D shape = area.GetNode<CollisionShape3D>("CollisionShape3D");
		shape.Shape = new BoxShape3D { Size = entry.Size };
		shape.Position = entry.Center;

		area.BodyEntered += OnBodyEntered;
		area.BodyExited += OnBodyExited;
	}

	private void OnDoorTriggerEntered(DoorSide side, Node3D body)
	{
		if (!body.IsInGroup(GameNames.PlayerGroup) || !_doors[side].IsOpen)
		{
			return;
		}

		EmitSignal(SignalName.DoorCrossed, (int)side);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (!body.IsInGroup(GameNames.PlayerGroup))
		{
			return;
		}

		_playerInside = true;
		TryStartCombat();
	}

	private void OnBodyExited(Node3D body)
	{
		if (body.IsInGroup(GameNames.PlayerGroup))
		{
			_playerInside = false;
		}
	}

	private void TryStartCombat()
	{
		if (_active && _playerInside && State == RoomState.Unvisited)
		{
			StartCombat();
		}
	}

	private async void StartCombat()
	{
		State = RoomState.Combat;
		foreach (Door door in _doors.Values)
		{
			door.SetOpen(false);
		}

		// Una sola voz por evento (no una por puerta); las salas sin puertas (aisladas en pruebas) no suenan.
		if (_doors.Count > 0)
		{
			AudioManager.Play(SoundCatalog.DoorCloses);
		}

		// Se cuenta antes de instanciar: si un enemigo muriera durante el bucle, no se daría la sala por despejada.
		_enemiesAlive = UsesBossOverride ? 1 : _spawnPoints.Count;

		if (UsesBossOverride)
		{
			// Aparece al activar el combate; su coordinador controla la pausa inicial, sin sumar StartDelay.
			// La entrada puede venir de BodyEntered: se instancia fuera del callback de física.
			Callable.From(SpawnBoss).CallDeferred();
			return;
		}

		// Durante la espera: un anillo en cada punto y los enemigos se instancian ocultos, repartidos en varios fotogramas.
		ShowSpawnMarkers();
		StageOverFrames();

		if (SpawnDelay > 0.0f)
		{
			// processAlways = false: la pausa también detiene este retraso.
			await ToSignal(GetTree().CreateTimer(SpawnDelay, false), SceneTreeTimer.SignalName.Timeout);
		}
		else
		{
			// Sin espera también se deja pasar un fotograma: la entrada puede venir de BodyEntered y la física no se toca en ese callback.
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		if (!IsInstanceValid(this) || !IsInsideTree())
		{
			return;
		}

		RevealEnemies();
	}

	// Un anillo de crayón por punto de aparición, que crece durante SpawnDelay. Cuelgan de un nodo propio, no de Enemies.
	private void ShowSpawnMarkers()
	{
		_markersRoot = new Node3D { Name = "SpawnMarkers" };
		AddChild(_markersRoot);

		foreach (Marker3D point in _spawnPoints)
		{
			SpawnMarker marker = new() { GrowTime = SpawnDelay, Position = new Vector3(point.Position.X, MarkerHeight, point.Position.Z) };
			_markersRoot.AddChild(marker);
			_markers.Add(marker);
		}
	}

	// Instancia un enemigo por fotograma mientras dura la espera. Lo que no dé tiempo a instanciar se completa en RevealEnemies.
	private async void StageOverFrames()
	{
		_staging = new Node3D { Name = "EnemyStaging" };
		AddChild(_staging);

		while (_staged.Count < _spawnPoints.Count)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			if (!IsInstanceValid(this) || !IsInsideTree() || _revealed)
			{
				return;
			}

			StageNext();
		}
	}

	// El enemigo nace oculto, sin procesar y sin colisión, en un nodo aparte: Enemies sigue vacío hasta que sale de verdad.
	private void StageNext()
	{
		Node3D node = EnemySceneFor(_staged.Count).Instantiate<Node3D>();
		if (node is EnemyBase enemy)
		{
			enemy.Stage();
			_staging.AddChild(enemy);
		}

		_staged.Add(node);
	}

	// A los SpawnDelay segundos los enemigos salen: pasan a Enemies en su punto y empiezan la aparición inerte (EnemyBase.BeginSpawn).
	private void RevealEnemies()
	{
		_revealed = true;
		while (_staged.Count < _spawnPoints.Count)
		{
			StageNext();
		}

		for (int i = 0; i < _spawnPoints.Count; i++)
		{
			Node3D enemy = _staged[i];
			enemy.GetParent()?.RemoveChild(enemy);
			_enemiesRoot.AddChild(enemy);
			enemy.GlobalPosition = _spawnPoints[i].GlobalPosition;
			// La conexión solo captura el índice y la sala, no el nodo del enemigo: un cierre que sujeta el envoltorio de un nodo dentro de la
			// conexión de su señal provocaba errores «Handle is not initialized» (carrera con el recolector) al cambiar de escena.
			int index = i;
			_revealedEnemies.Add(enemy);
			HealthComponent health = HealthComponent.FindIn(enemy);
			health.Died += () => NotifyEnemyKilled(index);
			health.Died += OnEnemyDied;
			(enemy as EnemyBase)?.BeginSpawn();
			_markers[i].Finish();
		}

		_staged.Clear();
		_staging.QueueFree();

		// Una sola petición por oleada, no una por enemigo.
		if (_spawnPoints.Count > 0)
		{
			AudioManager.Play(SoundCatalog.EnemySpawns);
		}
	}

	private void SpawnBoss()
	{
		if (!IsInsideTree() || IsQueuedForDeletion() || State != RoomState.Combat) return;
		BossEnemy = BossScene.Instantiate<EnemyBase>();
		BossEnemy.Position = new Vector3(0.0f, 0.5f, 0.0f);
		HealthComponent.FindIn(BossEnemy).Died += OnEnemyDied;
		_enemiesRoot.AddChild(BossEnemy);
		BossSpawned?.Invoke(BossEnemy);
		AudioManager.PlayFrom(SoundCatalog.BossAppears, BossEnemy);
	}

	// Con SpawnKinds (generador v2) cada punto tiene su tipo; si está vacío (v1), todos salen de EnemyScene.
	private PackedScene EnemySceneFor(int index)
	{
		return _data.SpawnKinds.Length == _data.SpawnPoints.Length
			? EnemyKinds.Scene((EnemyKind)_data.SpawnKinds[index])
			: _data.EnemyScene;
	}

	private void NotifyEnemyKilled(int index)
	{
		if (index >= 0 && index < _revealedEnemies.Count && IsInstanceValid(_revealedEnemies[index]))
		{
			EnemyKilled?.Invoke(index, _revealedEnemies[index].GlobalPosition);
		}
	}

	private void OnEnemyDied()
	{
		_enemiesAlive--;

		if (_enemiesAlive > 0 || State != RoomState.Combat)
		{
			return;
		}

		State = RoomState.Cleared;
		foreach (Door door in _doors.Values)
		{
			door.SetOpen(true);
		}

		if (_doors.Count > 0)
		{
			AudioManager.Play(SoundCatalog.DoorOpens);
		}

		// Los proyectiles enemigos que sigan en vuelo desaparecen al despejar la sala.
		foreach (Node projectile in ProjectilesRoot.GetChildren())
		{
			projectile.QueueFree();
		}

		EmitSignal(SignalName.RoomCleared);
	}
}
