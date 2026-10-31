using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la decoración por tipo de sala (piezas de pared de combate, piezas exentas de inicio, tesoro y jefe, pedestales).
///   godot --headless --fixed-fps 60 --path . res://tests/RoomDecorLayoutTest.tscn
///
/// 1. Salas de combate: el plan es determinista por semilla y celda, usa solo estanterías, cortinas y alfombra, no toca la pared sur,
///    deja el relieve en 0,3 m o menos, respeta esquinas y puertas, y no altera el azar global ni la generación de niveles.
/// 2. Piezas exentas (inicio, tesoro, jefe): deterministas, fuera de las llegadas, las puertas, el área central y la pared sur, sin solapes.
/// 3. Salas reales con ambientación: colisiones idénticas a las de la sala sin ambientar, ninguna pieza con colisión.
/// 4. Encuadre de cámara de todas las piezas, para todos los tamaños de plantilla y la sala del jefe.
/// 5. Presupuesto de nodos y triángulos por tipo de sala.
/// 6. Pedestales del tesoro: uno por oferta, con el objeto encima y sin cambiar la recogida.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class RoomDecorLayoutTest : Node
{
	private const int MeshBudget = 80;
	private const int TriangleBudget = 60000;
	private const float MotherReach = 11.4f; // centro a 3 m de la pared (ArenaMargin) en 26 m + ~1,4 m de medio cuerpo

	private static readonly Vector2[] Sizes =
	{
		new(12, 12), new(16, 16), new(18, 18), new(20, 20), new(26, 22), new(28, 16), new(16, 26), new(26, 26),
	};

	private static readonly DoorSide[][] DoorSets =
	{
		new[] { DoorSide.North }, new[] { DoorSide.South }, new[] { DoorSide.East, DoorSide.West },
		new[] { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West },
	};

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== RoomDecorLayoutTest ===");
		try
		{
			CombatPlans();
			FreeLayouts();
			await RealRooms();
			await CameraFraming();
			await Budget();
			await Pedestals();
		}
		catch (System.Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private void CombatPlans()
	{
		GD.Print("\n--- Salas de combate ---");
		bool deterministic = true, differs = false, onlyAllowed = true, noSouth = true, relief = true, margins = true, capped = true, rugInside = true;
		int total = 0, withRug = 0;
		string detail = "";
		HashSet<string> kinds = new();
		for (int seed = 1; seed <= 150; seed++)
		{
			foreach (Vector2 size in Sizes)
			{
				foreach (DoorSide[] doors in DoorSets)
				{
					Vector2I cell = new(seed % 7, seed % 5);
					List<DecorPiece> a = WallDecorPlanner.Plan(seed, cell, size, doors);
					List<DecorPiece> b = WallDecorPlanner.Plan(seed, cell, size, doors);
					deterministic &= a.SequenceEqual(b);
					differs |= !a.SequenceEqual(WallDecorPlanner.Plan(seed + 1000, cell, size, doors));
					float hx = size.X / 2.0f, hz = size.Y / 2.0f;
					capped &= a.Count(p => p.Model != PropModels.Rug) <= WallDecorPlanner.MaxWallPieces;
					foreach (DecorPiece piece in a)
					{
						kinds.Add(piece.Model);
						total++;
						if (piece.Model == PropModels.Rug)
						{
							withRug++;
							Aabb rug = World(piece);
							rugInside &= Mathf.Abs(piece.Position.X) <= hx - WallDecorPlanner.RugMargin + 0.001f && Mathf.Abs(piece.Position.Z) <= hz - WallDecorPlanner.RugMargin + 0.001f
								&& piece.Position.Y == 0.0f && rug.Size.Y < 0.1f;
							continue;
						}

						onlyAllowed &= piece.Model == PropModels.Shelf || piece.Model == PropModels.Curtain;
						Aabb box = World(piece);
						noSouth &= box.End.Z < hz - 1.0f;
						// Relieve: lo que sobresale de la cara interior de la pared más cercana.
						float toNorth = box.Position.Z + hz, toEast = hx - box.End.X, toWest = box.Position.X + hx;
						bool north = toNorth < 1.0f, east = toEast < 1.0f, west = toWest < 1.0f;
						float protrusion = north ? box.End.Z + hz : east ? hx - box.Position.X : west ? box.End.X + hx : 99.0f;
						if (!(protrusion <= WallDecorPlanner.MaxRelief + 0.001f))
						{
							relief = false;
							detail = $"{piece.Model} sobresale {protrusion:F3} en {size}";
						}

						float along = north ? piece.Position.X : piece.Position.Z;
						float half = (north ? size.X : size.Y) / 2.0f;
						float width = north ? box.Size.X : box.Size.Z;
						margins &= Mathf.Abs(along) + width / 2.0f <= half - WallDecorPlanner.CornerMargin + 0.001f;
						DoorSide wall = north ? DoorSide.North : east ? DoorSide.East : DoorSide.West;
						if (doors.Contains(wall))
						{
							margins &= Mathf.Abs(along) - width / 2.0f >= WallDecorPlanner.DoorClear - 0.001f;
						}
					}
				}
			}
		}

		GD.Print($"  {total} piezas en 150 semillas x {Sizes.Length} tamaños x {DoorSets.Length} juegos de puertas; alfombras {withRug}");
		Check("el plan es idéntico con la misma semilla y celda", deterministic, "planes distintos");
		Check("otra semilla da otro plan", differs, "siempre el mismo plan");
		Check("solo hay estanterías, cortinas y alfombra", onlyAllowed && kinds.IsSubsetOf(new[] { PropModels.Shelf, PropModels.Curtain, PropModels.Rug }), string.Join(",", kinds));
		Check("hay de las tres piezas", kinds.Count == 3, string.Join(",", kinds));
		Check($"como mucho {WallDecorPlanner.MaxWallPieces} piezas de pared", capped, "demasiadas");
		Check("nada en la pared sur", noSouth, "pieza al sur");
		Check("relieve de 0,3 m o menos", relief, detail);
		Check("a 1,2 m de las esquinas y a 2,3 m del centro de las puertas", margins, "pieza mal colocada");
		Check("la alfombra es plana y queda dentro de la sala", rugInside, "alfombra fuera");

		GD.Seed(777);
		uint before = GD.Randi();
		GD.Seed(777);
		for (int seed = 1; seed <= 50; seed++)
		{
			WallDecorPlanner.Plan(seed, new Vector2I(1, 1), new Vector2(20, 20), DoorSets[3]);
		}

		Check("planificar no consume el azar global", GD.Randi() == before, "el azar global cambió");

		// La generación de niveles no cambia por decorar: mismo nivel antes y después de planificar sus salas.
		RoomCatalog catalog = GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres");
		string levelsBefore = Levels(catalog), levelsMid;
		foreach (LevelMap map in Generate(catalog, 40))
		{
			foreach (RoomData room in map.Rooms)
			{
				RoomDecorator.PiecesFor(room.Type, room.Size, room.Cell, 99, new[] { DoorSide.North });
			}
		}

		levelsMid = Levels(catalog);
		Check("los niveles generados no cambian al decorar sus salas (40 semillas)", levelsBefore == levelsMid, "generación alterada");
	}

	private static IEnumerable<LevelMap> Generate(RoomCatalog catalog, int count)
	{
		DungeonGenerator generator = new(catalog);
		for (int seed = 1; seed <= count; seed++)
		{
			DungeonGenerator.Result result = generator.Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = 3 });
			if (result.Success)
			{
				yield return result.Map;
			}
		}
	}

	private static string Levels(RoomCatalog catalog) => string.Join("|", Generate(catalog, 40).Select(map => string.Join(";", map.Rooms.Select(r =>
		$"{r.Cell}{r.Type}{r.TemplateId}{r.Size}{string.Join(",", r.SpawnPoints)}{string.Join(",", r.SpawnKinds)}"))));

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private void FreeLayouts()
	{
		GD.Print("\n--- Piezas exentas (inicio, tesoro y jefe) ---");
		(RoomType type, Vector2 size)[] cases = { (RoomType.Start, new Vector2(16, 16)), (RoomType.Treasure, new Vector2(12, 12)), (RoomType.Boss, new Vector2(26, 26)) };
		foreach ((RoomType type, Vector2 size) in cases)
		{
			float hx = size.X / 2.0f, hz = size.Y / 2.0f;
			bool deterministic = true, counts = true, allowed = true, inside = true, arrivals = true, doorsClear = true, southClear = true;
			bool center = true, lane = true, overlaps = false, giantsPresent = true, noGiants = true;
			string detail = "";
			HashSet<string> used = new();
			(int min, int max) = FreeDecorPlanner.CountRange(type);
			for (int seed = 1; seed <= 80; seed++)
			{
				Vector2I cell = new(seed % 5, seed % 3);
				List<DecorPiece> pieces = FreeDecorPlanner.Plan(type, size, cell, seed);
				deterministic &= pieces.SequenceEqual(FreeDecorPlanner.Plan(type, size, cell, seed));
				int small = pieces.Count(p => FreeDecorPlanner.SmallCatalog.Contains(p.Model));
				counts &= small >= 3 && small <= max;
				giantsPresent &= type != RoomType.Boss || pieces.Any(p => FreeDecorPlanner.BossGiants.Contains(p.Model));
				noGiants &= type == RoomType.Boss || pieces.All(p => !FreeDecorPlanner.BossGiants.Contains(p.Model));
				for (int a = 0; a < pieces.Count; a++)
				{
					DecorPiece piece = pieces[a];
					used.Add(piece.Model);
					Aabb box = DecorGeometry.Footprint(piece);
					bool giant = FreeDecorPlanner.BossGiants.Contains(piece.Model);
					allowed &= giant || FreeDecorPlanner.SmallCatalog.Contains(piece.Model);
					float slack = giant ? 0.5f : 0.001f;
					inside &= box.Position.X >= -hx - slack && box.End.X <= hx + slack && box.Position.Z >= -hz - slack && box.End.Z <= hz + slack;
					southClear &= box.End.Z <= hz - 1.8f + 0.001f;
					foreach (Vector2 arrival in RoomRules.ArrivalPoints(size))
					{
						bool hits = box.Position.X < arrival.X + 1.5f && box.End.X > arrival.X - 1.5f && box.Position.Z < arrival.Y + 1.5f && box.End.Z > arrival.Y - 1.5f;
						arrivals &= !hits;
						if (hits) detail = $"{piece.Model} sobre la llegada {arrival}";
					}

					bool northSouthDoor = box.Position.X < 2.3f && box.End.X > -2.3f && (box.Position.Z < -hz + 2.0f || box.End.Z > hz - 2.0f);
					bool eastWestDoor = box.Position.Z < 2.3f && box.End.Z > -2.3f && (box.Position.X < -hx + 2.0f || box.End.X > hx - 2.0f);
					doorsClear &= !northSouthDoor && !eastWestDoor;
					if (northSouthDoor || eastWestDoor) detail = $"{piece.Model} en una puerta, semilla {seed}";
					float centralHalf = type == RoomType.Treasure ? 2.7f : type == RoomType.Start ? 3.0f : 0.0f;
					center &= centralHalf == 0.0f || !(box.Position.X < centralHalf && box.End.X > -centralHalf && box.Position.Z < centralHalf && box.End.Z > -centralHalf);
					if (type == RoomType.Boss)
					{
						float intrusion = FreeDecorPlanner.BossLaneIntrusion(box, size);
						float allowedIntrusion = giant ? FreeDecorPlanner.MaxBossLaneIntrusion : 0.001f;
						lane &= intrusion <= allowedIntrusion + 0.001f;
						if (intrusion > allowedIntrusion + 0.001f) detail = $"{piece.Model} invade {intrusion:F2} m, semilla {seed}";
					}

					for (int b = a + 1; b < pieces.Count; b++)
					{
						overlaps |= DecorGeometry.Overlaps(box, DecorGeometry.Footprint(pieces[b]), 0.0f);
					}
				}
			}

			GD.Print($"  {type}: piezas distintas usadas en 80 semillas: {used.Count} de {FreeDecorPlanner.SmallCatalog.Length + (type == RoomType.Boss ? 2 : 0)}");
			Check($"{type}: determinista y con entre 3 y {max} piezas pequeñas (de {min} a {max} pedidas)", deterministic && counts, "plan variable o con pocas piezas");
			Check($"{type}: usa muchas piezas distintas del catálogo", used.Count >= 11, $"{used.Count}");
			Check($"{type}: solo piezas del catálogo", allowed, "pieza ajena");
			Check($"{type}: todas dentro de la sala", inside, "pieza fuera");
			Check($"{type}: nada sobre las llegadas del jugador", arrivals, detail);
			Check($"{type}: nada en el hueco de las puertas", doorsClear, detail);
			Check($"{type}: nada pegado a la pared sur", southClear, "pieza al sur");
			Check($"{type}: nada en el área central", center, "pieza en el centro");
			Check($"{type}: ninguna pieza solapada con otra", !overlaps, "solape");
			if (type == RoomType.Boss)
			{
				Check("jefe: siempre hay una pieza gigante y ninguna invade el pasillo más de lo previsto (gigantes 0,5 m; el resto nada)", giantsPresent && lane, detail);
			}
			else
			{
				Check($"{type}: sin piezas gigantes", noGiants, "gigante fuera del jefe");
			}
		}
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task RealRooms()
	{
		GD.Print("\n--- Salas reales ---");
		foreach ((RoomType type, Vector2 size) in new[] { (RoomType.Normal, new Vector2(20, 20)), (RoomType.Start, new Vector2(16, 16)), (RoomType.Treasure, new Vector2(12, 12)), (RoomType.Boss, new Vector2(26, 26)) })
		{
			DoorSide[] doors = { DoorSide.North, DoorSide.East, DoorSide.South };
			RoomController off = await NewRoom(type, size, doors, false, 5);
			RoomController on = await NewRoom(type, size, doors, true, 5);
			RoomController again = await NewRoom(type, size, doors, true, 5);
			Node decor = on.GetNode(RoomDecorator.DecorName);
			int pieces = decor.GetChildren().Count(child => child.Name.ToString().Contains('_'));
			Check($"{type}: hay piezas de ambientación", pieces >= 1, "sin piezas");
			Check($"{type}: sin ambientar las colisiones son las mismas", Signature(off) == Signature(on), "colisiones distintas");
			Check($"{type}: ninguna pieza ni barrote tiene colisión", !Descendants(decor).Concat(doors.SelectMany(s => Descendants(on.GetNode<Door>($"Doors/Door{s}").Skin))).Any(n => n is CollisionObject3D or CollisionShape3D), "hay colisión");
			Check($"{type}: la misma semilla coloca lo mismo", Names(on) == Names(again), "distinto");
			off.Free();
			on.Free();
			again.Free();
		}
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task CameraFraming()
	{
		GD.Print("\n--- Encuadre de cámara ---");
		RoomCamera roomCamera = new() { Name = "Rig", RotationDegrees = new Vector3(-60, 0, 0) };
		Camera3D camera = new() { Name = "Camera3D", Fov = 60.0f, Current = true, Position = new Vector3(0, 0, 24) };
		roomCamera.AddChild(camera);
		AddChild(roomCamera);
		DoorSide[] all = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };
		List<(RoomType, Vector2, float)> cases = new();
		foreach (Vector2 size in Sizes)
		{
			cases.Add((RoomType.Normal, size, 3.0f));
		}

		cases.Add((RoomType.Start, new Vector2(16, 16), 3.0f));
		cases.Add((RoomType.Treasure, new Vector2(12, 12), 3.0f));
		// En la sala del jefe, RoomManager encuadra con la altura de The Mother (FramingHeight, 4,5 m) si es mayor que la pared.
		cases.Add((RoomType.Boss, new Vector2(26, 26), 4.5f));
		foreach ((RoomType type, Vector2 size, float height) in cases)
		{
			for (int seed = 1; seed <= 12; seed++)
			{
				RoomController room = await NewRoom(type, size, all, true, seed);
				roomCamera.Frame(size, height, 1.0f);
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

				Check($"{type} {size.X}x{size.Y} (semilla {seed}): todas las piezas dentro del encuadre", outside == 0, $"{outside} esquinas fuera, la primera {first}");
				room.Free();
			}
		}

		roomCamera.Free();
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task Budget()
	{
		GD.Print("\n--- Presupuesto ---");
		DoorSide[] all = { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West };
		foreach ((RoomType type, Vector2 size) in new[] { (RoomType.Normal, new Vector2(28, 16)), (RoomType.Start, new Vector2(16, 16)), (RoomType.Treasure, new Vector2(12, 12)), (RoomType.Boss, new Vector2(26, 26)) })
		{
			int worst = 0, worstTriangles = 0;
			for (int seed = 1; seed <= 20; seed++)
			{
				RoomController room = await NewRoom(type, size, all, true, seed);
				List<MeshInstance3D> meshes = Descendants(room.GetNode(RoomDecorator.DecorName)).OfType<MeshInstance3D>().ToList();
				foreach (DoorSide side in all)
				{
					meshes.AddRange(Descendants(room.GetNode<Door>($"Doors/Door{side}").Skin).OfType<MeshInstance3D>());
				}

				worst = Mathf.Max(worst, meshes.Count);
				worstTriangles = Mathf.Max(worstTriangles, meshes.Sum(mesh => mesh.Mesh.GetFaces().Length / 3));
				room.Free();
			}

			GD.Print($"  {type} {size.X}x{size.Y}, cuatro puertas: {worst} mallas, {worstTriangles} triángulos (peor caso)");
			Check($"{type}: como mucho {MeshBudget} mallas y {TriangleBudget} triángulos", worst <= MeshBudget && worstTriangles <= TriangleBudget, $"{worst} mallas, {worstTriangles} triángulos");
		}
	}

	// --- 6 ------------------------------------------------------------------------------------------------------------

	private async Task Pedestals()
	{
		GD.Print("\n--- Pedestales del tesoro ---");
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed);
		await Frames(3);
		TreasureItemSpawner spawner = level.Root.GetNode<TreasureItemSpawner>("TreasureItemSpawner");
		spawner.Enabled = true;
		spawner.BeginRun(level.Run.Seed, level.Rooms.Map);
		RoomData data = level.Rooms.Map.Rooms.First(room => room.Type == RoomType.Treasure);
		MethodInfo onRoomLoaded = typeof(TreasureItemSpawner).GetMethod("OnRoomLoaded", BindingFlags.Instance | BindingFlags.NonPublic);
		foreach (bool decor in new[] { false, true })
		{
			RoomController room = level.Rooms.RoomScene.Instantiate<RoomController>();
			room.Setup(data, new[] { DoorSide.North }, false);
			room.DecorEnabled = decor;
			level.Root.AddChild(room);
			onRoomLoaded.Invoke(spawner, new object[] { room });
			await Frames(2);
			Node items = room.GetNode("TreasureItems");
			List<ItemPickup> pickups = items.GetChildren().OfType<ItemPickup>().ToList();
			List<Node3D> pedestals = items.GetChildren().OfType<Node3D>().Where(n => n.Name.ToString().StartsWith("Pedestal")).ToList();
			int offers = spawner.OffersFor(data.Cell).Count;
			if (!decor)
			{
				Check("sin ambientación no hay pedestales y los objetos están a ras de suelo", pedestals.Count == 0 && pickups.Count == offers && pickups.All(p => p.Position.Y == 0.0f), $"{pedestals.Count} pedestales");
			}
			else
			{
				Check("con ambientación hay un pedestal por oferta", pedestals.Count == offers && pickups.Count == offers, $"{pedestals.Count} pedestales, {offers} ofertas");
				bool above = true, centered = true, noCollision = true;
				float top = ModelLibrary.BoundsOf(PropModels.Path(PropModels.Pedestal)).Value.Size.Y;
				foreach (ItemPickup pickup in pickups)
				{
					Node3D pedestal = pedestals.OrderBy(p => p.Position.DistanceTo(pickup.Position with { Y = 0.0f })).First();
					centered &= pedestal.Position.X == pickup.Position.X && pedestal.Position.Z == pickup.Position.Z;
					Aabb model = Bounds(pickup.Model);
					above &= model.Position.Y >= top - 0.01f && model.Position.Y <= top + 0.3f;
					noCollision &= !Descendants(pedestal).Any(n => n is CollisionObject3D or CollisionShape3D)
						&& pickup.CollisionLayer == 0 && pickup.CollisionMask == CollisionLayers.Player
						&& pickup.GetNode<CollisionShape3D>("CollisionShape3D").Shape is SphereShape3D { Radius: 0.65f };
				}

				Check("cada objeto está centrado sobre su pedestal", centered, "descentrado");
				Check("el modelo del objeto queda justo encima del pedestal (≤ 0,3 m de aire)", above, "altura incorrecta");
				Check("la recogida no cambia y el pedestal no tiene colisión", noCollision, "colisión distinta");
			}

			room.Free();
		}

		await BossTestHarness.Close(GetTree(), level);
	}

	// --- ayudas -------------------------------------------------------------------------------------------------------

	// Caja de la pieza en coordenadas de la sala (el modelo está centrado en XZ y apoyado en y = 0; giros múltiplos de 90° o no).
	private static Aabb World(DecorPiece piece)
	{
		Aabb box = ModelLibrary.BoundsOf(piece.Path).Value;
		Transform3D transform = new(Basis.FromEuler(new Vector3(0.0f, piece.Yaw, 0.0f)), piece.Position);
		Aabb local = new(new Vector3(-box.Size.X / 2.0f, 0.0f, -box.Size.Z / 2.0f), box.Size);
		return transform * local;
	}

	private async Task<RoomController> NewRoom(RoomType type, Vector2 size, DoorSide[] doors, bool decor, int seed)
	{
		RoomData data = new() { Type = type, Size = size, Cell = new Vector2I(3, 3) };
		RoomController room = GD.Load<PackedScene>("res://scenes/rooms/Room.tscn").Instantiate<RoomController>();
		room.Setup(data, doors, false);
		room.DecorEnabled = decor;
		room.DecorSeed = seed;
		AddChild(room);
		await Frames(1);
		return room;
	}

	private static string Names(Node room) => string.Join(",", room.GetNode(RoomDecorator.DecorName).GetChildren().Select(c => $"{c.Name}@{((Node3D)c).Position}"));

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

		lines.Sort(System.StringComparer.Ordinal);
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
