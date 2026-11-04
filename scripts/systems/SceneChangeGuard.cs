using System;
using System.Diagnostics;

/// <summary>
/// Blinda el cambio de escena contra la carrera con el recolector de basura de .NET. Cambiar de escena libera nodos y, si el recolector
/// finaliza en ese mismo momento los envoltorios de C# de objetos ya liberados, el motor puede registrar «Handle is not initialized» al
/// cambiar la escena o, a veces, cerrar el proceso (visto en MenuFlowTest). Collect fuerza esa recolección justo ANTES de cada cambio, con
/// la pantalla de carga (o el menú) delante: cuesta unos milisegundos y el jugador no lo nota.
///
/// Se llama antes de MainMenu.StartGame, RunManager.StartNewRun, RunManager.ReturnToMenu y el reinicio de depuración, y la pantalla de carga
/// la repite como primer paso de su trabajo. Mide cuánto tarda (LastMilliseconds, TotalMilliseconds y Calls) para poder informarlo.
/// </summary>
public static class SceneChangeGuard
{
	/// <summary>Interruptor general: con false Collect no hace nada (solo para pruebas que miden sin el guardia).</summary>
	public static bool Enabled { get; set; } = true;

	/// <summary>Veces que se ha recolectado.</summary>
	public static int Calls { get; private set; }

	/// <summary>Milisegundos de la última recolección.</summary>
	public static double LastMilliseconds { get; private set; }

	/// <summary>Milisegundos acumulados de todas las recolecciones.</summary>
	public static double TotalMilliseconds { get; private set; }

	/// <summary>
	/// Recolecta la basura y espera a los finalizadores. Devuelve los milisegundos que tardó (0 si está apagado). Nunca lanza: un fallo aquí no
	/// puede impedir el cambio de escena.
	/// </summary>
	public static double Collect()
	{
		if (!Enabled)
		{
			return 0.0;
		}

		try
		{
			Stopwatch clock = Stopwatch.StartNew();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			clock.Stop();
			LastMilliseconds = clock.Elapsed.TotalMilliseconds;
			TotalMilliseconds += LastMilliseconds;
			Calls++;
			return LastMilliseconds;
		}
		catch (Exception exception)
		{
			Godot.GD.PushWarning($"SceneChangeGuard: la recolección falló ({exception.Message}); se sigue con el cambio de escena.");
			return 0.0;
		}
	}

	/// <summary>Pone a cero los contadores (solo para pruebas).</summary>
	public static void Reset()
	{
		Calls = 0;
		LastMilliseconds = 0.0;
		TotalMilliseconds = 0.0;
	}
}
