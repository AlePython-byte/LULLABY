using Godot;

public partial class SeedHud : CanvasLayer
{
	private Label _label;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
	}

	/// <summary>
	/// Semilla de partida y nivel, y dificultad objetivo frente a la predicha. Con una versión del generador distinta de
	/// la original añade un aviso: el modelo de dificultad no está entrenado para ella.
	/// </summary>
	public void ShowRun(int gameSeed, int levelSeed, DifficultyTarget target, double targetValue, double predicted,
		int generatorVersion)
	{
		_label.Text = $"Semilla {gameSeed} · nivel {levelSeed} · generador v{generatorVersion}\n" +
			$"Dificultad {target} (objetivo {targetValue:0.0}) · predicha {predicted:0.0}";
		if (generatorVersion != DungeonGenerator.OriginalVersion)
		{
			_label.Text += $"\nAVISO: modelo de dificultad no entrenado para v{generatorVersion}";
		}
	}

	public void ShowError(int gameSeed, string message)
	{
		_label.Text = $"Semilla {gameSeed}: {message}";
	}
}
