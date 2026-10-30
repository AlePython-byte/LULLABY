using System.Collections.Generic;
using Godot;

/// <summary>
/// Carga modelos .glb (objetos, piezas de ambientación, proyectiles) ya centrados por su caja envolvente, porque algunos
/// orígenes no están centrados. Las escenas y su caja se guardan en caché. Si el archivo no existe devuelve null y quien
/// lo pide usa su aspecto provisional.
/// </summary>
public static class ModelLibrary
{
	/// <summary>Dónde queda el origen del nodo devuelto respecto a la caja del modelo.</summary>
	public enum Anchor
	{
		/// <summary>Centro en XZ y base en y = 0 (muebles, objetos apoyados).</summary>
		BaseCenter,
		/// <summary>Centro de la caja en los tres ejes (objetos que flotan, proyectiles).</summary>
		Center,
	}

	private sealed class Entry
	{
		public PackedScene Scene;
		public Aabb Bounds;
	}

	private static readonly Dictionary<string, Entry> Cache = new();
	private static readonly HashSet<string> Reported = new();

	/// <summary>Caja envolvente del modelo en su propio origen, o null si no existe.</summary>
	public static Aabb? BoundsOf(string path) => Get(path)?.Bounds;

	/// <summary>
	/// Instancia el modelo dentro de un nodo contenedor cuyo origen sigue el ancla. Con fitSize > 0 escala el modelo de forma
	/// uniforme para que su dimensión mayor mida fitSize metros. Devuelve null si el modelo no existe.
	/// </summary>
	public static Node3D Create(string path, Anchor anchor = Anchor.BaseCenter, float fitSize = 0.0f)
	{
		Entry entry = Get(path);
		if (entry == null)
		{
			return null;
		}

		Node3D model = entry.Scene.Instantiate<Node3D>();
		Aabb box = entry.Bounds;
		float scale = fitSize > 0.0f && box.GetLongestAxisSize() > 0.0f ? fitSize / box.GetLongestAxisSize() : 1.0f;
		Vector3 center = box.GetCenter();
		float y = anchor == Anchor.Center ? center.Y : box.Position.Y;

		Node3D holder = new() { Name = "Centered" };
		holder.AddChild(model);
		model.Scale = Vector3.One * scale;
		model.Position = -new Vector3(center.X, y, center.Z) * scale;
		return holder;
	}

	private static Entry Get(string path)
	{
		if (Cache.TryGetValue(path, out Entry cached))
		{
			return cached;
		}

		Entry entry = null;
		if (ResourceLoader.Exists(path) && ResourceLoader.Load<PackedScene>(path) is PackedScene scene)
		{
			Node3D probe = scene.Instantiate<Node3D>();
			Aabb? box = Measure(probe, Transform3D.Identity);
			probe.Free();
			if (box.HasValue)
			{
				entry = new Entry { Scene = scene, Bounds = box.Value };
			}
		}

		if (entry == null && Reported.Add(path))
		{
			GD.PushWarning($"ModelLibrary: no se pudo cargar el modelo {path}; se usa el aspecto provisional.");
		}

		Cache[path] = entry;
		return entry;
	}

	private static Aabb? Measure(Node3D node, Transform3D parent)
	{
		Transform3D local = parent * node.Transform;
		Aabb? result = null;
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			result = local * mesh.Mesh.GetAabb();
		}

		foreach (Node child in node.GetChildren())
		{
			if (child is Node3D childNode && Measure(childNode, local) is Aabb part)
			{
				result = result.HasValue ? result.Value.Merge(part) : part;
			}
		}

		return result;
	}
}
