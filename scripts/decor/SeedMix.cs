using Godot;

/// <summary>
/// Semillas de la decoración. Cada sala se decora con su PROPIO generador de azar, sembrado con (semilla de partida, celda, sal):
/// no usa el azar global ni el del generador de niveles, el director o los objetos, así que decorar no altera la generación, las
/// métricas ni los benchmarks, y visitar las salas en otro orden no cambia su aspecto.
/// </summary>
public static class SeedMix
{
	/// <summary>Constante propia de la decoración (distinta de la de los objetos del tesoro).</summary>
	private const ulong DecorSalt = 0x4445434F52524F4FUL;

	public static ulong For(int gameSeed, Vector2I cell, ulong salt = 0UL)
	{
		return Mix(Mix(Mix(DecorSalt ^ salt ^ (uint)gameSeed) ^ (uint)cell.X) ^ (uint)cell.Y);
	}

	/// <summary>Generador de azar propio de una sala, siempre el mismo para la misma semilla de partida y celda.</summary>
	public static RandomNumberGenerator RandomFor(int gameSeed, Vector2I cell, ulong salt = 0UL)
	{
		return new RandomNumberGenerator { Seed = For(gameSeed, cell, salt) };
	}

	private static ulong Mix(ulong value)
	{
		unchecked
		{
			value += 0x9E3779B97F4A7C15UL;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}
	}
}
