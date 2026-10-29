using Godot;

/// <summary>
/// Catálogo de plantillas por tipo de sala. Añadir una plantilla nueva no requiere tocar el generador.
///
/// Versiones del generador: NormalTemplates es la lista de la versión 1. NormalTemplatesV2 son plantillas que solo se
/// suman en la versión 2 (mezclan tipos de enemigo). NormalTemplatesV3 es la lista COMPLETA de la versión 3 (algunas
/// plantillas de básicos, las mixtas de la v2 y las nuevas con el Huidor y el Excavador): quitar plantillas de básicos
/// de esa lista baja su peso sin tocar el código de elección del generador. Con las versiones 1 y 2, el generador
/// recorre exactamente las mismas listas que antes y produce los mismos niveles con las mismas semillas.
/// </summary>
[GlobalClass]
public partial class RoomCatalog : Resource
{
	[Export] public Godot.Collections.Array<RoomTemplate> StartTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> NormalTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> BossTemplates { get; set; } = new();
	[Export] public Godot.Collections.Array<RoomTemplate> TreasureTemplates { get; set; } = new();

	/// <summary>Plantillas normales que se suman a las de la versión 1 en la versión 2 del generador.</summary>
	[Export] public Godot.Collections.Array<RoomTemplate> NormalTemplatesV2 { get; set; } = new();

	/// <summary>Lista completa de plantillas normales de la versión 3 del generador.</summary>
	[Export] public Godot.Collections.Array<RoomTemplate> NormalTemplatesV3 { get; set; } = new();

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

	/// <summary>
	/// Plantillas de una versión del generador. Solo cambian las normales: versión 2 = las de la versión 1 seguidas de
	/// NormalTemplatesV2; versión 3 = NormalTemplatesV3.
	/// </summary>
	public Godot.Collections.Array<RoomTemplate> For(RoomType type, int generatorVersion)
	{
		if (generatorVersion < 2 || type != RoomType.Normal)
		{
			return For(type);
		}

		if (generatorVersion >= 3)
		{
			return NormalTemplatesV3;
		}

		Godot.Collections.Array<RoomTemplate> templates = new(NormalTemplates);
		templates.AddRange(NormalTemplatesV2);
		return templates;
	}
}
