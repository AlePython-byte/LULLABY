using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de los modelos 3D de objetos, de los enemigos volador, huidor y excavador, y de los proyectiles (escenas reales).
///   godot --headless --fixed-fps 60 --path . res://tests/VisualModelsTest.tscn
/// Objetos: modelo de ~0,5 m, flota y gira, la figura provisional se oculta, la recogida no cambia y sin .glb se usa el respaldo.
/// Enemigos: Visual a y = -0.5 con el modelo girado 180°, sin color provisional, con Facing, destello sobre copias de los
/// materiales, pies en y = 0 (el volador a 0,33), colisión y parámetros de siempre, y el excavador con su Mound y su hundimiento.
/// Proyectiles: modelo cargado, provisional oculto, colisión de siempre y la lágrima orientada con su vuelo.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class VisualModelsTest : Node
{
	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		GD.Print("=== VisualModelsTest ===");
		await Items();
		await Enemies();
		await Projectiles();
		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	private async Task Items()
	{
		GD.Print("\n--- Objetos ---");
		PackedScene scene = GD.Load<PackedScene>("res://scenes/items/ItemPickup.tscn");
		ItemCatalog catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		foreach (ItemDefinition definition in catalog.Items)
		{
			ItemPickup pickup = scene.Instantiate<ItemPickup>();
			pickup.Definition = definition;
			AddChild(pickup);
			Check($"{definition.Id}: tiene modelo y oculta la figura provisional", pickup.Model != null && !pickup.GetNode<MeshInstance3D>("Visual").Visible, "sin modelo");
			if (pickup.Model != null)
			{
				Aabb box = BoundsOf(pickup.Model);
				Check($"{definition.Id}: mide 0,5 m en su dimensión mayor", Mathf.Abs(box.GetLongestAxisSize() - ItemPickup.ModelSize) < 0.01f, $"{box.GetLongestAxisSize():F3} m");
				Check($"{definition.Id}: centrado sobre la recogida", box.GetCenter().DistanceTo(pickup.GetNode<Node3D>("ModelHolder").GlobalPosition) < 0.02f, $"centro {box.GetCenter()}");
			}

			CollisionShape3D shape = pickup.GetNode<CollisionShape3D>("CollisionShape3D");
			Check($"{definition.Id}: la recogida no cambia", shape.Shape is SphereShape3D { Radius: 0.65f } && pickup.CollisionLayer == 0 && pickup.CollisionMask == CollisionLayers.Player, "colisión distinta");
			Node3D holder = pickup.GetNode<Node3D>("ModelHolder");
			float y0 = holder.Position.Y, yaw0 = holder.Rotation.Y;
			await Frames(30);
			Check($"{definition.Id}: flota y gira", Mathf.Abs(holder.Position.Y - y0) > 0.001f && Mathf.Abs(holder.Rotation.Y - yaw0) > 0.01f, "sin movimiento");
			pickup.Free();
		}

		ItemDefinition unknown = (ItemDefinition)catalog.Items[0].Duplicate();
		unknown.Id = "sin_modelo";
		ItemPickup fallback = scene.Instantiate<ItemPickup>();
		fallback.Definition = unknown;
		AddChild(fallback);
		Check("sin .glb se queda la figura provisional", fallback.Model == null && fallback.GetNode<MeshInstance3D>("Visual").Visible, "no hubo respaldo");
		fallback.Free();
	}

	private async Task Enemies()
	{
		(EnemyKind kind, string name, Vector3 collision, float minY, float speed, float health)[] specs =
		{
			(EnemyKind.Flyer, "Volador", new Vector3(0.7f, 0.7f, 0.7f), 0.33f, 4.0f, 1.0f),
			(EnemyKind.Runner, "Huidor", new Vector3(0.7f, 0.9f, 0.7f), 0.0f, 3.5f, 2.0f),
			(EnemyKind.Digger, "Excavador", new Vector3(0.9f, 0.9f, 0.9f), 0.0f, 3.5f, 3.0f),
		};
		foreach ((EnemyKind kind, string name, Vector3 collision, float minY, float speed, float health) in specs)
		{
			GD.Print($"\n--- {name} ---");
			Node3D world = new();
			AddChild(world);
			Node3D player = new() { Position = new Vector3(5, 0.5f, 0) };
			player.AddToGroup("player");
			world.AddChild(player);
			EnemyBase enemy = EnemyKinds.Scene(kind).Instantiate<EnemyBase>();
			world.AddChild(enemy);
			enemy.Position = new Vector3(0, 0.5f, 0);
			enemy.SetPhysicsProcess(false);
			await Frames(3);

			EnemyVisual visual = enemy.Visual;
			// El excavador baja el nodo Body (que se hunde) y no el Visual: su Mound ya está a -0.5 dentro de Visual.
			float visualY = visual is DiggerVisual ? 0.0f : -0.5f;
			Check($"{name}: pies a -0.5 del enemigo (Visual, o Body en el excavador) con el modelo girado 180°", visual != null
				&& Mathf.IsEqualApprox(visual.Position.Y, visualY) && (visual is not DiggerVisual || Mathf.IsEqualApprox(visual.GetNode<Node3D>("Body").Position.Y, -0.5f))
				&& enemy.FindChild("Model", true, false) is Node3D model && model.Transform.Basis.IsEqualApprox(Basis.FromEuler(new Vector3(0, Mathf.Pi, 0))), "estructura");
			Check($"{name}: sin color provisional y con Facing", !visual.UseKindColor && enemy.GetNodeOrNull("Facing") != null
				&& Meshes(visual).TrueForAll(m => m.MaterialOverride == null || m.Name == "Mound"), "color o Facing");
			CollisionShape3D shape = enemy.GetNode<CollisionShape3D>("CollisionShape3D");
			Check($"{name}: colisión y parámetros de siempre", shape.Shape is BoxShape3D box && box.Size.IsEqualApprox(collision)
				&& Mathf.IsEqualApprox(enemy.Speed, speed) && Mathf.IsEqualApprox(HealthComponent.FindIn(enemy).MaxHealth, health), "valores distintos");

			float feet = float.MaxValue;
			foreach (MeshInstance3D mesh in Meshes(visual))
			{
				if (mesh.Name != "Mound" || mesh.GetParent() != visual)
				{
					feet = Mathf.Min(feet, (mesh.GlobalTransform * mesh.Mesh.GetAabb()).Position.Y);
				}
			}

			Check($"{name}: pies en y = {minY:F2} del mundo", Mathf.Abs(feet - minY) < 0.06f, $"y mínima {feet:F3}");
			Check($"{name}: copias propias de los materiales", visual.ModelMaterials.Count > 0, "sin copias");
			Check($"{name}: mira al jugador", Mathf.Abs(Mathf.AngleDifference(visual.Rotation.Y, -Mathf.Pi / 2.0f)) < 0.05f, $"yaw {visual.Rotation.Y:F2}");

			HealthComponent.FindIn(enemy).TakeDamage(0.1f);
			Check($"{name}: destello de golpe sobre las copias", visual.HitFlashing && visual.ModelMaterials[0].EmissionEnabled, "sin destello");
			visual.SetHighlight(true);
			Check($"{name}: el aviso tiene prioridad", visual.Highlighted && visual.Effect == EnemyVisual.VisualEffect.Warning, "sin aviso");
			visual.SetHighlight(false);

			if (visual is DiggerVisual digger)
			{
				Node3D body = visual.GetNode<Node3D>("Body");
				Node3D mound = visual.GetNode<Node3D>("Mound");
				Check($"{name}: fuera de la tierra se ve el cuerpo y no el montículo", body.Visible && !mound.Visible, "estado inicial");
				digger.SetBurial(1.0f);
				Check($"{name}: enterrado oculta el cuerpo y muestra el montículo", !body.Visible && mound.Visible && body.Position.Y < -1.0f, $"cuerpo y {body.Position.Y:F2}");
				digger.SetBurial(0.0f);
			}

			world.Free();
		}
	}

	private async Task Projectiles()
	{
		GD.Print("\n--- Proyectiles ---");
		Tear tear = GD.Load<PackedScene>("res://scenes/combat/Tear.tscn").Instantiate<Tear>();
		AddChild(tear);
		ProjectileModel tearModel = tear.GetNode<ProjectileModel>("Model");
		Check("lágrima: modelo cargado y esfera provisional oculta", tearModel.HasModel && !tear.GetNode<Node3D>("Mesh").Visible, "sin modelo");
		Check("lágrima: colisión de siempre", tear.GetNode<CollisionShape3D>("CollisionShape3D").Shape is SphereShape3D { Radius: 0.15f }, "radio");
		tear.Launch(Vector3.Right);
		Vector3 tip = tearModel.Basis * Vector3.Up;
		Check("lágrima: eje largo en la dirección de vuelo con la punta atrás", tip.Dot(Vector3.Left) > 0.99f, $"punta {tip}");
		Check("lágrima: mide 0,4 m", Mathf.Abs(BoundsOf(tearModel).GetLongestAxisSize() - 0.4f) < 0.01f, "tamaño");
		tear.Free();

		Node3D projectile = GD.Load<PackedScene>("res://scenes/combat/EnemyProjectile.tscn").Instantiate<Node3D>();
		AddChild(projectile);
		Check("proyectil enemigo: modelo cargado y colisión de siempre", projectile.GetNode<ProjectileModel>("Model").HasModel
			&& !projectile.GetNode<Node3D>("Mesh").Visible && projectile.GetNode<CollisionShape3D>("CollisionShape3D").Shape is SphereShape3D { Radius: 0.2f }, "valores distintos");
		projectile.Free();
		await Frames(1);
	}

	private static List<MeshInstance3D> Meshes(Node root)
	{
		List<MeshInstance3D> result = new();
		Stack<Node> pending = new();
		pending.Push(root);
		while (pending.Count > 0)
		{
			Node node = pending.Pop();
			if (node is MeshInstance3D { Mesh: not null } mesh)
			{
				result.Add(mesh);
			}

			foreach (Node child in node.GetChildren())
			{
				pending.Push(child);
			}
		}

		return result;
	}

	private static Aabb BoundsOf(Node root)
	{
		Aabb? total = null;
		foreach (MeshInstance3D mesh in Meshes(root))
		{
			Aabb box = mesh.GlobalTransform * mesh.Mesh.GetAabb();
			total = total.HasValue ? total.Value.Merge(box) : box;
		}

		return total ?? new Aabb();
	}

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  {(ok ? "OK   " : "FALLO")} {name}" + (ok ? "" : $" ({detail})"));
		if (!ok)
		{
			_failures.Add(name);
		}
	}
}
