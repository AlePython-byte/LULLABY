using Godot;

public partial class SeedHud : CanvasLayer
{
	private Label _label;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
	}

	public void ShowSeed(int requested, int effective, bool success)
	{
		if (!success)
		{
			_label.Text = $"Semilla {requested}: sin nivel válido";
		}
		else if (requested == effective)
		{
			_label.Text = $"Semilla {requested}";
		}
		else
		{
			_label.Text = $"Semilla {requested} (efectiva {effective})";
		}
	}
}
