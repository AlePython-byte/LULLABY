using Godot;

public partial class PlayerHealth : Node
{
	[Export] public float InvulnerabilityTime { get; set; } = 1.0f;
	public float DamageTakenMultiplier { get; private set; } = 1.0f;

	private HealthComponent _health;
	private Node3D _mesh;
	private float _invulnerableTimer;
	private bool _locked;

	/// <summary>La partida terminó: Noah ya no recibe daño y, si llegara a morir, la escena no se reinicia.</summary>
	public bool IsLocked => _locked;

	/// <summary>
	/// Noah murió. Si hay alguien escuchando (RunManager con su pantalla de derrota), él decide qué pasa; si no, la escena
	/// se reinicia como siempre.
	/// </summary>
	public event System.Action Defeated;

	public override void _Ready()
	{
		Node player = GetParent();
		_health = HealthComponent.FindIn(player);
		_mesh = player.GetNode<Node3D>("Visual");
		_health.Died += OnDied;
		// Un golpe que no mata suena como daño; el que mata suena solo como muerte (OnDied).
		_health.Damaged += (amount, current) =>
		{
			if (current > 0.0f)
			{
				AudioManager.Play(SoundCatalog.NoahHurt);
			}
		};
	}

	public override void _Process(double delta)
	{
		if (_invulnerableTimer <= 0.0f)
		{
			return;
		}

		_invulnerableTimer -= (float)delta;

		// Parpadeo mientras dura la invulnerabilidad.
		_mesh.Visible = _invulnerableTimer <= 0.0f || (int)(_invulnerableTimer * 10.0f) % 2 == 0;
	}

	/// <summary>Devuelve true si el golpe hizo daño (false si estaba en invulnerabilidad o muerto).</summary>
	public bool TakeHit(float damage)
	{
		if (_locked || _invulnerableTimer > 0.0f || _health.IsDead)
		{
			return false;
		}

		_health.TakeDamage(damage * DamageTakenMultiplier);
		_invulnerableTimer = InvulnerabilityTime;
		return true;
	}

	public void SetItemDamageMultiplier(float multiplier)
	{
		if (!float.IsFinite(multiplier) || multiplier <= 0.0f || multiplier > 1.0f)
			throw new System.ArgumentOutOfRangeException(nameof(multiplier));
		DamageTakenMultiplier = multiplier;
	}

	/// <summary>Fin de la partida por victoria. ResetItemModifiers, que usa cada partida nueva, lo deshace.</summary>
	public void Lock()
	{
		_locked = true;
	}

	public void ResetItemModifiers()
	{
		_locked = false;
		DamageTakenMultiplier = 1.0f;
		_invulnerableTimer = 0.0f;
		_mesh.Visible = true;
	}

	private void OnDied()
	{
		if (_locked)
		{
			return;
		}

		AudioManager.Play(SoundCatalog.NoahDies);
		if (Defeated != null)
		{
			Defeated.Invoke();
			return;
		}

		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}
}
