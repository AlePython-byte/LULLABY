using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Plan determinista de la decoración de una SALA DE COMBATE (tipo Normal): solo piezas pegadas a la pared (estanterías y cortinas,
/// con la parte trasera hacia la pared y el frente hacia el interior) y una alfombra plana. Nada con colisión y nada que cambie
/// el juego que la IA evaluó. Sin pared sur: la cámara no la ve (RoomDecorator.SkipSouth).
///
/// Las piezas de pared sobresalen como mucho MaxRelief (0,3 m): se hunden Embed metros en la pared, que mide 1 m de grosor.
/// Quedan a CornerMargin de las esquinas y a DoorClear del centro de cada pared con puerta. El azar sale de SeedMix (generador
/// propio por sala), así que la misma semilla de partida y celda dan siempre las mismas piezas.
/// </summary>
public static class WallDecorPlanner
{
	public const float MaxRelief = 0.3f;
	public const float Embed = 0.03f;
	public const float CornerMargin = 1.2f;
	public const float DoorClear = 2.3f;
	public const int MaxWallPieces = 4;
	public const float RugChance = 0.6f;
	public const float RugMargin = 3.2f;

	private const ulong Salt = 0x57414C4CUL;

	public static List<DecorPiece> Plan(int gameSeed, Vector2I cell, Vector2 size, IReadOnlyCollection<DoorSide> doors)
	{
		RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, Salt);
		List<DecorPiece> pieces = new();
		float hx = size.X / 2.0f;
		float hz = size.Y / 2.0f;

		string[] kinds = { PropModels.Shelf, PropModels.Curtain };
		foreach (DoorSide side in new[] { DoorSide.North, DoorSide.East, DoorSide.West })
		{
			float half = (side == DoorSide.North ? size.X : size.Y) / 2.0f;
			List<(float From, float To)> segments = new();
			if (doors.Contains(side))
			{
				segments.Add((-half + CornerMargin, -DoorClear));
				segments.Add((DoorClear, half - CornerMargin));
			}
			else
			{
				segments.Add((-half + CornerMargin, half - CornerMargin));
			}

			foreach ((float from, float to) in segments)
			{
				float cursor = from + random.RandfRange(0.0f, 1.0f);
				while (true)
				{
					string kind = kinds[random.Randf() < 0.5f ? 0 : 1];
					float gap = random.RandfRange(0.8f, 2.5f);
					Aabb? box = ModelLibrary.BoundsOf(PropModels.Path(kind));
					if (!box.HasValue)
					{
						break;
					}

					float width = box.Value.Size.X;
					if (cursor + width > to)
					{
						break;
					}

					pieces.Add(OnWall(side, cursor + width / 2.0f, kind, box.Value.Size.Z, hx, hz));
					cursor += width + gap;
				}
			}
		}

		while (pieces.Count > MaxWallPieces)
		{
			pieces.RemoveAt(random.RandiRange(0, pieces.Count - 1));
		}

		if (random.Randf() < RugChance && ModelLibrary.BoundsOf(PropModels.Path(PropModels.Rug)).HasValue)
		{
			float x = random.RandfRange(-(hx - RugMargin), hx - RugMargin);
			float z = random.RandfRange(-(hz - RugMargin), hz - RugMargin);
			float yaw = random.RandiRange(0, 3) * Mathf.Pi / 2.0f;
			pieces.Add(new DecorPiece(PropModels.Rug, new Vector3(x, 0.0f, z), yaw));
		}

		return pieces;
	}

	/// <summary>Pieza con la espalda contra la cara interior de la pared (hundida Embed) y el frente (-Z del modelo) hacia el interior.</summary>
	public static DecorPiece OnWall(DoorSide side, float along, string model, float depth, float hx, float hz)
	{
		Vector3 inward = side switch
		{
			DoorSide.North => Vector3.Back,
			DoorSide.South => Vector3.Forward,
			DoorSide.East => Vector3.Left,
			_ => Vector3.Right,
		};
		Vector3 wallPoint = side switch
		{
			DoorSide.North => new Vector3(along, 0.0f, -hz),
			DoorSide.South => new Vector3(along, 0.0f, hz),
			DoorSide.East => new Vector3(hx, 0.0f, along),
			_ => new Vector3(-hx, 0.0f, along),
		};
		Vector3 position = wallPoint + inward * (depth / 2.0f - Embed);
		return new DecorPiece(model, position, Mathf.Atan2(-inward.X, -inward.Z));
	}
}
