using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

/// <summary>
/// Registro de una partida real para las pruebas de equilibrio: un CSV por partida en Directory (user://playtest/ por defecto), con una
/// fila SALA por cada visita a una sala y una fila FIN con los totales y el resultado. No cambia ninguna regla del juego: solo escucha
/// eventos que ya existen (RoomManager.RoomLoaded, la salud de Noah, PlayerItems.ItemCollected, la victoria) y escribe.
///
/// Columnas (todas las filas las traen; las que no aplican van vacías):
///   fila, dificultad, semilla, generador, celda (x:y), tipo_sala, vida_entrada, vida_salida, objeto (ids unidos con +),
///   curacion (vida recuperada, incluye el +1 de vida del oso), dano_&lt;causa&gt; (una columna por valor de DeathCause), segundos
///   (sin contar la pausa), resultado (victoria, muerte o abandono), causa_muerte, vida_al_jefe y segundos_jefe (tiempo dentro de la sala
///   del jefe, desde que se entra).
///
/// Cada fila se escribe y se vuelca al disco (Flush). El archivo se cierra explícitamente al terminar la partida (victoria o muerte), al
/// salir de la escena (volver al menú, nueva partida) y con la notificación de cierre de ventana; si la partida no había terminado, la
/// fila FIN lleva resultado=abandono. Si no se puede escribir, se avisa una vez y el juego sigue sin registro.
/// RunManager lo crea solo si GameSettings.PlaytestLogEnabled (lo enciende el botón de jugar del menú principal).
/// </summary>
public partial class PlaytestLog : Node
{
	public const string DefaultDirectory = "user://playtest";

	public const string ResultVictory = "victoria";
	public const string ResultDeath = "muerte";
	public const string ResultAbandoned = "abandono";

	/// <summary>Carpeta de los CSV. Las pruebas la apuntan a una carpeta temporal.</summary>
	public static string Directory { get; set; } = DefaultDirectory;

	private static readonly DeathCause[] Causes = Enum.GetValues<DeathCause>();

	private RunManager _run;
	private RoomManager _rooms;
	private HealthComponent _health;
	private PlayerHealth _playerHealth;
	private PlayerItems _items;
	private FileAccess _file;
	private string _path = "";
	private string _difficulty = "";
	private string _seed = "";
	private string _generator = "";

	private bool _begun;
	private bool _finished;
	private bool _hasRoom;
	private string _roomCell = "";
	private string _roomType = "";
	private float _healthOnEntry;
	private float _previousHealth;

	private readonly float[] _roomDamage = new float[Causes.Length];
	private readonly float[] _totalDamage = new float[Causes.Length];
	private readonly List<string> _roomItems = new();
	private readonly List<string> _allItems = new();
	private float _roomHealed;
	private float _totalHealed;
	private float _roomSeconds;
	private float _totalSeconds;
	private float _bossSeconds;
	private float? _healthAtBoss;
	private bool _inBossRoom;
	private bool _died;
	private DeathCause _deathCause = DeathCause.Unknown;

	/// <summary>Ruta del CSV de esta partida (vacía si no se pudo abrir).</summary>
	public string FilePath => _path;

	/// <summary>La fila FIN ya está escrita y el archivo cerrado.</summary>
	public bool IsFinished => _finished;

	/// <summary>
	/// Empieza el registro de la partida de `run` en `rooms`. Debe llamarse antes de que RoomManager cargue la primera sala, con el
	/// nodo ya dentro del árbol. La versión del generador no la expone RunManager, por eso llega aparte.
	/// </summary>
	public void Begin(RunManager run, RoomManager rooms, int generatorVersion)
	{
		if (_begun)
		{
			return;
		}

		_begun = true;
		_run = run;
		_rooms = rooms;
		_difficulty = run.CurrentDifficulty.ToString();
		_seed = run.Seed.ToString(CultureInfo.InvariantCulture);
		_generator = generatorVersion.ToString(CultureInfo.InvariantCulture);

		Node player = rooms.Player;
		_health = player != null ? HealthComponent.FindIn(player) : null;
		_playerHealth = player?.GetNodeOrNull<PlayerHealth>("PlayerHealth");
		_items = player?.GetNodeOrNull<PlayerItems>("Items");
		if (_health == null)
		{
			GD.PushWarning("PlaytestLog: no se encontró la salud de Noah; no se registra la partida.");
			_finished = true;
			return;
		}

		if (!OpenFile())
		{
			_finished = true;
			return;
		}

		_previousHealth = _health.CurrentHealth;
		_health.Damaged += OnDamaged;
		_health.HealthChanged += OnHealthChanged;
		_health.Died += OnDied;
		if (_items != null)
		{
			_items.ItemCollected += OnItemCollected;
		}

		_rooms.RoomLoaded += OnRoomLoaded;
		_run.RunCompleted += OnRunCompleted;
	}

	public override void _Process(double delta)
	{
		if (_finished || !_hasRoom)
		{
			return;
		}

		float step = (float)delta;
		_roomSeconds += step;
		_totalSeconds += step;
		if (_inBossRoom)
		{
			_bossSeconds += step;
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			Finish(ResultAbandoned);
		}
	}

	public override void _ExitTree()
	{
		Finish(ResultAbandoned);
	}

