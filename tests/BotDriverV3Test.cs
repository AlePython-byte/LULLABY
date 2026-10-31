using System;
using System.Threading.Tasks;
using Godot;

/// <summary>Dos encuentros reales breves y cancelacion; sin ejecutar la prueba de fidelidad.</summary>
public partial class BotDriverV3Test : Node
{
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			foreach (EnemyKind kind in new[] { EnemyKind.Runner, EnemyKind.Digger }) await Encounter(kind);
			GD.Print($"BotDriverV3Test: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private async Task Encounter(EnemyKind kind)
	{
		LevelMap map = new() { Rooms = new Godot.Collections.Array<RoomData> { new()
		{
			Type = RoomType.Start, Size = new Vector2(26, 26),
		}, new()
		{
			Cell = Vector2I.Right, Type = RoomType.Boss, Size = new Vector2(26, 26), EnemyScene = EnemyKinds.Scene(kind),
			SpawnPoints = new[] { Vector2.Zero }, SpawnKinds = new[] { (int)kind },
		} } };
		Check(LevelValidator.Validate(map).Count == 0, "Encuentro valido antes de cargar escena");
		GameplayParametersV3 parameters = GameplayParametersV3.FromScenes();
		SimLevelV3 level = SimLevelV3.From(map, parameters);
		RunManager.SetNextMap(map);
		Node realLevel = GD.Load<PackedScene>(GameplayParametersV3.LevelScenePath).Instantiate();
		RoomManager manager = realLevel.GetNode<RoomManager>("RoomManager");
		manager.BossScene = null;
		manager.DecorEnabled = false;
		realLevel.GetNode<TreasureItemSpawner>("TreasureItemSpawner").Enabled = false;
		HealthComponent.FindIn(realLevel.GetNode("Player")).MaxHealth = 1.0e9f;
		AddChild(realLevel);
		BotDriverV3 cancelled = new();
		AddChild(cancelled);
		cancelled.Begin(manager, level, new BotBrainV3(level, new BotSettings(), 42), parameters.PlayerMaxHealth, 60);
		for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(Input.IsActionPressed(PlayerController.MoveRight), "El controlador aplica movimiento real antes de cancelar");
		cancelled.Free();
		Check(!Input.IsActionPressed(PlayerController.MoveRight), "Cancelar libera las entradas");
		BotDriverV3 driver = new();
		AddChild(driver);
		TaskCompletionSource<SimulationResultV3> finished = new();
		driver.Finished += result => finished.TrySetResult(result);
		driver.Begin(manager, level, new BotBrainV3(level, new BotSettings(), 42), parameters.PlayerMaxHealth, 60);
		SimulationResultV3 result = await finished.Task;
		Check(!result.TimedOut, $"Bot despeja sala real con {kind}");
		Check(result.Visits.Count == 2 && result.Visits[1].ClearTime >= 0, "Registra visita, transicion y despeje real");
		Check(!Input.IsActionPressed(PlayerController.MoveLeft) && !Input.IsActionPressed(PlayerController.MoveRight)
			&& !Input.IsActionPressed(PlayerController.MoveUp) && !Input.IsActionPressed(PlayerController.MoveDown), "Libera movimiento al terminar");
		foreach (string action in PlayerShooter.Actions) Check(!Input.IsActionPressed(action), "Libera disparo al terminar");
		driver.QueueFree();
		realLevel.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		GD.Print($"Control real {kind}: sala despejada y entradas liberadas.");
	}
}
