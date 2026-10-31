using Godot;

/// <summary>
/// Minimapa del nivel: arriba a la derecha, pequeño y semitransparente; se expande mientras se mantiene Tab (acción
/// "minimapa_expandir", que no usa ui_focus_next) y vuelve a pequeño al soltar. El juego NO se pausa. SOLO LEE el nivel: el mapa
/// (RoomManager.Map) y las salas que se van cargando (RoomManager.RoomLoaded), de donde saca las salas visitadas. No crea nodos
/// por sala. Al reintentar se recarga la escena y el minimapa nace de nuevo, sin residuos.
///
/// Procesa siempre para poder volver a pequeño si se pausa con el mapa expandido; con el juego en pausa ignora Tab. Su capa (2) queda
/// por encima de los HUD y por debajo del fundido, la pausa y las pantallas de victoria y derrota.
/// </summary>
public partial class Minimap : CanvasLayer
{
	public const string ExpandAction = "minimapa_expandir";

	// Un StringName propio: pasar el texto a Input.IsActionPressed crearía uno nuevo (y su basura) en cada fotograma.
	private static readonly StringName ExpandActionName = new(ExpandAction);

	[Export] public RoomManager Rooms { get; set; }

	public MinimapView View { get; private set; }

	public MinimapModel Model => View?.Model;

	public bool IsExpanded => View != null && View.Expanded;

	public override void _Ready()
	{
		InputActions.Register(ExpandAction, Key.Tab);
		View = GetNode<MinimapView>("View");
		if (Rooms == null)
		{
			return;
		}

		Rooms.RoomLoaded += OnRoomLoaded;
		// Si el mapa ya empezó (el minimapa se creó después de RunManager), se recoge la sala actual.
		if (IsInstanceValid(Rooms.CurrentRoom))
		{
			OnRoomLoaded(Rooms.CurrentRoom);
		}
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Rooms))
		{
			Rooms.RoomLoaded -= OnRoomLoaded;
		}
	}

	public override void _Process(double delta)
	{
		bool want = !GetTree().Paused && Input.IsActionPressed(ExpandActionName);
		if (want != View.Expanded)
		{
			View.SetExpanded(want);
		}
	}

	private void OnRoomLoaded(RoomController room)
	{
		if (Rooms.Map == null || !IsInstanceValid(room))
		{
			return;
		}

		// Un mapa distinto (otra partida) empieza de cero.
		if (View.Model == null || View.Model.Map != Rooms.Map)
		{
			View.Model = new MinimapModel(Rooms.Map);
		}

		View.Model.Enter(room.Data.Cell);
		View.Relayout();
	}
}
