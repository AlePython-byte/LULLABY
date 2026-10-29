using Godot;

public partial class SeedHud : CanvasLayer
{
	private Label _label;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
	}

	/// <summary>
	/// Semilla de partida y nivel, versión del generador, y dificultad objetivo frente a la predicha. Cada versión del
	/// generador usa su propio modelo; si el modelo cargado es de otra versión (no existe aún el de esta), avisa.
	/// </summary>
	public void ShowRun(int gameSeed, int levelSeed, DifficultyTarget target, double targetValue, double predicted,
		int generatorVersion, int modelGeneratorVersion)
	{
		_label.Text = $"Semilla {gameSeed} · nivel {levelSeed} · generador v{generatorVersion}\n" +
			$"Dificultad {target} (objetivo {targetValue:0.0}) · predicha {predicted:0.0}";
		if (modelGeneratorVersion != generatorVersion)
		{
			_label.Text += $"\nAVISO: modelo de dificultad no entrenado para v{generatorVersion}";
		}
	}

	/// <summary>Nivel fijado desde fuera (pruebas de depuración), sin director.</summary>
	public void ShowFixedLevel(string description)
	{
		_label.Text = $"Nivel fijado: {description}";
	}

	public void ShowError(int gameSeed, string message)
	{
		_label.Text = $"Semilla {gameSeed}: {message}";
	}
}
