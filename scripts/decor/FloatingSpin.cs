using Godot;

/// <summary>Flotación vertical y giro suave de un nodo (los objetos del tesoro). Solo mueve su propio nodo; la pausa lo detiene.</summary>
public partial class FloatingSpin : Node3D
{
	[Export] public float BobHeight { get; set; } = 0.08f;
	[Export] public float BobSpeed { get; set; } = 2.0f;
	[Export] public float SpinSpeed { get; set; } = 0.9f;

	private float _baseY;
	private float _time;

	public override void _Ready()
	{
		_baseY = Position.Y;
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		Position = Position with { Y = _baseY + Mathf.Sin(_time * BobSpeed) * BobHeight };
		Rotation = new Vector3(0.0f, Rotation.Y + SpinSpeed * (float)delta, 0.0f);
	}
}
