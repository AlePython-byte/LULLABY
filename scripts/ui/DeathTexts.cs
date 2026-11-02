using System.Collections.Generic;

/// <summary>
/// Textos de la pantalla de derrota: el título, la etiqueta «Te venció», el nombre de lo que mató a Noah y dos frases por causa, con
/// tono de nana siniestra y algo de humor negro. La frase se elige con la semilla de partida (como las de victoria y derrota), sin
/// consumir ningún azar. Sin causa conocida (Unknown) se usan las tres frases de siempre (GameText.DefeatMessages).
/// </summary>
public static class DeathTexts
{
	public const string Title = "Fin de la nana";
	public const string KilledBy = "Te venció:";

	private static readonly Dictionary<DeathCause, (string Name, string[] Phrases)> Causes = new()
	{
		[DeathCause.Chaser] = ("Básico", new[]
		{
			"El más sencillo de todos… y te ganó. Arrúllate con eso.",
			"Te dio un abrazo demasiado apretado. Duérmete ya.",
		}),
		[DeathCause.Shooter] = ("Tirador", new[]
		{
			"Apuntó a la cabecita y no falló. Cierra los ojitos.",
			"Tenía puntería de nana: certera y sin piedad.",
		}),
		[DeathCause.Projectile] = ("Un proyectil", new[]
		{
			"Una lágrima ajena te encontró. Qué mal sabe el llanto del vecino.",
			"Nadie te enseñó a esquivar lo que viene de frente.",
		}),
		[DeathCause.Exploder] = ("Explosivo", new[]
		{
			"Hizo ¡pum! y la nana se quedó sin letra.",
			"Te quería tanto que explotó a tu lado.",
		}),
		[DeathCause.Flyer] = ("Volador", new[]
		{
			"Zumbaba una nana y la creíste. Los bebés confían en todo.",
			"Te picó el sueño, con alas.",
		}),
		[DeathCause.Runner] = ("Huidor", new[]
		{
			"Ni lo viste venir: se hacía el tímido.",
			"Corría hacia ti mientras mirabas a otro lado.",
		}),
		[DeathCause.Digger] = ("Excavador", new[]
		{
			"Salió de debajo de la cuna. Siempre hay algo debajo de la cuna.",
			"Cavó hasta tus pesadillas y las hizo realidad.",
		}),
		[DeathCause.TheMother] = ("The Mother", new[]
		{
			"Mamá te arropó para siempre. Las nanas siempre acaban así.",
			"Duérmete ya, duérmete ya… y esta vez obedeciste.",
		}),
	};

	/// <summary>Causas que tienen textos propios (todas menos Unknown).</summary>
	public static IEnumerable<DeathCause> KnownCauses => Causes.Keys;

	/// <summary>Nombre de lo que mató a Noah.</summary>
	public static string NameOf(DeathCause cause) => Causes.TryGetValue(cause, out var entry) ? entry.Name : "Desconocido";

	/// <summary>Frases de esa causa (las de siempre si no se conoce).</summary>
	public static IReadOnlyList<string> PhrasesOf(DeathCause cause) => Causes.TryGetValue(cause, out var entry) ? entry.Phrases : GameText.DefeatMessages;

	/// <summary>Frase elegida por la semilla (determinista, también con semilla negativa).</summary>
	public static string PhraseFor(DeathCause cause, int seed)
	{
		IReadOnlyList<string> phrases = PhrasesOf(cause);
		return phrases[(int)((uint)seed % (uint)phrases.Count)];
	}
}
