/// <summary>
/// Capas de colisión 3D (valores de máscara de bits, tal como se escriben en las escenas).
/// Los nombres también están en project.godot, sección [layer_names].
///
///   Capa 1 (1)   World             suelo, paredes y puertas.  Máscara: ninguna (son estáticos).
///   Capa 2 (2)   Player            Noah.                      Máscara: World + Enemies          = 9.
///   Capa 3 (4)   Tears             lágrimas de Noah.          Máscara: World + Enemies          = 9 (nunca Player).
///   Capa 4 (8)   Enemies           enemigos.                  Máscara: World + Player + Enemies = 11.
///                                  Excepción: el Volador usa World + Player = 3 (no choca con otros enemigos ni,
///                                  cuando existan, con obstáculos de otra capa: vuela por encima).
///   Capa 5 (16)  EnemyProjectiles  proyectiles enemigos.      Máscara: World + Player           = 3
///                                  (nunca Enemies ni Tears; los detecta el propio proyectil, que es un Area3D).
/// </summary>
public static class CollisionLayers
{
	public const uint World = 1;
	public const uint Player = 2;
	public const uint Tears = 4;
	public const uint Enemies = 8;
	public const uint EnemyProjectiles = 16;

	public const uint PlayerMask = World | Enemies;
	public const uint TearMask = World | Enemies;
	public const uint EnemyMask = World | Player | Enemies;
	public const uint FlyerMask = World | Player;
	public const uint EnemyProjectileMask = World | Player;
}
