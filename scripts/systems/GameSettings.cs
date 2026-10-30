/// <summary>
/// Opciones de la sesión que viajan entre escenas (del menú al nivel). Solo viven mientras el juego está abierto.
/// </summary>
public static class GameSettings
{
	public const string MenuScene = "res://scenes/ui/MainMenu.tscn";
	public const string LevelScene = "res://scenes/levels/TestLevel.tscn";

	/// <summary>
	/// Dificultad elegida en el menú. Nula si nadie eligió (TestLevel lanzado con F6, las pruebas): entonces manda el
	/// argumento --difficulty o el valor del inspector de RunManager.
	/// </summary>
	public static DifficultyTarget? Difficulty { get; set; }

	/// <summary>
	/// «Variedad extendida (experimental)»: la partida usa el generador v3 (seis tipos de enemigo, con el huidor y el excavador).
	/// Su modelo de dificultad no está entrenado para esos dos. Falso por defecto; un argumento --generator o una tecla de
	/// depuración mandan sobre esta opción.
	/// </summary>
	public static bool ExtendedVariety { get; set; }
}
