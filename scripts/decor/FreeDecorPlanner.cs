using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Plan determinista de las piezas EXENTAS de las salas de inicio, tesoro y jefe. Sin colisión. En cada sala se elige al azar (con el
/// generador propio de SeedMix) un subconjunto del catálogo de piezas y se coloca en las paredes norte, este y oeste (esquinas y
/// bordes) con giro, espejado y separación de la pared aleatorios. Misma semilla de partida y celda, misma sala.
///
/// Catálogo: 13 piezas pequeñas (bloques, pelota, tambor, xilófono, lámpara, baúl, tren, castillo, caballito, cuna, libro, móvil y
/// oso gigante) y, solo en el jefe, la mecedora y la cuna gigantes. Son todos los modelos disponibles: no se inventa ninguno.
///
/// Cada pieza cumple (si no cabe tras varios intentos, se descarta):
///   - dentro de la sala y a más de 1,8 m de la pared sur (la cámara no la ve);
///   - fuera de los puntos de llegada del jugador (centro de cada lado, a 2,5 m de la pared) y del hueco de las puertas (centro de
///     cada pared, a cualquier lado);
///   - fuera del área central: el centro de Noah en inicio (±3 m), la cuadrícula de objetos del tesoro (±2,7 m) y el recorrido de
///     The Mother en el jefe (su centro queda a 3 m de las paredes y su cuerpo llega a ~1,4 m más allá);
///   - sin solaparse con otra pieza (a MinGap).
/// La mecedora y la cuna gigantes van de lado contra la pared y solo pueden invadir MaxBossLaneIntrusion (0,5 m) el pasillo del
/// jefe: la parte que sobra se hunde en la pared (que mide 1 m de grosor).
/// </summary>
public static class FreeDecorPlanner
{
	public const float MinGap = 0.25f;
	public const float CornerMargin = 1.2f;
	public const float SouthHiddenDepth = 1.8f;
	public const float DoorClear = 2.3f;
	public const float DoorDepth = 2.0f;
	public const float ArrivalHalf = 1.5f;
	public const float MaxBossLaneIntrusion = 0.5f;

	/// <summary>Medio cuerpo de The Mother más allá de su centro, y distancia mínima de su centro a las paredes (ArenaMargin).</summary>
	public const float MotherHalfBody = 1.4f;
	public const float MotherWallMargin = 3.0f;

	private const ulong Salt = 0x46524545UL;
	private const int Attempts = 40;

	/// <summary>Piezas pequeñas que pueden salir en cualquier sala exenta (el catálogo completo salvo los gigantes del jefe).</summary>
	public static readonly string[] SmallCatalog =
	{
		PropModels.Blocks, PropModels.Ball, PropModels.Drum, PropModels.Xylophone, PropModels.Lamp, PropModels.Chest,
		PropModels.Train, PropModels.Castle, PropModels.Horse, PropModels.Crib, PropModels.Book, PropModels.Mobile, PropModels.GiantBear,
	};

	public static readonly string[] BossGiants = { PropModels.GiantCrib, PropModels.Rocker };

	/// <summary>Cuántas piezas pequeñas, como mínimo y como máximo, lleva cada tipo de sala.</summary>
	public static (int Min, int Max) CountRange(RoomType type) => type switch
	{
		RoomType.Treasure => (4, 6),
		RoomType.Boss => (6, 9),
		_ => (6, 8),
	};

	/// <summary>Medio ancho (m) del recorrido de The Mother en una sala de este tamaño.</summary>
	public static float MotherReach(Vector2 size) => Mathf.Min(size.X, size.Y) / 2.0f - MotherWallMargin + MotherHalfBody;

	public static List<DecorPiece> Plan(RoomType type, Vector2 size, Vector2I cell, int gameSeed)
	{
		RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, Salt + (ulong)type);
		List<DecorPiece> pieces = new();
		List<Aabb> footprints = new();
		float hx = size.X / 2.0f;
		float hz = size.Y / 2.0f;

		// Primero los gigantes del jefe, que son los que más restricciones tienen: al menos uno, a veces los dos.
		if (type == RoomType.Boss)
		{
			string[] giants = random.Randf() < 0.5f ? BossGiants : new[] { BossGiants[random.RandiRange(0, 1)] };
			foreach (string giant in giants)
			{
				TryPlace(giant, true);
			}
		}

		(int min, int max) = CountRange(type);
		int wanted = random.RandiRange(min, max);
		List<string> catalog = SmallCatalog.ToList();
		for (int i = catalog.Count - 1; i > 0; i--)
		{
			int j = random.RandiRange(0, i);
			(catalog[i], catalog[j]) = (catalog[j], catalog[i]);
		}

		foreach (string model in catalog.Take(wanted))
		{
			TryPlace(model, false);
		}

		return pieces;

		void TryPlace(string model, bool giant)
		{
			Aabb? box = ModelLibrary.BoundsOf(PropModels.Path(model));
			if (!box.HasValue)
			{
				return;
			}

			for (int attempt = 0; attempt < (giant ? Attempts * 2 : Attempts); attempt++)
			{
				float roll = random.Randf();
				DoorSide wall = roll < 0.45f ? DoorSide.North : roll < 0.725f ? DoorSide.East : DoorSide.West;
				float half = (wall == DoorSide.North ? size.X : size.Y) / 2.0f;
				float along = random.RandfRange(-half + CornerMargin, half - CornerMargin);

				// Los gigantes van de lado (su eje largo paralelo a la pared); el resto, de frente con algo de giro.
				float yaw = DecorGeometry.FacingYaw(wall);
				yaw += giant ? (random.Randf() < 0.5f ? Mathf.Pi / 2.0f : -Mathf.Pi / 2.0f) : random.RandfRange(-0.7f, 0.7f) + (random.Randf() < 0.25f ? Mathf.Pi : 0.0f);
				bool mirror = random.Randf() < 0.5f;

				DecorPiece piece = new(model, Vector3.Zero, yaw, mirror);
				Aabb rotated = DecorGeometry.Footprint(piece);
				float depth = wall == DoorSide.North ? rotated.Size.Z : rotated.Size.X;
				float embed = 0.0f;
				if (giant)
				{
					// La parte que invade el pasillo del jefe más allá de lo permitido se hunde en la pared.
					float lane = (wall == DoorSide.North ? hz : hx) - MotherReach(size);
					embed = Mathf.Max(0.0f, depth - (lane + MaxBossLaneIntrusion) + 0.02f);
				}

				float gap = giant ? 0.0f : random.RandfRange(0.05f, 0.6f);
				Vector3 position = DecorGeometry.WallPoint(wall, along, hx, hz) + DecorGeometry.Inward(wall) * (depth / 2.0f + gap - embed);
				piece = piece with { Position = position };
				Aabb footprint = DecorGeometry.Footprint(piece);
				if (!Fits(footprint, giant, embed))
				{
					continue;
				}

				pieces.Add(piece);
				footprints.Add(footprint);
				return;
			}
		}

		bool Fits(Aabb f, bool giant, float embed)
		{
			float slack = embed + 0.02f;
			if (f.Position.X < -hx - slack || f.End.X > hx + slack || f.Position.Z < -hz - slack || f.End.Z > hz + slack)
			{
				return false;
			}

			if (f.End.Z > hz - SouthHiddenDepth)
			{
				return false;
			}

			foreach (Vector2 arrival in RoomRules.ArrivalPoints(size))
			{
				if (f.Position.X < arrival.X + ArrivalHalf && f.End.X > arrival.X - ArrivalHalf
					&& f.Position.Z < arrival.Y + ArrivalHalf && f.End.Z > arrival.Y - ArrivalHalf)
				{
					return false;
				}
			}

			bool northSouthDoor = f.Position.X < DoorClear && f.End.X > -DoorClear && (f.Position.Z < -hz + DoorDepth || f.End.Z > hz - DoorDepth);
			bool eastWestDoor = f.Position.Z < DoorClear && f.End.Z > -DoorClear && (f.Position.X < -hx + DoorDepth || f.End.X > hx - DoorDepth);
			if (northSouthDoor || eastWestDoor)
			{
				return false;
			}

			float center = type == RoomType.Treasure ? 2.7f : type == RoomType.Start ? 3.0f : 0.0f;
			if (center > 0.0f && f.Position.X < center && f.End.X > -center && f.Position.Z < center && f.End.Z > -center)
			{
				return false;
			}

			if (type == RoomType.Boss && BossLaneIntrusion(f, size) > (giant ? MaxBossLaneIntrusion : 0.001f))
			{
				return false;
			}

			return !footprints.Any(other => DecorGeometry.Overlaps(f, other, MinGap));
		}
	}

	/// <summary>Cuánto (m) entra una huella en el recorrido de The Mother (0 si no lo toca): el menor solape entre los dos ejes.</summary>
	public static float BossLaneIntrusion(Aabb footprint, Vector2 size)
	{
		float reach = MotherReach(size);
		float overlapX = Mathf.Min(footprint.End.X, reach) - Mathf.Max(footprint.Position.X, -reach);
		float overlapZ = Mathf.Min(footprint.End.Z, reach) - Mathf.Max(footprint.Position.Z, -reach);
		return overlapX > 0.0f && overlapZ > 0.0f ? Mathf.Min(overlapX, overlapZ) : 0.0f;
	}
}
