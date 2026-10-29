using Godot;

/// <summary>Efecto visual provisional de una explosión: una esfera que crece hasta Radius y se desvanece. Sin lógica.</summary>
public partial class ExplosionEffect : Node3D
{
	[Export] public float Radius { get; set; } = 2.5f;
	[Export] public float Duration { get; set; } = 0.3f;

	private MeshInstance3D _mesh;
	private StandardMaterial3D _material;
	private float _age;

	public override void _Ready()
	{
		_mesh = GetNode<MeshInstance3D>("Mesh");
		_material = (StandardMaterial3D)_mesh.MaterialOverride.Duplicate();
		_mesh.MaterialOverride = _material;
	}

	public override void _Process(double delta)
	{
		_age += (float)delta;
		float progress = Mathf.Min(1.0f, _age / Duration);
		_mesh.Scale = Vector3.One * Mathf.Lerp(0.2f, Radius, progress);
		Color color = _material.AlbedoColor;
		_material.AlbedoColor = color with { A = 0.6f * (1.0f - progress) };

		if (progress >= 1.0f)
		{
			QueueFree();
		}
	}
}
