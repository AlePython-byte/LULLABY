using Godot;

/// <summary>
/// Mapa de un nivel: salas en una cuadrícula de coordenadas enteras.
///
/// Convención de ejes (coincide con la cámara: arriba en pantalla = -Z):
///   Norte = y-1 = -Z (arriba)      Este = x+1 = +X (derecha)
///   Sur   = y+1 = +Z (abajo)       Oeste = x-1 = -X (izquierda)
///
/// Ejemplo (el mapa de prueba), con x en columnas e y en filas:
///
///           x=0   x=1   x=2
///   y=-1     C     .     .
///   y= 0     A —   B —   D
///   y=+1     E     .     .
///
/// A tiene puertas al Norte (C), Este (B) y Sur (E); B, al Oeste (A) y Este (D).
/// Una sala tiene puerta hacia un lado si existe otra sala en la celda vecina de ese lado.
/// Un generador procedural solo tiene que producir un LevelMap con este formato.
/// </summary>
[GlobalClass]
public partial class LevelMap : Resource
{
	[Export] public Vector2I StartCell { get; set; }
	[Export] public Godot.Collections.Array<RoomData> Rooms { get; set; } = new();
}
