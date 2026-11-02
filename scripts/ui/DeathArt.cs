using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// El dibujo de la pantalla de derrota: qué imagen de assets/ui/muerte/ corresponde a cada DeathCause y cuánto mide en la hoja.
/// Cada causa tiene su dibujo; si su archivo falta (o la causa no se conoce) se usa el del desconocido y, si tampoco está, no hay dibujo.
/// Ninguna imagen decide nada de la partida.
/// </summary>
public static class DeathArt
{
	public const string Folder = "res://assets/ui/muerte/";
	public const string ArrowPath = Folder + "flecha_out.png";
	public const string UnknownPath = Folder + "asesino_desconocido.png";

	/// <summary>
	/// Caja máxima del dibujo (px, en la ventana de 1152×648) y de la flecha: el dibujo se ajusta a las dos medidas a la vez, así que el
	/// proyectil (alto y estrecho) queda limitado por el alto y The Mother (ancha) por el ancho. La zona útil de la hoja mide unos 356×501.
	/// </summary>
	public static readonly Vector2 MaxSize = new(300.0f, 220.0f);
	public static readonly Vector2 ArrowMaxSize = new(90.0f, 46.0f);

	private static readonly Dictionary<DeathCause, string> Files = new()
	{
		[DeathCause.Chaser] = Folder + "asesino_basico.png",
		[DeathCause.Shooter] = Folder + "asesino_tirador.png",
		[DeathCause.Projectile] = Folder + "asesino_proyectil.png",
		[DeathCause.Exploder] = Folder + "asesino_explosivo.png",
		[DeathCause.Flyer] = Folder + "asesino_volador.png",
		[DeathCause.Runner] = Folder + "asesino_huidor.png",
		[DeathCause.Digger] = Folder + "asesino_excavador.png",
		[DeathCause.TheMother] = Folder + "asesino_mother.png",
		[DeathCause.Unknown] = UnknownPath,
	};

	/// <summary>Ruta del dibujo de esa causa (la del desconocido si falta su archivo; null si tampoco está). `exists` solo lo cambian las pruebas.</summary>
	public static string PathOf(DeathCause cause, Func<string, bool> exists = null)
	{
		exists ??= file => ResourceLoader.Exists(file);
		if (Files.TryGetValue(cause, out string path) && exists(path))
		{
			return path;
		}

		return exists(UnknownPath) ? UnknownPath : null;
	}

	/// <summary>Medidas de una imagen ajustada a la caja sin deformarla y sin ampliarla.</summary>
	public static Vector2 Fit(Vector2 size, Vector2 box)
	{
		float scale = Mathf.Min(1.0f, Mathf.Min(box.X / size.X, box.Y / size.Y));
		return size * scale;
	}
}
