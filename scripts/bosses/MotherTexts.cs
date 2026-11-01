/// <summary>Textos del juego transcritos de docs/equipo/textos.md. No se carga documentación en ejecución.</summary>
public static class MotherTexts
{
	public const string Entrance = "Vuelve a la cuna, mi pequeño...";
	public const string PhaseTwo = "No huyas de mis brazos, mi pequeño...";
	public const string PhaseThree = "¡Duérmete ya, duérmete ya!";
	/// <summary>Etiqueta corta de fase de la barra del jefe (I, II, III).</summary>
	public static string PhaseLabel(int phase) => phase >= 3 ? "Fase III" : phase == 2 ? "Fase II" : "Fase I";
	public const string VictoryDawn = "La pesadilla se disipa; la luz del alba te recibe.";
	public const string VictoryFreedom = "Rompiste el abrazo amargo. Eres libre al fin.";
	public const string VictorySilence = "La nana ha cesado y la cuna queda en silencio.";
}
