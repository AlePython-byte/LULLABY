using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Consejos de la pantalla de carga, en voz de diario. El título «Consejo del diario» es fijo (Title), así que las frases van SIN ese
/// prefijo y miden como mucho MaxLength caracteres.
///
/// La lista se lee de resources/text/consejos_diario.json ({"consejos": [...]}) para ampliarla sin tocar código; es un JSON que el motor
/// importa como recurso (como los modelos de dificultad), así que también viaja en la exportación. Si el archivo no existe, no es válido o
/// no deja ningún consejo, se usa Fallback (seis consejos propios). Las entradas que no son texto o están vacías se ignoran.
///
/// Cada consejo del archivo y del respaldo se ha comprobado contra el código del juego (controles, objetos y enemigos reales).
/// </summary>
public static class LoadingTips
{
	public const string Title = "Consejo del diario";
	public const int MaxLength = 90;
	public const string DefaultPath = "res://resources/text/consejos_diario.json";

	/// <summary>Respaldo cuando el archivo falta, no es válido o está vacío.</summary>
	public static readonly string[] Fallback =
	{
		"Me muevo con W, A, S y D para escapar antes de que me alcancen.",
		"Mis lágrimas salen con las flechas del teclado, en cuatro direcciones.",
		"Si el miedo me gana, pulso Esc o P y la pesadilla espera un momento.",
		"Las puertas se cierran solas al entrar. Solo se abren si venzo a todos.",
		"El biberón me cura 2 puntos. Con la vida llena se queda en el suelo.",
		"La manta vieja me arropa: recibo la mitad del daño.",
	};

	/// <summary>Archivo de consejos. Solo las pruebas lo cambian (y llaman a Reload).</summary>
	public static string Path { get; set; } = DefaultPath;

	private static string[] _tips;
	private static string _loadedPath;
	private static Json _held; // se retiene (como DecorWarmup.Held) para que el recolector no finalice su envoltorio entre partidas

	/// <summary>Los consejos que se usan ahora: los del archivo, o el respaldo.</summary>
	public static IReadOnlyList<string> All
	{
		get
		{
			Ensure();
			return _tips;
		}
	}

	/// <summary>Los consejos salen del archivo (no del respaldo).</summary>
	public static bool UsingFile
	{
		get
		{
			Ensure();
			return !ReferenceEquals(_tips, Fallback);
		}
	}

	/// <summary>Un consejo cualquiera. Usa el generador que se le pase: no toca el azar de Godot ni el de la partida.</summary>
	public static string Pick(Random random)
	{
		IReadOnlyList<string> tips = All;
		return tips[random.Next(tips.Count)];
	}

	/// <summary>Olvida lo leído para volver a leer Path (solo para pruebas).</summary>
	public static void Reload()
	{
		_tips = null;
		_loadedPath = null;
	}

	private static void Ensure()
	{
		if (_tips != null && _loadedPath == Path)
		{
			return;
		}

		_loadedPath = Path;
		_tips = ReadFile() ?? Fallback;
	}

	private static string[] ReadFile()
	{
		if (string.IsNullOrEmpty(Path) || !ResourceLoader.Exists(Path) || ResourceLoader.Load<Json>(Path) is not Json json)
		{
			return null;
		}

		_held = json;
		if (json.Data.VariantType != Variant.Type.Dictionary)
		{
			return null;
		}

		Godot.Collections.Dictionary data = json.Data.AsGodotDictionary();
		if (!data.ContainsKey("consejos") || data["consejos"].VariantType != Variant.Type.Array)
		{
			return null;
		}

		List<string> tips = new();
		foreach (Variant entry in data["consejos"].AsGodotArray())
		{
			if (entry.VariantType == Variant.Type.String && entry.AsString().Trim() is { Length: > 0 } text)
			{
				tips.Add(text);
			}
		}

		return tips.Count > 0 ? tips.ToArray() : null;
	}
}
