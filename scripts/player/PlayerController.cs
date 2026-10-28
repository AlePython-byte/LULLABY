using Godot;

public partial class PlayerController : CharacterBody3D
{
	private const string MoveLeft = "move_left";
	private const string MoveRight = "move_right";
	private const string MoveUp = "move_up";
	private const string MoveDown = "move_down";

	[Export] public float Speed { get; set; } = 6.0f;
	[Export] public float TurnSpeed { get; set; } = 12.0f;

	private Node3D _mesh;

	public override void _Ready()
	{
		_mesh = GetNode<Node3D>("Mesh");

		RegisterAction(MoveLeft, Key.A, Key.Left);
		RegisterAction(MoveRight, Key.D, Key.Right);
		RegisterAction(MoveUp, Key.W, Key.Up);
		RegisterAction(MoveDown, Key.S, Key.Down);
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector2 input = Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown);
		Vector3 direction = new Vector3(input.X, 0.0f, input.Y);

		Velocity = direction * Speed;
		MoveAndSlide();

		if (direction != Vector3.Zero)
		{
			// Mira hacia -Z cuando el yaw es 0.
			float targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
			float yaw = Mathf.LerpAngle(_mesh.Rotation.Y, targetYaw, TurnSpeed * (float)delta);
			_mesh.Rotation = new Vector3(0.0f, yaw, 0.0f);
		}
	}

	private static void RegisterAction(string action, params Key[] keys)
	{
		if (!InputMap.HasAction(action))
		{
			InputMap.AddAction(action);
		}

		foreach (Key key in keys)
		{
			InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
		}
	}
}
