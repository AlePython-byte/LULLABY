using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Plan determinista de los juguetes sueltos de una SALA DE COMBATE (tipo Normal), que se SUMAN a las piezas de pared y las alfombras
/// de WallDecorPlanner. Sin colisión. Usa el catálogo pequeño de FreeDecorPlanner (sin el oso gigante) y su propio azar de SeedMix.
///
/// De MinProps a MaxProps piezas junto a las paredes norte, este y oeste (con tendencia a las esquinas), con giro y espejado
/// aleatorios. Cada una cumple (si no cabe tras varios intentos, se descarta):
///   - dentro de la sala y a más de FreeDecorPlanner.SouthHiddenDepth de la pared sur (la cámara no la ve), y no más alta que MaxHeight;
///   - fuera de los puntos de llegada, del hueco de las puertas (se reserva el de las cuatro paredes, estén o no conectadas) y del
///     centro de la sala (CenterHalf);
///   - sin solapar piezas ya colocadas (las de pared y alfombras que se pasan en existing) ni entre sí, a FreeDecorPlanner.MinGap.
///
/// Presupuesto: existing y lo ya gastado en la sala (marcos, barrotes, columnas) se pasan como mallas y triángulos usados. Si las
/// piezas nuevas se pasan de MeshBudget o TriangleBudget, se descarta la más cara (por triángulos) hasta que quepan.
/// </summary>
public static class FloorPropsPlanner
{
	public const int MinProps = 3;
	public const int MaxProps = 6;

	/// <summary>Topes de la ambientación de una sala con cuatro puertas (los mismos que comprueban las pruebas de la decoración).</summary>
	public const int MeshBudget = 60;
	public const int TriangleBudget = 60000;

	public const float MaxSide = 1.8f;
	public const float MaxHeight = 2.8f;
	public const float CenterHalf = 3.0f;

	private const ulong Salt = 0x464C5052UL;
	private const int Attempts = 40;

	private static readonly Dictionary<string, (int Meshes, int Triangles)> CostCache = new();

	/// <summary>Piezas pequeñas elegibles: el catálogo de las salas exentas sin el oso gigante.</summary>
	public static IEnumerable<string> Catalog => FreeDecorPlanner.SmallCatalog.Where(model => model != PropModels.GiantBear);

	/// <summary>Los triángulos de este modelo ya están contados y en caché.</summary>
	public static bool IsCosted(string path) => CostCache.ContainsKey(path);

	/// <summary>Mallas y triángulos de un modelo (0, 0 si no existe). Se guarda en caché.</summary>
	public static (int Meshes, int Triangles) CostOf(string path)
	{
		if (CostCache.TryGetValue(path, out (int Meshes, int Triangles) cached))
		{
			return cached;
		}

		(int Meshes, int Triangles) cost = (0, 0);
		Node3D holder = ModelLibrary.Create(path);
		if (holder != null)
		{
			cost = Count(holder);
			holder.Free();
		}

		CostCache[path] = cost;
		return cost;
	}

	private static (int Meshes, int Triangles) Count(Node node)
	{
		(int meshes, int triangles) = (0, 0);
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			meshes++;
			triangles += mesh.Mesh.GetFaces().Length / 3;
		}

		foreach (Node child in node.GetChildren())
		{
			(int childMeshes, int childTriangles) = Count(child);
			meshes += childMeshes;
			triangles += childTriangles;
		}

