using System.Collections.Generic;

/// <summary>
/// Estado del precalentamiento de los enemigos (lo ejecuta EnemyWarmupRunner mientras el jugador está en el menú): un enemigo de cada
/// uno de los tipos de EnemyKinds se carga, se instancia, se revela y se dibuja una vez fuera de la partida, para que el JIT de C#, la
/// carga de modelos y la subida de mallas y texturas a la tarjeta gráfica se hagan antes de la primera sala y no al nacer los enemigos.
///
/// Es idempotente (lo que ya está hecho no se repite), reanudable (si se sale del menú a medias, la siguiente vez continúa) y no cambia
/// la lógica del juego: solo rellena cachés que el juego rellenaría igualmente.
/// </summary>
public static class EnemyWarmup
{
	private static readonly HashSet<EnemyKind> WarmedKinds = new();

	/// <summary>Interruptor general: con false el menú no lanza el precalentamiento de enemigos.</summary>
	public static bool Enabled { get; set; } = true;

	/// <summary>Se terminó todo, incluido el dibujo previo. Lo marca el runner.</summary>
	public static bool Done { get; private set; }

	/// <summary>Tipos ya cargados, instanciados y dibujados al menos una vez.</summary>
	public static IReadOnlyCollection<EnemyKind> Warmed => WarmedKinds;

	/// <summary>Tipos cuya escena no llegó a cargar (se ignoran y se sigue con los demás).</summary>
	public static int Failed { get; private set; }

	public static void MarkWarmed(EnemyKind kind) => WarmedKinds.Add(kind);

	public static void MarkFailed() => Failed++;

	public static void MarkDone() => Done = true;

	/// <summary>Olvida lo hecho para volver a empezar (solo para pruebas). No vacía las escenas retenidas ni el caché del motor.</summary>
	public static void Reset()
	{
		WarmedKinds.Clear();
		Failed = 0;
		Done = false;
	}
}
