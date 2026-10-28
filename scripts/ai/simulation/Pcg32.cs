/// <summary>
/// Generador pseudoaleatorio PCG32 (O'Neill, 2014). Propio y sin estado global: cada bot tiene el suyo, así que
/// las simulaciones se pueden ejecutar en paralelo sin perder el determinismo.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class Pcg32
{
	private const ulong Multiplier = 6364136223846793005UL;

	private readonly ulong _increment;
	private ulong _state;

	public Pcg32(ulong seed, ulong stream = 0xDA3E39CB94B95BDBUL)
	{
		_increment = (stream << 1) | 1UL;
		NextUInt();
		_state = unchecked(_state + seed);
		NextUInt();
	}

	public uint NextUInt()
	{
		ulong old = _state;
		_state = unchecked(old * Multiplier + _increment);
		uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
		int rotation = (int)(old >> 59);
		return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
	}

	/// <summary>Real uniforme en [0, 1).</summary>
	public float NextFloat()
	{
		return (NextUInt() >> 8) * (1.0f / 16777216.0f);
	}

	/// <summary>Entero uniforme en [min, max], ambos incluidos.</summary>
	public int Range(int min, int max)
	{
		return min + (int)(NextUInt() % (uint)(max - min + 1));
	}
}
