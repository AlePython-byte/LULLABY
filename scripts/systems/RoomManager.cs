using System.Collections.Generic;
using Godot;

/// <summary>
/// Gestiona el mapa de salas: carga una sala cada vez, deriva sus puertas de las salas vecinas y hace la
/// transición al cruzar una puerta. Jugador, HUD, luz, entorno y cámara viven en el nivel y no se recrean.
///
/// El mapa llega por Begin (normalmente desde RunManager, con un nivel generado). Si Map está asignado en el
/// inspector, arranca solo con ese mapa fijo.
///
/// Convención de ejes (ver LevelMap): Norte = y-1 = -Z (arriba en pantalla), Este = x+1 = +X.
/// Todas las salas se centran en el origen del mundo; solo hay una cargada a la vez.
///
/// Estado persistente: las celdas despejadas se recuerdan; al volver, la sala no tiene enemigos y sus puertas
/// están abiertas. Una sala solo cuenta como despejada cuando se emite su RoomCleared.
/// El estado vive en este nodo: si se recarga el nivel (por ejemplo, al morir el jugador), se reinicia.
/// </summary>
public partial class RoomManager : Node3D
{
	/// <summary>Sala preparada, con el jugador colocado y el fundido de entrada terminado.</summary>
	public event System.Action<RoomController> RoomLoaded;
	[Export] public LevelMap Map { get; set; }
	[Export] public PackedScene RoomScene { get; set; }
	/// <summary>Vacío conserva los seis enemigos históricos de la plantilla del jefe.</summary>
	[Export] public PackedScene BossScene { get; set; }
	[Export] public PlayerController Player { get; set; }
	[Export] public RoomCamera Camera { get; set; }
	[Export] public ScreenFade Fade { get; set; }

	// Distancia del centro del jugador al interior de la pared de la puerta por la que aparece.
	// El LevelValidator asume RoomRules.ArrivalOffset: si se cambia aquí, cambia también la regla.
	[Export] public float ArrivalOffset { get; set; } = RoomRules.ArrivalOffset;

	// Separación mínima entre un punto de aparición y las paredes.
	[Export] public float SpawnMargin { get; set; } = RoomRules.SpawnWallMargin;
	[Export] public float FadeDuration { get; set; } = 0.15f;

	private readonly Dictionary<Vector2I, RoomData> _rooms = new();
	private readonly HashSet<Vector2I> _cleared = new();

	private RoomController _room;
	private Vector2I _currentCell;
	private bool _transitioning;

	public RoomController CurrentRoom => _room;

	public override void _Ready()
	{
		if (Map != null)
		{
			Begin(Map);
		}
	}

	public void Begin(LevelMap map)
	{
		Map = map;
		_rooms.Clear();
		_cleared.Clear();

		if (!ValidateMap())
		{
			return;
		}

		LoadRoom(Map.StartCell);
		_room.Activate();
		RoomLoaded?.Invoke(_room);
	}

	private bool ValidateMap()
	{
		List<string> errors = new();

		if (Player == null)
		{
			errors.Add("falta asignar Player.");
		}

		if (Camera == null)
		{
			errors.Add("falta asignar Camera.");
		}

		if (Fade == null)
		{
			errors.Add("falta asignar Fade.");
		}

		if (RoomScene == null)
		{
			errors.Add("falta asignar RoomScene.");
		}

		if (Map != null && Map.Rooms != null)
		{
			foreach (RoomData room in Map.Rooms)
			{
				if (room != null)
				{
					ClampSpawnPoints(room);
				}
			}
		}

		errors.AddRange(LevelValidator.Validate(Map));

		if (errors.Count > 0)
		{
			foreach (string error in errors)
			{
				GD.PushError($"RoomManager: mapa inválido: {error}");
			}

			return false;
		}

		foreach (RoomData room in Map.Rooms)
		{
			_rooms[room.Cell] = room;
		}

		return true;
	}

