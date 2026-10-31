using System.Collections.Generic;

/// <summary>
/// Todos los sonidos del juego como constantes de C# (no se leen rutas de docs/ en ejecución). Cada uno dice su archivo, su bus,
/// si es plano o posicional (3D), su volumen, la variación de tono, cuántas voces simultáneas admite, el intervalo mínimo entre
/// repeticiones y si es prioritario (puede robar la voz más antigua cuando no queda ninguna libre).
///
/// Los archivos viven en assets/audio/sfx/{File}.ogg. Si uno no existe (los de objetos, interfaz y jefe se añadirán después) no
/// pasa nada: AudioManager no reproduce nada y no lanza errores. Un sonido con Fallback usa el del id indicado, con su tono
/// multiplicado por FallbackPitch, mientras no exista su propio archivo.
/// </summary>
public static class SoundCatalog
{
	public const string SfxFolder = "res://assets/audio/sfx/";
	public const string MusicFolder = "res://assets/audio/music/";

	public const string BusMaster = "Master";
	public const string BusSfx = "SFX";
	public const string BusMusic = "Musica";

	// Ids (coinciden con el nombre del archivo).
	public const string TearShot = "lagrima_disparo";
	public const string TearImpact = "lagrima_impacto";
	public const string NoahHurt = "noah_dano";
	public const string NoahDies = "noah_muere";
	public const string EnemyHit = "enemigo_golpe";
	public const string EnemyDies = "enemigo_muere";
	public const string FuseLit = "mecha_explosivo";
	public const string Explosion = "explosion";
	public const string ShooterShot = "tirador_disparo";
	public const string FlyerWings = "volador_aleteo";

	// Aún sin archivo: se usarán en cuanto existan.
	public const string ItemPickedUp = "objeto_recogido";
	public const string UiClick = "ui_click";
	public const string BossHit = "jefe_golpe";
	public const string BossDies = "jefe_muere";

	public sealed record Sound(string Id, bool Positional = false, float VolumeDb = 0.0f, float PitchVariation = 0.0f, int MaxVoices = 4,
		float MinInterval = 0.0f, bool Priority = false, string Fallback = null, float FallbackPitch = 1.0f)
	{
		public string File => Id;
		public string Path => $"{SfxFolder}{Id}.ogg";
		public string Bus => BusSfx;
	}

	/// <summary>Variación de tono (±5 %) de los sonidos que se repiten mucho.</summary>
	public const float RepeatedPitchVariation = 0.05f;

	private static readonly Dictionary<string, Sound> Sounds = new()
	{
		[TearShot] = new Sound(TearShot, VolumeDb: -4.0f, PitchVariation: RepeatedPitchVariation, MaxVoices: 3, MinInterval: 0.06f),
		[TearImpact] = new Sound(TearImpact, VolumeDb: -3.0f, PitchVariation: RepeatedPitchVariation, MaxVoices: 4, MinInterval: 0.04f),
		[NoahHurt] = new Sound(NoahHurt, MaxVoices: 2, MinInterval: 0.1f),
		[NoahDies] = new Sound(NoahDies, MaxVoices: 1, Priority: true),
		[EnemyHit] = new Sound(EnemyHit, Positional: true, VolumeDb: -3.0f, PitchVariation: RepeatedPitchVariation, MaxVoices: 4, MinInterval: 0.05f),
		[EnemyDies] = new Sound(EnemyDies, Positional: true, MaxVoices: 4, MinInterval: 0.05f),
		[FuseLit] = new Sound(FuseLit, Positional: true, MaxVoices: 3),
		[Explosion] = new Sound(Explosion, Positional: true, MaxVoices: 3, Priority: true),
		[ShooterShot] = new Sound(ShooterShot, Positional: true, MaxVoices: 3, MinInterval: 0.05f),
		[FlyerWings] = new Sound(FlyerWings, Positional: true, VolumeDb: -6.0f, MaxVoices: 1),
		[ItemPickedUp] = new Sound(ItemPickedUp, MaxVoices: 2),
		[UiClick] = new Sound(UiClick, MaxVoices: 2, MinInterval: 0.05f),
		// Hasta que existan jefe_golpe y jefe_muere, The Mother usa los de los enemigos con el tono un 20 % más grave.
		[BossHit] = new Sound(BossHit, Positional: true, VolumeDb: -3.0f, PitchVariation: RepeatedPitchVariation, MaxVoices: 4, MinInterval: 0.05f, Fallback: EnemyHit, FallbackPitch: 0.8f),
		[BossDies] = new Sound(BossDies, Positional: true, MaxVoices: 2, Priority: true, Fallback: EnemyDies, FallbackPitch: 0.8f),
	};

	public static Sound Get(string id) => id != null && Sounds.TryGetValue(id, out Sound sound) ? sound : null;

	public static IEnumerable<Sound> All => Sounds.Values;

	/// <summary>Ruta de una pista de música (assets/audio/music/{name}.ogg).</summary>
	public static string MusicPath(string name) => $"{MusicFolder}{name}.ogg";
}
