using System.Collections.Generic;

/// <summary>
/// Textos de la pantalla de derrota: el título, la etiqueta «Te venció», el nombre de lo que mató a Noah y dos frases por causa, en
/// primera persona (Noah las escribe en su diario después de «Querido diario,»), con tono de nana siniestra y algo de humor negro. La
/// frase se elige con la semilla de partida (como las de victoria y derrota), sin consumir ningún azar. Sin causa conocida (Unknown)
/// hay otras dos propias.
/// </summary>
public static class DeathTexts
{
	public const string Title = "Fin de la nana";
	public const string KilledBy = "Te venció:";

	/// <summary>Las dos líneas de la hoja de diario que enmarcan la frase: el saludo de arriba y la que presenta el dibujo del asesino.</summary>
	public const string DiaryGreeting = "Querido diario,";
	public const string DiaryCause = "Morí a causa de:";

	private static readonly Dictionary<DeathCause, (string Name, string[] Phrases)> Causes = new()
	{
		[DeathCause.Chaser] = ("Básico", new[]
		{
			"Hoy me abrazaron tan fuerte que me quedé sin aire. Qué cariñoso.",
			"El más sencillo de todos me ganó. Me arrullé yo solo con eso.",
		}),
		[DeathCause.Shooter] = ("Tirador", new[]
		{
			"Hoy aprendí que sí hay quien apunta bien a una cabecita.",
			"Me dispararon desde lejos. Ni me dio tiempo de llorar.",
		}),
		[DeathCause.Projectile] = ("Un proyectil", new[]
		{
			"Me llegó una lágrima ajena. Qué mal sabe el llanto del vecino.",
			"Algo salió volando y yo no lo vi venir.",
		}),
		[DeathCause.Exploder] = ("Explosivo", new[]
		{
			"Alguien me quiso tanto que explotó a mi lado.",
			"Me dieron un abrazo con mecha. Qué detalle.",
		}),
		[DeathCause.Flyer] = ("Volador", new[]
		{
			"Me arrulló un zumbido con alas. Los bebés confiamos en todo.",
			"Me picó el sueño, con alas.",
		}),
		[DeathCause.Runner] = ("Huidor", new[]
		{
			"Me corrieron encima mientras miraba para otro lado.",
			"Ni lo vi venir: se hacía el tímido.",
		}),
		[DeathCause.Digger] = ("Excavador", new[]
		{
			"Salió algo de debajo de la cuna. Siempre hay algo debajo de la cuna.",
			"Algo me jaló desde el suelo. Debí mirar debajo de la cuna.",
		}),
		[DeathCause.TheMother] = ("The Mother", new[]
		{
			"Mamá me arropó para siempre. Las nanas siempre acaban así.",
			"Mamá cantó hasta que me dormí. Esta vez fue para siempre.",
		}),
	};

	/// <summary>Frases sin causa conocida (Unknown): también en primera persona.</summary>
	private static readonly string[] UnknownPhrases =
	{
		"No sé qué pasó, pero fue un día largo.",
		"Algo me pasó y no sé qué. Fue un día muy largo.",
	};

	/// <summary>Causas que tienen textos propios (todas menos Unknown).</summary>
	public static IEnumerable<DeathCause> KnownCauses => Causes.Keys;

	/// <summary>Nombre de lo que mató a Noah.</summary>
	public static string NameOf(DeathCause cause) => Causes.TryGetValue(cause, out var entry) ? entry.Name : "Desconocido";

	/// <summary>Frases de esa causa (las del desconocido si no se conoce).</summary>
	public static IReadOnlyList<string> PhrasesOf(DeathCause cause) => Causes.TryGetValue(cause, out var entry) ? entry.Phrases : UnknownPhrases;

	/// <summary>Frase elegida por la semilla (determinista, también con semilla negativa).</summary>
	public static string PhraseFor(DeathCause cause, int seed)
	{
		IReadOnlyList<string> phrases = PhrasesOf(cause);
		return phrases[(int)((uint)seed % (uint)phrases.Count)];
	}
}
