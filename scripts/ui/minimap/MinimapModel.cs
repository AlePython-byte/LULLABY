using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Cómo se ve una sala en el minimapa.</summary>
public enum RoomVisibility
{
	/// <summary>No se dibuja (aún no se ha visitado ninguna sala contigua).</summary>
	Hidden,

	/// <summary>Contigua a una sala visitada, sin visitar: visible pero atenuada.</summary>
	Neighbor,

	Visited,

	/// <summary>La sala en la que está Noah (también cuenta como visitada).</summary>
	Current,
}

/// <summary>
/// Estado del minimapa de un nivel, sin nodos. SOLO LEE el LevelMap (celdas en rejilla y tipos de sala): no cambia nada del nivel ni
/// de la generación. Una sala vecina es una celda contigua a una sala visitada (en el mapa hay puerta hacia cada celda vecina con sala).
/// Las conexiones (puertas) se dibujan entre una sala visitada o actual y cualquier sala visible; los iconos de tesoro y jefe, solo en
/// salas vecinas, visitadas o actual.
/// </summary>
public sealed class MinimapModel
{
	private static readonly Vector2I[] Offsets = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

	private readonly Dictionary<Vector2I, RoomData> _rooms = new();
	private readonly HashSet<Vector2I> _visited = new();

	public MinimapModel(LevelMap map)
	{
		Map = map;
		foreach (RoomData room in map.Rooms)
		{
			_rooms[room.Cell] = room;
		}
	}

	public LevelMap Map { get; }

	/// <summary>Celda de la sala actual, o null si aún no se entró en ninguna.</summary>
	public Vector2I? Current { get; private set; }

	/// <summary>Noah entra en una sala: pasa a ser la actual y queda visitada.</summary>
	public void Enter(Vector2I cell)
	{
		if (!_rooms.ContainsKey(cell))
		{
			return;
		}

		Current = cell;
		_visited.Add(cell);
	}

	public bool Has(Vector2I cell) => _rooms.ContainsKey(cell);

	public RoomVisibility StateOf(Vector2I cell)
	{
		if (!_rooms.ContainsKey(cell))
		{
			return RoomVisibility.Hidden;
		}

		if (Current == cell)
		{
			return RoomVisibility.Current;
		}

		if (_visited.Contains(cell))
		{
			return RoomVisibility.Visited;
		}

		return Offsets.Any(offset => _visited.Contains(cell + offset)) ? RoomVisibility.Neighbor : RoomVisibility.Hidden;
	}

	/// <summary>Tipo de sala si el minimapa puede enseñar su icono (tesoro o jefe en una sala no oculta); null en otro caso.</summary>
	public RoomType? IconType(Vector2I cell)
	{
		if (StateOf(cell) == RoomVisibility.Hidden)
		{
			return null;
		}

		RoomType type = _rooms[cell].Type;
		return type == RoomType.Treasure || type == RoomType.Boss ? type : null;
	}

	/// <summary>Celdas que se dibujan (todas menos las ocultas).</summary>
	public IEnumerable<Vector2I> VisibleCells => _rooms.Keys.Where(cell => StateOf(cell) != RoomVisibility.Hidden);

	/// <summary>Conexiones que se dibujan, cada una una sola vez. A siempre es una sala visitada o actual; B, cualquier sala visible.</summary>
	public IEnumerable<(Vector2I A, Vector2I B)> Connections()
	{
		foreach (Vector2I cell in _visited)
		{
			foreach (Vector2I offset in Offsets)
			{
				Vector2I other = cell + offset;
				if (!_rooms.ContainsKey(other))
				{
					continue;
				}

				// Entre dos visitadas se cuenta una sola vez.
				if (_visited.Contains(other) && (other.X < cell.X || (other.X == cell.X && other.Y < cell.Y)))
				{
					continue;
				}

				yield return (cell, other);
			}
		}
	}

	/// <summary>Rectángulo (en celdas) que contiene todas las salas visibles; vacío si no hay ninguna.</summary>
	public Rect2I VisibleBounds()
	{
		List<Vector2I> cells = VisibleCells.ToList();
		if (cells.Count == 0)
		{
			return new Rect2I();
		}

		int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
		return new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
	}
}
