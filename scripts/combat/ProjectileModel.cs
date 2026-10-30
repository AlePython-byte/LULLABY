using Godot;

/// <summary>
/// Modelo 3D de un proyectil (nodo hijo "Model" del proyectil). Lo carga centrado en su caja y oculta la figura provisional
/// hermana ("Mesh") solo si el archivo existe. Es solo visual: no toca la colisión ni el movimiento.
/// </summary>
public partial class ProjectileModel : Node3D
{
	[Export(PropertyHint.File, "*.glb")] public string ModelPath { get; set; } = "";

	/// <summary>Dimensión mayor del modelo en metros (0 = tamaño original).</summary>
	[Export] public float FitSize { get; set; }

	public bool HasModel { get; private set; }

	public override void _Ready()
	{
		Node3D model = ModelLibrary.Create(ModelPath, ModelLibrary.Anchor.Center, FitSize);
		if (model == null)
		{
			return;
		}

		AddChild(model);
		HasModel = true;
		if (GetParent()?.GetNodeOrNull<Node3D>("Mesh") is Node3D provisional)
		{
			provisional.Visible = false;
		}
	}

	/// <summary>Alinea el eje largo del modelo con la dirección de vuelo, con la punta (+Y del modelo) hacia atrás.</summary>
	public void Orient(Vector3 direction)
	{
		direction.Y = 0.0f;
		if (direction.LengthSquared() < 0.0001f)
		{
			return;
		}

		Basis = Basis.LookingAt(direction.Normalized(), Vector3.Up) * new Basis(Vector3.Right, Mathf.Pi / 2.0f);
	}
}
