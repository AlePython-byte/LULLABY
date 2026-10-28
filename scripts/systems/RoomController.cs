using System.Collections.Generic;
using Godot;

/// <summary>
/// Datos que definen una sala (contrato que usará el generador de niveles):
///
///   RoomSize                           interior jugable en metros (X, Z). Suelo y paredes se construyen por código.
///   WallHeight / WallThickness         altura y grosor de las paredes.
///   HasDoorNorth/East/South/West       qué puertas existen. Un lado sin puerta es pared completa.
///   DoorWidth                          ancho del hueco de cada puerta.
///   Nodos Marker3D bajo "SpawnPoints"  un enemigo por marcador (se usa su GlobalPosition).
///   EnemyScene                         escena del enemigo que aparece en cada marcador.
///   StartDelay                         segundos entre entrar y que aparezcan los enemigos.
///   DoorPlugsActive                    tapones de colisión fuera de cada hueco (temporal).
///   FloorColor / WallColor             aspecto provisional.
///
/// Estados: Unvisited → Combat → Cleared. Una sala sin enemigos nace Cleared.
/// Señal RoomCleared: se emite al morir el último enemigo.
/// Los nodos Geometry, Doors, SpawnPoints, Enemies y EntryArea vienen de Room.tscn.
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

	[Export] public Vector2 RoomSize { get; set; } = new Vector2(20.0f, 20.0f);
	[Export] public float WallHeight { get; set; } = 3.0f;
	[Export] public float WallThickness { get; set; } = 1.0f;
	[Export] public float DoorWidth { get; set; } = 3.0f;
	[Export] public float EntryMargin { get; set; } = 1.5f;

	[Export] public bool HasDoorNorth { get; set; }
	[Export] public bool HasDoorEast { get; set; }
	[Export] public bool HasDoorSouth { get; set; }
	[Export] public bool HasDoorWest { get; set; }

	[Export] public PackedScene DoorScene { get; set; }
	[Export] public PackedScene EnemyScene { get; set; }
	[Export] public float StartDelay { get; set; }

	// Temporal hasta conectar salas: sin sala vecina, el hueco de la puerta daría al vacío.
	[Export] public bool DoorPlugsActive { get; set; } = true;

	[Export] public Color FloorColor { get; set; } = new Color(0.18f, 0.15f, 0.15f, 1.0f);
	[Export] public Color WallColor { get; set; } = new Color(0.25f, 0.2f, 0.22f, 1.0f);

	public RoomState State { get; private set; } = RoomState.Unvisited;

	private readonly Dictionary<DoorSide, Door> _doors = new();
	private readonly List<Marker3D> _spawnPoints = new();

	private Node3D _geometry;
	private Node3D _doorsRoot;
	private Node3D _enemiesRoot;
	private int _enemiesAlive;

	public override void _Ready()
	{
		_geometry = GetNode<Node3D>("Geometry");
		_doorsRoot = GetNode<Node3D>("Doors");
		_enemiesRoot = GetNode<Node3D>("Enemies");

		foreach (Node child in GetNode("SpawnPoints").GetChildren())
		{
			if (child is Marker3D marker)
			{
				_spawnPoints.Add(marker);
			}
		}

		BuildGeometry();
		BuildEntryArea();

		foreach (Door door in _doors.Values)
		{
			door.SetOpen(true);
		}

		if (EnemyScene == null || _spawnPoints.Count == 0)
		{
			State = RoomState.Cleared;
		}
	}

	private bool HasDoor(DoorSide side)
	{
		return side switch
		{
			DoorSide.North => HasDoorNorth,
			DoorSide.East => HasDoorEast,
			DoorSide.South => HasDoorSouth,
			_ => HasDoorWest,
		};
	}

	private void BuildGeometry()
	{
		StandardMaterial3D floorMaterial = new() { AlbedoColor = FloorColor };
		StandardMaterial3D wallMaterial = new() { AlbedoColor = WallColor };

		float t = WallThickness;
		float margin = t * 2.0f;
		CreateBox(_geometry, "Floor", new Vector3(RoomSize.X + margin, 1.0f, RoomSize.Y + margin),
			new Vector3(0.0f, -0.5f, 0.0f), floorMaterial);

		foreach (DoorSide side in System.Enum.GetValues<DoorSide>())
		{
			BuildWall(side, wallMaterial);
		}
	}

	private void BuildWall(DoorSide side, StandardMaterial3D material)
	{
		float t = WallThickness;
		float hx = RoomSize.X / 2.0f;
		float hz = RoomSize.Y / 2.0f;

		// Norte y Sur recorren el eje X e incluyen las esquinas; Este y Oeste cubren solo el tramo interior.
		bool alongX = side == DoorSide.North || side == DoorSide.South;
		float sign = side == DoorSide.North || side == DoorSide.West ? -1.0f : 1.0f;
		float half = alongX ? hx + t : hz;
		float fixedCoord = sign * ((alongX ? hz : hx) + t / 2.0f);
		float doorHalf = DoorWidth / 2.0f;

		if (HasDoor(side))
		{
			AddWallSegment(side, "A", -half, -doorHalf, fixedCoord, alongX, material);
			AddWallSegment(side, "B", doorHalf, half, fixedCoord, alongX, material);
			AddDoor(side, fixedCoord, alongX);

			if (DoorPlugsActive)
			{
				float plugCoord = fixedCoord + sign * t;
				Vector3 plugSize = DoorSize(alongX);
				CreateBox(_geometry, $"DoorPlug{side}", plugSize, DoorPosition(plugCoord, alongX), null);
			}
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

		if (material != null)
		{
			body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
		}

		parent.AddChild(body);
		return body;
	}

	private void BuildEntryArea()
	{
		Area3D area = GetNode<Area3D>("EntryArea");
		CollisionShape3D shape = area.GetNode<CollisionShape3D>("CollisionShape3D");

		float sizeX = Mathf.Max(0.5f, RoomSize.X - 2.0f * EntryMargin);
		float sizeZ = Mathf.Max(0.5f, RoomSize.Y - 2.0f * EntryMargin);
		shape.Shape = new BoxShape3D { Size = new Vector3(sizeX, 2.0f, sizeZ) };
		shape.Position = new Vector3(0.0f, 1.0f, 0.0f);

		area.BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (State != RoomState.Unvisited || !body.IsInGroup("player"))
		{
			return;
		}

		StartCombat();
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
			Node3D enemy = EnemyScene.Instantiate<Node3D>();
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
