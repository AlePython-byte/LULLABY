using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la ambientación visual de las salas de combate: juguetes sueltos (FloorPropsPlanner), aspecto propio (RoomLook) y luces
/// cálidas (RoomLights). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/RoomAmbienceTest.tscn
///
///   1. Juguetes: de 3 a 6 por sala, misma semilla y celda dan lo mismo, semillas distintas dan salas distintas y piezas variadas
///      (giro y espejado incluidos); sin solape con las piezas de pared ni entre sí; fuera de puertas, llegadas, centro y pared sur.
///      El azar global no se toca. Los demás tipos de sala no llevan juguetes.
///   2. En la sala real: sin colisión, dentro del encuadre de RoomCamera y dentro del presupuesto de mallas y triángulos.
///   3. Aspecto: sin ninguna textura (carpeta vacía) las salas se distinguen por tono y brillo, dentro del rango; con una textura
///      ausente entre las variantes no pasa nada; si hay variantes reales en assets/textures/ se aplican en triplanar y varían.
///   4. Luces: 1 o 2 OmniLight3D sin sombras, cálidas, de rango limitado e interiores; RoomLights.Enabled = false no crea ninguna.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class RoomAmbienceTest : Node
{
	private const int Batch = 200;

	private static readonly DoorSide[] AllDoors = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };
	private static readonly Vector2[] Sizes = { new(16, 16), new(20, 20), new(28, 16), new(26, 22) };

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== RoomAmbienceTest ===");
		string folder = RoomSurfaces.TextureFolder;
		bool lights = RoomLights.Enabled;
		try
		{
			Props();
			await RealRooms();
			await Looks();
			await Lights();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}
		finally
		{
			RoomSurfaces.TextureFolder = folder;
			RoomLights.Enabled = lights;
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private static Vector2I CellOf(int seed) => new(seed % 7, seed / 7);

	private static List<DecorPiece> WallPlan(int seed, Vector2 size) => WallDecorPlanner.Plan(seed, CellOf(seed), size, AllDoors);

	private static List<DecorPiece> Toys(int seed, Vector2 size) =>
		RoomDecorator.FloorPropsFor(RoomType.Normal, size, CellOf(seed), seed, WallPlan(seed, size), AllDoors.Length);

	private static string Describe(IEnumerable<DecorPiece> pieces) => string.Join("|", pieces.Select(p => $"{p.Model}@{p.Position}/{p.Yaw}/{p.Mirror}"));

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void Props()
	{
		GD.Print("\n--- Juguetes sueltos (plan) ---");
		foreach (Vector2 size in Sizes)
		{
			string label = $"{size.X}x{size.Y}";
			int tooMany = 0, tooFew = 0, overlapping = 0, onDoor = 0, onArrival = 0, inCenter = 0, outside = 0, notDeterministic = 0, sameAsNext = 0;
			HashSet<string> models = new();
			bool mirrored = false, unmirrored = false;
			HashSet<int> yaws = new();
			string previous = null;
			for (int seed = 1; seed <= Batch; seed++)
			{
				List<DecorPiece> wall = WallPlan(seed, size);
				List<DecorPiece> toys = Toys(seed, size);
				string description = Describe(toys);
				if (description != Describe(Toys(seed, size)))
				{
					notDeterministic++;
				}

				if (description == previous)
				{
					sameAsNext++;
				}

				previous = description;
				tooMany += toys.Count > FloorPropsPlanner.MaxProps ? 1 : 0;
				tooFew += toys.Count < FloorPropsPlanner.MinProps ? 1 : 0;

				List<Aabb> footprints = toys.Select(DecorGeometry.Footprint).ToList();
				List<Aabb> others = wall.Select(DecorGeometry.Footprint).ToList();
				for (int i = 0; i < footprints.Count; i++)
				{
					models.Add(toys[i].Model);
					mirrored |= toys[i].Mirror;
					unmirrored |= !toys[i].Mirror;
					yaws.Add(Mathf.RoundToInt(Mathf.RadToDeg(toys[i].Yaw) / 15.0f));
					Aabb f = footprints[i];
					overlapping += others.Concat(footprints.Skip(i + 1)).Any(other => DecorGeometry.Overlaps(f, other, 0.0f)) ? 1 : 0;
					outside += f.Position.X < -size.X / 2.0f || f.End.X > size.X / 2.0f || f.Position.Z < -size.Y / 2.0f || f.End.Z > size.Y / 2.0f - 1.8f ? 1 : 0;
					inCenter += f.Position.X < 3.0f && f.End.X > -3.0f && f.Position.Z < 3.0f && f.End.Z > -3.0f ? 1 : 0;
					onArrival += RoomRules.ArrivalPoints(size).Any(p => f.Position.X < p.X + 1.0f && f.End.X > p.X - 1.0f && f.Position.Z < p.Y + 1.0f && f.End.Z > p.Y - 1.0f) ? 1 : 0;
					onDoor += Doors(size).Any(r => f.Position.X < r.End.X && f.End.X > r.Position.X && f.Position.Z < r.End.Y && f.End.Z > r.Position.Y) ? 1 : 0;
				}
			}

			Check($"{label}: nunca más de {FloorPropsPlanner.MaxProps} juguetes", tooMany == 0, $"{tooMany} salas");
			Check($"{label}: casi siempre al menos {FloorPropsPlanner.MinProps} (salvo recorte por presupuesto, ≤ 10 %)", tooFew <= Batch / 10, $"{tooFew} salas con menos");
			Check($"{label}: la misma semilla y celda dan lo mismo", notDeterministic == 0, $"{notDeterministic} distintas");
			Check($"{label}: semillas distintas dan salas distintas (≥ 80 %)", sameAsNext <= Batch / 5, $"{sameAsNext} iguales a la anterior");
			Check($"{label}: piezas variadas (≥ 8 modelos distintos), con giros variados y espejado", models.Count >= 8 && yaws.Count >= 8 && mirrored && unmirrored,
				$"{models.Count} modelos, {yaws.Count} giros");
			Check($"{label}: sin solapar piezas de pared ni entre sí", overlapping == 0, $"{overlapping}");
			Check($"{label}: dentro de la sala y fuera de la pared sur oculta", outside == 0, $"{outside}");
			Check($"{label}: fuera del centro", inCenter == 0, $"{inCenter}");
			Check($"{label}: fuera de las llegadas del jugador", onArrival == 0, $"{onArrival}");
			Check($"{label}: fuera del hueco de las puertas", onDoor == 0, $"{onDoor}");
			GD.Print($"  {label}: {models.Count} modelos distintos en {Batch} salas");
		}

		foreach (RoomType type in new[] { RoomType.Start, RoomType.Treasure, RoomType.Boss })
		{
			Check($"{type}: no lleva juguetes sueltos nuevos", RoomDecorator.FloorPropsFor(type, new Vector2(20, 20), CellOf(3), 3, new List<DecorPiece>(), 4).Count == 0, "tiene");
		}

		GD.Seed(99);
		ulong expected = GD.Randi();
		GD.Seed(99);
		for (int seed = 1; seed <= 20; seed++)
		{
			Toys(seed, Sizes[1]);
			RoomLights.Plan(Sizes[1], CellOf(seed), seed);
			using RandomNumberGenerator random = SeedMix.RandomFor(seed, CellOf(seed));
			RoomLook.Vary(new Color(0.5f, 0.4f, 0.4f), random);
		}

		Check("el azar global no cambia al planear", GD.Randi() == expected, "consumió el azar global");
	}

	// Huecos de las cuatro puertas (3 m de ancho, 1,5 m hacia dentro), en coordenadas de la sala (X, Z del rectángulo).
	private static List<Rect2> Doors(Vector2 size)
	{
		float hx = size.X / 2.0f, hz = size.Y / 2.0f;
		return new List<Rect2>
		{
			new(-1.5f, -hz, 3.0f, 1.5f), new(-1.5f, hz - 1.5f, 3.0f, 1.5f),
			new(hx - 1.5f, -1.5f, 1.5f, 3.0f), new(-hx, -1.5f, 1.5f, 3.0f),
		};
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private async Task RealRooms()
	{
		GD.Print("\n--- Salas reales ---");
		RoomCamera roomCamera = new() { Name = "Rig", RotationDegrees = new Vector3(-60, 0, 0) };
		Camera3D camera = new() { Name = "Camera3D", Fov = 60.0f, Current = true, Position = new Vector3(0, 0, 24) };
		roomCamera.AddChild(camera);
		AddChild(roomCamera);

		foreach (Vector2 size in new[] { new Vector2(20, 20), new Vector2(28, 16) })
		{
			int outsideFrame = 0, withCollision = 0, noToys = 0, overMeshes = 0, overTriangles = 0, worstMeshes = 0, worstTriangles = 0;
			for (int seed = 1; seed <= 12; seed++)
			{
				RoomController room = await NewRoom(RoomType.Normal, size, seed, true);
				roomCamera.Frame(size, 3.0f, 1.0f);
				await Frames(1);
				Node3D decor = room.GetNode<Node3D>(RoomDecorator.DecorName);
				List<Node3D> toys = decor.GetChildren().OfType<Node3D>().Where(n => n.Name.ToString().StartsWith("Toy")).ToList();
				noToys += toys.Count == 0 ? 1 : 0;
				outsideFrame += toys.Sum(toy => Corners(Bounds(toy)).Count(corner => !camera.IsPositionInFrustum(corner)));
				withCollision += Descendants(decor).Concat(Descendants(room.GetNode(RoomLights.NodeName))).Count(n => n is CollisionObject3D or CollisionShape3D);

				List<MeshInstance3D> meshes = Descendants(decor).OfType<MeshInstance3D>().Where(m => m.Mesh != null).ToList();
				foreach (DoorSide side in AllDoors)
				{
					meshes.AddRange(Descendants(room.GetNode<Door>($"Doors/Door{side}").Skin).OfType<MeshInstance3D>().Where(m => m.Mesh != null));
				}

				int triangles = meshes.Sum(m => m.Mesh.GetFaces().Length / 3);
				worstMeshes = Mathf.Max(worstMeshes, meshes.Count);
				worstTriangles = Mathf.Max(worstTriangles, triangles);
				overMeshes += meshes.Count > FloorPropsPlanner.MeshBudget ? 1 : 0;
				overTriangles += triangles > FloorPropsPlanner.TriangleBudget ? 1 : 0;
				room.Free();
			}

			string label = $"{size.X}x{size.Y}";
			GD.Print($"  {label}, cuatro puertas (12 semillas): peor caso {worstMeshes} mallas, {worstTriangles} triángulos");
			Check($"{label}: la sala real lleva juguetes", noToys == 0, $"{noToys} salas sin juguetes");
			Check($"{label}: todos dentro del encuadre de RoomCamera", outsideFrame == 0, $"{outsideFrame} esquinas fuera");
			Check($"{label}: sin colisión en los juguetes ni en las luces", withCollision == 0, $"{withCollision}");
			Check($"{label}: dentro del presupuesto de {FloorPropsPlanner.MeshBudget} mallas y {FloorPropsPlanner.TriangleBudget} triángulos", overMeshes == 0 && overTriangles == 0,
				$"{overMeshes} salas por mallas, {overTriangles} por triángulos");
		}

		roomCamera.Free();

		// Colisiones de la sala igual con y sin ambientación (los juguetes y las luces no las cambian).
		RoomController off = await NewRoom(RoomType.Normal, Sizes[1], 4, false);
		RoomController on = await NewRoom(RoomType.Normal, Sizes[1], 4, true);
		Check("sin ambientación no hay Decor ni Lights", off.GetNodeOrNull(RoomDecorator.DecorName) == null && off.GetNodeOrNull(RoomLights.NodeName) == null, "hay nodos");
		Check("las colisiones de la sala son las mismas con y sin ambientación", Signature(off) == Signature(on), "colisiones distintas");
		off.Free();
		on.Free();
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task Looks()
	{
		GD.Print("\n--- Aspecto de la sala ---");
		RoomSurfaces.TextureFolder = "res://tests/fixtures/none/";
		HashSet<string> floors = new(), walls = new();
		int outOfRange = 0, textured = 0, count = 30;
		for (int seed = 1; seed <= count; seed++)
		{
			StandardMaterial3D floor = RoomLook.Floor(RoomType.Normal, new Color(0.56f, 0.46f, 0.40f), 2.0f, seed, CellOf(seed));
			StandardMaterial3D wall = RoomLook.Wall(RoomType.Normal, new Color(0.68f, 0.60f, 0.64f), 2.0f, seed, CellOf(seed));
			floors.Add(floor.AlbedoColor.ToHtml());
			walls.Add(wall.AlbedoColor.ToHtml());
			textured += floor.AlbedoTexture != null || wall.AlbedoTexture != null ? 1 : 0;
			outOfRange += RoomLook.IsVariationOf(new Color(0.56f, 0.46f, 0.40f), floor.AlbedoColor) && RoomLook.IsVariationOf(new Color(0.68f, 0.60f, 0.64f), wall.AlbedoColor) ? 0 : 1;
		}

		Check("sin ninguna textura: suelos distintos entre salas (≥ 70 % distintos)", floors.Count >= count * 7 / 10, $"{floors.Count} de {count}");
		Check("sin ninguna textura: paredes distintas entre salas (≥ 70 % distintas)", walls.Count >= count * 7 / 10, $"{walls.Count} de {count}");
		Check("sin ninguna textura: la variación queda dentro del rango sutil", outOfRange == 0, $"{outOfRange} fuera de rango");
		Check("sin ninguna textura: no aparece ninguna textura", textured == 0, $"{textured}");
		Check("la misma semilla y celda dan el mismo color",
			RoomLook.Floor(RoomType.Normal, Colors.Gray, 2.0f, 5, CellOf(5)).AlbedoColor == RoomLook.Floor(RoomType.Normal, Colors.Gray, 2.0f, 5, CellOf(5)).AlbedoColor, "distinto");

		RoomSurfaces.TextureFolder = "res://tests/fixtures/textures/";
		string picked = RoomLook.PickVariant(new[] { "no_existe_uno", RoomSurfaces.Parquet, "no_existe_dos" }, 1, CellOf(1), true);
		Check("una variante ausente se ignora y se elige la que existe", picked == RoomSurfaces.Parquet, $"eligió {picked}");
		Check("sin ninguna variante presente PickVariant devuelve null", RoomLook.PickVariant(new[] { "no_existe_uno" }, 1, CellOf(1), false) == null, "no es null");
		StandardMaterial3D legacy = RoomLook.Floor(RoomType.Normal, Colors.Gray, 2.0f, 1, CellOf(1));
		Check("sin variantes, el suelo normal sigue usando parquet", legacy.AlbedoTexture != null && legacy.Uv1Triplanar && legacy.Uv1WorldTriplanar, "sin parquet triplanar");

		// Salas reales sin texturas: se construyen igual, con juguetes y con luces.
		RoomSurfaces.TextureFolder = "res://tests/fixtures/none/";
		RoomController room = await NewRoom(RoomType.Normal, Sizes[1], 6, true);
		Check("sin texturas la sala se construye con juguetes y luces", room.GetNode(RoomDecorator.DecorName).GetChildren().Any(c => c.Name.ToString().StartsWith("Toy")) && room.GetNodeOrNull(RoomLights.NodeName) != null, "sala incompleta");
		room.Free();

		// Con las variantes reales del artista (si ya están en assets/textures/).
		RoomSurfaces.TextureFolder = "res://assets/textures/";
		List<string> floorVariants = RoomLook.Available(RoomLook.FloorVariants), wallVariants = RoomLook.Available(RoomLook.WallVariants);
		GD.Print($"  variantes reales presentes: suelo {floorVariants.Count} ({string.Join(",", floorVariants)}), pared {wallVariants.Count} ({string.Join(",", wallVariants)})");
		if (floorVariants.Count > 0)
		{
			HashSet<string> used = new();
			bool allTriplanar = true, allFiltered = true, allMipmapped = true;
			for (int seed = 1; seed <= count; seed++)
			{
				StandardMaterial3D floor = RoomLook.Floor(RoomType.Normal, Colors.Gray, 2.0f, seed, CellOf(seed));
				allTriplanar &= floor.AlbedoTexture != null && floor.Uv1Triplanar && floor.Uv1WorldTriplanar && floor.AlbedoColor.IsEqualApprox(Colors.White);
				allFiltered &= floor.TextureFilter == BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
				allMipmapped &= floor.AlbedoTexture?.GetImage()?.HasMipmaps() == true;
				used.Add(RoomLook.PickVariant(RoomLook.FloorVariants, seed, CellOf(seed), true));
			}

			Check("con variantes de suelo: filtro anisotrópico con mipmaps (contra el moiré)", allFiltered, "otro filtro");
			Check("con variantes de suelo: las texturas están importadas con mipmaps", allMipmapped, "alguna textura sin mipmaps");
			RoomController tiled = GD.Load<PackedScene>("res://scenes/rooms/Room.tscn").Instantiate<RoomController>();
			float tile = tiled.TextureTileMeters;
			tiled.Free();
			Check("la repetición de las texturas no es demasiado fina (≥ 3 m)", tile >= 3.0f, $"{tile} m");

			Check("con variantes de suelo: triplanar mundial y sin estirar", allTriplanar, "alguna sala sin triplanar");
			Check("con variantes de suelo: se reparten entre las salas", floorVariants.Count == 1 || used.Count > 1, $"{used.Count} usadas");
		}
		else
		{
			GD.Print("  (no hay floor_*.png todavía: se omite la comprobación de variantes de suelo)");
		}

		if (wallVariants.Count > 0)
		{
			StandardMaterial3D wall = RoomLook.Wall(RoomType.Normal, Colors.Gray, 2.0f, 2, CellOf(2));
			Check("con variantes de pared: triplanar mundial", wall.AlbedoTexture != null && wall.Uv1Triplanar && wall.Uv1WorldTriplanar, "sin triplanar");
			Check("con variantes de pared: filtro anisotrópico con mipmaps", wall.TextureFilter == BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
				&& wall.AlbedoTexture?.GetImage()?.HasMipmaps() == true, "sin mipmaps o con otro filtro");
		}
		else
		{
			GD.Print("  (no hay wall_*.png todavía: se omite la comprobación de variantes de pared)");
		}
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task Lights()
	{
		GD.Print("\n--- Luces ---");
		RoomLights.Enabled = true;
		int badCount = 0, outside = 0, badEnergy = 0, notWarm = 0, ones = 0, twos = 0, notDeterministic = 0;
		foreach (Vector2 size in Sizes)
		{
			for (int seed = 1; seed <= Batch; seed++)
			{
				List<RoomLights.LightSpec> lights = RoomLights.Plan(size, CellOf(seed), seed);
				notDeterministic += string.Join("|", lights) == string.Join("|", RoomLights.Plan(size, CellOf(seed), seed)) ? 0 : 1;
				badCount += lights.Count is < 1 or > 2 ? 1 : 0;
				ones += lights.Count == 1 ? 1 : 0;
				twos += lights.Count == 2 ? 1 : 0;
				foreach (RoomLights.LightSpec light in lights)
				{
					outside += Mathf.Abs(light.Position.X) > size.X / 2.0f - RoomLights.EdgeMargin + 0.001f || Mathf.Abs(light.Position.Z) > size.Y / 2.0f - RoomLights.EdgeMargin + 0.001f
						|| light.Position.Y >= 3.0f ? 1 : 0;
					badEnergy += light.Energy < RoomLights.MinEnergy - 0.001f || light.Energy > RoomLights.MaxEnergy + 0.001f || light.Range > RoomLights.Range + 0.001f ? 1 : 0;
					notWarm += light.Color.R >= light.Color.G && light.Color.G >= light.Color.B ? 0 : 1;
				}
			}
		}

		Check("cada sala lleva 1 o 2 luces (y salen de las dos cantidades)", badCount == 0 && ones > 0 && twos > 0, $"{badCount} fuera de rango, {ones} de una, {twos} de dos");
		Check("las luces quedan en el interior, bajo el borde de las paredes", outside == 0, $"{outside}");
		Check("energía suave y rango limitado", badEnergy == 0, $"{badEnergy}");
		Check("color cálido (rojo ≥ verde ≥ azul)", notWarm == 0, $"{notWarm}");
		Check("la misma semilla y celda dan las mismas luces", notDeterministic == 0, $"{notDeterministic}");

		foreach (RoomType type in new[] { RoomType.Normal, RoomType.Start, RoomType.Treasure, RoomType.Boss })
		{
			RoomController room = await NewRoom(type, type == RoomType.Boss ? new Vector2(26, 26) : Sizes[1], 7, true);
			List<OmniLight3D> nodes = room.GetNode(RoomLights.NodeName).GetChildren().OfType<OmniLight3D>().ToList();
			Check($"{type}: 1 o 2 OmniLight3D sin sombras y de rango limitado", nodes.Count is 1 or 2 && nodes.All(l => !l.ShadowEnabled && l.OmniRange <= RoomLights.Range + 0.001f), $"{nodes.Count} luces");
			room.Free();
		}

		RoomLights.Enabled = false;
		RoomController dark = await NewRoom(RoomType.Normal, Sizes[1], 7, true);
		Check("RoomLights.Enabled = false: la sala no crea ninguna luz y sigue decorada", dark.GetNodeOrNull(RoomLights.NodeName) == null
			&& Descendants(dark).All(n => n is not OmniLight3D) && dark.GetNodeOrNull(RoomDecorator.DecorName) != null, "hay luces");
		dark.Free();
		RoomLights.Enabled = true;
	}

	// --- Utilidades ---------------------------------------------------------------------------------------------------

	private async Task<RoomController> NewRoom(RoomType type, Vector2 size, int seed, bool decor)
	{
		RoomData data = new() { Type = type, Size = size, Cell = CellOf(seed) };
		RoomController room = GD.Load<PackedScene>("res://scenes/rooms/Room.tscn").Instantiate<RoomController>();
		room.Setup(data, AllDoors, false);
		room.DecorEnabled = decor;
		room.DecorSeed = seed;
		AddChild(room);
		await Frames(1);
		return room;
	}

	private static string Signature(Node room)
	{
		List<string> lines = new();
		foreach (Node node in Descendants(room))
		{
			if (node is CollisionObject3D body && !InDecor(node))
			{
				string shapes = string.Join(",", body.GetChildren().OfType<CollisionShape3D>().Select(s => s.Shape is BoxShape3D box ? $"box{box.Size}@{s.Position}" : s.Shape?.GetType().Name ?? "none"));
				lines.Add($"{node.Name} {body.Position} L{body.CollisionLayer} M{body.CollisionMask} [{shapes}]");
			}
		}

		lines.Sort(StringComparer.Ordinal);
		return string.Join("\n", lines);
	}

	private static bool InDecor(Node node)
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
		foreach (Node child in root.GetChildren())
		{
			yield return child;
			foreach (Node deeper in Descendants(child))
			{
				yield return deeper;
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
		if (ok)
		{
			GD.Print($"  OK     {name}");
		}
		else
		{
			GD.Print($"  FALLO  {name}: {detail}");
			_failures.Add(name);
		}
	}
}
