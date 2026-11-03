using Godot;

/// <summary>
/// Opciones de la sesión que viajan entre escenas (del menú al nivel). Solo viven mientras el juego está abierto, salvo los
/// volúmenes, que se guardan en user://settings.cfg (ConfigFile) y se cargan al iniciar.
/// </summary>
public static class GameSettings
{
	public const string MenuScene = "res://scenes/ui/MainMenu.tscn";
	public const string LevelScene = "res://scenes/levels/TestLevel.tscn";

	/// <summary>
	/// Dificultad elegida en el menú. Nula si nadie eligió (TestLevel lanzado con F6, las pruebas): entonces manda el
	/// argumento --difficulty o el valor del inspector de RunManager.
	/// </summary>
	public static DifficultyTarget? Difficulty { get; set; }

	/// <summary>
	/// «Variedad extendida (experimental)»: la partida usa el generador v3 (seis tipos de enemigo, con el huidor y el excavador).
	/// Su modelo de dificultad no está entrenado para esos dos. Falso por defecto; un argumento --generator o una tecla de
	/// depuración mandan sobre esta opción.
	/// </summary>
	public static bool ExtendedVariety { get; set; }

	/// <summary>
	/// Registro de la partida (PlaytestLog). Apagado por defecto: solo lo enciende el botón de jugar del menú principal
	/// (MainMenu.StartGame) y el menú lo vuelve a apagar al abrirse. TestLevel lanzado con F6 y las pruebas no registran.
	/// </summary>
	public static bool PlaytestLogEnabled { get; set; }

	/// <summary>
	/// El registro solo se escribe con ventana: en modo headless (la suite de pruebas) el menú no lo enciende, aunque una prueba
	/// pulse «Jugar».
	/// </summary>
	public static bool CanRecordPlaytest => !PlaytestNeedsWindow || DisplayServer.GetName() != "headless";

	/// <summary>Verdadero siempre, salvo que una prueba lo apague para simular una partida con ventana.</summary>
	public static bool PlaytestNeedsWindow { get; set; } = true;

	public const float DefaultMasterVolume = 0.8f;
	public const float DefaultSfxVolume = 1.0f;
	public const float DefaultMusicVolume = 0.7f;

	/// <summary>Dónde se guardan los volúmenes. Las pruebas lo apuntan a otro archivo para no tocar el del jugador.</summary>
	public static string SettingsPath { get; set; } = "user://settings.cfg";

	/// <summary>Volumen general (bus Master), de 0 a 1.</summary>
	public static float MasterVolume { get; set; } = DefaultMasterVolume;

	/// <summary>Volumen de los efectos (bus SFX), de 0 a 1.</summary>
	public static float SfxVolume { get; set; } = DefaultSfxVolume;

	/// <summary>Volumen de la música (bus Musica), de 0 a 1.</summary>
	public static float MusicVolume { get; set; } = DefaultMusicVolume;

	/// <summary>
	/// Lee los volúmenes (general, efectos y música) de SettingsPath. Si el archivo no existe, está dañado o trae valores no válidos, usa los valores por defecto
	/// (sin lanzar nada). Devuelve true si el archivo se leyó entero.
	/// </summary>
	public static bool LoadSettings()
	{
		MasterVolume = DefaultMasterVolume;
		SfxVolume = DefaultSfxVolume;
		MusicVolume = DefaultMusicVolume;
		ConfigFile file = new();
		if (file.Load(SettingsPath) != Error.Ok)
		{
			return false;
		}

		bool valid = TryReadVolume(file, "master", out float master) & TryReadVolume(file, "sfx", out float sfx) & TryReadVolume(file, "music", out float music);
		MasterVolume = valid || float.IsFinite(master) ? master : DefaultMasterVolume;
		SfxVolume = valid || float.IsFinite(sfx) ? sfx : DefaultSfxVolume;
		MusicVolume = valid || float.IsFinite(music) ? music : DefaultMusicVolume;
		return valid;
	}

	/// <summary>Guarda los volúmenes en SettingsPath. Devuelve false si no se pudo escribir (no es un error del juego).</summary>
	public static bool SaveSettings()
	{
		ConfigFile file = new();
		file.SetValue("audio", "master", MasterVolume);
		file.SetValue("audio", "sfx", SfxVolume);
		file.SetValue("audio", "music", MusicVolume);
		return file.Save(SettingsPath) == Error.Ok;
	}

	private static bool TryReadVolume(ConfigFile file, string key, out float volume)
	{
		volume = float.NaN;
		Variant value = file.GetValue("audio", key, Variant.CreateFrom(float.NaN));
		if (value.VariantType is not (Variant.Type.Float or Variant.Type.Int))
		{
			return false;
		}

		float read = (float)value.AsDouble();
		if (!float.IsFinite(read))
		{
			return false;
		}

		volume = Mathf.Clamp(read, 0.0f, 1.0f);
		return true;
	}
}
