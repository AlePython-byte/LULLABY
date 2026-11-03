using Godot;

/// <summary>
/// Los ocho cuadros de la intro estilo libro de cuentos: subtítulo, ilustración (assets/intro/escena_NN.png), voz (voz_NN.mp3) y el movimiento
/// lento de cada uno. Zoom va de ZoomFrom a ZoomTo y el foco (punto de la imagen, de 0 a 1, que intenta quedar en el centro) de FocusFrom a
/// FocusTo; el paneo siempre queda dentro de la imagen, así que nunca se ve un borde. Ajustar a ojo.
/// </summary>
public static class IntroFrames
{
	/// <summary>Carpeta de los archivos de la intro. Solo las pruebas la apuntan a otra para simular archivos ausentes.</summary>
	public static string Folder { get; set; } = "res://assets/intro/";

	public static string SongPath => $"{Folder}canto_mama.mp3";

	/// <summary>Si un archivo existe. Solo las pruebas lo sustituyen para quitar archivos sueltos (nulo = ResourceLoader.Exists).</summary>
	public static System.Func<string, bool> ExistsOverride { get; set; }

	public static bool Exists(string path) => ExistsOverride != null ? ExistsOverride(path) : ResourceLoader.Exists(path);

	/// <summary>Nada se queda en pantalla menos que esto (segundos), aunque la voz sea corta.</summary>
	public const float MinSeconds = 3.5f;

	/// <summary>Silencio tras la voz antes de pasar al siguiente cuadro (segundos).</summary>
	public const float PauseAfterVoice = 0.6f;

	public const float FadeInSeconds = 0.8f;
	public const float FadeOutSeconds = 0.5f;

	/// <summary>El último cuadro se funde a negro más despacio (y el canto se apaga a la vez).</summary>
	public const float LastFadeOutSeconds = 1.8f;

	public const string SkipHint = "Pulsa Enter para saltar";

	public sealed record Frame(int Number, string Subtitle, float ZoomFrom, float ZoomTo, Vector2 FocusFrom, Vector2 FocusTo)
	{
		public string ImagePath => $"{Folder}escena_{Number:00}.png";
		public string VoicePath => $"{Folder}voz_{Number:00}.mp3";
	}

	public static readonly Frame[] All =
	{
		new(1, "Querido diario: esta es mi cuna. Es pequeña, pero es mía.", 1.00f, 1.06f, new Vector2(0.50f, 0.50f), new Vector2(0.52f, 0.40f)), // zoom a Noah
		new(2, "Mamá me canta todas las noches. Dice que las nanas nos protegen.", 1.06f, 1.06f, new Vector2(0.55f, 0.55f), new Vector2(0.46f, 0.25f)), // paneo a la cara de la madre
		new(3, "Mis juguetes siempre me cuidaron. Eso creía.", 1.00f, 1.06f, new Vector2(0.50f, 0.50f), new Vector2(0.50f, 0.30f)), // zoom al móvil
		new(4, "Hoy la canción sonó distinta.", 1.00f, 1.04f, new Vector2(0.50f, 0.50f), new Vector2(0.56f, 0.60f)), // zoom leve
		new(5, "Y la cuna se hizo enorme.", 1.06f, 1.00f, new Vector2(0.50f, 0.62f), new Vector2(0.50f, 0.50f)), // zoom hacia atrás
		new(6, "Mis juguetes ya no quieren abrazarme. Quieren alcanzarme.", 1.06f, 1.06f, new Vector2(0.30f, 0.55f), new Vector2(0.70f, 0.55f)), // paneo lateral
		new(7, "Mamá está al final. Todavía canta.", 1.00f, 1.06f, new Vector2(0.50f, 0.58f), new Vector2(0.52f, 0.36f)), // zoom a la puerta
		new(8, "Voy a llegar hasta ella. Aunque tenga que gatear.", 1.00f, 1.06f, new Vector2(0.50f, 0.50f), new Vector2(0.50f, 0.45f)), // zoom lento y fundido a negro
	};

	/// <summary>Duración de un cuadro: lo que dure su voz más la pausa, nunca menos de MinSeconds. Sin voz, MinSeconds.</summary>
	public static float Duration(double voiceSeconds) => Mathf.Max(MinSeconds, (float)voiceSeconds + PauseAfterVoice);

	/// <summary>Tamaño y posición de la ilustración dentro de la ventana: cubre toda la ventana (con el zoom no hay bordes) y deja el foco lo más al centro que permite la imagen.</summary>
	public static Rect2 Layout(Vector2 stage, Vector2 texture, float zoom, Vector2 focus)
	{
		float cover = Mathf.Max(stage.X / texture.X, stage.Y / texture.Y);
		Vector2 size = texture * cover * Mathf.Max(zoom, 1.0f);
		Vector2 position = stage / 2.0f - focus * size;
		position = new Vector2(Mathf.Clamp(position.X, stage.X - size.X, 0.0f), Mathf.Clamp(position.Y, stage.Y - size.Y, 0.0f));
		return new Rect2(position, size);
	}

	/// <summary>Zoom y foco del cuadro cuando ha pasado la fracción progress (0 a 1) de su duración.</summary>
	public static (float Zoom, Vector2 Focus) At(Frame frame, float progress)
	{
		float p = Mathf.Clamp(progress, 0.0f, 1.0f);
		return (Mathf.Lerp(frame.ZoomFrom, frame.ZoomTo, p), frame.FocusFrom.Lerp(frame.FocusTo, p));
	}
}
