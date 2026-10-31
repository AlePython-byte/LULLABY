using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Plan determinista de la decoración de una SALA DE COMBATE (tipo Normal): solo piezas pegadas a la pared (estanterías y cortinas,
/// con la parte trasera hacia la pared y el frente hacia el interior) y alfombras planas. Nada con colisión y nada que cambie el
/// juego que la IA evaluó. Sin pared sur: la cámara no la ve (RoomDecorator.SkipSouth).
///
/// Variedad por semilla: en cada pared (norte, este, oeste) se eligen al azar la cantidad (0 a 2), la pieza, la posición a lo largo de
/// la pared y el espejado; y de 0 a 2 alfombras con posición y giro propios. En una sala de combate solo caben dos piezas de pared
/// distintas (estantería y cortina): el resto del catálogo tiene más de 0,3 m de fondo y vive en las salas de inicio, tesoro y
/// jefe (FreeDecorPlanner).
///
/// Reglas: relieve de MaxRelief (0,3 m) como mucho (las piezas se hunden Embed metros en la pared de 1 m), a CornerMargin de las
/// esquinas, a DoorClear del centro de cada pared con puerta, a MinGap entre sí y como mucho MaxWallPieces por sala. El azar sale de
/// SeedMix (generador propio por sala): la misma semilla de partida y celda dan siempre las mismas piezas.
/// </summary>
public static class WallDecorPlanner
{
	public const float MaxRelief = 0.3f;
	public const float Embed = 0.03f;
	public const float CornerMargin = 1.2f;
	public const float DoorClear = 2.3f;
	public const float MinGap = 0.6f;
	public const int MaxWallPieces = 4;
	public const float RugMargin = 3.2f;
	public const float RugGap = 0.3f;

	private const ulong Salt = 0x57414C4CUL;
	private const int Attempts = 14;

	public static List<DecorPiece> Plan(int gameSeed, Vector2I cell, Vector2 size, IReadOnlyCollection<DoorSide> doors)
	{
		RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, Salt);
		List<DecorPiece> pieces = new();
		List<Aabb> footprints = new();
		float hx = size.X / 2.0f;
		float hz = size.Y / 2.0f;

		string[] kinds = { PropModels.Shelf, PropModels.Curtain };
		foreach (DoorSide side in new[] { DoorSide.North, DoorSide.East, DoorSide.West })
		{
			float roll = random.Randf();
			int count = roll < 0.25f ? 0 : roll < 0.70f ? 1 : 2;
			float half = (side == DoorSide.North ? size.X : size.Y) / 2.0f;
			for (int i = 0; i < count; i++)
			{
				for (int attempt = 0; attempt < Attempts; attempt++)
				{
					string kind = kinds[random.Randf() < 0.5f ? 0 : 1];
					bool mirror = random.Randf() < 0.5f;
					float along = random.RandfRange(-half, half);
					Aabb? box = ModelLibrary.BoundsOf(PropModels.Path(kind));
					if (!box.HasValue)
					{
						continue;
					}

					float width = box.Value.Size.X;
					if (Mathf.Abs(along) + width / 2.0f > half - CornerMargin)
					{
						continue;
					}

					if (doors.Contains(side) && Mathf.Abs(along) - width / 2.0f < DoorClear)
					{
						continue;
					}

					DecorPiece piece = OnWall(side, along, kind, box.Value.Size.Z, hx, hz) with { Mirror = mirror };
					Aabb footprint = DecorGeometry.Footprint(piece);
					if (footprints.Any(other => DecorGeometry.Overlaps(footprint, other, MinGap)))
					{
						continue;
					}

					pieces.Add(piece);
					footprints.Add(footprint);
					break;
				}
			}
		}

		while (pieces.Count > MaxWallPieces)
		{
			int removed = random.RandiRange(0, pieces.Count - 1);
			pieces.RemoveAt(removed);
			footprints.RemoveAt(removed);
		}

		float rugRoll = random.Randf();
		int rugs = rugRoll < 0.35f ? 0 : rugRoll < 0.85f ? 1 : 2;
		string rugPath = PropModels.Path(PropModels.Rug);
		if (ModelLibrary.BoundsOf(rugPath).HasValue)
		{
			List<Aabb> rugFootprints = new();
			for (int i = 0; i < rugs; i++)
			{
				for (int attempt = 0; attempt < Attempts; attempt++)
				{
					float x = random.RandfRange(-(hx - RugMargin), hx - RugMargin);
					float z = random.RandfRange(-(hz - RugMargin), hz - RugMargin);
					float yaw = random.RandiRange(0, 3) * Mathf.Pi / 2.0f;
					DecorPiece rug = new(PropModels.Rug, new Vector3(x, 0.0f, z), yaw, random.Randf() < 0.5f);
					Aabb footprint = DecorGeometry.Footprint(rug);
					if (rugFootprints.Any(other => DecorGeometry.Overlaps(footprint, other, RugGap)))
					{
						continue;
					}

					pieces.Add(rug);
					rugFootprints.Add(footprint);
					break;
				}
			}
		}

		return pieces;
	}

	/// <summary>Pieza con la espalda contra la cara interior de la pared (hundida Embed) y el frente (-Z del modelo) hacia el interior.</summary>
	public static DecorPiece OnWall(DoorSide side, float along, string model, float depth, float hx, float hz)
	{
		Vector3 inward = DecorGeometry.Inward(side);
		Vector3 position = DecorGeometry.WallPoint(side, along, hx, hz) + inward * (depth / 2.0f - Embed);
		return new DecorPiece(model, position, DecorGeometry.FacingYaw(side));
	}
}
