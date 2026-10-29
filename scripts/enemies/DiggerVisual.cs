using Godot;

/// <summary>
/// Parte gráfica del Excavador: el cuerpo (nodo hijo "Body") se hunde en la tierra y en su lugar queda un montículo
/// (nodo hijo "Mound"), de color tierra y no del color del tipo. La lógica solo llama a SetBurial (0 = fuera del todo,
/// 1 = enterrado del todo); el resto es provisional y se sustituirá por el modelo final (digger_enemy.glb).
/// </summary>
public partial class DiggerVisual : EnemyVisual
{
	[Export] public Color MoundColor { get; set; } = new(0.36f, 0.26f, 0.16f);
	[Export] public float SinkDepth { get; set; } = 1.0f;

	private Node3D _body;
	private Node3D _mound;
	private Vector3 _bodyRest;

	public override void _Ready()
	{
		base._Ready();
		_body = GetNodeOrNull<Node3D>("Body");
		_mound = GetNodeOrNull<Node3D>("Mound");
		_bodyRest = _body?.Position ?? Vector3.Zero;
		SetBurial(0.0f);
	}

	public override void ApplyKind(EnemyKind kind)
	{
		base.ApplyKind(kind);
		if (_mound is MeshInstance3D mound)
		{
			mound.MaterialOverride = new StandardMaterial3D { AlbedoColor = MoundColor };
		}
	}

	/// <summary>0 = el cuerpo fuera y sin montículo; 1 = el cuerpo oculto bajo tierra y el montículo entero.</summary>
	public void SetBurial(float amount)
	{
		amount = Mathf.Clamp(amount, 0.0f, 1.0f);
		if (_body != null)
		{
			_body.Position = _bodyRest + Vector3.Down * (SinkDepth * amount);
			_body.Visible = amount < 0.999f;
		}

		if (_mound != null)
		{
			_mound.Visible = amount > 0.001f;
			_mound.Scale = new Vector3(1.0f, Mathf.Max(amount, 0.001f), 1.0f);
		}
	}
}
