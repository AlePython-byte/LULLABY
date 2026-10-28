using Godot;

public partial class Door : StaticBody3D
{
	[Export] public Color ClosedColor { get; set; } = new Color(0.5f, 0.1f, 0.1f, 1.0f);
	[Export] public Color OpenColor { get; set; } = new Color(0.1f, 0.35f, 0.15f, 0.2f);

	public bool IsOpen { get; private set; }

	private MeshInstance3D _mesh;
	private CollisionShape3D _collision;
	private StandardMaterial3D _material;

	public override void _Ready()
	{
		_mesh = GetNode<MeshInstance3D>("Mesh");
		_collision = GetNode<CollisionShape3D>("CollisionShape3D");

		_material = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
		_mesh.MaterialOverride = _material;
	}

	// Mesh y colisión se crean nuevos por puerta para no compartir recursos entre instancias.
	public void Configure(Vector3 size)
	{
		_mesh.Mesh = new BoxMesh { Size = size };
		_collision.Shape = new BoxShape3D { Size = size };
	}

	public void SetOpen(bool open)
	{
		IsOpen = open;
		_collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, open);
		_material.AlbedoColor = open ? OpenColor : ClosedColor;
	}
}
