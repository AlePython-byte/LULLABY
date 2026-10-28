using Godot;

public partial class HealthComponent : Node
{
	[Signal] public delegate void DamagedEventHandler(float amount, float current);
	[Signal] public delegate void DiedEventHandler();

	[Export] public float MaxHealth { get; set; } = 3.0f;

	public float CurrentHealth { get; private set; }
	public bool IsDead => CurrentHealth <= 0.0f;

	// Se inicializa al entrar al árbol para que ya sea válida cuando cualquier nodo haga _Ready.
	public override void _EnterTree()
	{
		CurrentHealth = MaxHealth;
	}

	public void TakeDamage(float amount)
	{
		if (IsDead || amount <= 0.0f)
		{
			return;
		}

		CurrentHealth = Mathf.Max(0.0f, CurrentHealth - amount);
		EmitSignal(SignalName.Damaged, amount, CurrentHealth);

		if (IsDead)
		{
			EmitSignal(SignalName.Died);
		}
	}

	public static HealthComponent FindIn(Node owner)
	{
		foreach (Node child in owner.GetChildren())
		{
			if (child is HealthComponent health)
			{
				return health;
			}
		}

		return null;
	}
}
