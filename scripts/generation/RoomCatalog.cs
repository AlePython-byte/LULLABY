using Godot;

/// <summary>
/// Catálogo de plantillas por tipo de sala. Añadir una plantilla nueva no requiere tocar el generador.
/// </summary>
[GlobalClass]
public partial class RoomCatalog : Resource
{
	[Export] public Godot.Collections.Array<RoomTemplate> StartTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> NormalTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> BossTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> TreasureTemplates { get; set; } = new();

	public Godot.Collections.Array<RoomTemplate> For(RoomType type)
	{
		return type switch
		{
			RoomType.Start => StartTemplates,
			RoomType.Boss => BossTemplates,
			RoomType.Treasure => TreasureTemplates,
			_ => NormalTemplates,
		};
	}
}
