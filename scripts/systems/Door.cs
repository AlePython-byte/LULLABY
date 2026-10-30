using Godot;

public partial class Door : StaticBody3D
{
	[Export] public Color ClosedColor { get; set; } = new Color(0.5f, 0.1f, 0.1f, 1.0f);
	[Export] public Color OpenColor { get; set; } = new Color(0.1f, 0.35f, 0.15f, 0.2f);

	public bool IsOpen { get; private set; }

	private MeshInstance3D _mesh;
	private CollisionShape3D _collision;
	private StandardMaterial3D _material;
	private Node3D _skin;

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

	/// <summary>
	/// Visual de la puerta cerrada (los barrotes de la ambientación): cuelga de la puerta, se muestra mientras está cerrada y se
	/// oculta al abrirla. Sustituye a la caja provisional, que deja de verse. Solo es visual: la colisión no cambia.
	/// </summary>
	public Node3D Skin => _skin;

	public void AttachSkin(Node3D skin)
	{
		_skin = skin;
		AddChild(skin);
		_mesh.Visible = false;
		skin.Visible = !IsOpen;
	}

	public void SetOpen(bool open)
	{
		IsOpen = open;
		if (_skin != null)
		{
			_skin.Visible = !open;
		}

		_collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, open);
		_material.AlbedoColor = open ? OpenColor : ClosedColor;
	}
}
