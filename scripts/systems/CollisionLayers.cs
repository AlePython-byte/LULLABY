/// <summary>
/// Capas de colisión 3D (valores de máscara de bits, tal como se escriben en las escenas).
/// Los nombres también están en project.godot, sección [layer_names].
///
///   Capa 1 (1)  World    suelo y paredes.            Máscara: ninguna (son estáticos).
///   Capa 2 (2)  Player   Noah.                       Máscara: World + Enemies.
///   Capa 3 (4)  Tears    lágrimas de Noah.           Máscara: World + Enemies (nunca Player).
///   Capa 4 (8)  Enemies  enemigos (aún no existen).  Máscara: World + Player + Tears.
/// </summary>
public static class CollisionLayers
{
	public const uint World = 1;
	public const uint Player = 2;
	public const uint Tears = 4;
	public const uint Enemies = 8;
}