		return (meshes, triangles);
	}

	public static List<DecorPiece> Plan(Vector2 size, Vector2I cell, int gameSeed, IReadOnlyCollection<DecorPiece> existing, int usedMeshes, int usedTriangles)
	{
		using RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, Salt);
		List<DecorPiece> pieces = new();
		List<Aabb> footprints = existing.Select(DecorGeometry.Footprint).ToList();
		float hx = size.X / 2.0f;
		float hz = size.Y / 2.0f;

		int wanted = random.RandiRange(MinProps, MaxProps);
		List<string> catalog = Catalog.ToList();
		for (int i = catalog.Count - 1; i > 0; i--)
		{
			int j = random.RandiRange(0, i);
			(catalog[i], catalog[j]) = (catalog[j], catalog[i]);
		}

		foreach (string model in catalog)
		{
			if (pieces.Count >= wanted)
			{
				break;
			}

			TryPlace(model);
		}

		// Si no caben en el presupuesto de la sala, se descarta la más cara.
		while (pieces.Count > 0)
		{
			(int meshes, int triangles) = (usedMeshes, usedTriangles);
			foreach (DecorPiece piece in pieces)
			{
				(int pieceMeshes, int pieceTriangles) = CostOf(piece.Path);
				meshes += pieceMeshes;
				triangles += pieceTriangles;
			}

			if (meshes <= MeshBudget && triangles <= TriangleBudget)
			{
				break;
			}

			pieces.Remove(pieces.OrderByDescending(piece => CostOf(piece.Path).Triangles).ThenByDescending(piece => CostOf(piece.Path).Meshes).First());
		}

		return pieces;

		void TryPlace(string model)
		{
			Aabb? box = ModelLibrary.BoundsOf(PropModels.Path(model));
			if (!box.HasValue || box.Value.Size.X > MaxSide || box.Value.Size.Z > MaxSide || box.Value.Size.Y > MaxHeight)
			{
				return;
			}

			for (int attempt = 0; attempt < Attempts; attempt++)
			{
				float roll = random.Randf();
				DoorSide wall = roll < 0.45f ? DoorSide.North : roll < 0.725f ? DoorSide.East : DoorSide.West;
				float half = (wall == DoorSide.North ? size.X : size.Y) / 2.0f;
				float reach = half - FreeDecorPlanner.CornerMargin;

				// Una de cada tres veces se pega a una esquina; el resto, a lo largo de la pared.
				float along = random.Randf() < 0.35f
					? (random.Randf() < 0.5f ? -1.0f : 1.0f) * (reach - random.RandfRange(0.0f, 1.5f))
					: random.RandfRange(-reach, reach);

				float yaw = DecorGeometry.FacingYaw(wall) + random.RandfRange(-0.7f, 0.7f) + (random.Randf() < 0.25f ? Mathf.Pi : 0.0f);
				bool mirror = random.Randf() < 0.5f;

				DecorPiece piece = new(model, Vector3.Zero, yaw, mirror);
				Aabb rotated = DecorGeometry.Footprint(piece);
				float depth = wall == DoorSide.North ? rotated.Size.Z : rotated.Size.X;
				float gap = random.RandfRange(0.05f, 0.6f);
				piece = piece with { Position = DecorGeometry.WallPoint(wall, along, hx, hz) + DecorGeometry.Inward(wall) * (depth / 2.0f + gap) };
				Aabb footprint = DecorGeometry.Footprint(piece);
				if (!Fits(footprint))
				{
					continue;
				}

				pieces.Add(piece);
				footprints.Add(footprint);
				return;
			}
		}

		bool Fits(Aabb f) => IsClear(f, size) && !footprints.Any(other => DecorGeometry.Overlaps(f, other, FreeDecorPlanner.MinGap));
	}

	/// <summary>
	/// Reglas de suelo libre de una huella en una sala de este tamaño (sin contar otras piezas): dentro de la sala, fuera de la
	/// pared sur oculta, de las llegadas, de las puertas de las cuatro paredes y del centro.
	/// </summary>
	public static bool IsClear(Aabb f, Vector2 size)
	{
		float hx = size.X / 2.0f;
		float hz = size.Y / 2.0f;
		if (f.Position.X < -hx || f.End.X > hx || f.Position.Z < -hz || f.End.Z > hz - FreeDecorPlanner.SouthHiddenDepth)
		{
			return false;
		}

		foreach (Vector2 arrival in RoomRules.ArrivalPoints(size))
		{
			float half = FreeDecorPlanner.ArrivalHalf;
			if (f.Position.X < arrival.X + half && f.End.X > arrival.X - half && f.Position.Z < arrival.Y + half && f.End.Z > arrival.Y - half)
			{
				return false;
			}
		}

		float doorClear = FreeDecorPlanner.DoorClear;
		float doorDepth = FreeDecorPlanner.DoorDepth;
		bool northSouthDoor = f.Position.X < doorClear && f.End.X > -doorClear && (f.Position.Z < -hz + doorDepth || f.End.Z > hz - doorDepth);
		bool eastWestDoor = f.Position.Z < doorClear && f.End.Z > -doorClear && (f.Position.X < -hx + doorDepth || f.End.X > hx - doorDepth);
		if (northSouthDoor || eastWestDoor)
		{
			return false;
		}

		return !(f.Position.X < CenterHalf && f.End.X > -CenterHalf && f.Position.Z < CenterHalf && f.End.Z > -CenterHalf);
	}
}
