using System.Collections.Generic;
using Godot;

/// <summary>
/// Prueba del modelo de Noah dentro del Player real (no se da por supuesta ninguna convención de ejes). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/PlayerVisualTest.tscn
///
/// 1. Estructura: Player tiene el nodo Visual con el modelo de Noah y ya no tiene el cubo provisional (Mesh); aparecen
///    las partes con nombre (Body, Head, EyeL, EyeR, Hat, FacingMark, Pacifier, ArmL, ArmR, LegL, LegR) y el
///    CollisionShape3D sigue siendo la caja de 1 x 1 x 1.
/// 2. Orientación en reposo: la marca de dirección (la flecha del gorro) y la cara (ojos y chupete) quedan hacia -Z.
///    La marca se mide por su extensión: hasta dónde llega hacia delante y hacia atrás de la cabeza, no por su pivote.
/// 3. Orientación en movimiento: con el jugador real moviéndose en las cuatro direcciones, la marca y la cara apuntan
///    hacia donde se mueve; y una vez parado y asentado, la marca tampoco se tuerce hacia los lados (en movimiento el
///    balanceo del animador desplaza la cabeza unos 0.1 m de lado, así que ahí no se mide el desvío).
/// 4. Tamaño: la caja que envuelve todas las mallas cabe en 0.9 x 0.9 m; con el Player a y = 0.5, los pies quedan en
///    y = 0 del mundo.
/// 5. Animador: al caminar hay rebote e inclinación y al pararse vuelve al reposo; al disparar se comprime y se recupera.
/// 6. Parpadeo: tras un golpe, Visual (y con él todas las partes) parpadea y termina visible.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class PlayerVisualTest : Node
{
	private const string PlayerPath = "res://scenes/player/Player.tscn";
	private const float PlayerHeight = 0.5f;
	private const float MaxFootprint = 0.9f;
	private const int SettleFrames = 45;

	private static readonly string[] PartNames =
	{
		"Body", "Head", "EyeL", "EyeR", "Hat", "FacingMark", "Pacifier", "ArmL", "ArmR", "LegL", "LegR",
	};

	private PlayerController _player;
	private bool _ok = true;

	public override async void _Ready()
	{
		await Run();
		GD.Print(_ok ? "RESULTADO: OK" : "RESULTADO: FALLO");
		GetTree().Quit(_ok ? 0 : 1);
	}

	private async System.Threading.Tasks.Task Run()
	{
		_player = GD.Load<PackedScene>(PlayerPath).Instantiate<PlayerController>();
		_player.Position = new Vector3(0.0f, PlayerHeight, 0.0f);
		AddChild(_player);
		await Frames(3);

		CheckStructure();
		CheckSize();
		CheckRestOrientation();
		await CheckMovementOrientation();
		await CheckAnimator();
		await CheckBlink();
	}

	private void Check(bool condition, string message)
	{
		GD.Print($"  {(condition ? "ok " : "MAL")} {message}");
		_ok &= condition;
	}

	private async System.Threading.Tasks.Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private Node3D Part(string name)
	{
		return _player.FindChild(name, true, false) as Node3D;
	}

	private void CheckStructure()
	{
		GD.Print("=== 1. Estructura ===");
		Check(_player.GetNodeOrNull("Mesh") == null, "ya no existe el cubo provisional (Mesh)");
		Node3D visual = _player.GetNodeOrNull<Node3D>("Visual");
		Check(visual != null && visual.Position.IsEqualApprox(new Vector3(0.0f, -0.5f, 0.0f)), "Visual a y = -0.5 (los pies en el suelo)");
		Check(_player.GetNodeOrNull<Node3D>("Visual/Noah") != null, "el modelo de Noah cuelga de Visual");

		foreach (string name in PartNames)
		{
			Check(Part(name) is MeshInstance3D, $"parte '{name}' presente");
		}

		CollisionShape3D collision = _player.GetNode<CollisionShape3D>("CollisionShape3D");
		Check(collision.Shape is BoxShape3D box && box.Size.IsEqualApprox(Vector3.One), "la colisión sigue siendo la caja de 1 x 1 x 1");
	}

	private IEnumerable<MeshInstance3D> Meshes()
	{
		Stack<Node> pending = new();
		pending.Push(_player.GetNode("Visual"));
		while (pending.Count > 0)
		{
			Node node = pending.Pop();
			if (node is MeshInstance3D mesh)
			{
				yield return mesh;
			}

			foreach (Node child in node.GetChildren())
			{
				pending.Push(child);
			}
		}
	}

	private static IEnumerable<Vector3> GlobalVertices(MeshInstance3D mesh)
	{
		for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
		{
			Vector3[] vertices = mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			foreach (Vector3 vertex in vertices)
			{
				yield return mesh.GlobalTransform * vertex;
			}
		}
	}

	private void CheckSize()
	{
		GD.Print("\n=== 4. Tamaño (AABB del modelo importado, con el Player a y = 0.5) ===");
		bool first = true;
		Aabb box = new();
		float radius = 0.0f;
		foreach (MeshInstance3D mesh in Meshes())
		{
			Aabb local = mesh.GetAabb();
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = mesh.GlobalTransform * local.GetEndpoint(i);
				box = first ? new Aabb(corner, Vector3.Zero) : box.Expand(corner);
				first = false;
			}

			foreach (Vector3 vertex in GlobalVertices(mesh))
			{
				radius = Mathf.Max(radius, new Vector2(vertex.X - _player.GlobalPosition.X, vertex.Z - _player.GlobalPosition.Z).Length());
			}
		}

		GD.Print($"  caja del modelo: X {box.Size.X:0.000} m, Y {box.Size.Y:0.000} m, Z {box.Size.Z:0.000} m; " +
			$"y mínima {box.Position.Y:0.0000}, y máxima {box.End.Y:0.000}; radio horizontal máximo {radius:0.000} m");
		Check(box.Size.X <= MaxFootprint && box.Size.Z <= MaxFootprint, $"cabe en {MaxFootprint} x {MaxFootprint} m");
		Check(Mathf.Abs(box.Position.Y) < 0.005f, "los pies quedan en y = 0 del mundo");
		Check(radius <= 0.5f, "con cualquier giro cabe en la huella de 1 x 1 m (radio máximo <= 0.5)");
	}

	// Cuánto llega el mallado de `mesh` hacia delante y hacia atrás (en el plano) de `origin`, medido sobre `direction`.
	private static (float Front, float Back, float Side) Extent(MeshInstance3D mesh, Vector3 origin, Vector3 direction)
	{
		Vector3 across = new(-direction.Z, 0.0f, direction.X);
		float front = float.MinValue, back = float.MaxValue, sideMax = float.MinValue, sideMin = float.MaxValue;
		foreach (Vector3 vertex in GlobalVertices(mesh))
		{
			Vector3 offset = vertex - origin;
			offset.Y = 0.0f;
			float along = offset.Dot(direction);
			float lateral = offset.Dot(across);
			front = Mathf.Max(front, along);
			back = Mathf.Min(back, along);
			sideMax = Mathf.Max(sideMax, lateral);
			sideMin = Mathf.Min(sideMin, lateral);
		}

		return (front, back, sideMax + sideMin);
	}

	private Vector3 FaceCentre(Vector3 origin)
	{
		Vector3 sum = Vector3.Zero;
		foreach (string name in new[] { "EyeL", "EyeR", "Pacifier" })
		{
			Aabb aabb = ((MeshInstance3D)Part(name)).GetAabb();
			sum += ((MeshInstance3D)Part(name)).GlobalTransform * aabb.GetCenter();
		}

		Vector3 offset = sum / 3.0f - origin;
		offset.Y = 0.0f;
		return offset;
	}

	// La marca apunta a `direction` si llega bastante más lejos hacia delante que hacia atrás y, con checkSide, no se tuerce
	// de lado (un giro de 45° daría un desvío de ~0.13 m; el gorrito va ladeado a propósito y da ~0.04).
	private void CheckDirection(Vector3 direction, string label, bool checkSide = true)
	{
		Vector3 head = Part("Head").GlobalPosition;
		(float front, float back, float side) = Extent((MeshInstance3D)Part("FacingMark"), head, direction);
		float asymmetry = front + back;
		Vector3 face = FaceCentre(head);
		float faceAlignment = face.Length() > 0.0001f ? face.Normalized().Dot(direction) : 0.0f;

		GD.Print($"  {label}: marca llega {front:0.000} m hacia delante y {-back:0.000} m hacia atrás (asimetría {asymmetry:+0.000;-0.000}, " +
			$"desvío lateral {side:+0.000;-0.000}); la cara está a {face.Length():0.000} m, alineación {faceAlignment:0.00}");
		Check(asymmetry > 0.10f, $"{label}: la marca apunta hacia {DirectionName(direction)}");
		if (checkSide)
		{
			Check(Mathf.Abs(side) < 0.08f, $"{label}: la marca no se tuerce hacia los lados");
		}

		Check(faceAlignment > 0.95f, $"{label}: la cara mira hacia {DirectionName(direction)}");
	}

	private static string DirectionName(Vector3 direction)
	{
		if (Mathf.Abs(direction.Z) > Mathf.Abs(direction.X))
		{
			return direction.Z < 0.0f ? "-Z (arriba)" : "+Z (abajo)";
		}

		return direction.X < 0.0f ? "-X (izquierda)" : "+X (derecha)";
	}

	private void CheckRestOrientation()
	{
		GD.Print("\n=== 2. Orientación en reposo (debe ser hacia -Z) ===");
		CheckDirection(Vector3.Forward, "reposo");
	}

	private async System.Threading.Tasks.Task CheckMovementOrientation()
	{
		GD.Print("\n=== 3. Orientación al moverse en las cuatro direcciones ===");
		(string Action, Vector3 Direction)[] moves =
		{
			(PlayerController.MoveRight, Vector3.Right),
			(PlayerController.MoveDown, Vector3.Back),
			(PlayerController.MoveLeft, Vector3.Left),
			(PlayerController.MoveUp, Vector3.Forward),
		};

		foreach ((string action, Vector3 direction) in moves)
		{
			Vector3 start = _player.GlobalPosition;
			Input.ActionPress(action);
			await Frames(SettleFrames);
			Vector3 travelled = _player.GlobalPosition - start;
			Check(travelled.Normalized().Dot(direction) > 0.99f && travelled.Length() > 0.5f,
				$"el jugador se movió {travelled.Length():0.00} m hacia {DirectionName(direction)}");
			CheckDirection(direction, "moviéndose " + DirectionName(direction), checkSide: false);

			Input.ActionRelease(action);
			await Frames(SettleFrames);
			CheckDirection(direction, "parado " + DirectionName(direction));
		}
	}

	private async System.Threading.Tasks.Task CheckAnimator()
	{
		GD.Print("\n=== 5. Animador ===");
		Node3D model = _player.GetNode<Node3D>("Visual/Noah");
		Node3D legR = Part("LegR");
		float maxBounce = 0.0f, maxRoll = 0.0f, maxLimb = 0.0f;

		Input.ActionPress(PlayerController.MoveRight);
		for (int i = 0; i < 90; i++)
		{
			await Frames(1);
			maxBounce = Mathf.Max(maxBounce, model.Position.Y);
			maxRoll = Mathf.Max(maxRoll, Mathf.Abs(model.Rotation.Z));
			maxLimb = Mathf.Max(maxLimb, Mathf.Abs(legR.Rotation.X));
		}

		Input.ActionRelease(PlayerController.MoveRight);
		await Frames(90);
		GD.Print($"  caminando: rebote máx. {maxBounce:0.000} m, balanceo máx. {Mathf.RadToDeg(maxRoll):0.0}°, pierna {Mathf.RadToDeg(maxLimb):0.0}°");
		Check(maxBounce > 0.02f, "al caminar hay rebote");
		Check(maxRoll > Mathf.DegToRad(2.0f), "al caminar hay inclinación lateral");
		Check(maxLimb > Mathf.DegToRad(5.0f), "las piernas oscilan");
		Check(Mathf.Abs(model.Position.Y) < 0.002f && Mathf.Abs(model.Rotation.Z) < Mathf.DegToRad(0.2f) && Mathf.Abs(legR.Rotation.X) < Mathf.DegToRad(0.5f),
			"al pararse vuelve al reposo");

		float minScaleY = 1.0f;
		Input.ActionPress(PlayerShooter.ShootUp);
		for (int i = 0; i < 30; i++)
		{
			await Frames(1);
			minScaleY = Mathf.Min(minScaleY, model.Scale.Y);
		}

		Input.ActionRelease(PlayerShooter.ShootUp);
		await Frames(30);
		GD.Print($"  disparando: escala vertical mínima {minScaleY:0.000}");
		Check(minScaleY < 0.95f, "al disparar se comprime");
		Check(model.Scale.IsEqualApprox(Vector3.One), "tras disparar recupera su tamaño");
		foreach (Node tear in GetTree().GetNodesInGroup("tears"))
		{
			tear.QueueFree();
		}
	}

	private async System.Threading.Tasks.Task CheckBlink()
	{
		GD.Print("\n=== 6. Parpadeo de invulnerabilidad ===");
		Node3D visual = _player.GetNode<Node3D>("Visual");
		bool sawHidden = false, sawShown = false;
		_player.GetNode<PlayerHealth>("PlayerHealth").TakeHit(1.0f);
		for (int i = 0; i < 40; i++)
		{
			await Frames(1);
			sawHidden |= !visual.Visible;
			sawShown |= visual.Visible;
		}

		await Frames(60);
		Check(sawHidden && sawShown, "tras un golpe, Visual parpadea (todas las partes a la vez)");
		Check(visual.Visible, "al acabar la invulnerabilidad queda visible");
	}
}