	private bool OpenFile()
	{
		Error made = DirAccess.MakeDirRecursiveAbsolute(Directory);
		if (made != Error.Ok && made != Error.AlreadyExists)
		{
			GD.PushWarning($"PlaytestLog: no se pudo crear la carpeta {Directory} ({made}).");
			return false;
		}

		string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		_path = $"{Directory.TrimEnd('/', '\\')}/partida_{stamp}_{_seed}.csv";
		_file = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
		if (_file == null)
		{
			GD.PushWarning($"PlaytestLog: no se pudo abrir {_path} ({FileAccess.GetOpenError()}).");
			_path = "";
			return false;
		}

		WriteRow(Header());
		return true;
	}

	private static string Header()
	{
		StringBuilder header = new("fila,dificultad,semilla,generador,celda,tipo_sala,vida_entrada,vida_salida,objeto,curacion");
		foreach (DeathCause cause in Causes)
		{
			header.Append(",dano_").Append(cause);
		}

		header.Append(",segundos,resultado,causa_muerte,vida_al_jefe,segundos_jefe");
		return header.ToString();
	}

	private void OnRoomLoaded(RoomController room)
	{
		if (_finished)
		{
			return;
		}

		CloseRoomRow();
		_hasRoom = true;
		_roomCell = $"{room.Data.Cell.X}:{room.Data.Cell.Y}";
		_roomType = room.Data.Type.ToString();
		_healthOnEntry = _health.CurrentHealth;
		_roomHealed = 0.0f;
		_roomSeconds = 0.0f;
		_inBossRoom = room.Data.Type == RoomType.Boss;
		Array.Clear(_roomDamage);
		_roomItems.Clear();
		if (_inBossRoom && _healthAtBoss == null)
		{
			_healthAtBoss = _health.CurrentHealth;
		}
	}

	private void OnDamaged(float amount, float current)
	{
		if (_finished)
		{
			return;
		}

		// PlayerHealth fija LastCause antes de aplicar el daño, así que aquí ya es la de este golpe.
		DeathCause cause = _playerHealth?.LastCause ?? DeathCause.Unknown;
		int index = Array.IndexOf(Causes, cause);
		_roomDamage[index] += amount;
		_totalDamage[index] += amount;
	}

	private void OnHealthChanged(float current, float maximum)
	{
		if (_finished)
		{
			return;
		}

		if (current > _previousHealth + 0.0001f)
		{
			float restored = current - _previousHealth;
			_roomHealed += restored;
			_totalHealed += restored;
		}

		_previousHealth = current;
	}

	private void OnItemCollected(ItemDefinition item)
	{
		if (_finished)
		{
			return;
		}

		_roomItems.Add(item.Id);
		_allItems.Add(item.Id);
	}

	private void OnDied()
	{
		if (_finished)
		{
			return;
		}

		_died = true;
		_deathCause = _playerHealth?.LastCause ?? DeathCause.Unknown;
		Finish(ResultDeath);
	}

	private void OnRunCompleted()
	{
		Finish(ResultVictory);
	}

	// Escribe la fila de la sala en la que está Noah (si hay) y la deja sin sala.
	private void CloseRoomRow()
	{
		if (!_hasRoom)
		{
			return;
		}

		WriteRow(Row("SALA", _roomCell, _roomType, _healthOnEntry, _health.CurrentHealth, _roomItems, _roomHealed, _roomDamage,
			_roomSeconds, "", "", null, null));
		_hasRoom = false;
	}

	/// <summary>Escribe la fila FIN y cierra el archivo. Solo la primera llamada hace algo.</summary>
	public void Finish(string result)
	{
		if (_finished)
		{
			return;
		}

		_finished = true;
		if (_health != null)
		{
			_health.Damaged -= OnDamaged;
			_health.HealthChanged -= OnHealthChanged;
			_health.Died -= OnDied;
		}

		if (_items != null)
		{
			_items.ItemCollected -= OnItemCollected;
		}

		if (_rooms != null)
		{
			_rooms.RoomLoaded -= OnRoomLoaded;
		}

		if (_run != null)
		{
			_run.RunCompleted -= OnRunCompleted;
		}

		if (_file == null)
		{
			return;
		}

		try
		{
			CloseRoomRow();
			string cause = result == ResultDeath && _died ? _deathCause.ToString() : "";
			WriteRow(Row("FIN", "", "", null, _health?.CurrentHealth, _allItems, _totalHealed, _totalDamage, _totalSeconds, result, cause,
				_healthAtBoss, _healthAtBoss != null ? _bossSeconds : null));
		}
		finally
		{
			_file.Close();
			_file.Dispose();
			_file = null;
		}
	}

	private string Row(string kind, string cell, string roomType, float? healthIn, float? healthOut, List<string> items, float healed,
		float[] damage, float seconds, string result, string cause, float? healthAtBoss, float? bossSeconds)
	{
		StringBuilder row = new();
		row.Append(kind).Append(',').Append(_difficulty).Append(',').Append(_seed).Append(',').Append(_generator).Append(',')
			.Append(cell).Append(',').Append(roomType).Append(',').Append(Number(healthIn)).Append(',').Append(Number(healthOut)).Append(',')
			.Append(string.Join('+', items)).Append(',').Append(Number(healed));
		foreach (float value in damage)
		{
			row.Append(',').Append(Number(value));
		}

		row.Append(',').Append(Number(seconds, "0.0")).Append(',').Append(result).Append(',').Append(cause).Append(',')
			.Append(Number(healthAtBoss)).Append(',').Append(Number(bossSeconds, "0.0"));
		return row.ToString();
	}

	private static string Number(float? value, string format = "0.##")
	{
		return value == null ? "" : value.Value.ToString(format, CultureInfo.InvariantCulture);
	}

	private void WriteRow(string row)
	{
		_file.StoreLine(row);
		_file.Flush();
	}
}
