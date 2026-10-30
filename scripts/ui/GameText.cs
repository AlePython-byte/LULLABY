using Godot;

/// <summary>
/// Textos de la interfaz transcritos de docs/equipo/textos.md (docs/ no viaja en la exportación y no se lee en ejecución).
/// Las frases de victoria y las de The Mother están en MotherTexts. Los controles son los reales del juego.
/// </summary>
public static class GameText
{
	public const string Title = "LULLABY";
	public const string Subtitle = "El eco de una nana olvidada";

	public const string Play = "Jugar";
	public const string Difficulty = "Dificultad";
	public const string Controls = "Controles";
	public const string Quit = "Salir";
	public const string Back = "Volver";

	public const string Pause = "Pausa";
	public const string Resume = "Reanudar";
	public const string Restart = "Reiniciar partida";
	public const string BackToMenu = "Volver al menú";

	public const string Defeat = "Derrota";
	public const string Retry = "Reintentar";
	public const string Menu = "Menú";

	/// <summary>Frases de derrota (textos.md, sección 5).</summary>
	public static readonly string[] DefeatMessages =
	{
		"El sueño te ha vencido. Descansa en la penumbra.",
		"Tus lágrimas se secaron antes de encontrar la salida.",
		"La noche es demasiado pesada para un corazón tan pequeño.",
	};

	/// <summary>Teclas reales del juego (PlayerController, PlayerShooter y la pausa).</summary>
	public static readonly string[] ControlLines =
	{
		"Moverse: W, A, S, D",
		"Disparar lágrimas: flechas direccionales",
		"Pausa: Esc o P",
	};

	/// <summary>Frase de derrota de una partida: determinista por semilla, sin tocar ningún generador de azar.</summary>
	public static string DefeatMessageFor(int seed)
	{
		return DefeatMessages[(int)((uint)seed % (uint)DefeatMessages.Length)];
	}

	public static string DifficultyName(DifficultyTarget target)
	{
		return target switch
		{
			DifficultyTarget.Easy => "Fácil",
			DifficultyTarget.Hard => "Difícil",
			_ => "Normal",
		};
	}
}
