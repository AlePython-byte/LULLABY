using Godot;

/// <summary>Una recogida independiente. La señal permite asociarla después a un grupo de ofertas sin acoplar sus efectos.</summary>
public partial class ItemPickup : Area3D
{
	[Signal] public delegate void CollectedEventHandler(ItemDefinition item);
	[Export] public ItemDefinition Definition { get; set; }
	public bool IsCollected { get; private set; }

	/// <summary>Tamaño (dimensión mayor, en metros) del modelo 3D de un objeto.</summary>
	public const float ModelSize = 0.5f;

	/// <summary>El modelo 3D de un objeto vive en assets/models/items/{Id}.glb; si no existe se queda la figura provisional.</summary>
	public static string ModelPathFor(string id) => $"res://assets/models/items/{id}.glb";

	/// <summary>Nodo con el modelo 3D (flotando y girando); nulo si se usa la figura provisional.</summary>
	public Node3D Model { get; private set; }

	public override void _Ready()
	{
		if (Definition == null || !Definition.IsValid)
		{
			GD.PushError("ItemPickup: falta una definición válida.");
			SetDeferred(PropertyName.Monitoring, false);
			return;
		}
		MeshInstance3D visual = GetNode<MeshInstance3D>("Visual");
		visual.Mesh = Definition.Shape switch
		{
			ItemShape.Cylinder => new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.23f, Height = 0.7f },
			ItemShape.Box => new BoxMesh { Size = new Vector3(0.65f, 0.45f, 0.2f) },
			ItemShape.Sphere => new SphereMesh { Radius = 0.28f, Height = 0.56f },
			ItemShape.Capsule => new CapsuleMesh { Radius = 0.22f, Height = 0.8f },
			_ => new PrismMesh { Size = new Vector3(0.8f, 0.35f, 0.6f) },
		};
		visual.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = Definition.Color,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		ShowModel(visual);
		BodyEntered += OnBodyEntered;
	}

	// La figura provisional conserva su forma y color (respaldo y pruebas); solo se oculta cuando hay modelo.
	private void ShowModel(MeshInstance3D provisional)
	{
		FloatingSpin holder = GetNodeOrNull<FloatingSpin>("ModelHolder");
		Node3D model = holder == null ? null : ModelLibrary.Create(ModelPathFor(Definition.Id), ModelLibrary.Anchor.Center, ModelSize);
		if (model == null)
		{
			return;
		}

		holder.AddChild(model);
		Model = model;
		provisional.Visible = false;
	}

	public bool TryCollect(Node3D body)
	{
		if (IsCollected || IsQueuedForDeletion() || !IsInsideTree() || body == null || !body.IsInGroup("player")) return false;
		PlayerItems inventory = body.GetNodeOrNull<PlayerItems>("Items");
		if (inventory == null) return false;
		// Se marca antes de aplicar el efecto: las señales del inventario tampoco pueden recogerlo de nuevo.
		IsCollected = true;
		if (!inventory.TryCollect(Definition))
		{
			IsCollected = false;
			return false;
		}
		Visible = false;
		SetDeferred(PropertyName.Monitoring, false);
		AudioManager.Play(SoundCatalog.ItemPickedUp);
		EmitSignal(SignalName.Collected, Definition);
		QueueFree();
		return true;
	}

	private void OnBodyEntered(Node3D body) => TryCollect(body);
}
