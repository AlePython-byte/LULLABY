using Godot;

/// <summary>
/// Mensaje corto al recoger un objeto («Biberón: recupera 2 puntos de vida» y su descripción). Aparece arriba, en el centro, y se
/// desvanece solo; recoger otro objeto lo sustituye. Los textos salen de GameText.ItemMessage. Se detiene con la pausa.
/// </summary>
public partial class ItemMessageHud : CanvasLayer
{
	[Export] public PlayerItems Inventory { get; set; }

	/// <summary>Segundos que el mensaje sigue en pantalla (incluye el desvanecido final).</summary>
	[Export] public float Duration { get; set; } = 2.5f;
	[Export] public float FadeTime { get; set; } = 0.5f;

	private Control _box;
	private Label _title;
	private Label _description;
	private float _remaining;

	/// <summary>El mensaje está visible ahora mismo.</summary>
	public bool IsShowing => _remaining > 0.0f;

	public string TitleText => _title.Text;
	public string DescriptionText => _description.Text;

	public override void _Ready()
	{
		_box = GetNode<Control>("Box");
		_title = GetNode<Label>("Box/Title");
		_description = GetNode<Label>("Box/Description");
		_box.Visible = false;
		if (Inventory != null)
		{
			Inventory.ItemCollected += Show;
		}
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Inventory))
		{
			Inventory.ItemCollected -= Show;
		}
	}

	public override void _Process(double delta)
	{
		if (_remaining <= 0.0f)
		{
			return;
		}

		_remaining -= (float)delta;
		if (_remaining <= 0.0f)
		{
			_box.Visible = false;
			return;
		}

		_box.Modulate = _box.Modulate with { A = FadeTime > 0.0f ? Mathf.Clamp(_remaining / FadeTime, 0.0f, 1.0f) : 1.0f };
	}

	public void Show(ItemDefinition item)
	{
		(string title, string description) = GameText.ItemMessage(item);
		_title.Text = title;
		_description.Text = description;
		_box.Modulate = Colors.White;
		_box.Visible = true;
		_remaining = Duration;
	}
}
