using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Ambientación de una sala hecha con las piezas de assets/models/props/. Todo es SOLO visual: ninguna pieza tiene colisión y
/// la geometría y las colisiones de la sala (RoomLayout, paredes, Door) no cambian.
///
/// Por ahora:
///   - un marco de puerta (marco_puerta) alrededor de cada hueco; su hueco libre mide 3 m, como el de la puerta;
///   - los barrotes (puerta_barrotes) como visual de la puerta cerrada, colgando de Door (se ocultan al abrir);
///   - columnas (columna_esquina) en las esquinas interiores del lado norte;
///   - con el tipo de sala: en las de combate (Normal), estanterías, cortinas y alfombras planas pegadas a la pared (WallDecorPlanner);
///     en inicio, tesoro y jefe, un subconjunto del catálogo de piezas exentas junto a las paredes (FreeDecorPlanner). Las dos
///     cosas con azar propio por semilla de partida y celda, y sin colisión.
///
/// La cámara mira desde el sur con 60° de inclinación: la pared sur oculta el suelo que tiene delante, así que sus esquinas no
/// llevan columna (SkipSouth). Una pieza que falte en assets/ simplemente no se pone.
/// </summary>
public static class RoomDecorator
{
	public const string DecorName = "Decor";
	public const string FramePath = "res://assets/models/props/marco_puerta.glb";
	public const string BarsPath = "res://assets/models/props/puerta_barrotes.glb";
	public const string ColumnPath = "res://assets/models/props/columna_esquina.glb";

	/// <summary>La pared sur no se ve desde la cámara: no se decoran sus esquinas.</summary>
	public const bool SkipSouth = true;

	// Las caras del marco (1 m de fondo) coinciden con las de la pared (1 m de grosor); un poco más de fondo evita el parpadeo.
	private const float FrameDepthScale = 1.03f;

	/// <summary>Piezas de una sala según su tipo: plan de pared (combate) o piezas exentas (inicio, tesoro, jefe), ambos con azar propio por semilla y celda.</summary>
	public static List<DecorPiece> PiecesFor(RoomType type, Vector2 size, Vector2I cell, int gameSeed, IReadOnlyCollection<DoorSide> doors)
	{
		return type == RoomType.Normal ? WallDecorPlanner.Plan(gameSeed, cell, size, doors) : FreeDecorPlanner.Plan(type, size, cell, gameSeed);
	}

	/// <summary>
	/// Crea el nodo "Decor" de la sala (marcos, columnas y, si se da el tipo, sus piezas) y pone los barrotes en cada puerta.
	/// Sin tipo solo se hace la parte estructural.
	/// </summary>
	public static Node3D Build(Node3D room, RoomLayout layout, Vector2 roomSize, IReadOnlyDictionary<DoorSide, Door> doors,
		RoomType? type = null, Vector2I cell = default, int gameSeed = 0)
	{
		Node3D decor = new() { Name = DecorName };
		room.AddChild(decor);

		foreach (RoomLayout.DoorLayout door in layout.Doors)
		{
			float yaw = YawFor(door.Side);
			Vector3 center = door.Door.Center;

			Node3D frame = ModelLibrary.Create(FramePath);
			if (frame != null)
			{
				frame.Name = $"Frame{door.Side}";
				frame.Position = new Vector3(center.X, 0.0f, center.Z);
				frame.Rotation = new Vector3(0.0f, yaw, 0.0f);
				frame.Scale = new Vector3(1.0f, 1.0f, FrameDepthScale);
				decor.AddChild(frame);
			}

			Node3D bars = ModelLibrary.Create(BarsPath);
			if (bars != null && doors.TryGetValue(door.Side, out Door doorNode))
			{
				bars.Name = "Bars";
				bars.Position = new Vector3(0.0f, -center.Y, 0.0f);
				bars.Rotation = new Vector3(0.0f, yaw, 0.0f);
				doorNode.AttachSkin(bars);
			}
			else
			{
				bars?.Free();
			}
		}

		float hx = roomSize.X / 2.0f;
		float hz = roomSize.Y / 2.0f;
		foreach (float sx in new[] { -1.0f, 1.0f })
		{
			foreach (float sz in new[] { -1.0f, 1.0f })
			{
				if (SkipSouth && sz > 0.0f)
				{
					continue;
				}

				// Centrada en la esquina interior: la mitad queda dentro de las paredes y sobresale 0,29 m.
				Node3D column = ModelLibrary.Create(ColumnPath);
				if (column != null)
				{
					column.Name = $"Column{(sx < 0.0f ? "W" : "E")}{(sz < 0.0f ? "N" : "S")}";
					column.Position = new Vector3(sx * hx, 0.0f, sz * hz);
					decor.AddChild(column);
				}
			}
		}

		if (type.HasValue)
		{
			AddPieces(decor, PiecesFor(type.Value, roomSize, cell, gameSeed, layout.Doors.Select(door => door.Side).ToList()));
		}

		return decor;
	}

	private static void AddPieces(Node3D decor, List<DecorPiece> pieces)
	{
		for (int i = 0; i < pieces.Count; i++)
		{
			DecorPiece piece = pieces[i];
			Node3D model = ModelLibrary.Create(piece.Path);
			if (model == null)
			{
				continue;
			}

			model.Name = $"{piece.Model}_{i}";
			model.Position = piece.Position;
			model.Rotation = new Vector3(0.0f, piece.Yaw, 0.0f);
			if (piece.Mirror)
			{
				model.Scale = new Vector3(-1.0f, 1.0f, 1.0f);
			}

			decor.AddChild(model);

			// Las piezas bajas y planas no proyectan sombra: cuestan sin aportar.
			Aabb? box = ModelLibrary.BoundsOf(piece.Path);
			if (piece.Model == PropModels.Rug || piece.Model == PropModels.Curtain || (box.HasValue && box.Value.Size.Y < 0.6f))
			{
				SetShadows(model, false);
			}
		}
	}

	private static void SetShadows(Node node, bool cast)
	{
		if (node is GeometryInstance3D geometry)
		{
			geometry.CastShadow = cast ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		}

		foreach (Node child in node.GetChildren())
		{
			SetShadows(child, cast);
		}
	}

	// Norte y sur recorren el eje X (el modelo ya mira así); este y oeste, el Z.
	private static float YawFor(DoorSide side) => side == DoorSide.East || side == DoorSide.West ? Mathf.Pi / 2.0f : 0.0f;
}