	// Ajusta en memoria (sin tocar el archivo) los puntos que caen fuera del interior.
	private void ClampSpawnPoints(RoomData room)
	{
		float limitX = Mathf.Max(0.0f, room.Size.X / 2.0f - SpawnMargin);
		float limitZ = Mathf.Max(0.0f, room.Size.Y / 2.0f - SpawnMargin);

		Vector2[] adjusted = new Vector2[room.SpawnPoints.Length];
		for (int i = 0; i < adjusted.Length; i++)
		{
			Vector2 point = room.SpawnPoints[i];
			adjusted[i] = new Vector2(Mathf.Clamp(point.X, -limitX, limitX), Mathf.Clamp(point.Y, -limitZ, limitZ));

			if (adjusted[i] != point)
			{
				GD.PushWarning($"RoomManager: sala {room.Cell}: punto de aparición {point} fuera del interior; ajustado a {adjusted[i]}.");
			}
		}

		room.SpawnPoints = adjusted;

		if (room.EnemyScene == null && adjusted.Length > 0)
		{
			GD.PushWarning($"RoomManager: sala {room.Cell} tiene puntos de aparición pero no EnemyScene; nacerá despejada.");
		}
	}

	private void LoadRoom(Vector2I cell)
	{
		RoomData data = _rooms[cell];

		List<DoorSide> connected = new();
		foreach (DoorSide side in System.Enum.GetValues<DoorSide>())
		{
			if (_rooms.ContainsKey(cell + side.GridOffset()))
			{
				connected.Add(side);
			}
		}

		RoomController room = RoomScene.Instantiate<RoomController>();
		room.Setup(data, connected, _cleared.Contains(cell));
		room.BossScene = BossScene;
		room.BossSpawned += boss =>
		{
			float height = boss.GetNodeOrNull<MotherVisual>("Animation")?.FramingHeight ?? room.WallHeight;
			Camera.Frame(data.Size, Mathf.Max(room.WallHeight, height), room.WallThickness);
		};
		room.DoorCrossed += OnDoorCrossed;
		room.RoomCleared += OnRoomCleared;
		AddChild(room);

		_room = room;
		_currentCell = cell;
		Camera.Frame(data.Size, room.WallHeight, room.WallThickness);
	}

	private void OnRoomCleared()
	{
		_cleared.Add(_currentCell);
	}

	// Viene de una colisión: la descarga de la sala se difiere para no tocar la física en pleno callback.
	private void OnDoorCrossed(int side)
	{
		if (_transitioning)
		{
			return;
		}

		_transitioning = true;
		DoorSide exitSide = (DoorSide)side;
		Callable.From(() => BeginTransition(exitSide)).CallDeferred();
	}

	private async void BeginTransition(DoorSide exitSide)
	{
		Vector2I nextCell = _currentCell + exitSide.GridOffset();
		if (!_rooms.ContainsKey(nextCell))
		{
			GD.PushError($"RoomManager: la puerta {exitSide} de {_currentCell} no lleva a ninguna sala.");
			_transitioning = false;
			return;
		}

		SetPlayerFrozen(true);
		await Fade.FadeTo(1.0f, FadeDuration);

		foreach (Node tear in GetTree().GetNodesInGroup("tears"))
		{
			tear.QueueFree();
		}

		// La sala nueva se instancia cuando la anterior ya se liberó (colisiones y nodos fuera del árbol).
		RoomController previous = _room;
		previous.QueueFree();
		await ToSignal(previous, Node.SignalName.TreeExited);

		LoadRoom(nextCell);
		PlacePlayer(exitSide);

		await Fade.FadeTo(0.0f, FadeDuration);

		SetPlayerFrozen(false);
		_room.Activate();
		_transitioning = false;
		RoomLoaded?.Invoke(_room);
	}

	// Aparece junto a la puerta opuesta a la que cruzó, dentro de la sala y mirando hacia dentro.
	private void PlacePlayer(DoorSide exitSide)
	{
		Vector2 size = _rooms[_currentCell].Size;
		Vector3 direction = exitSide.Direction();
		float halfExtent = direction.X != 0.0f ? size.X / 2.0f : size.Y / 2.0f;

		Vector3 position = -direction * (halfExtent - ArrivalOffset);
		position.Y = 0.5f;

		Player.GlobalPosition = position;
		Player.Velocity = Vector3.Zero;
		Player.FaceDirection(direction);
	}

	private void SetPlayerFrozen(bool frozen)
	{
		Player.SetPhysicsProcess(!frozen);
		Player.GetNode<PlayerShooter>("Shooter").SetPhysicsProcess(!frozen);
	}
}
