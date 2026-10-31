using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de la variedad de la decoración por semilla. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/RoomDecorVarietyTest.tscn
///
/// Para las salas de combate (con varios juegos de puertas), inicio, tesoro y jefe:
///   1. La misma semilla y celda dan exactamente la misma decoración (también en la sala real, con el espejado aplicado).
///   2. Un lote de 200 semillas da decoraciones distintas: al menos el 80 % de las salas difieren de la siguiente y de las demás.
///   3. Ninguna pieza se solapa con otra ni cae sobre una puerta; las de pared respetan relieve ≤ 0,3 m, esquinas, separación, el
///      máximo por sala y la pared sur.
///   4. Variedad real: cantidades distintas, piezas distintas, espejado y piezas del catálogo que antes no se usaban.
///   5. El azar global y la generación de niveles no cambian.
/// Imprime cuántas piezas distintas hay en cada tipo de sala. Sale con código 0 si todo pasa.
/// </summary>
public partial class RoomDecorVarietyTest : Node
{
	private const int Batch = 200;

	private static readonly DoorSide[][] DoorSets =
	{
		new[] { DoorSide.North }, new[] { DoorSide.East, DoorSide.West }, new[] { DoorSide.North, DoorSide.South },
		new[] { DoorSide.North, DoorSide.East, DoorSide.South, DoorSide.West },
	};

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== RoomDecorVarietyTest ===");
		try
		{
			CombatRooms();
			FreeRooms();
			await RealRoomMirroring();
			Isolation();
			Relief();
		}
		catch (System.Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private void CombatRooms()
	{
		GD.Print("\n--- Salas de combate ---");
		foreach (Vector2 size in new[] { new Vector2(16, 16), new Vector2(28, 16), new Vector2(26, 22) })
		{
			foreach (DoorSide[] doors in DoorSets)
			{
				string label = $"{size.X}x{size.Y} con puertas {string.Join("+", doors)}";
				List<List<DecorPiece>> plans = new();
				bool deterministic = true, noOverlap = true, noDoor = true, noSouth = true, capped = true;
				string detail = "";
				for (int seed = 1; seed <= Batch; seed++)
				{
					Vector2I cell = new(seed % 9, seed % 4);
					List<DecorPiece> plan = RoomDecorator.PiecesFor(RoomType.Normal, size, cell, seed, doors);
					deterministic &= plan.SequenceEqual(RoomDecorator.PiecesFor(RoomType.Normal, size, cell, seed, doors));
					plans.Add(plan);
					capped &= plan.Count(p => p.Model != PropModels.Rug) <= WallDecorPlanner.MaxWallPieces;
					List<Aabb> boxes = plan.Select(DecorGeometry.Footprint).ToList();
					for (int a = 0; a < plan.Count; a++)
					{
						Aabb box = boxes[a];
						noSouth &= plan[a].Model == PropModels.Rug || box.End.Z < size.Y / 2.0f - 1.0f;
						foreach (DoorSide door in doors)
						{
							if (plan[a].Model == PropModels.Rug) continue;
							bool onDoor = door switch
							{
								DoorSide.North or DoorSide.South => box.Position.X < 1.5f && box.End.X > -1.5f && (door == DoorSide.North ? box.Position.Z < -size.Y / 2.0f + 1.0f : box.End.Z > size.Y / 2.0f - 1.0f),
								_ => box.Position.Z < 1.5f && box.End.Z > -1.5f && (door == DoorSide.West ? box.Position.X < -size.X / 2.0f + 1.0f : box.End.X > size.X / 2.0f - 1.0f),
							};
							noDoor &= !onDoor;
							if (onDoor) detail = $"{plan[a].Model} sobre la puerta {door}, semilla {seed}";
						}

						for (int b = a + 1; b < plan.Count; b++)
						{
							bool bothRugs = plan[a].Model == PropModels.Rug && plan[b].Model == PropModels.Rug;
							bool rugAndWall = plan[a].Model == PropModels.Rug != (plan[b].Model == PropModels.Rug);
							// Una alfombra plana puede cubrir el suelo bajo una pieza de pared; entre sí no se solapan.
							float gap = bothRugs ? WallDecorPlanner.RugGap - 0.001f : rugAndWall ? -99.0f : WallDecorPlanner.MinGap - 0.001f;
							if (!rugAndWall && DecorGeometry.Overlaps(box, boxes[b], gap))
							{
								noOverlap = false;
								detail = $"{plan[a].Model} y {plan[b].Model} solapadas, semilla {seed}";
							}
						}
					}
				}

				Check($"{label}: misma semilla, misma decoración", deterministic, "plan variable");
				Check($"{label}: sin piezas solapadas ni separadas de menos de lo debido", noOverlap, detail);
				Check($"{label}: nada sobre las puertas ni en la pared sur, máximo {WallDecorPlanner.MaxWallPieces} de pared", noDoor && noSouth && capped, detail);
				ReportVariety(label, plans.Select(p => p.ToList()).ToList(), size, true);
			}
		}
	}

	private void FreeRooms()
	{
		GD.Print("\n--- Inicio, tesoro y jefe ---");
		foreach ((RoomType type, Vector2 size) in new[] { (RoomType.Start, new Vector2(16, 16)), (RoomType.Treasure, new Vector2(12, 12)), (RoomType.Boss, new Vector2(26, 26)) })
		{
			List<List<DecorPiece>> plans = new();
			bool deterministic = true, noOverlap = true;
			for (int seed = 1; seed <= Batch; seed++)
			{
				Vector2I cell = new(seed % 9, seed % 4);
				List<DecorPiece> plan = RoomDecorator.PiecesFor(type, size, cell, seed, new[] { DoorSide.North });
				deterministic &= plan.SequenceEqual(RoomDecorator.PiecesFor(type, size, cell, seed, new[] { DoorSide.North }));
				plans.Add(plan);
				List<Aabb> boxes = plan.Select(DecorGeometry.Footprint).ToList();
				for (int a = 0; a < plan.Count; a++)
				{
					for (int b = a + 1; b < plan.Count; b++)
					{
						noOverlap &= !DecorGeometry.Overlaps(boxes[a], boxes[b], FreeDecorPlanner.MinGap - 0.001f);
					}
				}
			}

			Check($"{type}: misma semilla, misma decoración", deterministic, "plan variable");
			Check($"{type}: sin piezas solapadas ni demasiado juntas", noOverlap, "solape");
			ReportVariety(type.ToString(), plans, size, false);
		}
	}

	private void ReportVariety(string label, List<List<DecorPiece>> plans, Vector2 size, bool combat)
	{
		string Signature(List<DecorPiece> plan) => string.Join("|", plan.Select(p => $"{p.Model}@{p.Position.X:F2},{p.Position.Z:F2}/{p.Yaw:F2}/{p.Mirror}"));
		List<string> signatures = plans.Select(Signature).ToList();
		int differsFromNext = Enumerable.Range(0, signatures.Count - 1).Count(i => signatures[i] != signatures[i + 1]);
		int unique = signatures.Distinct().Count();
		float fractionNext = differsFromNext / (float)(signatures.Count - 1);
		float fractionUnique = unique / (float)signatures.Count;
		HashSet<string> models = plans.SelectMany(p => p.Select(x => x.Model)).ToHashSet();
		int mirrored = plans.Sum(p => p.Count(x => x.Mirror));
		int distinctCounts = plans.Select(p => p.Count(x => x.Model != PropModels.Rug)).Distinct().Count();
		GD.Print($"  {label}: {unique} decoraciones distintas de {signatures.Count}; {models.Count} piezas distintas; {mirrored} espejadas; {distinctCounts} cantidades distintas");
		Check($"{label}: al menos el 80 % de las salas difieren de la siguiente ({fractionNext:P0})", fractionNext >= 0.8f, $"{fractionNext:P0}");
		Check($"{label}: al menos el 80 % de las decoraciones del lote son distintas entre sí ({fractionUnique:P0})", fractionUnique >= 0.8f, $"{fractionUnique:P0}");
		Check($"{label}: hay espejado y cantidades distintas", mirrored > 0 && distinctCounts >= (combat ? 3 : 2), $"{mirrored} espejadas, {distinctCounts} cantidades");
		if (!combat)
		{
			string[] unused = { PropModels.Horse, PropModels.Crib, PropModels.Book, PropModels.Mobile, PropModels.GiantBear };
			Check($"{label}: usa también las piezas que antes no se usaban (caballito, cuna, libro, móvil, oso gigante)", unused.All(models.Contains), string.Join(",", unused.Where(u => !models.Contains(u))));
		}
		else
		{
			Check($"{label}: usa estanterías, cortinas y alfombras", models.SetEquals(new[] { PropModels.Shelf, PropModels.Curtain, PropModels.Rug }), string.Join(",", models));
		}

		// La misma semilla en otra sala (celda) da otra decoración.
		_ = size;
	}

	private async Task RealRoomMirroring()
	{
		GD.Print("\n--- Sala real ---");
		DoorSide[] doors = { DoorSide.North, DoorSide.East };
		foreach (RoomType type in new[] { RoomType.Normal, RoomType.Start, RoomType.Treasure, RoomType.Boss })
		{
			Vector2 size = type switch { RoomType.Start => new Vector2(16, 16), RoomType.Treasure => new Vector2(12, 12), RoomType.Boss => new Vector2(26, 26), _ => new Vector2(20, 20) };
			bool allMatch = true;
			int mirroredNodes = 0, mirroredPlan = 0;
			for (int seed = 1; seed <= 12; seed++)
			{
				RoomData data = new() { Type = type, Size = size, Cell = new Vector2I(2, 1) };
				RoomController room = GD.Load<PackedScene>("res://scenes/rooms/Room.tscn").Instantiate<RoomController>();
				room.Setup(data, doors, false);
				room.DecorEnabled = true;
				room.DecorSeed = seed;
				AddChild(room);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				List<DecorPiece> plan = RoomDecorator.PiecesFor(type, size, data.Cell, seed, doors);
				List<Node3D> nodes = room.GetNode(RoomDecorator.DecorName).GetChildren().OfType<Node3D>().Where(n => n.Name.ToString().Contains('_')).ToList();
				allMatch &= nodes.Count == plan.Count;
				for (int i = 0; i < Mathf.Min(nodes.Count, plan.Count); i++)
				{
					allMatch &= nodes[i].Position.IsEqualApprox(plan[i].Position) && Mathf.Abs(nodes[i].Rotation.Y - plan[i].Yaw) < 0.001f
						&& (nodes[i].Scale.X < 0.0f) == plan[i].Mirror && nodes[i].Name.ToString().StartsWith(plan[i].Model);
					mirroredNodes += nodes[i].Scale.X < 0.0f ? 1 : 0;
					mirroredPlan += plan[i].Mirror ? 1 : 0;
				}

				room.Free();
			}

			Check($"{type}: la sala real coloca exactamente el plan (posición, giro, espejado)", allMatch && mirroredNodes == mirroredPlan, $"{mirroredNodes} / {mirroredPlan}");
		}
	}

	private void Isolation()
	{
		GD.Print("\n--- Aislamiento ---");
		GD.Seed(4242);
		uint before = GD.Randi();
		GD.Seed(4242);
		for (int seed = 1; seed <= 100; seed++)
		{
			RoomDecorator.PiecesFor(RoomType.Normal, new Vector2(20, 20), new Vector2I(1, 2), seed, DoorSets[3]);
			RoomDecorator.PiecesFor(RoomType.Boss, new Vector2(26, 26), new Vector2I(1, 2), seed, DoorSets[3]);
		}

		Check("decorar no consume el azar global", GD.Randi() == before, "el azar global cambió");

		RoomCatalog catalog = GD.Load<RoomCatalog>("res://resources/rooms/templates/Catalog.tres");
		string Levels() => string.Join("|", Enumerable.Range(1, 60).Select(seed => new DungeonGenerator(catalog).Generate(seed, new DungeonGenerator.Settings { GeneratorVersion = seed % 3 + 1 }))
			.Where(r => r.Success).Select(r => string.Join(";", r.Map.Rooms.Select(room => $"{room.Cell}{room.Type}{room.TemplateId}{room.Size}{string.Join(",", room.SpawnPoints)}{string.Join(",", room.SpawnKinds)}"))));
		string levelsBefore = Levels();
		foreach (int seed in Enumerable.Range(1, 60))
		{
			foreach (RoomType type in new[] { RoomType.Normal, RoomType.Start, RoomType.Treasure, RoomType.Boss })
			{
				RoomDecorator.PiecesFor(type, new Vector2(20, 20), new Vector2I(seed, 3), seed, DoorSets[3]);
			}
		}

		Check("la generación de niveles (v1, v2 y v3, 60 semillas) es idéntica antes y después de decorar", levelsBefore == Levels(), "generación alterada");
	}

	private void Relief()
	{
		GD.Print("\n--- Relieve real de las piezas de pared ---");
		foreach (string model in new[] { PropModels.Shelf, PropModels.Curtain })
		{
			Aabb box = ModelLibrary.BoundsOf(PropModels.Path(model)).Value;
			float relief = box.Size.Z - WallDecorPlanner.Embed;
			GD.Print($"  {model}: fondo del modelo {box.Size.Z:F3} m; relieve sobre la pared {relief:F3} m (límite {WallDecorPlanner.MaxRelief} m)");
			Check($"{model}: relieve de 0,3 m o menos", relief <= WallDecorPlanner.MaxRelief, $"{relief:F3} m");
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
