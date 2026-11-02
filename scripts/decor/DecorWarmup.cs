using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Precalentamiento de la ambientación para que la primera sala de combate no dé un tirón: lo que hasta ahora se hacía la primera vez que
/// se entraba en ella (cargar las texturas de suelo y pared, cargar y medir los modelos de la decoración y contar los triángulos de cada
/// uno para el presupuesto) se hace mientras el jugador está en el menú.
///
///   - Request pide en segundo plano (ResourceLoader.LoadThreadedRequest) las texturas y los modelos; PollLoads recoge los terminados y los
///     guarda (Held), con lo que no se vuelven a descargar de la caché al salir de una sala.
///   - PrimeNext hace en el hilo principal, de uno en uno, lo que no se puede hacer en otro hilo: medir cada modelo (ModelLibrary) y contar
///     sus triángulos (FloorPropsPlanner.CostOf).
///   - DecorWarmupRunner dibuja una vez las variantes de material y los modelos fuera de la partida para que el dibujo y las texturas ya
///     estén preparados en la tarjeta gráfica.
///
/// Es idempotente (cada ruta se pide una sola vez; lo que ya está hecho no se repite), reanudable (si se sale del menú a medias, la
/// siguiente vez sigue donde quedó) y tolerante a archivos que falten o no carguen (se cuentan y se ignoran). No cambia la lógica del
/// juego: solo rellena cachés que el juego rellenaría igualmente.
/// </summary>
public static class DecorWarmup
{
	/// <summary>Interruptor general: con false el menú no lanza el precalentamiento.</summary>
	public static bool Enabled { get; set; } = true;

	/// <summary>Rutas extra a precalentar (las pruebas añaden una inexistente para comprobar la tolerancia).</summary>
	public static List<string> ExtraPaths { get; } = new();

	private static readonly HashSet<string> RequestedPaths = new();
	private static readonly List<string> Pending = new();
	private static readonly List<Resource> HeldResources = new();
	private static readonly Queue<string> PrimeQueue = new();
	private static bool _primeBuilt;

	/// <summary>Se terminó todo, incluido el dibujo previo. Lo marca el runner.</summary>
	public static bool Done { get; private set; }

	/// <summary>Rutas ya pedidas o descartadas (incluye las que faltan).</summary>
	public static int Requested => RequestedPaths.Count;

	/// <summary>Rutas pedidas cuyo archivo no existe.</summary>
	public static int Missing { get; private set; }

	/// <summary>Rutas pedidas que no llegaron a cargar.</summary>
	public static int Failed { get; private set; }

	/// <summary>Modelos ya medidos y con sus triángulos contados.</summary>
	public static int Primed { get; private set; }

	/// <summary>Recursos cargados que se conservan (texturas y escenas).</summary>
	public static IReadOnlyList<Resource> Held => HeldResources;

	/// <summary>Cargas en segundo plano que aún no han terminado.</summary>
	public static int PendingLoads => Pending.Count;

	/// <summary>Rutas de las texturas opcionales de suelo y pared (las variantes y las de siempre) en la carpeta actual.</summary>
	public static IEnumerable<string> TexturePaths()
	{
		return RoomLook.FloorVariants.Concat(RoomLook.WallVariants)
			.Concat(new[] { RoomSurfaces.Parquet, RoomSurfaces.Wallpaper, RoomSurfaces.Rug, RoomSurfaces.CribWood })
			.Select(RoomSurfaces.PathOf);
	}

	/// <summary>Rutas de todos los modelos de la decoración (más las extra).</summary>
	public static IEnumerable<string> ModelPaths()
	{
		List<string> paths = new()
		{
			RoomDecorator.FramePath, RoomDecorator.BarsPath, RoomDecorator.ColumnPath,
			PropModels.Path(PropModels.Rug), PropModels.Path(PropModels.Pedestal),
		};
		paths.AddRange(WallDecorPlanner.WallPieces.Select(piece => PropModels.Path(piece.Model)));
		paths.AddRange(FreeDecorPlanner.SmallCatalog.Select(PropModels.Path));
		paths.AddRange(FreeDecorPlanner.BossGiants.Select(PropModels.Path));
		paths.AddRange(ExtraPaths);
		return paths.Distinct();
	}

	/// <summary>Pide en segundo plano todo lo que falte por pedir. Se puede llamar cuantas veces se quiera.</summary>
	public static void Request()
	{
		foreach (string path in TexturePaths().Concat(ModelPaths()))
		{
			if (!RequestedPaths.Add(path))
			{
				continue;
			}

			if (!ResourceLoader.Exists(path))
			{
				Missing++;
				continue;
			}

			if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok)
			{
				Pending.Add(path);
			}
			else
			{
				Failed++;
			}
		}
	}

	/// <summary>Recoge las cargas terminadas y las guarda. Devuelve true cuando ya no queda ninguna pendiente.</summary>
	public static bool PollLoads()
	{
		for (int i = Pending.Count - 1; i >= 0; i--)
		{
			string path = Pending[i];
			ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
			if (status == ResourceLoader.ThreadLoadStatus.InProgress)
			{
				continue;
			}

			if (status == ResourceLoader.ThreadLoadStatus.Loaded && ResourceLoader.LoadThreadedGet(path) is Resource resource)
			{
				HeldResources.Add(resource);
			}
			else
			{
				Failed++;
			}

			Pending.RemoveAt(i);
		}

		return Pending.Count == 0;
	}

	/// <summary>
	/// Mide un modelo y cuenta sus triángulos en el hilo principal (uno por llamada, para repartirlo entre fotogramas). Devuelve true si
	/// quedan más por hacer.
	/// </summary>
	public static bool PrimeNext()
	{
		if (!_primeBuilt)
		{
			foreach (string path in ModelPaths().Where(path => ResourceLoader.Exists(path)))
			{
				PrimeQueue.Enqueue(path);
			}

			_primeBuilt = true;
		}

		if (PrimeQueue.Count == 0)
		{
			return false;
		}

		string next = PrimeQueue.Dequeue();
		ModelLibrary.BoundsOf(next);
		FloorPropsPlanner.CostOf(next);
		Primed++;
		return PrimeQueue.Count > 0;
	}

	public static void MarkDone() => Done = true;
}
