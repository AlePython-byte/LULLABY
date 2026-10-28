using Godot;

public partial class SeedHud : CanvasLayer
{
	private Label _label;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
	}

	/// <summary>Dos líneas pequeñas: semilla de partida y nivel, y dificultad objetivo frente a la predicha.</summary>
	public void ShowRun(int gameSeed, int levelSeed, DifficultyTarget target, double targetValue, double predicted)
	{
		_label.Text = $"Semilla {gameSeed} · nivel {levelSeed}\n" +
			$"Dificultad {target} (objetivo {targetValue:0.0}) · predicha {predicted:0.0}";
	}

	public void ShowError(int gameSeed, string message)
	{
		_label.Text = $"Semilla {gameSeed}: {message}";
	}
}
