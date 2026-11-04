using System.Collections.Generic;
using Godot;

/// <summary>
/// Mapa objeto → ícono de pixel art (128×128) en un solo lugar. Las texturas se cargan una vez y se retienen aquí (estáticos) para que
/// el recolector de basura no las libere mientras algún HUD las usa. Un objeto sin ícono (o sin archivo) devuelve null: quien lo dibuja
/// usa solo texto.
/// </summary>
public static class ItemIcons
{
	public const string DefaultFolder = "res://assets/ui/pixelart/";

	/// <summary>Id del objeto → nombre del archivo. El corazón del suelo no tiene ícono todavía.</summary>
	public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(System.StringComparer.Ordinal)
	{
		["chupete"] = "icono_chupete.png",
		["biberon"] = "icono_biberon.png",
		["fotografia_rota"] = "icono_fotografia.png",
		["manta_vieja"] = "icono_manta.png",
		["oso_peluche"] = "icono_oso.png",
	};

	/// <summary>Carpeta de las imágenes. Solo las pruebas la apuntan a otra para simular íconos ausentes (y llaman a Reset).</summary>
	public static string Folder { get; set; } = DefaultFolder;

	private static readonly Dictionary<string, Texture2D> HeldTextures = new(System.StringComparer.Ordinal);

	/// <summary>Texturas cargadas y retenidas.</summary>
	public static IReadOnlyCollection<Texture2D> Held => HeldTextures.Values;

	/// <summary>El ícono del objeto, o null si no tiene archivo asignado, falta o no carga (sin errores).</summary>
	public static Texture2D For(string itemId)
	{
		if (itemId == null || !Files.TryGetValue(itemId, out string file))
		{
			return null;
		}

		string path = Folder + file;
		if (HeldTextures.TryGetValue(path, out Texture2D held))
		{
			return held;
		}

		if (!ResourceLoader.Exists(path) || ResourceLoader.Load<Texture2D>(path) is not Texture2D texture)
		{
			return null;
		}

		HeldTextures[path] = texture;
		return texture;
	}

	/// <summary>Olvida lo cargado (solo para pruebas).</summary>
	public static void Reset() => HeldTextures.Clear();
}
