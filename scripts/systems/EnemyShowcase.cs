using System;
using System.Linq;
using Godot;

/// <summary>
/// Escena de exhibición (scenes/levels/EnemyShowcase.tscn): una sala cerrada con un enemigo de cada tipo de EnemyKinds,
/// para probarlos a mano. Usa una sala real (RoomController con SpawnKinds), así que también prueba la aparición por tipo.
/// El jugador tiene PlayerMaxHealth de vida para poder probar sin morir. R reinicia la escena.
/// </summary>
public partial class EnemyShowcase : Node3D
{
	private const string RestartAction = "showcase_restart";

	[Export] public PackedScene PlayerScene { get; set; }
	[Export] public PackedScene HudScene { get; set; }
	[Export] public PackedScene RoomScene { get; set; }
	[Export] public RoomCamera Camera { get; set; }
	[Export] public float PlayerMaxHealth { get; set; } = 99.0f;
	[Export] public float RoomSide { get; set; } = 20.0f;

	public override void _Ready()
	{
		InputActions.Register(RestartAction, Key.R);

		int count = EnemyKinds.List.Count;
		float spacing = (RoomSide - 5.0f) / Math.Max(1, count - 1);
		RoomData data = new()
		{
			Cell = Vector2I.Zero,
			Type = RoomType.Normal,
			TemplateId = "exhibicion",
			Size = new Vector2(RoomSide, RoomSide),
			EnemyScene = EnemyKinds.Scene(EnemyKind.Chaser),
			SpawnPoints = Enumerable.Range(0, count)
				.Select(i => new Vector2(-(RoomSide - 5.0f) / 2.0f + i * spacing, -RoomSide / 2.0f + 4.0f)).ToArray(),
			SpawnKinds = EnemyKinds.List.Select(info => (int)info.Kind).ToArray(),
		};

		RoomController room = RoomScene.Instantiate<RoomController>();
		room.Setup(data, Array.Empty<DoorSide>(), false);
		AddChild(room);

		PlayerController player = PlayerScene.Instantiate<PlayerController>();
		HealthComponent.FindIn(player).MaxHealth = PlayerMaxHealth;
		player.Position = new Vector3(0.0f, 0.5f, RoomSide / 2.0f - 4.0f);
		AddChild(player);
		AddChild(HudScene.Instantiate());

		Camera.Frame(data.Size, room.WallHeight, room.WallThickness);
		room.Activate();
		GD.Print("EnemyShowcase: " + string.Join(", ", EnemyKinds.List.Select(info => info.Name)) + ". R reinicia.");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed(RestartAction))
		{
			GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
		}
	}
}
