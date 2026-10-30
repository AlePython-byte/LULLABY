using Godot;

public partial class HealthComponent : Node
{
	[Signal] public delegate void DamagedEventHandler(float amount, float current);
	[Signal] public delegate void DiedEventHandler();
	[Signal] public delegate void HealthChangedEventHandler(float current, float maximum);

	[Export] public float MaxHealth { get; set; } = 3.0f;

	public float CurrentHealth { get; private set; }
	public float MaxHealthBonus { get; private set; }
	public float EffectiveMaxHealth => MaxHealth + MaxHealthBonus;
	public bool IsDead => CurrentHealth <= 0.0f;

	// Se inicializa al entrar al árbol para que ya sea válida cuando cualquier nodo haga _Ready.
	public override void _EnterTree()
	{
		MaxHealthBonus = 0.0f;
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
		EmitSignal(SignalName.HealthChanged, CurrentHealth, EffectiveMaxHealth);

		if (IsDead)
		{
			EmitSignal(SignalName.Died);
		}
	}

	/// <summary>Cura sin superar el máximo efectivo ni revivir. Devuelve la vida realmente recuperada.</summary>
	public float Heal(float amount)
	{
		if (IsDead || !float.IsFinite(amount) || amount <= 0.0f) return 0.0f;
		float previous = CurrentHealth;
		CurrentHealth = Mathf.Min(EffectiveMaxHealth, CurrentHealth + amount);
		float restored = CurrentHealth - previous;
		if (restored > 0.0f) EmitSignal(SignalName.HealthChanged, CurrentHealth, EffectiveMaxHealth);
		return restored;
	}

	/// <summary>Bonificación por instancia: añade el mismo incremento a vida máxima y actual sin escribir MaxHealth.</summary>
	public bool AddMaxHealthBonus(float amount)
	{
		if (IsDead || !float.IsFinite(amount) || amount <= 0.0f) return false;
		MaxHealthBonus += amount;
		CurrentHealth = Mathf.Min(EffectiveMaxHealth, CurrentHealth + amount);
		EmitSignal(SignalName.HealthChanged, CurrentHealth, EffectiveMaxHealth);
		return true;
	}

	public void ResetForNewRun()
	{
		MaxHealthBonus = 0.0f;
		CurrentHealth = MaxHealth;
		EmitSignal(SignalName.HealthChanged, CurrentHealth, EffectiveMaxHealth);
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
