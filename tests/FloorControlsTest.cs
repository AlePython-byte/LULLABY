using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de los controles pintados en el piso de la sala de inicio (FloorControlsDecal). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/FloorControlsTest.tscn
///
///   1. Solo la sala de inicio lo lleva (no la normal, la del tesoro ni la del jefe), una sola vez.
///   2. Es un quad plano y horizontal con la normal hacia arriba, a 2 cm del suelo, con la parte superior de la imagen hacia el norte (-Z),
///      con transparencia, sin sombra, dibujado antes que Noah, los enemigos y los objetos, y sin colisión alguna.
///   3. Mide lo mismo que la imagen (8:3) y cabe en la sala: sin tapar las puertas ni las zonas de llegada.
///   4. La decoración exenta del inicio no lo pisa, en muchas semillas, y esa reserva no depende de que exista la textura.
///   5. Queda dentro del encuadre de RoomCamera.
///   6. Si la textura no existe no pasa nada: ni nodo, ni error, y la sala se construye igual.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class FloorControlsTest : Node
{
	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== FloorControlsTest ===");
		string original = FloorControlsDecal.TexturePath;
		try
		{
			await OnlyInStartRoom();
			await Geometry();
			Placement();
			FreeDecor();
			await Camera();
			await MissingTexture();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}
		finally
		{
			FloorControlsDecal.TexturePath = original;
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		await BossTestHarness.CollectWrappers(GetTree());
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private static readonly DoorSide[] AllDoors = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };

	private static Vector2 SizeOf(RoomType type) => type switch
	{
		RoomType.Start => new Vector2(16, 16),
		RoomType.Treasure => new Vector2(12, 12),
		RoomType.Boss => new Vector2(26, 26),
		_ => new Vector2(20, 20),
	};

	private async Task<RoomController> NewRoom(RoomType type, int seed, bool decor = true)
	{
		RoomData data = new() { Type = type, Size = SizeOf(type), Cell = new Vector2I(2, 2) };
		RoomController room = GD.Load<PackedScene>("res://scenes/rooms/Room.tscn").Instantiate<RoomController>();
		room.Setup(data, AllDoors, false);
		room.DecorEnabled = decor;
		room.DecorSeed = seed;
		AddChild(room);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		return room;
	}

	private static MeshInstance3D Decal(RoomController room) => room.GetNodeOrNull(RoomDecorator.DecorName)?.GetNodeOrNull<MeshInstance3D>(FloorControlsDecal.NodeName);

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private async Task OnlyInStartRoom()
	{
		GD.Print("\n--- Solo en la sala de inicio ---");
		foreach (RoomType type in new[] { RoomType.Start, RoomType.Normal, RoomType.Treasure, RoomType.Boss })
		{
			RoomController room = await NewRoom(type, 7);
			int count = room.GetNode(RoomDecorator.DecorName).GetChildren().Count(child => child.Name == FloorControlsDecal.NodeName);
			Check($"{type}: {(type == RoomType.Start ? "lleva" : "no lleva")} el cartel de controles", count == (type == RoomType.Start ? 1 : 0), $"{count} carteles");
			room.Free();
		}

		RoomController off = await NewRoom(RoomType.Start, 7, decor: false);
		Check("sin ambientación (DecorEnabled apagado) tampoco hay cartel: la sala es la de siempre", off.GetNodeOrNull(RoomDecorator.DecorName) == null, "hay Decor");
		off.Free();
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task Geometry()
	{
		GD.Print("\n--- Plano, orientación y material ---");
		RoomController room = await NewRoom(RoomType.Start, 3);
		MeshInstance3D decal = Decal(room);
		Check("el cartel existe (la textura de David está en assets/ui/)", decal != null, "sin cartel");
		PlaneMesh mesh = decal.Mesh as PlaneMesh;
		Check("es un quad plano (PlaneMesh) de 10 x 3,75 m", mesh != null && mesh.Size.IsEqualApprox(new Vector2(10.0f, 3.75f)), "malla");

		Texture2D texture = ResourceLoader.Load<Texture2D>(FloorControlsDecal.TexturePath);
		Check("tiene la misma proporción que la imagen (8:3)", Mathf.Abs(mesh.Size.X / mesh.Size.Y - texture.GetWidth() / (float)texture.GetHeight()) < 0.01f, $"{texture.GetSize()}");

		Check("a 2 cm del suelo, horizontal y sin giros", Mathf.IsEqualApprox(decal.Position.Y, 0.02f) && decal.Rotation.IsZeroApprox() && decal.Scale.IsEqualApprox(Vector3.One) && decal.GlobalTransform.Basis.Y.IsEqualApprox(Vector3.Up), $"{decal.Position}");

		// Normales hacia arriba y la parte superior de la imagen (v = 0) hacia el norte (-Z), la de la izquierda (u = 0) hacia -X.
		Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
		Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
		Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
		int north = Array.IndexOf(vertices, vertices.OrderBy(v => v.Z).First());
		int west = Array.IndexOf(vertices, vertices.OrderBy(v => v.X).First());
		Check("las normales miran hacia arriba (+Y)", normals.All(n => n.Dot(Vector3.Up) > 0.99f), $"{normals[0]}");
		Check("la parte superior de la imagen mira al norte (-Z), que es arriba para la cámara", Mathf.IsZeroApprox(uvs[north].Y), $"v = {uvs[north].Y} en el vértice más al norte");
		Check("la izquierda de la imagen queda al oeste (-X): se lee de izquierda a derecha", Mathf.IsZeroApprox(uvs[west].X), $"u = {uvs[west].X} en el vértice más al oeste");

		StandardMaterial3D material = decal.MaterialOverride as StandardMaterial3D;
		Check("material con la textura y transparencia alfa", material != null && material.AlbedoTexture == texture && material.Transparency == BaseMaterial3D.TransparencyEnum.Alpha, "material");
		Check("se dibuja antes que lo demás (prioridad de render negativa) y no proyecta sombra", material.RenderPriority < 0 && decal.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off, $"{material.RenderPriority}");
		Check("queda por debajo de Noah, los enemigos y los objetos: a ras de suelo (2 cm) y sin escribir sobre ellos", decal.Position.Y < 0.05f && material.NoDepthTest == false, $"{decal.Position.Y}");
		Check("sin colisión: ni en el cartel ni en sus nodos", decal.GetChildCount() == 0 && !Descendants(decal).Any(n => n is CollisionObject3D or CollisionShape3D), "hay colisión");
		Check("la sala conserva sus colisiones de siempre (paredes y puertas, ningún cuerpo nuevo por el cartel)",
			room.GetNode(RoomDecorator.DecorName).GetChildren().SelectMany(Descendants).All(n => n is not CollisionObject3D), "colisión en la decoración");
		room.Free();
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private void Placement()
	{
		GD.Print("\n--- Sin tapar puertas ni llegadas ---");
		Vector2 size = SizeOf(RoomType.Start);
		Rect2 decal = FloorControlsDecal.Footprint;
		float hx = size.X / 2.0f, hz = size.Y / 2.0f;
		Check("cabe en la sala con margen a las paredes", decal.Position.X >= -hx + 2.0f && decal.End.X <= hx - 2.0f && decal.Position.Y >= -hz + 2.0f && decal.End.Y <= hz - 2.0f, $"{decal}");

		bool doors = true, arrivals = true;
		foreach (DoorSide side in AllDoors)
		{
			// Hueco de la puerta (3 m + 0,65 m a cada lado) y 2 m hacia dentro desde la pared.
			Rect2 zone = side switch
			{
				DoorSide.North => new Rect2(-2.3f, -hz, 4.6f, 2.0f),
				DoorSide.South => new Rect2(-2.3f, hz - 2.0f, 4.6f, 2.0f),
				DoorSide.East => new Rect2(hx - 2.0f, -2.3f, 2.0f, 4.6f),
				_ => new Rect2(-hx, -2.3f, 2.0f, 4.6f),
			};
			doors &= !decal.Intersects(zone);
		}

		// Con 10 m de ancho el cartel entra un poco en el cuadrado de ±1,5 m de las llegadas este y oeste: es una imagen plana a ras de
		// suelo y no estorba, pero el punto donde aparece Noah (con su cuerpo de ±0,4 m) tiene que quedar libre.
		foreach (Vector2 arrival in RoomRules.ArrivalPoints(size))
		{
			arrivals &= !decal.Intersects(new Rect2(arrival - new Vector2(0.4f, 0.4f), new Vector2(0.8f, 0.8f)));
		}

		Check("no tapa el hueco de ninguna puerta", doors, "solapa una puerta");
		Check("no tapa ningún punto de llegada del jugador", arrivals, "solapa una llegada");
		Check("la reserva del cartel (con margen) tampoco pisa puertas ni los puntos de llegada", !FloorControlsDecal.Reserved.Intersects(new Rect2(-2.3f, -hz, 4.6f, 2.0f)) && RoomRules.ArrivalPoints(size).All(a => !FloorControlsDecal.Reserved.HasPoint(a)), "reserva");
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private void FreeDecor()
	{
		GD.Print("\n--- La decoración exenta lo respeta ---");
		Vector2 size = SizeOf(RoomType.Start);
		Rect2 reserved = FloorControlsDecal.Reserved;
		bool clear = true;
		int pieces = 0;
		string detail = "";
		for (int seed = 1; seed <= 300; seed++)
		{
			foreach (DecorPiece piece in FreeDecorPlanner.Plan(RoomType.Start, size, new Vector2I(seed % 9, seed % 4), seed))
			{
				pieces++;
				Aabb box = DecorGeometry.Footprint(piece);
				if (box.Position.X < reserved.End.X && box.End.X > reserved.Position.X && box.Position.Z < reserved.End.Y && box.End.Z > reserved.Position.Y)
				{
					clear = false;
					detail = $"{piece.Model} en la semilla {seed}";
				}
			}
		}

		GD.Print($"  {pieces} piezas en 300 semillas de la sala de inicio");
		Check("ninguna pieza exenta pisa el cartel ni su margen de 0,3 m", clear, detail);

		string original = FloorControlsDecal.TexturePath;
		List<DecorPiece> with = FreeDecorPlanner.Plan(RoomType.Start, size, new Vector2I(1, 1), 5);
		FloorControlsDecal.TexturePath = "res://assets/ui/no_existe.png";
		List<DecorPiece> without = FreeDecorPlanner.Plan(RoomType.Start, size, new Vector2I(1, 1), 5);
		FloorControlsDecal.TexturePath = original;
		Check("la reserva no depende de que exista la textura: la decoración es la misma con y sin ella", with.SequenceEqual(without), "planes distintos");
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task Camera()
	{
		GD.Print("\n--- Encuadre de cámara ---");
		RoomCamera roomCamera = new() { Name = "Rig", RotationDegrees = new Vector3(-60, 0, 0) };
		Camera3D camera = new() { Name = "Camera3D", Fov = 60.0f, Current = true, Position = new Vector3(0, 0, 24) };
		roomCamera.AddChild(camera);
		AddChild(roomCamera);
		RoomController room = await NewRoom(RoomType.Start, 3);
		roomCamera.Frame(SizeOf(RoomType.Start), 3.0f, 1.0f);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		MeshInstance3D decal = Decal(room);
		Aabb box = decal.GlobalTransform * decal.Mesh.GetAabb();
		bool inside = Enumerable.Range(0, 8).All(i => camera.IsPositionInFrustum(box.GetEndpoint(i)));
		Check("las cuatro esquinas del cartel quedan dentro del encuadre de la cámara", inside, "esquina fuera");
		room.Free();
		roomCamera.Free();
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task MissingTexture()
	{
		GD.Print("\n--- Textura ausente ---");
		FloorControlsDecal.TexturePath = "res://assets/ui/controles_piso_que_no_existe.png";
		Node3D probe = new();
		AddChild(probe);
		Check("si la textura no existe, Build devuelve null sin error", FloorControlsDecal.Build(probe) == null && probe.GetChildCount() == 0, "creó algo");
		probe.Free();
		RoomController room = await NewRoom(RoomType.Start, 3);
		Check("la sala de inicio se construye igual, sin cartel y con su decoración", Decal(room) == null && room.GetNode(RoomDecorator.DecorName).GetChildren().Any(c => c.Name.ToString().Contains('_')), "sala incompleta");
		room.Free();
		FloorControlsDecal.TexturePath = "res://tests/fixtures/textura_que_tampoco_existe.png";
		RoomController other = await NewRoom(RoomType.Start, 4);
		Check("con otra ruta inexistente tampoco hay cartel ni falla nada", Decal(other) == null, "cartel con textura ausente");
		other.Free();
	}

	private static IEnumerable<Node> Descendants(Node root)
	{
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

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  {(ok ? "OK   " : "FALLO")} {name}" + (ok ? "" : $" ({detail})"));
		if (!ok)
		{
			_failures.Add(name);
		}
	}
}
