using System.Collections.Generic;
using Godot;

/// <summary>
/// Ambientación de una sala hecha con las piezas de assets/models/props/. Todo es SOLO visual: ninguna pieza tiene colisión y
/// la geometría y las colisiones de la sala (RoomLayout, paredes, Door) no cambian.
///
/// Por ahora:
///   - un marco de puerta (marco_puerta) alrededor de cada hueco; su hueco libre mide 3 m, como el de la puerta;
///   - los barrotes (puerta_barrotes) como visual de la puerta cerrada, colgando de Door (se ocultan al abrir);
///   - columnas (columna_esquina) en las esquinas interiores del lado norte.
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

	/// <summary>Crea el nodo "Decor" de la sala (marcos y columnas) y pone los barrotes en cada puerta.</summary>
	public static Node3D Build(Node3D room, RoomLayout layout, Vector2 roomSize, IReadOnlyDictionary<DoorSide, Door> doors)
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

		return decor;
	}

	// Norte y sur recorren el eje X (el modelo ya mira así); este y oeste, el Z.
	private static float YawFor(DoorSide side) => side == DoorSide.East || side == DoorSide.West ? Mathf.Pi / 2.0f : 0.0f;
}
