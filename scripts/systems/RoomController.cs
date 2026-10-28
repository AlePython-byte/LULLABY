using System.Collections.Generic;
using Godot;

/// <summary>
/// Datos que definen una sala (contrato que usa el generador de niveles). Vienen de un RoomData:
///
///   Size                               interior jugable en metros (X, Z). Suelo y paredes se construyen por código.
///   SpawnPoints                        un enemigo por punto (X, Z relativo al centro de la sala).
///   EnemyScene                         escena del enemigo. Sin ella o sin puntos, la sala nace despejada.
///   Type                               Start, Normal, Boss o Treasure; por ahora solo cambia el color del suelo.
///
/// Y de quien la carga (RoomManager), mediante Setup:
///
///   puertas conectadas                 solo existen puertas hacia las salas vecinas; el resto es pared completa.
///   ya despejada                       si es así, no hay enemigos y las puertas están abiertas.
///
/// Parámetros propios (inspector de Room.tscn): WallHeight, WallThickness, DoorWidth, EntryMargin,
/// StartDelay, DoorScene, FloorColor, WallColor.
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

	[Export] public float WallHeight { get; set; } = 3.0f;
	[Export] public float WallThickness { get; set; } = 1.0f;
	[Export] public float DoorWidth { get; set; } = 3.0f;
	[Export] public float EntryMargin { get; set; } = 1.5f;
	[Export] public float StartDelay { get; set; }

	[Export] public PackedScene DoorScene { get; set; }

	[Export] public Color FloorColor { get; set; } = new Color(0.18f, 0.15f, 0.15f, 1.0f);
	[Export] public Color WallColor { get; set; } = new Color(0.25f, 0.2f, 0.22f, 1.0f);

	// Aspecto provisional de los tipos especiales hasta que tengan contenido propio.
	[Export] public Color StartFloorColor { get; set; } = new Color(0.13f, 0.15f, 0.22f, 1.0f);
	[Export] public Color BossFloorColor { get; set; } = new Color(0.32f, 0.07f, 0.07f, 1.0f);
	[Export] public Color TreasureFloorColor { get; set; } = new Color(0.32f, 0.26f, 0.08f, 1.0f);

	public RoomState State { get; private set; } = RoomState.Unvisited;

	private readonly Dictionary<DoorSide, Door> _doors = new();
	private readonly List<Marker3D> _spawnPoints = new();

	private RoomData _data;
	private HashSet<DoorSide> _connectedDoors = new();
	private bool _alreadyCleared;

	private Node3D _geometry;
	private Node3D _doorsRoot;
	private Node3D _enemiesRoot;
	private int _enemiesAlive;
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
		Node spawnRoot = GetNode("SpawnPoints");

		for (int i = 0; i < _data.SpawnPoints.Length; i++)
		{
			Vector2 point = _data.SpawnPoints[i];
			Marker3D marker = new() { Name = $"Spawn{i + 1}", Position = new Vector3(point.X, 0.5f, point.Y) };
			spawnRoot.AddChild(marker);
			_spawnPoints.Add(marker);
		}

		BuildGeometry();
		BuildEntryArea();

		foreach (Door door in _doors.Values)
		{
			door.SetOpen(true);
		}

		if (_alreadyCleared || _data.EnemyScene == null || _spawnPoints.Count == 0)
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

		float margin = WallThickness * 2.0f;
		CreateBox(_geometry, "Floor", new Vector3(_data.Size.X + margin, 1.0f, _data.Size.Y + margin),
			new Vector3(0.0f, -0.5f, 0.0f), floorMaterial);

		foreach (DoorSide side in System.Enum.GetValues<DoorSide>())
		{
			BuildWall(side, wallMaterial);
		}
	}

	private void BuildWall(DoorSide side, StandardMaterial3D material)
	{
		float t = WallThickness;
		float hx = _data.Size.X / 2.0f;
		float hz = _data.Size.Y / 2.0f;

		// Norte y Sur recorren el eje X e incluyen las esquinas; Este y Oeste cubren solo el tramo interior.
		bool alongX = side == DoorSide.North || side == DoorSide.South;
		float sign = side == DoorSide.North || side == DoorSide.West ? -1.0f : 1.0f;
		float half = alongX ? hx + t : hz;
		float fixedCoord = sign * ((alongX ? hz : hx) + t / 2.0f);
		float doorHalf = DoorWidth / 2.0f;

		if (_connectedDoors.Contains(side))
		{
			AddWallSegment(side, "A", -half, -doorHalf, fixedCoord, alongX, material);
			AddWallSegment(side, "B", doorHalf, half, fixedCoord, alongX, material);
			AddDoor(side, fixedCoord, alongX);
		}
		else
		{
			AddWallSegment(side, "", -half, half, fixedCoord, alongX, material);
		}
	}

	private void AddWallSegment(DoorSide side, string suffix, float from, float to, float fixedCoord,
		bool alongX, StandardMaterial3D material)
	{
		float length = to - from;
		float center = (from + to) / 2.0f;
		Vector3 size = alongX
			? new Vector3(length, WallHeight, WallThickness)
			: new Vector3(WallThickness, WallHeight, length);
		Vector3 position = alongX
			? new Vector3(center, WallHeight / 2.0f, fixedCoord)
			: new Vector3(fixedCoord, WallHeight / 2.0f, center);

		CreateBox(_geometry, $"Wall{side}{suffix}", size, position, material);
	}

	private void AddDoor(DoorSide side, float fixedCoord, bool alongX)
	{
		Door door = DoorScene.Instantiate<Door>();
		door.Name = $"Door{side}";
		_doorsRoot.AddChild(door);
		door.Position = DoorPosition(fixedCoord, alongX);
		door.Configure(DoorSize(alongX));
		_doors[side] = door;

		// Disparador dentro del grosor de la pared: el jugador solo lo alcanza si la puerta está abierta.
		Vector3 triggerSize = alongX
			? new Vector3(DoorWidth, 2.0f, WallThickness)
			: new Vector3(WallThickness, 2.0f, DoorWidth);
		Area3D trigger = new()
		{
			Name = $"DoorTrigger{side}",
			Position = DoorPosition(fixedCoord, alongX) with { Y = 1.0f },
			CollisionLayer = 0,
			CollisionMask = CollisionLayers.Player,
		};
		trigger.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = triggerSize } });
		trigger.BodyEntered += body => OnDoorTriggerEntered(side, body);
		_doorsRoot.AddChild(trigger);
	}

	private Vector3 DoorSize(bool alongX)
	{
		return alongX
			? new Vector3(DoorWidth, WallHeight, WallThickness)
			: new Vector3(WallThickness, WallHeight, DoorWidth);
	}

	private Vector3 DoorPosition(float fixedCoord, bool alongX)
	{
		return alongX
			? new Vector3(0.0f, WallHeight / 2.0f, fixedCoord)
			: new Vector3(fixedCoord, WallHeight / 2.0f, 0.0f);
	}

	private static StaticBody3D CreateBox(Node parent, string name, Vector3 size, Vector3 position,
		StandardMaterial3D material)
	{
		StaticBody3D body = new()
		{
			Name = name,
			Position = position,
			CollisionLayer = CollisionLayers.World,
			CollisionMask = 0,
		};
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });

		parent.AddChild(body);
		return body;
	}

	private void BuildEntryArea()
	{
		Area3D area = GetNode<Area3D>("EntryArea");
		CollisionShape3D shape = area.GetNode<CollisionShape3D>("CollisionShape3D");

		float sizeX = Mathf.Max(0.5f, _data.Size.X - 2.0f * EntryMargin);
		float sizeZ = Mathf.Max(0.5f, _data.Size.Y - 2.0f * EntryMargin);
		shape.Shape = new BoxShape3D { Size = new Vector3(sizeX, 2.0f, sizeZ) };
		shape.Position = new Vector3(0.0f, 1.0f, 0.0f);

		area.BodyEntered += OnBodyEntered;
		area.BodyExited += OnBodyExited;
	}

	private void OnDoorTriggerEntered(DoorSide side, Node3D body)
	{
		if (!body.IsInGroup("player") || !_doors[side].IsOpen)
		{
			return;
		}

		EmitSignal(SignalName.DoorCrossed, (int)side);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (!body.IsInGroup("player"))
		{
			return;
		}

		_playerInside = true;
		TryStartCombat();
	}

	private void OnBodyExited(Node3D body)
	{
		if (body.IsInGroup("player"))
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

		// Se cuenta antes de instanciar: si un enemigo muriera durante el bucle, no se daría la sala por despejada.
		_enemiesAlive = _spawnPoints.Count;

		if (StartDelay > 0.0f)
		{
			await ToSignal(GetTree().CreateTimer(StartDelay), SceneTreeTimer.SignalName.Timeout);

			if (!IsInstanceValid(this) || !IsInsideTree())
			{
				return;
			}
		}

		foreach (Marker3D marker in _spawnPoints)
		{
			Node3D enemy = _data.EnemyScene.Instantiate<Node3D>();
			_enemiesRoot.AddChild(enemy);
			enemy.GlobalPosition = marker.GlobalPosition;
			HealthComponent.FindIn(enemy).Died += OnEnemyDied;
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

		EmitSignal(SignalName.RoomCleared);
	}
}
