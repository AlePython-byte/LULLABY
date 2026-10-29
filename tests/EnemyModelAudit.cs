using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Auditoría de SOLO LECTURA de los modelos 3D de los enemigos (assets/models/*_enemy.glb). Modo headless:
///   godot --headless --path . res://tests/EnemyModelAudit.tscn
///
/// Para cada modelo imprime: el árbol de nodos con sus nombres y tipos; la caja que envuelve todas las mallas (AABB) con el
/// modelo en el origen; la posición del origen respecto a los pies; hacia dónde mira el frente (según las partes que
/// suelen estar delante: ojos, boca, cañón...); el pivote de cada parte respecto a su propia malla; el número de
/// materiales y de triángulos. Lo compara con las reglas del arte y, de la escena actual del enemigo, describe el nodo
/// Visual (qué mallas tiene, su tamaño y cómo se colorea).
/// No cambia escenas, no guarda nada y no añade nada al proyecto: instancia, mide e imprime. Sale siempre con código 0.
/// </summary>
public partial class EnemyModelAudit : Node
{
	private const int TriangleBudget = 5000;
	private const float FeetTolerance = 0.01f;

	// Partes que en un personaje suelen estar delante; sirven para deducir hacia dónde mira el modelo.
	private static readonly string[] FrontHints = { "eye", "pupil", "mouth", "nose", "face", "muzzle", "barrel", "gun", "cannon", "beak", "visor", "lens" };

	private static readonly (string Name, string Glb, string Scene, float FootprintLimit)[] Enemies =
	{
		("Básico", "res://assets/models/basic_enemy.glb", "res://scenes/enemies/BasicEnemy.tscn", 0.9f),
		("Tirador", "res://assets/models/shooter_enemy.glb", "res://scenes/enemies/ShooterEnemy.tscn", 1.0f),
		("Explosivo", "res://assets/models/exploder_enemy.glb", "res://scenes/enemies/ExploderEnemy.tscn", 1.0f),
	};

	public override void _Ready()
	{
		foreach ((string name, string glb, string scene, float limit) in Enemies)
		{
			Audit(name, glb, scene, limit);
		}

		GetTree().Quit(0);
	}

	private void Audit(string name, string glbPath, string scenePath, float footprintLimit)
	{
		GD.Print($"\n==================== {name}: {glbPath} ====================");
		PackedScene packed = GD.Load<PackedScene>(glbPath);
		if (packed == null)
		{
			GD.Print("  NO SE PUDO CARGAR el modelo (¿falta importarlo?).");
			return;
		}

		Node3D world = new();
		AddChild(world);
		Node3D model = packed.Instantiate<Node3D>();
		world.AddChild(model);

		GD.Print("--- Árbol de nodos (nombre [tipo] posición local; mallas: superficies, triángulos, materiales) ---");
		PrintTree(model, 0);

		List<MeshInstance3D> meshes = Meshes(model).ToList();
		Aabb box = Bounds(meshes, out bool any);
		int triangles = meshes.Sum(m => m.Mesh.GetFaces().Length / 3);
		HashSet<Material> materials = new();
		foreach (MeshInstance3D mesh in meshes)
		{
			for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
			{
				Material material = mesh.GetActiveMaterial(s);
				if (material != null)
				{
					materials.Add(material);
				}
			}
		}

		GD.Print("\n--- Medidas (con el modelo en el origen) ---");
		if (!any)
		{
			GD.Print("  el modelo no tiene mallas");
			world.QueueFree();
			return;
		}

		Vector2 centre = new(box.Position.X + box.Size.X / 2.0f, box.Position.Z + box.Size.Z / 2.0f);
		float radius = 0.0f;
		foreach (MeshInstance3D mesh in meshes)
		{
			foreach (Vector3 vertex in GlobalVertices(mesh))
			{
				radius = Mathf.Max(radius, new Vector2(vertex.X, vertex.Z).Length());
			}
		}

		GD.Print($"  AABB: X {F(box.Position.X)}..{F(box.End.X)} ({F(box.Size.X)} m), Y {F(box.Position.Y)}..{F(box.End.Y)} ({F(box.Size.Y)} m), " +
			$"Z {F(box.Position.Z)}..{F(box.End.Z)} ({F(box.Size.Z)} m)");
		GD.Print($"  huella {F(box.Size.X)} x {F(box.Size.Z)} m; centro de la huella respecto al origen: X {F(centre.X)}, Z {F(centre.Y)}; " +
			$"radio horizontal máximo desde el origen {F(radius)} m");
		GD.Print($"  origen respecto a los pies: los pies (y mínima) están a {F(box.Position.Y)} m del origen");
		GD.Print($"  materiales únicos: {materials.Count}; triángulos: {triangles}; mallas: {meshes.Count}");
		foreach (Material material in materials)
		{
			GD.Print("    material " + DescribeMaterial(material));
		}

		GD.Print("\n--- Hacia dónde mira el frente ---");
		Vector2 front = FrontDirection(meshes, centre, out List<string> used);
		if (used.Count == 0)
		{
			GD.Print("  ninguna parte con nombre de 'delante' (" + string.Join(", ", FrontHints) + "): no se puede deducir por nombres.");
		}
		else
		{
			GD.Print($"  partes usadas: {string.Join(", ", used)}");
			GD.Print($"  dirección media del frente (X, Z): ({F(front.X)}, {F(front.Y)}) -> {DirectionName(front)}");
		}

		GD.Print("\n--- Superficies (material, triángulos y dónde cae su centro respecto al centro de la huella: X derecha, Z delante si es +Z) ---");
		foreach (MeshInstance3D mesh in meshes)
		{
			for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
			{
				Godot.Collections.Array arrays = mesh.Mesh.SurfaceGetArrays(s);
				Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
				Vector3 sum = Vector3.Zero;
				foreach (Vector3 vertex in vertices)
				{
					sum += mesh.GlobalTransform * vertex;
				}

				Vector3 mean = vertices.Length > 0 ? sum / vertices.Length : Vector3.Zero;
				string materialName = mesh.GetActiveMaterial(s)?.ResourceName ?? "(sin material)";
				GD.Print($"  {mesh.Name,-8} superficie {s}: {materialName,-22} {(indices.Length > 0 ? indices.Length / 3 : vertices.Length / 3),5} triángulos; " +
					$"centro: X {F(mean.X - centre.X)}, Y {F(mean.Y)}, Z {F(mean.Z - centre.Y)}");
			}
		}

		GD.Print("\n--- Pivotes (posición del origen de cada parte dentro de su propia malla, 0 % = borde mínimo, 100 % = borde máximo) ---");
		foreach (MeshInstance3D mesh in meshes)
		{
			Aabb local = mesh.GetAabb();
			string Pct(float position, float size) => size < 1e-5f ? "  - " : $"{(-position / size) * 100.0f,4:0}%";
			GD.Print($"  {mesh.Name,-16} origen local en X {Pct(local.Position.X, local.Size.X)}  Y {Pct(local.Position.Y, local.Size.Y)}  Z {Pct(local.Position.Z, local.Size.Z)}   " +
				$"(tamaño local {F(local.Size.X)} x {F(local.Size.Y)} x {F(local.Size.Z)}; centro global {FV(mesh.GlobalTransform * local.GetCenter())})");
		}

		GD.Print("\n--- Reglas del arte ---");
		Rule("origen en los pies (y mínima ≈ 0)", Mathf.Abs(box.Position.Y) <= FeetTolerance, $"y mínima = {F(box.Position.Y)} m");
		Rule("huella centrada sobre el origen (X y Z)", Mathf.Abs(centre.X) <= 0.05f && Mathf.Abs(centre.Y) <= 0.05f, $"centro de la huella ({F(centre.X)}, {F(centre.Y)})");
		Rule($"huella <= {F(footprintLimit)} x {F(footprintLimit)} m", box.Size.X <= footprintLimit && box.Size.Z <= footprintLimit, $"{F(box.Size.X)} x {F(box.Size.Z)} m");
		Rule($"cabe girando en un círculo de diámetro {F(footprintLimit)} m (radio <= {F(footprintLimit / 2.0f)})", radius <= footprintLimit / 2.0f, $"radio {F(radius)} m");
		Rule($"presupuesto de {TriangleBudget} triángulos", triangles <= TriangleBudget, $"{triangles} triángulos");
		Rule("partes separadas (más de una malla)", meshes.Count > 1, $"{meshes.Count} mallas");
		Rule("frente hacia -Z", used.Count > 0 && front.Y < 0.0f && Mathf.Abs(front.Y) >= Mathf.Abs(front.X), used.Count == 0 ? "no deducible por nombres" : DirectionName(front));
		GD.Print("  (los nombres en inglés y el frente se confirman leyendo el árbol de arriba)");

		DescribeCurrentVisual(name, scenePath, box);
		world.QueueFree();
	}

	// --- árbol ---------------------------------------------------------------------------------------------------------

	private static void PrintTree(Node node, int depth)
	{
		string indent = new(' ', 2 + depth * 2);
		string line = $"{indent}{node.Name} [{node.GetClass()}]";
		if (node is Node3D node3D)
		{
			Transform3D t = node3D.Transform;
			line += $" pos {FV(t.Origin)}";
			Vector3 rotation = t.Basis.GetEuler() * (180.0f / Mathf.Pi);
			if (rotation.Length() > 0.05f)
			{
				line += $" rot° {FV(rotation)}";
			}

			Vector3 scale = t.Basis.Scale;
			if (!scale.IsEqualApprox(Vector3.One))
			{
				line += $" escala {FV(scale)}";
			}
		}

		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			line += $"  -> {mesh.Mesh.GetSurfaceCount()} superficie(s), {mesh.Mesh.GetFaces().Length / 3} triángulos";
		}

		GD.Print(line);
		foreach (Node child in node.GetChildren())
		{
			PrintTree(child, depth + 1);
		}
	}

	private static IEnumerable<MeshInstance3D> Meshes(Node root)
	{
		Stack<Node> pending = new();
		pending.Push(root);
		List<MeshInstance3D> found = new();
		while (pending.Count > 0)
		{
			Node node = pending.Pop();
			if (node is MeshInstance3D mesh && mesh.Mesh != null)
			{
				found.Add(mesh);
			}

			foreach (Node child in node.GetChildren())
			{
				pending.Push(child);
			}
		}

		return found.OrderBy(m => m.GetPath().ToString(), StringComparer.Ordinal);
	}

	private static IEnumerable<Vector3> GlobalVertices(MeshInstance3D mesh)
	{
		for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
		{
			foreach (Vector3 vertex in mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
			{
				yield return mesh.GlobalTransform * vertex;
			}
		}
	}

	private static Aabb Bounds(IEnumerable<MeshInstance3D> meshes, out bool any)
	{
		any = false;
		Aabb box = new();
		foreach (MeshInstance3D mesh in meshes)
		{
			foreach (Vector3 vertex in GlobalVertices(mesh))
			{
				box = any ? box.Expand(vertex) : new Aabb(vertex, Vector3.Zero);
				any = true;
			}
		}

		return box;
	}

	// --- frente ---------------------------------------------------------------------------------------------------------

	private static Vector2 FrontDirection(List<MeshInstance3D> meshes, Vector2 footprintCentre, out List<string> used)
	{
		used = new List<string>();
		Vector2 sum = Vector2.Zero;
		foreach (MeshInstance3D mesh in meshes)
		{
			string lower = mesh.Name.ToString().ToLowerInvariant();
			if (!FrontHints.Any(hint => lower.Contains(hint)))
			{
				continue;
			}

			Vector3 centre = mesh.GlobalTransform * mesh.GetAabb().GetCenter();
			sum += new Vector2(centre.X - footprintCentre.X, centre.Z - footprintCentre.Y);
			used.Add(mesh.Name.ToString());
		}

		return used.Count > 0 ? sum / used.Count : Vector2.Zero;
	}

	private static string DirectionName(Vector2 direction)
	{
		if (direction.Length() < 0.02f)
		{
			return "sin dirección clara (partes centradas)";
		}

		return Mathf.Abs(direction.Y) >= Mathf.Abs(direction.X)
			? (direction.Y < 0.0f ? "-Z (CORRECTO)" : "+Z (INVERTIDO)")
			: (direction.X < 0.0f ? "-X (girado a la izquierda)" : "+X (girado a la derecha)");
	}

	// --- escena actual --------------------------------------------------------------------------------------------------

	private void DescribeCurrentVisual(string name, string scenePath, Aabb modelBox)
	{
		GD.Print($"\n--- Escena actual: {scenePath} (el nodo Visual de hoy) ---");
		EnemyBase enemy = GD.Load<PackedScene>(scenePath).Instantiate<EnemyBase>();
		Node3D visual = enemy.GetNodeOrNull<Node3D>("Visual");
		CollisionShape3D collision = enemy.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (collision?.Shape is BoxShape3D box)
		{
			GD.Print($"  CollisionShape3D: caja {FV(box.Size)} en {FV(collision.Position)} (de {F(collision.Position.Y - box.Size.Y / 2.0f)} a {F(collision.Position.Y + box.Size.Y / 2.0f)} m en Y respecto al origen del enemigo)");
			GD.Print($"  el modelo mide {F(modelBox.Size.X)} x {F(modelBox.Size.Y)} x {F(modelBox.Size.Z)} m frente a una caja de {F(box.Size.X)} x {F(box.Size.Y)} x {F(box.Size.Z)} m");
		}

		GD.Print($"  tipo {enemy.Kind}, velocidad {F(enemy.Speed)}, color provisional de EnemyKinds: {EnemyKinds.Get(enemy.Kind).Color}");
		if (visual == null)
		{
			GD.Print("  no tiene nodo Visual");
			enemy.Free();
			return;
		}

		EnemyVisual enemyVisual = visual as EnemyVisual;
		GD.Print($"  Visual: {visual.GetClass()} con script {(enemyVisual != null ? enemyVisual.GetType().Name : "ninguno")}" +
			(enemyVisual != null ? $", UseKindColor = {enemyVisual.UseKindColor}, HighlightColor = {enemyVisual.HighlightColor}" : "") +
			$", posición {FV(visual.Position)}");
		foreach (Node child in visual.GetChildren())
		{
			GD.Print($"    {child.Name} [{child.GetClass()}] {Describe(child)}");
		}

		List<string> effects = new();
		foreach (Node child in enemy.GetChildren())
		{
			switch (child)
			{
				case ShootBehavior shoot:
					effects.Add($"aviso del Tirador: SetHighlight(true) durante WarnTime = {F(shoot.WarnTime)} s antes de cada disparo");
					break;
				case FuseBehavior fuse:
					effects.Add($"mecha del Explosivo: SetHighlight alternando cada BlinkInterval = {F(fuse.BlinkInterval)} s durante FuseTime = {F(fuse.FuseTime)} s");
					break;
			}
		}

		GD.Print(effects.Count == 0 ? "  efectos sobre Visual: ninguno propio del tipo (el golpe al enemigo no se pinta hoy)" : "  efectos sobre Visual: " + string.Join("; ", effects));
		enemy.Free();
	}

	private static string Describe(Node child)
	{
		if (child is not MeshInstance3D mesh || mesh.Mesh == null)
		{
			return child is Node3D n ? $"pos {FV(n.Position)}" : "";
		}

		string shape = mesh.Mesh switch
		{
			BoxMesh box => $"BoxMesh {FV(box.Size)}",
			SphereMesh sphere => $"SphereMesh radio {F(sphere.Radius)} alto {F(sphere.Height)}",
			CylinderMesh cylinder => $"CylinderMesh radios {F(cylinder.TopRadius)}/{F(cylinder.BottomRadius)} alto {F(cylinder.Height)}",
			CapsuleMesh capsule => $"CapsuleMesh radio {F(capsule.Radius)} alto {F(capsule.Height)}",
			_ => mesh.Mesh.GetClass(),
		};
		return $"{shape}, pos {FV(mesh.Position)}, material en la escena: " +
			(mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D material ? $"albedo {material.AlbedoColor}" : "ninguno (lo pone EnemyVisual)");
	}

	// --- ayudas ---------------------------------------------------------------------------------------------------------

	private static string DescribeMaterial(Material material)
	{
		string name = string.IsNullOrEmpty(material.ResourceName) ? "(sin nombre)" : material.ResourceName;
		if (material is BaseMaterial3D standard)
		{
			return $"{name} [{material.GetClass()}] albedo {standard.AlbedoColor}, emisión {(standard.EmissionEnabled ? "sí" : "no")}, " +
				$"rugosidad {F(standard.Roughness)}, metal {F(standard.Metallic)}, transparencia {standard.Transparency}";
		}

		return $"{name} [{material.GetClass()}]";
	}

	private static void Rule(string rule, bool ok, string detail)
	{
		GD.Print($"  [{(ok ? "CUMPLE" : "NO CUMPLE")}] {rule}: {detail}");
	}

	private static string F(double value)
	{
		return value.ToString("0.000", CultureInfo.InvariantCulture);
	}

	private static string FV(Vector3 v)
	{
		return $"({F(v.X)}, {F(v.Y)}, {F(v.Z)})";
	}
}
