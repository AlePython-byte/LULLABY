using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba del minimapa. Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/MinimapTest.tscn
///
///   1. Modelo con un mapa conocido: estado de cada sala (actual, visitada, vecina, oculta), iconos de tesoro y jefe solo en salas
///      vecinas o visitadas, conexiones solo con salas visibles y una sola vez, y celdas que no existen.
///   2. Vista: iconos a escala entera con filtro Nearest, tamaño pequeño y expandido (ajuste automático con tope de 512), y dibujo
///      por código si faltan los iconos.
///   3. Nivel real (generadores v1, v2 y v3): el estado sigue al jugador sala a sala hasta la del jefe y coincide con lo que se calcula
///      aparte a partir del mapa; el minimapa solo lee (el mapa no cambia).
///   4. Posición y transparencia: pequeño arriba a la derecha sin pisar el resto de HUD; expandido centrado y más opaco; mantener y
///      soltar la acción lo agranda y lo devuelve a pequeño sin pausar el juego; la pausa lo devuelve a pequeño.
///   5. Reintentar y volver al menú: el minimapa nuevo empieza solo con la sala de inicio y no quedan nodos huérfanos ni acumulados.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class MinimapTest : Node
{
	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== MinimapTest ===");
		try
		{
			Model();
			Views();
			foreach (int version in new[] { 1, 2, 3 })
			{
				await RealLevel(version);
			}

			await Placement();
			await Retry();
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			_failures.Add("excepción: " + exception.Message);
		}
		finally
		{
			Input.ActionRelease(Minimap.ExpandAction);
			GetTree().Paused = false;
		}

		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		await BossTestHarness.CollectWrappers(GetTree());
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	// --- 1 ------------------------------------------------------------------------------------------------------------

	private static LevelMap SampleMap()
	{
		//        x=0   x=1   x=2   x=3
		//  y=-2          B
		//  y=-1          E
		//  y= 0   S      A     T     C
		//  y= 1   D
		LevelMap map = new() { StartCell = new Vector2I(0, 0) };
		void Add(int x, int y, RoomType type) => map.Rooms.Add(new RoomData { Cell = new Vector2I(x, y), Type = type });
		Add(0, 0, RoomType.Start);
		Add(1, 0, RoomType.Normal);
		Add(2, 0, RoomType.Treasure);
		Add(3, 0, RoomType.Normal);
		Add(0, 1, RoomType.Normal);
		Add(1, -1, RoomType.Normal);
		Add(1, -2, RoomType.Boss);
		return map;
	}

	private void Model()
	{
		GD.Print("\n--- Modelo ---");
		LevelMap map = SampleMap();
		MinimapModel model = new(map);
		Vector2I S = new(0, 0), A = new(1, 0), T = new(2, 0), C = new(3, 0), D = new(0, 1), E = new(1, -1), B = new(1, -2);

		Check("al empezar todas las salas están ocultas y no hay nada que dibujar", map.Rooms.All(r => model.StateOf(r.Cell) == RoomVisibility.Hidden) && !model.VisibleCells.Any() && !model.Connections().Any(), "algo visible");

		model.Enter(S);
		Check("entrar en la sala de inicio: es la actual", model.StateOf(S) == RoomVisibility.Current && model.Current == S, "inicio");
		Check("sus salas contiguas aparecen como vecinas", model.StateOf(A) == RoomVisibility.Neighbor && model.StateOf(D) == RoomVisibility.Neighbor, "vecinas");
		Check("las demás siguen ocultas (incluidas las de tesoro y jefe)", new[] { T, C, E, B }.All(c => model.StateOf(c) == RoomVisibility.Hidden) && model.IconType(T) == null && model.IconType(B) == null, "ocultas");
		Check("se dibujan las conexiones del inicio con sus vecinas, y solo esas", model.Connections().Count() == 2 && model.Connections().All(c => c.A == S && (c.B == A || c.B == D)), "conexiones");

		model.Enter(A);
		Check("pasar a la sala siguiente: la anterior queda visitada y la nueva es la actual", model.StateOf(S) == RoomVisibility.Visited && model.StateOf(A) == RoomVisibility.Current, "estados");
		Check("aparecen como vecinas las contiguas a la nueva (tesoro y la de arriba), y la que ya era vecina sigue", model.StateOf(T) == RoomVisibility.Neighbor && model.StateOf(E) == RoomVisibility.Neighbor && model.StateOf(D) == RoomVisibility.Neighbor, "vecinas");
		Check("el icono de tesoro se ve en la vecina; el del jefe sigue oculto", model.IconType(T) == RoomType.Treasure && model.IconType(B) == null && model.StateOf(B) == RoomVisibility.Hidden, "iconos");
		Check("la sala a dos pasos de las visitadas sigue oculta", model.StateOf(C) == RoomVisibility.Hidden, "dos pasos");
		Check("la conexión entre dos visitadas se cuenta una vez; no hay conexiones con salas ocultas",
			model.Connections().Count(c => (c.A == S && c.B == A) || (c.A == A && c.B == S)) == 1 && model.Connections().All(c => model.StateOf(c.B) != RoomVisibility.Hidden), "conexiones");
		Check("hay cuatro conexiones (inicio-A, inicio-D, A-tesoro y A-E)", model.Connections().Count() == 4, $"{model.Connections().Count()}");

		model.Enter(E);
		Check("la vecina de la visitada E con el jefe lo enseña: icono de jefe visible", model.StateOf(B) == RoomVisibility.Neighbor && model.IconType(B) == RoomType.Boss, "jefe");
		model.Enter(B);
		Check("estar en el jefe: es la actual y conserva el icono", model.StateOf(B) == RoomVisibility.Current && model.IconType(B) == RoomType.Boss, "actual");
		Check("las visitadas con tesoro vecino siguen enseñándolo", model.StateOf(T) == RoomVisibility.Neighbor && model.IconType(T) == RoomType.Treasure, "tesoro");
		model.Enter(new Vector2I(9, 9));
		Check("una celda que no existe se ignora", model.Current == B, "celda inexistente");
		Rect2I bounds = model.VisibleBounds();
		Check("los límites cubren solo las salas visibles", bounds.Position == new Vector2I(0, -2) && bounds.End == new Vector2I(3, 2), $"{bounds}");
	}

	// --- 2 ------------------------------------------------------------------------------------------------------------

	private void Views()
	{
		GD.Print("\n--- Vista ---");
		MinimapView view = new();
		AddChild(view);
		Check("iconos con filtro Nearest, sin suavizado", view.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, "filtro");
		Check("pequeño: panel de 192 px, escala x1 y opacidad ~0,6", view.Size == new Vector2(192, 192) && view.IconScale == 1 && Mathf.Abs(view.Modulate.A - 0.6f) < 0.01f, $"{view.Size} x{view.IconScale} {view.Modulate.A}");

		// Pocas salas: expandido a escala x2 (icono de 64 px, 32 px de hueco para la puerta).
		MinimapModel few = new(SampleMap());
		few.Enter(new Vector2I(0, 0));
		view.Model = few;
		view.SetExpanded(true);
		Check("expandido con pocas salas: iconos x2 y panel ajustado entre 320 y 512 px", view.IconScale == 2 && view.Pitch == 96 && view.Size.X >= 320 && view.Size.X <= 512 && view.Size.Y <= 512, $"x{view.IconScale} {view.Size}");
		Check("la opacidad sube a ~0,92", Mathf.Abs(view.Modulate.A - 0.92f) < 0.01f, $"{view.Modulate.A}");
		Check("las escalas son enteras y los pasos pares (sin medios píxeles)", view.IconScale is 1 or 2 && view.Pitch % 2 == 0, $"{view.Pitch}");
		view.SetExpanded(false);
		Check("al soltar vuelve a pequeño", view.Size == new Vector2(192, 192) && Mathf.Abs(view.Modulate.A - 0.6f) < 0.01f, $"{view.Size}");

		// Muchas salas: no caben a x2 y se usa x1 con el tope de 512.
		LevelMap corridor = new();
		for (int x = 0; x < 9; x++)
		{
			corridor.Rooms.Add(new RoomData { Cell = new Vector2I(x, 0), Type = x == 0 ? RoomType.Start : RoomType.Normal });
		}

		MinimapModel many = new(corridor);
		for (int x = 0; x < 9; x++)
		{
			many.Enter(new Vector2I(x, 0));
		}

		view.Model = many;
		view.SetExpanded(true);
		Check("expandido con muchas salas: iconos x1 y nunca más de 512 px", view.IconScale == 1 && view.Size.X <= 512 && view.Size.Y <= 512, $"x{view.IconScale} {view.Size}");
		view.SetExpanded(false);

		// Iconos de David presentes y dibujo por código cuando faltan.
		string[] used = { MinimapView.CurrentIcon, MinimapView.VisitedIcon, MinimapView.UnvisitedIcon, MinimapView.DoorIcon, MinimapView.TreasureIcon, MinimapView.BossIcon };
		Check("con los iconos de assets/ui/ se usan los PNG", used.All(view.HasTexture), string.Join(",", used.Where(n => !view.HasTexture(n))));
		view.Free();

		string original = MinimapView.IconFolder;
		try
		{
			MinimapView.IconFolder = "res://tests/fixtures/none/";
			MinimapView fallback = new() { Model = few };
			AddChild(fallback);
			fallback.SetExpanded(true);
			fallback.QueueRedraw();
			Check("sin iconos usa el dibujo por código (ningún PNG) y no falla", used.All(name => !fallback.HasTexture(name)), "algún PNG cargó");
			fallback.Free();
		}
		finally
		{
			MinimapView.IconFolder = original;
		}

		Check("la tecla Tab figura en los controles del juego", GameText.ControlLines.Any(line => line.Contains("Tab")), "falta Tab");
		Check("la acción no usa ui_focus_next: es una acción propia con Tab", Minimap.ExpandAction == "minimapa_expandir" && Minimap.ExpandAction != "ui_focus_next", Minimap.ExpandAction);
	}

	// --- 3 ------------------------------------------------------------------------------------------------------------

	private async Task RealLevel(int version)
	{
		GD.Print($"\n--- Nivel real (generador v{version}) ---");
		SceneTree tree = GetTree();
		RunManager.SetNextGeneratorVersion(version);
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed + version);
		await BossTestHarness.Frames(tree, 4);
		Minimap minimap = level.Root.GetNode<Minimap>("Minimap");
		LevelMap map = level.Rooms.Map;
		string signature = string.Join("|", map.Rooms.Select(r => $"{r.Cell}{r.Type}{r.TemplateId}{r.Size}{string.Join(",", r.SpawnPoints)}"));

		HashSet<Vector2I> visited = new() { map.StartCell };
		bool consistent = Matches(minimap, map, visited, map.StartCell);
		Check($"v{version}: al empezar solo se ve la sala de inicio y sus vecinas", consistent && minimap.Model.StateOf(map.StartCell) == RoomVisibility.Current, "estado inicial");

		bool all = true;
		string detail = "";
		Vector2I current = map.StartCell;
		foreach (DoorSide side in BossTestHarness.PathToBoss(map))
		{
			await CrossAndSettle(level, side);
			current = level.Rooms.CurrentRoom.Data.Cell;
			visited.Add(current);
			bool ok = Matches(minimap, map, visited, current);
			all &= ok;
			if (!ok) detail = $"tras entrar en {current}";
		}

		Check($"v{version}: el estado sigue al jugador hasta la sala del jefe y coincide con el calculado aparte", all && level.Rooms.CurrentRoom.Data.Type == RoomType.Boss, detail);
		Check($"v{version}: en la sala del jefe, su icono es visible y es la sala actual", minimap.Model.IconType(current) == RoomType.Boss && minimap.Model.StateOf(current) == RoomVisibility.Current, "jefe");
		Check($"v{version}: el minimapa solo lee: el mapa no cambió", signature == string.Join("|", map.Rooms.Select(r => $"{r.Cell}{r.Type}{r.TemplateId}{r.Size}{string.Join(",", r.SpawnPoints)}")), "mapa alterado");
		await BossTestHarness.Close(tree, level);
	}

	// Cruza una puerta pidiéndolo a la sala (sin caminar). El motor entrega a veces un cruce en cola con un fotograma de retraso y la
	// sala nueva rebota a la anterior: si pasa, se reintenta hasta que la sala esperada se mantiene.
	private async Task CrossAndSettle(BossTestHarness.Level level, DoorSide side)
	{
		SceneTree tree = GetTree();
		Vector2I expected = level.Rooms.CurrentRoom.Data.Cell + side.GridOffset();
		for (int attempt = 0; attempt < 4; attempt++)
		{
			level.Rooms.CurrentRoom.EmitSignal(RoomController.SignalName.DoorCrossed, (int)side);
			for (int frame = 0; frame < 240; frame++)
			{
				await BossTestHarness.Frames(tree, 1);
				if (!level.Rooms.IsTransitioning && level.Player.IsPhysicsProcessing() && level.Rooms.CurrentRoom.Data.Cell == expected)
				{
					break;
				}
			}

			await BossTestHarness.Frames(tree, 8);
			if (level.Rooms.CurrentRoom.Data.Cell == expected)
			{
				return;
			}

			// Rebotó: se vuelve a pedir desde donde se esté (la sala anterior, que ya está despejada o es la misma).
			if (level.Rooms.CurrentRoom.Data.Cell + side.Opposite().GridOffset() == expected)
			{
				side = side.Opposite().Opposite();
			}
		}

		throw new InvalidOperationException($"no se pudo cruzar hacia {expected}");
	}

	// Lo esperado, calculado sin usar el modelo: visitadas + celdas contiguas existentes; el tesoro y el jefe solo si son visibles.
	private bool Matches(Minimap minimap, LevelMap map, HashSet<Vector2I> visited, Vector2I current)
	{
		Dictionary<Vector2I, RoomData> rooms = map.Rooms.ToDictionary(r => r.Cell);
		HashSet<Vector2I> expectedVisible = new(visited);
		foreach (Vector2I cell in visited)
		{
			foreach (DoorSide side in Enum.GetValues<DoorSide>())
			{
				if (rooms.ContainsKey(cell + side.GridOffset()))
				{
					expectedVisible.Add(cell + side.GridOffset());
				}
			}
		}

		MinimapModel model = minimap.Model;
		bool ok = model != null && model.VisibleCells.ToHashSet().SetEquals(expectedVisible) && model.Current == current;
		foreach (RoomData room in map.Rooms)
		{
			RoomVisibility expected = room.Cell == current ? RoomVisibility.Current : visited.Contains(room.Cell) ? RoomVisibility.Visited : expectedVisible.Contains(room.Cell) ? RoomVisibility.Neighbor : RoomVisibility.Hidden;
			ok &= model != null && model.StateOf(room.Cell) == expected;
			bool special = room.Type == RoomType.Treasure || room.Type == RoomType.Boss;
			ok &= model != null && (model.IconType(room.Cell) != null) == (special && expected != RoomVisibility.Hidden);
		}

		return ok;
	}

	// --- 4 ------------------------------------------------------------------------------------------------------------

	private async Task Placement()
	{
		GD.Print("\n--- Posición, transparencia y expansión ---");
		SceneTree tree = GetTree();
		GetWindow().Size = new Vector2I(1152, 648); // la ventana de juego (en headless puede nacer con otra altura)
		// Con la vida real de Noah (5 corazones): con vida ilimitada la fila de corazones llegaría al tope de 24 y cruzaría el minimapa.
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		await BossTestHarness.Frames(tree, 4);
		Minimap minimap = level.Root.GetNode<Minimap>("Minimap");
		MinimapView view = minimap.View;
		Vector2 screen = level.Root.GetViewport().GetVisibleRect().Size;

		Rect2 small = view.GetGlobalRect();
		Check($"pequeño: arriba a la derecha ({small.Position} tamaño {small.Size}) dentro de la pantalla {screen}", small.Size == new Vector2(192, 192) && small.End.X <= screen.X && small.Position.Y >= 0.0f && small.End.X > screen.X - 24.0f, $"{small}");
		Check("pequeño: semitransparente (~60 %)", Mathf.Abs(view.Modulate.A - 0.6f) < 0.01f, $"{view.Modulate.A}");

		Dictionary<string, Control> others = new()
		{
			["HealthHud"] = level.Root.GetNode("HealthHud").GetChildren().OfType<Control>().First(),
			["ItemsHud"] = level.Root.GetNode<Control>("ItemsHud/Label"),
			["BossHud"] = level.Root.GetNode<Control>("BossHud/Panel"),
			["ItemMessageHud"] = level.Root.GetNode<Control>("ItemMessageHud/Box"),
			["SeedHud"] = level.Root.GetNode<Control>("SeedHud/Label"),
		};
		foreach ((string name, Control control) in others)
		{
			Rect2 other = control.GetGlobalRect();
			Check($"el minimapa pequeño no pisa {name} ({other.Position} a {other.End})", !small.Intersects(other), $"{small} con {other}");
		}

		Check("el minimapa está por encima de los HUD y por debajo del fundido, la pausa y las pantallas finales", minimap.Layer > 1 && minimap.Layer < level.Root.GetNode<CanvasLayer>("ScreenFade").Layer
			&& minimap.Layer < level.Root.GetNode<CanvasLayer>("PauseMenu").Layer && minimap.Layer < level.Root.GetNode<CanvasLayer>("VictoryScreen").Layer && minimap.Layer < level.Root.GetNode<CanvasLayer>("DefeatScreen").Layer, $"capa {minimap.Layer}");

		// Mantener la acción expande; soltar la devuelve a pequeño; el juego no se pausa.
		Check("la acción minimapa_expandir existe con Tab y sin usar ui_focus_next", InputMap.HasAction(Minimap.ExpandAction) && InputMap.ActionGetEvents(Minimap.ExpandAction).OfType<InputEventKey>().Any(e => e.PhysicalKeycode == Key.Tab), "acción");
		Input.ActionPress(Minimap.ExpandAction);
		await BossTestHarness.Frames(tree, 3);
		Rect2 big = view.GetGlobalRect();
		Vector2 center = big.GetCenter();
		Check("al mantener la acción se expande, centrado, y más grande", minimap.IsExpanded && big.Size.X > small.Size.X && big.Size.X <= 512 && big.Size.Y <= 512 && center.DistanceTo(screen / 2.0f) <= 1.5f, $"{big} centro {center}");
		Check("más opaco (~92 %) y el juego NO se pausa", Mathf.Abs(view.Modulate.A - 0.92f) < 0.01f && !tree.Paused && level.Player.IsPhysicsProcessing(), $"alfa {view.Modulate.A}, pausa {tree.Paused}");
		Input.ActionRelease(Minimap.ExpandAction);
		await BossTestHarness.Frames(tree, 3);
		Check("al soltar vuelve a pequeño", !minimap.IsExpanded && view.GetGlobalRect().Size == small.Size && Mathf.Abs(view.Modulate.A - 0.6f) < 0.01f, $"{view.GetGlobalRect()}");

		// Pausa: vuelve a pequeño aunque se mantenga la tecla, y no reacciona mientras dura.
		Input.ActionPress(Minimap.ExpandAction);
		await BossTestHarness.Frames(tree, 2);
		tree.Paused = true;
		await BossTestHarness.Frames(tree, 3);
		Check("al pausar con el mapa expandido vuelve a pequeño y no reacciona a la tecla", !minimap.IsExpanded, "sigue expandido en pausa");
		tree.Paused = false;
		Input.ActionRelease(Minimap.ExpandAction);
		await BossTestHarness.Frames(tree, 2);
		await BossTestHarness.Close(tree, level);
	}

	// --- 5 ------------------------------------------------------------------------------------------------------------

	private async Task Retry()
	{
		GD.Print("\n--- Reintentar y volver al menú ---");
		SceneTree tree = GetTree();
		Node testScene = this;
		BossTestHarness.Level level = BossTestHarness.OpenAsCurrent(tree, BossTestHarness.Seed);
		await BossTestHarness.Frames(tree, 4);
		await BossTestHarness.GoToBoss(tree, level);
		Minimap first = level.Root.GetNode<Minimap>("Minimap");
		Check("tras recorrer el nivel hay varias salas visitadas", first.Model.VisibleCells.Count(c => first.Model.StateOf(c) is RoomVisibility.Visited or RoomVisibility.Current) > 2, "pocas visitadas");
		int nodesBefore = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		long orphansBefore = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

		for (int round = 1; round <= 3; round++)
		{
			level.Run.StartNewRun();
			Node next = await BossTestHarness.WaitForScene(tree, level.Root, GameSettings.LevelScene);
			Minimap minimap = next.GetNode<Minimap>("Minimap");
			RoomManager rooms = next.GetNode<RoomManager>("RoomManager");
			MinimapModel model = minimap.Model;
			Check($"reintento {round}: el minimapa viejo se liberó y el nuevo no lo conoce", !GodotObject.IsInstanceValid(first) && model != null && model.Map == rooms.Map, "residuo del anterior");
			Check($"reintento {round}: solo la sala de inicio está visitada, y es la actual", model.Current == rooms.Map.StartCell && model.VisibleCells.Count(c => model.StateOf(c) is RoomVisibility.Visited or RoomVisibility.Current) == 1 && !minimap.IsExpanded, "estado tras reintentar");
			first = minimap;
			level = new BossTestHarness.Level { Root = next, Rooms = rooms, Run = next.GetNode<RunManager>("RunManager"), Player = next.GetNode<PlayerController>("Player") };
			level.Health = HealthComponent.FindIn(level.Player);
			await BossTestHarness.Frames(tree, 4);
		}

		int nodesAfter = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		long orphansAfter = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
		Check($"tras tres reintentos no se acumulan nodos ({nodesBefore} antes, {nodesAfter} después) ni huérfanos ({orphansBefore} → {orphansAfter})", Mathf.Abs(nodesAfter - nodesBefore) <= 40 && orphansAfter <= orphansBefore, "acumulación");

		// Volver al menú libera el nivel con su minimapa.
		Minimap beforeMenu = level.Root.GetNode<Minimap>("Minimap");
		level.Run.ReturnToMenu();
		Node menu = await BossTestHarness.WaitForScene(tree, level.Root, GameSettings.MenuScene);
		Check("al volver al menú el minimapa se libera", !GodotObject.IsInstanceValid(beforeMenu), "minimapa vivo en el menú");
		menu.QueueFree();
		await tree.ToSignal(menu, Node.SignalName.TreeExited);
		tree.Paused = false;
		tree.CurrentScene = testScene;
		await BossTestHarness.Frames(tree, 2);
	}

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  {(ok ? "OK   " : "FALLO")} {name}" + (ok ? "" : $" ({detail})"));
		if (!ok)
		{
			_failures.Add(name);
		}
	}
}
