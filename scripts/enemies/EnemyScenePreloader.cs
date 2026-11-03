using System.Collections.Generic;
using Godot;

/// <summary>
/// Precarga las escenas de enemigo de un nivel para que ninguna se cargue de golpe en el momento en que nacen los enemigos de una sala.
///
/// Medido con tests/SpawnTimingProbe.tscn: cargar la escena de un tipo la primera vez cuesta entre 8 y 50 ms (más instanciarla y
/// añadirla al árbol), y eso ocurría justo al aparecer los enemigos, con Noah ya moviéndose. Aquí se cargan al empezar la partida,
/// cuando RoomManager recibe el mapa, y se conservan (Held) para que el caché de recursos del motor no las suelte.
///
/// Los tipos salen de las salas del mapa: SpawnKinds si la sala los trae (generador v2) o el tipo de EnemyScene (generador v1).
/// Es idempotente (un tipo ya cargado no se vuelve a cargar), no cambia la lógica del juego (solo rellena un caché que el juego
/// rellenaría igualmente) y tolera escenas que no carguen (se cuentan en Failed y se ignoran).
/// </summary>
public static class EnemyScenePreloader
{
	private static readonly Dictionary<EnemyKind, PackedScene> HeldScenes = new();

	/// <summary>Tipos cuya escena está cargada y retenida.</summary>
	public static IReadOnlyCollection<EnemyKind> Held => HeldScenes.Keys;

	/// <summary>Escenas que no llegaron a cargar.</summary>
	public static int Failed { get; private set; }

	/// <summary>Escena retenida de un tipo, o null si aún no se precargó.</summary>
	public static PackedScene TryGet(EnemyKind kind)
	{
		return HeldScenes.TryGetValue(kind, out PackedScene scene) ? scene : null;
	}

	/// <summary>Carga y retiene la escena de un tipo. Devuelve false si no se pudo cargar.</summary>
	public static bool Preload(EnemyKind kind)
	{
		if (HeldScenes.ContainsKey(kind))
		{
			return true;
		}

		string path = EnemyKinds.Get(kind).ScenePath;
		if (!ResourceLoader.Exists(path) || ResourceLoader.Load<PackedScene>(path) is not PackedScene scene)
		{
			Failed++;
			return false;
		}

		HeldScenes[kind] = scene;
		return true;
	}

	/// <summary>Tipos de enemigo que aparecen en alguna sala del mapa, sin repetir y en orden de tipo.</summary>
	public static List<EnemyKind> KindsOf(LevelMap map)
	{
		SortedSet<EnemyKind> kinds = new();
		if (map?.Rooms != null)
		{
			foreach (RoomData room in map.Rooms)
			{
				AddKindsOf(room, kinds);
			}
		}

		return new List<EnemyKind>(kinds);
	}

	/// <summary>Precarga todos los tipos del mapa. Devuelve cuántos tipos distintos tiene el mapa.</summary>
	public static int PreloadFor(LevelMap map)
	{
		List<EnemyKind> kinds = KindsOf(map);
		foreach (EnemyKind kind in kinds)
		{
			Preload(kind);
		}

		return kinds.Count;
	}

	/// <summary>Olvida lo retenido (solo para pruebas: así se comprueba la precarga desde cero).</summary>
	public static void Reset()
	{
		HeldScenes.Clear();
		Failed = 0;
	}

	private static void AddKindsOf(RoomData room, SortedSet<EnemyKind> kinds)
	{
		if (room == null || room.SpawnPoints.Length == 0)
		{
			return;
		}

		if (room.SpawnKinds.Length == room.SpawnPoints.Length)
		{
			foreach (int value in room.SpawnKinds)
			{
				if (EnemyKinds.IsValid(value))
				{
					kinds.Add((EnemyKind)value);
				}
			}

			return;
		}

		if (room.EnemyScene != null)
		{
			kinds.Add(EnemyKinds.KindOfScene(room.EnemyScene));
		}
	}
}
