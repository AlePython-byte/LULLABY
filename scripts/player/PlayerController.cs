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

		InputActions.Register(MoveLeft, Key.A);
		InputActions.Register(MoveRight, Key.D);
		InputActions.Register(MoveUp, Key.W);
		InputActions.Register(MoveDown, Key.S);
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

	// Orienta el cubo de inmediato (por ejemplo, al aparecer en una sala nueva).
	public void FaceDirection(Vector3 direction)
	{
		if (direction.LengthSquared() < 0.0001f)
		{
			return;
		}

		_mesh.Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.X, -direction.Z), 0.0f);
	}
}
