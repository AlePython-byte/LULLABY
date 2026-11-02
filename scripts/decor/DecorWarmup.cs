using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

/// <summary>
/// Precalentamiento de la ambientación para que la primera sala de combate no dé un tirón: lo que hasta ahora se hacía la primera vez que
/// se entraba en ella (cargar las texturas de suelo y pared, cargar y medir los modelos de la decoración y contar los triángulos de cada
/// uno para el presupuesto) se hace mientras el jugador está en el menú.
///
///   - Request pone en cola las texturas y los modelos que existen (y cuenta los que faltan).
///   - Step carga y prepara lo que cabe en un presupuesto de tiempo por fotograma, en el HILO PRINCIPAL y de uno en uno: carga la textura o
///     la escena y la guarda (Held, para que no se vuelva a leer al salir de una sala), mide el modelo (ModelLibrary) y cuenta sus
///     triángulos (FloorPropsPlanner.CostOf).
///   - DecorWarmupRunner llama a Step cada fotograma y luego dibuja una vez las variantes de material y los modelos fuera de la partida,
///     para que el dibujo y las texturas ya estén preparados en la tarjeta gráfica.
///
/// Sin hilos a propósito: ResourceLoader.LoadThreadedRequest con varias cargas a la vez creaba materiales y mallas en paralelo y, con el
/// renderizador de pruebas (headless), chocaban entre sí y tiraban el proceso de vez en cuando (errores de RID en material_storage y
/// cierres con -1073741819, 3 de cada 30 corridas); con una sola carga a la vez el cargador tardaba más de un segundo por recurso. Lo
/// caro (medir y contar triángulos) ya iba en el hilo principal, y cargar de uno en uno con presupuesto de tiempo no se nota en el menú.
///
/// Es idempotente (cada ruta se pone en cola una sola vez; lo que ya está hecho no se repite), reanudable (si se sale del menú a medias,
/// la siguiente vez sigue donde quedó) y tolerante a archivos que falten o no carguen (se cuentan y se ignoran). No cambia la lógica del
/// juego: solo rellena cachés que el juego rellenaría igualmente.
/// </summary>
public static class DecorWarmup
{
	/// <summary>Interruptor general: con false el menú no lanza el precalentamiento.</summary>
	public static bool Enabled { get; set; } = true;

	/// <summary>Rutas extra a precalentar (las pruebas añaden una inexistente para comprobar la tolerancia).</summary>
	public static List<string> ExtraPaths { get; } = new();

	/// <summary>Milisegundos por fotograma que Step puede gastar (siempre hace al menos un elemento).</summary>
	public const double FrameBudgetMs = 6.0;

	private static readonly HashSet<string> RequestedPaths = new();
	private static readonly Queue<(string Path, bool Model)> Work = new();
	private static readonly List<Resource> HeldResources = new();

	/// <summary>Se terminó todo, incluido el dibujo previo. Lo marca el runner.</summary>
	public static bool Done { get; private set; }

	/// <summary>Rutas ya puestas en cola o descartadas (incluye las que faltan).</summary>
	public static int Requested => RequestedPaths.Count;

	/// <summary>Rutas pedidas cuyo archivo no existe.</summary>
	public static int Missing { get; private set; }

	/// <summary>Rutas que existen pero no llegaron a cargar.</summary>
	public static int Failed { get; private set; }

	/// <summary>Modelos ya cargados, medidos y con sus triángulos contados.</summary>
	public static int Primed { get; private set; }

	/// <summary>Recursos cargados que se conservan (texturas y escenas).</summary>
	public static IReadOnlyList<Resource> Held => HeldResources;

	/// <summary>Elementos que aún faltan por cargar.</summary>
	public static int Remaining => Work.Count;

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

	/// <summary>Pone en cola todo lo que falte por pedir. Se puede llamar cuantas veces se quiera.</summary>
	public static void Request()
	{
		foreach (string path in TexturePaths())
		{
			Enqueue(path, false);
		}

		foreach (string path in ModelPaths())
		{
			Enqueue(path, true);
		}
	}

	private static void Enqueue(string path, bool model)
	{
		if (!RequestedPaths.Add(path))
		{
			return;
		}

		if (!ResourceLoader.Exists(path))
		{
			Missing++;
			return;
		}

		Work.Enqueue((path, model));
	}

	/// <summary>
	/// Carga y prepara elementos de la cola, de uno en uno, hasta gastar budgetMs (al menos uno por llamada). Devuelve true si aún quedan.
	/// Hay que llamarla siempre desde el hilo principal.
	/// </summary>
	public static bool Step(double budgetMs = FrameBudgetMs)
	{
		Stopwatch clock = Stopwatch.StartNew();
		do
		{
			if (Work.Count == 0)
			{
				break;
			}

			(string path, bool model) = Work.Dequeue();
			Load(path, model);
		}
		while (clock.Elapsed.TotalMilliseconds < budgetMs);

		return Work.Count > 0;
	}

	private static void Load(string path, bool model)
	{
		if (model)
		{
			// Medir carga la escena en ModelLibrary; contar triángulos instancia el modelo una vez (se guarda en caché).
			if (ModelLibrary.BoundsOf(path) == null || ResourceLoader.Load<PackedScene>(path) is not PackedScene scene)
			{
				Failed++;
				return;
			}

			HeldResources.Add(scene);
			FloorPropsPlanner.CostOf(path);
			Primed++;
			return;
		}

		if (ResourceLoader.Load<Texture2D>(path) is Texture2D texture)
		{
			HeldResources.Add(texture);
		}
		else
		{
			Failed++;
		}
	}

	public static void MarkDone() => Done = true;

	/// <summary>
	/// Olvida lo pedido y lo hecho para volver a empezar (solo para pruebas: así se comprueba el precalentamiento desde cero). No vacía las
	/// cachés de ModelLibrary ni de FloorPropsPlanner ni el caché de recursos del motor.
	/// </summary>
	public static void Reset()
	{
		RequestedPaths.Clear();
		Work.Clear();
		HeldResources.Clear();
		Done = false;
		Missing = 0;
		Failed = 0;
		Primed = 0;
	}
}
