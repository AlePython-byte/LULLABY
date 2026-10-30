using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la ambientación base de las salas (superficies, marcos y barrotes de puerta, columnas, luz y ambiente).
///   godot --headless --fixed-fps 60 --path . res://tests/RoomDecorBaseTest.tscn
///
/// 1. DecorEnabled apagado (por defecto): la sala es la de siempre (sin nodo Decor, caja provisional en las puertas).
/// 2. DecorEnabled encendido: las colisiones (paredes, puertas, disparadores, área de entrada) son idénticas; ninguna pieza de la
///    ambientación tiene colisión; marcos centrados en cada hueco, barrotes sincronizados con Door, columnas solo al norte.
/// 3. Superficies: sin texturas, colores pastel por tipo de sala; con textura (de tests/fixtures/textures), triplanar mundial.
/// 4. Cámara: todas las piezas quedan dentro del encuadre de RoomCamera para todos los tamaños de plantilla.
/// 5. Presupuesto: recuento de nodos y triángulos de la ambientación de una sala con las cuatro puertas.
/// 6. Nivel jugable: TestLevel activa la ambientación y aplica el recurso de luz y ambiente.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class RoomDecorBaseTest : Node
{
	private const int MeshBudget = 60;
	private const int TriangleBudget = 60000;

	private static readonly Vector2[] Sizes =
	{
		new(12, 12), new(16, 16), new(18, 18), new(20, 20), new(26, 22), new(28, 16), new(16, 26), new(26, 26),
	};

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== RoomDecorBaseTest ===");
		try
		{
			await DecorOffAndOn();
			await Surfaces();
			await CameraFraming();
			await Budget();
			await PlayableLevel();
		}
		catch (System.Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private async Task DecorOffAndOn()
	{
		GD.Print("\n--- Sala con y sin ambientación ---");
		DoorSide[] connected = { DoorSide.North, DoorSide.East, DoorSide.South };
		RoomController off = await NewRoom(RoomType.Normal, new Vector2(20, 20), connected, false);
		RoomController on = await NewRoom(RoomType.Normal, new Vector2(20, 20), connected, true);

		Check("apagada: no hay nodo Decor", off.GetNodeOrNull(RoomDecorator.DecorName) == null, "hay Decor");
		Check("apagada: las puertas conservan su caja provisional", connected.All(side => Door(off, side).Skin == null && Door(off, side).GetNode<MeshInstance3D>("Mesh").Visible), "caja oculta");
		Check("encendida: hay nodo Decor", on.GetNodeOrNull(RoomDecorator.DecorName) != null, "sin Decor");

		string before = CollisionSignature(off);
		string after = CollisionSignature(on);
		Check("encendida: las colisiones son idénticas a las de la sala sin ambientar", before == after, $"\n{before}\n!=\n{after}");

		Node3D decor = on.GetNode<Node3D>(RoomDecorator.DecorName);
		bool noCollision = !Descendants(decor).Concat(connected.SelectMany(side => Descendants(Door(on, side).Skin))).Any(node => node is CollisionObject3D or CollisionShape3D);
		Check("ninguna pieza de la ambientación tiene colisión", noCollision, "hay colisión");

		foreach (DoorSide side in connected)
		{
			Door door = Door(on, side);
			Node3D frame = decor.GetNodeOrNull<Node3D>($"Frame{side}");
			Check($"{side}: marco centrado en el hueco", frame != null && Bounds(frame).GetCenter().DistanceTo(door.GlobalPosition with { Y = Bounds(frame).GetCenter().Y }) < 0.05f, "marco descentrado");
			Check($"{side}: tiene barrotes colgando de la puerta", door.Skin != null && door.Skin.GetParent() == door, "sin barrotes");
			if (door.Skin == null)
			{
				continue;
			}

			Aabb bars = Bounds(door.Skin);
			Check($"{side}: barrotes entre el suelo y 3 m, centrados en el hueco", Mathf.Abs(bars.Position.Y) < 0.05f && bars.End.Y <= 3.0f && bars.GetCenter().DistanceTo(door.GlobalPosition with { Y = bars.GetCenter().Y }) < 0.05f, $"caja {bars}");
			door.SetOpen(false);
			Check($"{side}: cerrada muestra los barrotes y oculta la caja", door.Skin.Visible && !door.GetNode<MeshInstance3D>("Mesh").Visible, "visibilidad cerrada");
			door.SetOpen(true);
			Check($"{side}: abierta oculta los barrotes", !door.Skin.Visible, "barrotes visibles");
			CollisionShape3D shape = door.GetNode<CollisionShape3D>("CollisionShape3D");
			Check($"{side}: la colisión de Door es la de siempre", shape.Shape is BoxShape3D box && (box.Size.IsEqualApprox(new Vector3(3, 3, 1)) || box.Size.IsEqualApprox(new Vector3(1, 3, 3))), "colisión distinta");
		}

		Check("marco hacia el oeste inexistente (sin puerta)", decor.GetNodeOrNull("FrameWest") == null, "marco sin puerta");
		Check("columnas solo en las esquinas del norte", decor.GetNodeOrNull("ColumnWN") != null && decor.GetNodeOrNull("ColumnEN") != null
			&& decor.GetNodeOrNull("ColumnWS") == null && decor.GetNodeOrNull("ColumnES") == null, "columnas inesperadas");
		Aabb column = Bounds(decor.GetNode<Node3D>("ColumnEN"));
		Check("columna centrada en la esquina interior (sobresale 0,29 m)", column.GetCenter().DistanceTo(new Vector3(10, column.GetCenter().Y, -10)) < 0.05f && Mathf.Abs(column.Size.Y - 3.0f) < 0.05f, $"caja {column}");

		off.Free();
		on.Free();
	}

	private async Task Surfaces()
	{
		GD.Print("\n--- Superficies ---");
		string original = RoomSurfaces.TextureFolder;
		try
		{
			RoomSurfaces.TextureFolder = "res://tests/fixtures/none/";
			foreach (RoomType type in new[] { RoomType.Normal, RoomType.Start, RoomType.Treasure, RoomType.Boss })
			{
				RoomController room = await NewRoom(type, new Vector2(16, 16), new[] { DoorSide.North }, true);
				StandardMaterial3D floor = Material(room, "Floor");
				Color expected = type switch
				{
					RoomType.Start => room.DecorStartFloorColor,
					RoomType.Boss => room.DecorBossFloorColor,
					RoomType.Treasure => room.DecorTreasureFloorColor,
					_ => room.DecorFloorColor,
				};
				Check($"{type}: sin texturas, suelo de color pastel propio", floor.AlbedoTexture == null && floor.AlbedoColor.IsEqualApprox(expected), $"color {floor.AlbedoColor}");
				Check($"{type}: sin texturas, paredes de color pastel", Material(room, "WallNorthA").AlbedoTexture == null && Material(room, "WallNorthA").AlbedoColor.IsEqualApprox(room.DecorWallColor), "paredes");
				room.Free();
			}

			RoomSurfaces.TextureFolder = "res://tests/fixtures/textures/";
			RoomController textured = await NewRoom(RoomType.Normal, new Vector2(16, 16), new[] { DoorSide.North }, true);
			StandardMaterial3D parquet = Material(textured, "Floor");
			Check("con textura: el suelo normal usa parquet en triplanar mundial", parquet.AlbedoTexture != null && parquet.Uv1Triplanar && parquet.Uv1WorldTriplanar
				&& Mathf.IsEqualApprox(parquet.Uv1Scale.X, 0.5f) && parquet.AlbedoColor.IsEqualApprox(Colors.White), "sin textura");
			Check("con textura de otro tipo ausente, el resto sigue en color", Material(textured, "WallNorthA").AlbedoTexture == null, "pared con textura");
			textured.Free();
		}
		finally
		{
			RoomSurfaces.TextureFolder = original;
		}

		Check("sin la carpeta real de texturas todo sigue sin fallar", RoomSurfaces.LoadOptional("no_existe") == null, "carga inesperada");
	}

	private async Task CameraFraming()
	{
		GD.Print("\n--- Encuadre de cámara ---");
		RoomCamera roomCamera = new() { Name = "Rig", RotationDegrees = new Vector3(-60, 0, 0) };
		Camera3D camera = new() { Name = "Camera3D", Fov = 60.0f, Current = true, Position = new Vector3(0, 0, 24) };
		roomCamera.AddChild(camera);
		AddChild(roomCamera);
		DoorSide[] all = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };
		foreach (Vector2 size in Sizes)
		{
			RoomController room = await NewRoom(RoomType.Normal, size, all, true);
			roomCamera.Frame(size, 3.0f, 1.0f);
			await Frames(1);
			int outside = 0;
			string first = "";
			foreach (Node3D piece in room.GetNode(RoomDecorator.DecorName).GetChildren().Cast<Node3D>())
			{
				foreach (Vector3 corner in Corners(Bounds(piece)))
				{
					if (!camera.IsPositionInFrustum(corner))
					{
						outside++;
						first = first == "" ? $"{piece.Name} {corner}" : first;
					}
				}
			}

			Check($"{size.X}x{size.Y}: marcos y columnas dentro del encuadre", outside == 0, $"{outside} esquinas fuera, la primera {first}");
			room.Free();
		}

		roomCamera.Free();
	}

	private async Task Budget()
	{
		GD.Print("\n--- Presupuesto ---");
		RoomController room = await NewRoom(RoomType.Normal, new Vector2(28, 16), new[] { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West }, true);
		List<MeshInstance3D> meshes = Descendants(room.GetNode(RoomDecorator.DecorName)).OfType<MeshInstance3D>().ToList();
		foreach (Door door in new[] { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West }.Select(side => Door(room, side)))
		{
			meshes.AddRange(Descendants(door.Skin).OfType<MeshInstance3D>());
		}

		int triangles = meshes.Sum(mesh => mesh.Mesh.GetFaces().Length / 3);
		GD.Print($"  ambientación de una sala 28x16 con cuatro puertas: {meshes.Count} mallas, {triangles} triángulos");
		Check($"como mucho {MeshBudget} mallas", meshes.Count <= MeshBudget, $"{meshes.Count}");
		Check($"como mucho {TriangleBudget} triángulos", triangles <= TriangleBudget, $"{triangles}");
		room.Free();
	}

	private async Task PlayableLevel()
	{
		GD.Print("\n--- Nivel jugable ---");
		Node level = GD.Load<PackedScene>(GameSettings.LevelScene).Instantiate();
		Check("TestLevel activa la ambientación", level.GetNode<RoomManager>("RoomManager").DecorEnabled, "DecorEnabled apagado");
		AtmosphereApplier applier = level.GetNode<AtmosphereApplier>("Atmosphere");
		Check("TestLevel tiene un recurso de atmósfera con entorno", applier.Atmosphere?.Environment != null, "sin recurso");
		applier.Apply();
		DirectionalLight3D light = level.GetNode<DirectionalLight3D>("DirectionalLight3D");
		Check("la luz y el entorno del nivel salen del recurso",
			light.LightColor.IsEqualApprox(applier.Atmosphere.LightColor) && Mathf.IsEqualApprox(light.LightEnergy, applier.Atmosphere.LightEnergy)
			&& level.GetNode<WorldEnvironment>("WorldEnvironment").Environment == applier.Atmosphere.Environment, "no se aplicó");
		Check("el recurso es una luz cálida (rojo mayor que azul)", applier.Atmosphere.LightColor.R > applier.Atmosphere.LightColor.B, "luz fría");
		level.Free();
		await Frames(1);
	}

	// --- ayudas ----------------------------------------------------------------------------------------------------

	private async Task<RoomController> NewRoom(RoomType type, Vector2 size, DoorSide[] doors, bool decor)
	{
		RoomData data = new() { Type = type, Size = size, Cell = new Vector2I(3, 3) };
		RoomController room = GD.Load<PackedScene>("res://scenes/rooms/Room.tscn").Instantiate<RoomController>();
		room.Setup(data, doors, false);
		room.DecorEnabled = decor;
		AddChild(room);
		await Frames(1);
		return room;
	}

	private static Door Door(Node room, DoorSide side) => room.GetNode<Door>($"Doors/Door{side}");

	private static StandardMaterial3D Material(Node room, string wall) =>
		(StandardMaterial3D)room.GetNode<StaticBody3D>($"Geometry/{wall}").GetChildren().OfType<MeshInstance3D>().First().MaterialOverride;

	// Todas las colisiones de la sala fuera de la ambientación: tipo, nombre, posición, tamaño y capas.
	private static string CollisionSignature(Node room)
	{
		List<string> lines = new();
		foreach (Node node in Descendants(room))
		{
			if (node is CollisionObject3D body && !IsDecor(node))
			{
				string shapes = string.Join(",", body.GetChildren().OfType<CollisionShape3D>().Select(s => s.Shape switch
				{
					BoxShape3D box => $"box{box.Size}@{s.Position}",
					_ => s.Shape?.GetType().Name ?? "none",
				}));
				lines.Add($"{node.GetType().Name} {node.Name} {body.Position} L{body.CollisionLayer} M{body.CollisionMask} [{shapes}]");
			}
		}

		lines.Sort(System.StringComparer.Ordinal);
		return string.Join("\n", lines);
	}

	private static bool IsDecor(Node node)
	{
		for (Node n = node; n != null; n = n.GetParent())
		{
			if (n.Name == RoomDecorator.DecorName || n.Name == "Bars")
			{
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<Node> Descendants(Node root)
	{
		if (root == null)
		{
			yield break;
		}

		Stack<Node> pending = new();
		pending.Push(root);
		while (pending.Count > 0)
		{
			Node node = pending.Pop();
			yield return node;
			foreach (Node child in node.GetChildren())
			{
				pending.Push(child);
			}
		}
	}

	private static Aabb Bounds(Node3D root)
	{
		Aabb? total = null;
		foreach (MeshInstance3D mesh in Descendants(root).OfType<MeshInstance3D>().Where(m => m.Mesh != null))
		{
			Aabb box = mesh.GlobalTransform * mesh.Mesh.GetAabb();
			total = total.HasValue ? total.Value.Merge(box) : box;
		}

		return total ?? new Aabb();
	}

	private static IEnumerable<Vector3> Corners(Aabb box)
	{
		for (int i = 0; i < 8; i++)
		{
			yield return box.GetEndpoint(i);
		}
	}

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  {(ok ? "OK   " : "FALLO")} {name}" + (ok ? "" : $" ({detail})"));
		if (!ok)
		{
			_failures.Add(name);
		}
	}
}
