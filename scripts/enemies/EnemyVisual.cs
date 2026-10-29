using System.Collections.Generic;
using Godot;

/// <summary>
/// Parte gráfica de un enemigo (nodo hijo "Visual"). La lógica solo usa este contrato (ApplyKind y SetHighlight), nunca
/// las mallas concretas: para poner el modelo final basta con sustituir lo que cuelga de "Visual" y, si hace falta,
/// heredar de esta clase para reproducir los efectos con animaciones.
///
/// Mientras UseKindColor esté activo, todas las mallas bajo "Visual" se pintan con el color provisional del tipo
/// (EnemyKinds). SetHighlight sirve de aviso (el Tirador antes de disparar, el Explosivo durante la mecha).
/// </summary>
public partial class EnemyVisual : Node3D
{
	[Export] public bool UseKindColor { get; set; } = true;
	[Export] public Color HighlightColor { get; set; } = new(1.0f, 0.95f, 0.75f);

	private readonly List<MeshInstance3D> _meshes = new();
	private StandardMaterial3D _material;
	private Color _baseColor;

	public override void _Ready()
	{
		Collect(this);
	}

	public virtual void ApplyKind(EnemyKind kind)
	{
		if (!UseKindColor)
		{
			return;
		}

		_baseColor = EnemyKinds.Get(kind).Color;
		_material = new StandardMaterial3D { AlbedoColor = _baseColor, Emission = HighlightColor };
		foreach (MeshInstance3D mesh in _meshes)
		{
			mesh.MaterialOverride = _material;
		}
	}

	public virtual void SetHighlight(bool on)
	{
		if (_material == null)
		{
			return;
		}

		_material.AlbedoColor = on ? HighlightColor : _baseColor;
		_material.EmissionEnabled = on;
	}

	private void Collect(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mesh)
			{
				_meshes.Add(mesh);
			}

			Collect(child);
		}
	}
}
