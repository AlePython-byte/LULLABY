using Godot;

/// <summary>
/// Catálogo de plantillas por tipo de sala. Añadir una plantilla nueva no requiere tocar el generador.
///
/// Versiones del generador: NormalTemplates es la lista de la versión 1. NormalTemplatesV2 son plantillas que solo se
/// suman a partir de la versión 2 (mezclan tipos de enemigo). Así, con la versión 1, el generador recorre exactamente
/// las mismas listas que antes y produce los mismos niveles con las mismas semillas.
/// </summary>
[GlobalClass]
public partial class RoomCatalog : Resource
{
	[Export] public Godot.Collections.Array<RoomTemplate> StartTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> NormalTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> BossTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> TreasureTemplates { get; set; } = new();

	/// <summary>Plantillas normales que solo existen a partir de la versión 2 del generador.</summary>
	[Export] public Godot.Collections.Array<RoomTemplate> NormalTemplatesV2 { get; set; } = new();

	/// <summary>Plantillas de la versión 1.</summary>
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

	/// <summary>Plantillas de una versión del generador. Versión 2: las normales de la versión 1 seguidas de las nuevas.</summary>
	public Godot.Collections.Array<RoomTemplate> For(RoomType type, int generatorVersion)
	{
		if (generatorVersion < 2 || type != RoomType.Normal)
		{
			return For(type);
		}

		Godot.Collections.Array<RoomTemplate> templates = new(NormalTemplates);
		templates.AddRange(NormalTemplatesV2);
		return templates;
	}
}
