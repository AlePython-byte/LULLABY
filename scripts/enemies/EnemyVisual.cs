using System.Collections.Generic;
using Godot;

/// <summary>
/// Parte gráfica de un enemigo (nodo hijo "Visual"). La lógica solo usa este contrato (ApplyKind y SetHighlight), nunca
/// las mallas concretas: para poner el modelo final basta con colgar de "Visual" la escena del modelo.
///
/// Dos modos, según UseKindColor:
///   - true (figuras provisionales): todas las mallas bajo "Visual" se pintan con UN material del color provisional del tipo
///     (EnemyKinds), que es también el que se enciende con los efectos.
///   - false (modelos con sus propios colores): no se pinta nada de más. Cada malla recibe una COPIA de sus materiales
///     (los materiales importados de un .glb se comparten entre todas las instancias de la escena: si se encendieran,
///     parpadearían todos los enemigos de ese tipo a la vez) y los efectos se muestran como emisión sobre esas copias, así
///     que se conservan los colores y detalles del modelo.
///
/// Efectos (mismo mecanismo en los dos modos):
///   - Aviso: SetHighlight(true/false). Lo usan el Tirador (aviso antes de disparar), el Explosivo (mecha parpadeante), la
///     embestida del Huidor y el Excavador. Se muestra con HighlightColor.
///   - Golpe: destello de HitFlashTime segundos (HitFlashColor) cuando el enemigo recibe daño.
/// El aviso y la mecha tienen prioridad: mientras están activos (WarningActive) el destello no se muestra ni se programa.
/// Cada llamada a SetHighlight, para encender o para apagar, marca el aviso como en curso durante WarningHold segundos. La
/// mecha del Explosivo la llama en cada fotograma, alternando (empieza apagada y parpadea cada BlinkInterval), así que
/// está activa de principio a fin sin huecos en los que colar el destello; el Tirador la llama al empezar y al acabar el
/// aviso, que entre medias está activo porque Highlighted es true.
/// El estado se puede consultar sin mirar los materiales (Highlighted, WarningActive, HitFlashing y Effect).
/// </summary>
public partial class EnemyVisual : Node3D
{
	/// <summary>Qué efecto se está dibujando ahora mismo.</summary>
	public enum VisualEffect
	{
		None,
		Warning,
		HitFlash,
	}

	[Export] public bool UseKindColor { get; set; } = true;
	[Export] public Color HighlightColor { get; set; } = new(1.0f, 0.95f, 0.75f);

	/// <summary>Intensidad de la emisión del aviso sobre los materiales de un modelo (UseKindColor = false).</summary>
	[Export] public float HighlightEnergy { get; set; } = 1.2f;

	[Export] public Color HitFlashColor { get; set; } = new(1.0f, 1.0f, 1.0f);
	[Export] public float HitFlashEnergy { get; set; } = 1.5f;
	[Export] public float HitFlashTime { get; set; } = 0.1f;

	/// <summary>Segundos que el aviso sigue contando como activo tras la última llamada a SetHighlight (debe cubrir el hueco del parpadeo de la mecha).</summary>
	[Export] public float WarningHold { get; set; } = 0.15f;

	private sealed class ModelMaterial
	{
		public BaseMaterial3D Material;
		public bool Enabled;
		public Color Color;
		public float Energy;
	}

	private readonly List<MeshInstance3D> _meshes = new();
	private readonly List<ModelMaterial> _model = new();
	private StandardMaterial3D _material;
	private Color _baseColor;
	private HealthComponent _health;
	private bool _highlight;
	private float _hold;
	private float _flash;

	/// <summary>Materiales propios de esta instancia del modelo (copias; vacío con UseKindColor = true).</summary>
	public IReadOnlyList<BaseMaterial3D> ModelMaterials
	{
		get
		{
			List<BaseMaterial3D> materials = new();
			foreach (ModelMaterial entry in _model)
			{
				materials.Add(entry.Material);
			}

			return materials;
		}
	}

	/// <summary>El estado pedido con SetHighlight: true = aviso o fase encendida de la mecha.</summary>
	public bool Highlighted => _highlight;

	/// <summary>Aviso o mecha en curso (incluye las fases apagadas del parpadeo): tiene prioridad sobre el destello.</summary>
	public bool WarningActive => _highlight || _hold > 0.0f;

	/// <summary>El destello de golpe se está mostrando (no hay aviso activo).</summary>
	public bool HitFlashing => !WarningActive && _flash > 0.0f;

	public VisualEffect Effect => _highlight ? VisualEffect.Warning : HitFlashing ? VisualEffect.HitFlash : VisualEffect.None;

	public override void _Ready()
	{
		Collect(this);
		if (!UseKindColor)
		{
			CopyModelMaterials();
		}

		Node owner = GetParent();
		_health = owner != null ? HealthComponent.FindIn(owner) : null;
		if (_health != null)
		{
			_health.Damaged += OnDamaged;
		}
	}

	public override void _ExitTree()
	{
		if (_health != null && IsInstanceValid(_health))
		{
			_health.Damaged -= OnDamaged;
		}
	}

	public override void _Process(double delta)
	{
		bool changed = false;
		if (_hold > 0.0f)
		{
			_hold -= (float)delta;
			changed = _hold <= 0.0f;
		}

		if (_flash > 0.0f)
		{
			_flash -= (float)delta;
			changed |= _flash <= 0.0f;
		}

		if (changed)
		{
			Refresh();
		}
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

		Refresh();
	}

	public virtual void SetHighlight(bool on)
	{
		_highlight = on;
		_hold = WarningHold; // encender o apagar significa que un aviso está en curso
		Refresh();
	}

	private void OnDamaged(float amount, float current)
	{
		if (WarningActive || HitFlashTime <= 0.0f)
		{
			return;
		}

		_flash = HitFlashTime;
		Refresh();
	}

	// Dibuja el efecto actual (o lo apaga) en los materiales.
	private void Refresh()
	{
		VisualEffect effect = Effect;
		bool on = effect != VisualEffect.None;
		Color color = effect == VisualEffect.HitFlash ? HitFlashColor : HighlightColor;
		float energy = effect == VisualEffect.HitFlash ? HitFlashEnergy : HighlightEnergy;

		if (_material != null)
		{
			_material.AlbedoColor = on ? color : _baseColor;
			_material.Emission = color;
			_material.EmissionEnabled = on;
		}

		foreach (ModelMaterial entry in _model)
		{
			entry.Material.EmissionEnabled = on || entry.Enabled;
			entry.Material.Emission = on ? color : entry.Color;
			entry.Material.EmissionEnergyMultiplier = on ? energy : entry.Energy;
		}
	}

	private void CopyModelMaterials()
	{
		foreach (MeshInstance3D mesh in _meshes)
		{
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				if (mesh.GetActiveMaterial(surface) is not BaseMaterial3D original)
				{
					continue;
				}

				BaseMaterial3D copy = (BaseMaterial3D)original.Duplicate();
				mesh.SetSurfaceOverrideMaterial(surface, copy);
				_model.Add(new ModelMaterial
				{
					Material = copy,
					Enabled = copy.EmissionEnabled,
					Color = copy.Emission,
					Energy = copy.EmissionEnergyMultiplier,
				});
			}
		}
	}

	private void Collect(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mesh && mesh.Mesh != null)
			{
				_meshes.Add(mesh);
			}

			Collect(child);
		}
	}
}
