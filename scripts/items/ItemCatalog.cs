using System.Collections.Generic;
using Godot;

/// <summary>Catálogo de objetos. La cantidad del tesoro se configura aquí; cada oferta será una recogida independiente.</summary>
[GlobalClass]
public partial class ItemCatalog : Resource
{
	[Export] public Godot.Collections.Array<ItemDefinition> Items { get; set; } = new();
	[Export(PropertyHint.Range, "1,20,1,or_greater")] public int ItemsPerTreasure { get; set; } = 1;

	public ItemDefinition Find(string id)
	{
		foreach (ItemDefinition item in Items)
			if (item != null && item.Id == id) return item;
		return null;
	}

	public bool IsValid()
	{
		if (ItemsPerTreasure < 1 || Items.Count == 0) return false;
		HashSet<string> ids = new(System.StringComparer.Ordinal);
		foreach (ItemDefinition item in Items)
			if (item == null || !item.IsValid || !ids.Add(item.Id)) return false;
		return true;
	}
}
