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
	/// <summary>
	/// Mensaje al recoger un objeto, por id: título (nombre y efecto) y descripción. Copiados de docs/equipo/textos.md (sección 4);
	/// el juego no lee docs/ en ejecución.
	/// </summary>
	public static readonly System.Collections.Generic.Dictionary<string, (string Title, string Description)> ItemMessages = new()
	{
		["biberon"] = ("Biberón: recupera 2 puntos de vida", "Un sorbo tibio que calma el dolor y devuelve las fuerzas."),
		["fotografia_rota"] = ("Fotografía rota: +25% de daño", "Un recuerdo fragmentado que enciende una rabia punzante."),
		["chupete"] = ("Chupete: +20% de cadencia", "El llanto se vuelve un flujo incesante y apresurado."),
		["oso_peluche"] = ("Oso de peluche: +1 de vida máxima y +1 punto de vida actual", "Un abrazo mullido que ensancha la resistencia del corazón."),
		["manta_vieja"] = ("Manta vieja: -20% de daño recibido", "Un tejido gastado que amortigua la frialdad de los golpes."),
	};

	/// <summary>Mensaje de un objeto recogido; si no está en la tabla, su nombre y la descripción de su recurso.</summary>
	public static (string Title, string Description) ItemMessage(ItemDefinition item)
	{
		return ItemMessages.TryGetValue(item.Id, out var message) ? message : (item.DisplayName, item.Description);
	}

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
