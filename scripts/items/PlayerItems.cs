using System.Collections.Generic;
using Godot;

/// <summary>Inventario y mejoras de una sola partida. Vive con el jugador, nunca en recursos compartidos.</summary>
public partial class PlayerItems : Node
{
	[Signal] public delegate void InventoryChangedEventHandler();

	/// <summary>Se recogió un objeto (se emite después de aplicar su efecto y de InventoryChanged).</summary>
	public event System.Action<ItemDefinition> ItemCollected;
	private readonly List<ItemDefinition> _collected = new();
	private readonly HashSet<string> _upgrades = new(System.StringComparer.Ordinal);
	private HealthComponent _health;
	private PlayerHealth _playerHealth;
	private PlayerShooter _shooter;

	public IReadOnlyList<ItemDefinition> CollectedItems => _collected.AsReadOnly();

	public override void _Ready()
	{
		Node player = GetParent();
		_health = HealthComponent.FindIn(player);
		_playerHealth = player.GetNode<PlayerHealth>("PlayerHealth");
		_shooter = player.GetNode<PlayerShooter>("Shooter");
	}

	/// <summary>Una curación sin efecto o una mejora duplicada se rechazan sin consumir el recogible.</summary>
	public bool TryCollect(ItemDefinition item)
	{
		if (item == null || !item.IsValid || _health.IsDead) return false;
		if (item.Effect != ItemEffect.Heal && _upgrades.Contains(item.Id)) return false;
		if (item.Effect == ItemEffect.Heal && _health.Heal(item.Amount) <= 0.0f) return false;
		if (item.Effect == ItemEffect.MaxHealth && !_health.AddMaxHealthBonus(item.Amount)) return false;

		_collected.Add(item);
		if (item.Effect != ItemEffect.Heal) _upgrades.Add(item.Id);
		UpdateModifiers();
		EmitSignal(SignalName.InventoryChanged);
		ItemCollected?.Invoke(item);
		return true;
	}

	public void ResetForNewRun()
	{
		_collected.Clear();
		_upgrades.Clear();
		_shooter.ResetItemModifiers();
		_playerHealth.ResetItemModifiers();
		_health.ResetForNewRun();
		EmitSignal(SignalName.InventoryChanged);
	}

	private void UpdateModifiers()
	{
		float damage = 1.0f, cadence = 1.0f, received = 1.0f;
		foreach (ItemDefinition item in _collected)
		{
			switch (item.Effect)
			{
				case ItemEffect.Damage: damage *= 1.0f + item.Amount; break;
				case ItemEffect.Cadence: cadence *= 1.0f + item.Amount; break;
				case ItemEffect.Resistance: received *= 1.0f - item.Amount; break;
			}
		}
		_shooter.SetItemModifiers(damage, cadence);
		_playerHealth.SetItemDamageMultiplier(received);
	}
}
