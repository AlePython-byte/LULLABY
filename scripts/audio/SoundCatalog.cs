using System.Collections.Generic;

/// <summary>
/// Todos los sonidos del juego como constantes de C# (no se leen rutas de docs/ en ejecución). Cada uno dice su archivo, su bus,
/// si es plano o posicional (3D), su volumen, la variación de tono, cuántas voces simultáneas admite, el intervalo mínimo entre
/// repeticiones y si es prioritario (puede robar la voz más antigua cuando no queda ninguna libre).
///
/// La música (menu, the_cradle, jefe) se registra aparte como MusicTrack, siempre en bucle, y vive en assets/audio/music/.
///
/// Los archivos viven en assets/audio/sfx/{File}.ogg. Si uno no existe (hoy faltan jefe_golpe y jefe_fase) no
/// pasa nada: AudioManager no reproduce nada y no lanza errores. Un sonido con Fallback usa el del id indicado, con su tono
/// multiplicado por FallbackPitch, mientras no exista su propio archivo.
/// </summary>
public static class SoundCatalog
{
	public const string SfxFolder = "res://assets/audio/sfx/";

	/// <summary>Carpeta de la música. Solo las pruebas la apuntan a otra para simular archivos ausentes.</summary>
	public static string MusicFolder { get; set; } = "res://assets/audio/music/";

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
	public const string ItemPickedUp = "objeto_recoger";
	public const string Heal = "curar";
	public const string UiClick = "menu_click";
	public const string MenuConfirm = "menu_confirmar";

	// Puertas de las salas (al cerrarse al empezar el combate y al abrirse al despejarla).
	public const string DoorOpens = "puerta_abre";
	public const string DoorCloses = "puerta_cierra";
	public const string BossHit = "jefe_golpe";
	public const string BossDies = "jefe_muere";
	public const string BossPhase = "jefe_fase";
	public const string BossAppears = "jefe_aparece";
	public const string BossAttackRing = "jefe_ataque_1";
	public const string BossAttackSpiral = "jefe_ataque_2";

	// Fin de la partida: la victoria suena al mostrar su pantalla y la derrota al mostrar la suya.
	public const string Victory = "victoria";

	// Pantalla de derrota (al mostrarse, no al morir: eso es noah_muere).
	public const string Defeat = "derrota";

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
		[TearShot] = new Sound(TearShot, VolumeDb: -10.0f, PitchVariation: RepeatedPitchVariation, MaxVoices: 3, MinInterval: 0.06f),
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
		[Heal] = new Sound(Heal, MaxVoices: 1),
		[UiClick] = new Sound(UiClick, MaxVoices: 2, MinInterval: 0.05f),
		[MenuConfirm] = new Sound(MenuConfirm, MaxVoices: 1, Priority: true),
		// Las puertas de una sala se mueven a la vez: una sola voz por evento, no una por puerta.
		[DoorOpens] = new Sound(DoorOpens, MaxVoices: 1),
		[DoorCloses] = new Sound(DoorCloses, MaxVoices: 1),
		[BossAppears] = new Sound(BossAppears, Positional: true, MaxVoices: 1, Priority: true),
		[BossAttackRing] = new Sound(BossAttackRing, Positional: true, MaxVoices: 1),
		[BossAttackSpiral] = new Sound(BossAttackSpiral, Positional: true, MaxVoices: 1),
		[Victory] = new Sound(Victory, MaxVoices: 1, Priority: true),
		// Hasta que existan jefe_golpe y jefe_muere, The Mother usa los de los enemigos con el tono un 20 % más grave.
		[BossHit] = new Sound(BossHit, Positional: true, VolumeDb: -3.0f, PitchVariation: RepeatedPitchVariation, MaxVoices: 4, MinInterval: 0.05f, Fallback: EnemyHit, FallbackPitch: 0.8f),
		[BossDies] = new Sound(BossDies, Positional: true, MaxVoices: 2, Priority: true, Fallback: EnemyDies, FallbackPitch: 0.8f),
		// Cambio de fase del jefe. Sin su archivo no suena (no tiene sonido de respaldo) y no da error.
		[BossPhase] = new Sound(BossPhase, Positional: true, MaxVoices: 1, Priority: true),
		// Sin su archivo no suena nada y no da error: no tiene sonido de respaldo (noah_muere ya suena al morir y se oiría dos veces).
		[Defeat] = new Sound(Defeat, MaxVoices: 1, Priority: true),
	};

	public static Sound Get(string id) => id != null && Sounds.TryGetValue(id, out Sound sound) ? sound : null;

	public static IEnumerable<Sound> All => Sounds.Values;

	// --- música (assets/audio/music/{id}.ogg, bus Musica) ---

	public const string MusicMenu = "menu";
	public const string MusicCradle = "the_cradle";
	public const string MusicBoss = "jefe";

	// Segundos desde donde arranca cada pista al pedirla y adonde vuelve el bucle al terminar (ajustar a oído). the_cradle y jefe traen
	// una entrada casi muda; medido con las muestras, la música real empieza en 7,75 s y 16,0 s, y aquí se deja medio segundo antes.
	public const float CradleStartSeconds = 7.25f;
	public const float CradleLoopOffsetSeconds = 7.25f;
	public const float BossStartSeconds = 15.5f;
	public const float BossLoopOffsetSeconds = 15.5f;

	/// <summary>
	/// Una pista de música: siempre en bucle, con un ajuste de volumen propio (dB) por si una suena más fuerte que otra. StartSeconds es
	/// desde donde empieza a sonar al pedirla y LoopOffsetSeconds adonde vuelve el bucle al llegar al final (0 y 0: la pista entera, sin tocar nada).
	/// </summary>
	public sealed record MusicTrack(string Id, bool Loop = true, float VolumeDb = 0.0f, float StartSeconds = 0.0f, float LoopOffsetSeconds = 0.0f)
	{
		public string Path => $"{MusicFolder}{Id}.ogg";
		public string Bus => BusMusic;
	}

	private static readonly Dictionary<string, MusicTrack> Tracks = new()
	{
		[MusicMenu] = new MusicTrack(MusicMenu),
		[MusicCradle] = new MusicTrack(MusicCradle, StartSeconds: CradleStartSeconds, LoopOffsetSeconds: CradleLoopOffsetSeconds),
		[MusicBoss] = new MusicTrack(MusicBoss, StartSeconds: BossStartSeconds, LoopOffsetSeconds: BossLoopOffsetSeconds),
	};

	public static MusicTrack GetMusic(string id) => id != null && Tracks.TryGetValue(id, out MusicTrack track) ? track : null;

	public static IEnumerable<MusicTrack> AllMusic => Tracks.Values;

	/// <summary>Ruta de una pista de música (assets/audio/music/{name}.ogg).</summary>
	public static string MusicPath(string name) => $"{MusicFolder}{name}.ogg";
}
