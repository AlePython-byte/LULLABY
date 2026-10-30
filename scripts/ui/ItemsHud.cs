using System.Linq;
using Godot;

/// <summary>Objetos obtenidos durante la partida; las curaciones quedan marcadas como usadas.</summary>
public partial class ItemsHud : CanvasLayer
{
	[Export] public PlayerItems Inventory { get; set; }
	private Label _label;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
		if (Inventory != null) Inventory.InventoryChanged += UpdateText;
		UpdateText();
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Inventory)) Inventory.InventoryChanged -= UpdateText;
	}

	private void UpdateText()
	{
		if (Inventory == null || Inventory.CollectedItems.Count == 0)
		{
			_label.Text = "Objetos: —";
			return;
		}
		_label.Text = "Objetos\n" + string.Join("\n", Inventory.CollectedItems.GroupBy(item => item.Id, System.StringComparer.Ordinal)
			.Select(group => group.First().DisplayName
				+ (group.First().Effect == ItemEffect.Heal ? " (usado)" : "")
				+ (group.Count() > 1 ? $" ×{group.Count()}" : "")));
	}
}
