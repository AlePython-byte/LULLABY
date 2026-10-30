using Godot;

public enum ItemEffect { Heal, Damage, Cadence, MaxHealth, Resistance }
public enum ItemShape { Cylinder, Box, Sphere, Capsule, Prism }

/// <summary>Datos compartidos e inmutables durante la partida; los efectos se aplican a cada jugador.</summary>
[GlobalClass]
public partial class ItemDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string DisplayName { get; set; } = "";
	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
	[Export] public ItemEffect Effect { get; set; }
	[Export] public float Amount { get; set; }
	[Export] public Color Color { get; set; } = Colors.White;
	[Export] public ItemShape Shape { get; set; }

	public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(DisplayName)
		&& System.Enum.IsDefined(Effect) && System.Enum.IsDefined(Shape)
		&& float.IsFinite(Amount) && Amount > 0.0f && (Effect != ItemEffect.Resistance || Amount < 1.0f);
}
