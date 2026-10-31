using Godot;

/// <summary>
/// Nombres de grupos que se consultan en cada fotograma o en cada colisión. Son StringName fijos: pasar texto a
/// GetNodesInGroup, IsInGroup o AddToGroup crea un StringName nuevo en cada llamada.
/// </summary>
public static class GameNames
{
	public static readonly StringName PlayerGroup = "player";
	public static readonly StringName TearsGroup = "tears";
	public static readonly StringName EnemyProjectilesGroup = "enemy_projectiles";
}
