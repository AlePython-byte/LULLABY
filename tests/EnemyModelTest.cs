using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Prueba de los modelos 3D integrados en los enemigos básico, tirador y explosivo (el código y las escenas reales).
/// Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/EnemyModelTest.tscn
///
/// Por cada enemigo:
///   1. Estructura: Visual (a y = -0.5 sobre el origen del enemigo) con el modelo, sus partes con nombre, sin el color
///      provisional (UseKindColor = false, ninguna malla con MaterialOverride), con copias de los materiales por instancia
///      (distintas de las del recurso importado y de las de otra instancia) y la colisión de siempre.
///   2. Geometría con el enemigo a y = 0.5: los pies en y = 0, la huella dentro del límite (0.9 x 0.9 el básico y 1 x 1 los
///      demás) y yaw puro (ninguna rotación fuera del eje Y).
///   3. Frente: sin jugador, el frente del modelo mira hacia -Z; con el jugador a cada uno de los cuatro lados, el Visual
///      gira y el frente mira hacia él. La colisión y la rotación del propio enemigo no cambian.
///   4. Efectos sobre los materiales del modelo: el aviso (SetHighlight) enciende la emisión solo en las copias, el destello
///      de golpe dura ~0.1 s, y el aviso tiene prioridad sobre el destello.
/// Además, con el comportamiento real y midiendo por ESTADO (no solo por el material):
///   5. Tirador: el aviso (EnemyVisual.Highlighted) dura WarnTime (0.5 s) y empieza/acaba con WarningStarted/Fired.
///   6. Explosivo: la mecha dura FuseTime (0.8 s), el aviso parpadea cada BlinkInterval (0.1 s) y está activo
///      (WarningActive) durante toda la mecha, sin que un golpe en mitad de ella cuele el destello.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class EnemyModelTest : Node
{
	private const string PlayerPath = "res://scenes/player/Player.tscn";

	private sealed class Spec
	{
		public EnemyKind Kind;
		public string Name;
		public float FootprintLimit;
		public Vector3 Collision;
		public string[] Parts;
		public int UniqueMaterials;
	}

	private static readonly Spec[] Specs =
	{
		new() { Kind = EnemyKind.Chaser, Name = "Básico", FootprintLimit = 0.9f, Collision = new Vector3(0.9f, 0.9f, 0.9f),
			Parts = new[] { "Body", "Face", "LegL", "LegR" }, UniqueMaterials = 3 },
		new() { Kind = EnemyKind.Shooter, Name = "Tirador", FootprintLimit = 1.0f, Collision = new Vector3(0.8f, 0.9f, 0.8f),
			Parts = new[] { "Body", "Head", "Cannon" }, UniqueMaterials = 5 },
		new() { Kind = EnemyKind.Exploder, Name = "Explosivo", FootprintLimit = 1.0f, Collision = new Vector3(0.8f, 0.9f, 0.8f),
			Parts = new[] { "Body", "Fuse", "LegL", "LegR" }, UniqueMaterials = 5 },
	};

	private readonly List<string> _failures = new();
	private float _dt;
	private int _frame;

	public override async void _Ready()
	{
		_dt = 1.0f / Engine.PhysicsTicksPerSecond;
		GD.Print("=== EnemyModelTest ===");
		foreach (Spec spec in Specs)
		{
			await ModelChecks(spec);
		}

		await ShooterWarningByState();
		await ExploderFuseByState();

		GD.Print("");
		GD.Print(_failures.Count == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLO ({_failures.Count} comprobaciones)");
		GetTree().Quit(_failures.Count == 0 ? 0 : 1);
	}

	// --- por enemigo -----------------------------------------------------------------------------------------------

	private async Task ModelChecks(Spec spec)
	{
		GD.Print($"\n--- {spec.Name} ---");
		Node3D world = NewWorld();
		EnemyBase enemy = NewEnemy(world, spec.Kind, new Vector3(0, 0.5f, 0));
		EnemyBase other = NewEnemy(world, spec.Kind, new Vector3(6, 0.5f, 0));
		enemy.SetPhysicsProcess(false);
		other.SetPhysicsProcess(false);
		await Frames(3);
		EnemyVisual visual = enemy.Visual;

		// 1. Estructura
		Check($"{spec.Name}: Visual a y = -0.5 con el modelo dentro", visual != null && Mathf.IsEqualApprox(visual.Position.Y, -0.5f)
			&& visual.GetNodeOrNull<Node3D>("Model") != null, $"Visual en {visual?.Position}");
		Check($"{spec.Name}: el modelo gira 180° dentro de Visual (frente +Z → -Z)",
			visual.GetNode<Node3D>("Model").Transform.Basis.IsEqualApprox(Basis.FromEuler(new Vector3(0, Mathf.Pi, 0))), "giro fijo del nodo Model");
		Check($"{spec.Name}: tiene las partes {string.Join(", ", spec.Parts)}", spec.Parts.All(name => enemy.FindChild(name, true, false) is MeshInstance3D),
			$"mallas: {string.Join(", ", Meshes(visual).Select(m => m.Name.ToString()))}");
		Check($"{spec.Name}: sin color provisional (UseKindColor = false)", !visual.UseKindColor && Meshes(visual).All(m => m.MaterialOverride == null),
			$"UseKindColor {visual.UseKindColor}");

		HashSet<Material> originals = new();
		bool copies = true;
		foreach (MeshInstance3D mesh in Meshes(visual))
		{
			for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
			{
				Material original = mesh.Mesh.SurfaceGetMaterial(s);
				originals.Add(original);
				copies &= mesh.GetActiveMaterial(s) != original && mesh.GetActiveMaterial(s) is BaseMaterial3D copy
					&& original is BaseMaterial3D source && copy.AlbedoColor.IsEqualApprox(source.AlbedoColor);
			}
		}

		Check($"{spec.Name}: conserva los {spec.UniqueMaterials} materiales del modelo", originals.Count == spec.UniqueMaterials, $"{originals.Count} materiales únicos");
		Check($"{spec.Name}: cada instancia tiene sus copias (iguales en color, distintas del recurso y de otra instancia)",
			copies && visual.ModelMaterials.Count > 0 && !visual.ModelMaterials.Intersect(other.Visual.ModelMaterials).Any(),
			$"{visual.ModelMaterials.Count} copias");
		CollisionShape3D collision = enemy.GetNode<CollisionShape3D>("CollisionShape3D");
		Check($"{spec.Name}: la colisión es la de siempre", collision.Shape is BoxShape3D box && box.Size.IsEqualApprox(spec.Collision) && collision.Position == Vector3.Zero,
			$"caja {(collision.Shape as BoxShape3D)?.Size} en {collision.Position}");

		// 2. Geometría
		Aabb bounds = Bounds(visual, out float radius);
		GD.Print($"  caja del modelo: X {F(bounds.Size.X)}, Y {F(bounds.Size.Y)}, Z {F(bounds.Size.Z)} m; y mínima {F(bounds.Position.Y)}; radio horizontal {F(radius)} m");
		Check($"{spec.Name}: los pies en y = 0 del mundo", Mathf.Abs(bounds.Position.Y) < 0.01f, $"y mínima {F(bounds.Position.Y)}");
		Check($"{spec.Name}: huella dentro de {F(spec.FootprintLimit)} x {F(spec.FootprintLimit)} m", bounds.Size.X <= spec.FootprintLimit && bounds.Size.Z <= spec.FootprintLimit,
			$"{F(bounds.Size.X)} x {F(bounds.Size.Z)} m");

		// 3. Frente
		Vector3 restFront = FrontDirection(spec.Kind, enemy);
		Check($"{spec.Name}: sin jugador, el frente mira hacia -Z", restFront.Dot(Vector3.Forward) > 0.95f, $"frente ({F(restFront.X)}, {F(restFront.Z)})");
		if (spec.Kind == EnemyKind.Exploder)
		{
			Vector3 back = BackDirection(enemy);
			Check($"{spec.Name}: la llave de cuerda queda detrás (+Z)", back.Dot(Vector3.Back) > 0.9f, $"llave ({F(back.X)}, {F(back.Z)})");
		}

		PlayerController player = NewPlayer(world, new Vector3(0, 0.5f, 0));
		(string label, Vector3 side)[] sides =
		{
			("+X", new Vector3(5, 0.5f, 0)), ("+Z", new Vector3(0, 0.5f, 5)), ("-X", new Vector3(-5, 0.5f, 0)), ("-Z", new Vector3(0, 0.5f, -5)),
		};
		foreach ((string label, Vector3 position) in sides)
		{
			player.GlobalPosition = position;
			await Frames(60);
			Vector3 toPlayer = (position - enemy.GlobalPosition) with { Y = 0.0f };
			Vector3 front = FrontDirection(spec.Kind, enemy);
			Check($"{spec.Name}: con el jugador en {label}, el frente mira hacia él", front.Dot(toPlayer.Normalized()) > 0.95f,
				$"frente ({F(front.X)}, {F(front.Z)}), jugador ({F(toPlayer.Normalized().X)}, {F(toPlayer.Normalized().Z)})");
		}

		Check($"{spec.Name}: el giro es solo yaw del Visual (el enemigo y la colisión no giran)",
			Mathf.Abs(visual.Rotation.X) < 0.001f && Mathf.Abs(visual.Rotation.Z) < 0.001f && enemy.Rotation == Vector3.Zero && collision.Rotation == Vector3.Zero,
			$"Visual {visual.RotationDegrees}, enemigo {enemy.RotationDegrees}");

		// 4. Efectos sobre los materiales del modelo
		await EffectChecks(spec, enemy, other);
		await Dispose(world);
	}

	private async Task EffectChecks(Spec spec, EnemyBase enemy, EnemyBase other)
	{
		EnemyVisual visual = enemy.Visual;
		HealthComponent health = HealthComponent.FindIn(enemy);
		List<BaseMaterial3D> shared = Meshes(visual).SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(s => m.Mesh.SurfaceGetMaterial(s)))
			.OfType<BaseMaterial3D>().Distinct().ToList();

		visual.SetHighlight(true);
		bool copiesOn = visual.ModelMaterials.All(m => m.EmissionEnabled && m.Emission.IsEqualApprox(visual.HighlightColor));
		Check($"{spec.Name}: el aviso enciende la emisión de sus materiales", visual.Highlighted && visual.Effect == EnemyVisual.VisualEffect.Warning && copiesOn, $"Effect {visual.Effect}");
		Check($"{spec.Name}: el aviso no toca el recurso importado ni a otra instancia", shared.All(m => !m.EmissionEnabled) && other.Visual.ModelMaterials.All(m => !m.EmissionEnabled),
			"los materiales compartidos y los del otro enemigo siguen apagados");

		float before = health.CurrentHealth;
		health.TakeDamage(0.5f);
		Check($"{spec.Name}: con el aviso activo, el golpe no cuela el destello", !visual.HitFlashing && visual.Effect == EnemyVisual.VisualEffect.Warning,
			$"vida {F(before)} → {F(health.CurrentHealth)}, Effect {visual.Effect}");
		visual.SetHighlight(false);
		await Frames(30);
		Check($"{spec.Name}: al apagar el aviso vuelve a su aspecto", visual.Effect == EnemyVisual.VisualEffect.None && visual.ModelMaterials.All(m => !m.EmissionEnabled),
			$"Effect {visual.Effect}");

		health.TakeDamage(0.5f);
		bool flashColor = visual.ModelMaterials.All(m => m.EmissionEnabled && m.Emission.IsEqualApprox(visual.HitFlashColor));
		Check($"{spec.Name}: un golpe sin aviso enciende el destello", visual.HitFlashing && visual.Effect == EnemyVisual.VisualEffect.HitFlash && flashColor,
			$"Effect {visual.Effect}");
		int flashFrames = 0;
		for (int i = 0; i < 30 && visual.HitFlashing; i++)
		{
			flashFrames++;
			await Frames(1);
		}

		Check($"{spec.Name}: el destello dura {F(visual.HitFlashTime)} s", Math.Abs(flashFrames * _dt - visual.HitFlashTime) <= 2.5f * _dt && visual.ModelMaterials.All(m => !m.EmissionEnabled),
			$"{flashFrames} fotogramas = {F(flashFrames * _dt)} s");
	}

	// --- Tirador y Explosivo con su comportamiento real ------------------------------------------------------------

	private async Task ShooterWarningByState()
	{
		GD.Print("\n--- Tirador: aviso de 0.5 s medido por estado ---");
		Node3D world = NewWorld();
		NewPlayer(world, new Vector3(6, 0.5f, 0));
		EnemyBase shooter = NewEnemy(world, EnemyKind.Shooter, new Vector3(0, 0.5f, 0));
		ShootBehavior shoot = shooter.Behavior<ShootBehavior>();
		EnemyVisual visual = shooter.Visual;
		int warningFrame = -1, firedFrame = -1;
		shoot.WarningStarted += () => warningFrame = warningFrame < 0 ? _frame : warningFrame;
		shoot.Fired += _ => firedFrame = firedFrame < 0 ? _frame : firedFrame;

		List<(int Frame, bool Highlighted, bool MaterialsOn)> samples = new();
		for (int i = 0; i < 160 && firedFrame < 0; i++)
		{
			await Frames(1);
			samples.Add((_frame, visual.Highlighted, visual.ModelMaterials.All(m => m.EmissionEnabled)));
		}

		List<int> highlighted = samples.Where(s => s.Highlighted).Select(s => s.Frame).ToList();
		int warnFrames = Mathf.RoundToInt(shoot.WarnTime / _dt);
		Check("el aviso empieza cuando el Tirador avisa y acaba al disparar", highlighted.Count > 0 && Math.Abs(highlighted.First() - warningFrame) <= 2 && Math.Abs(highlighted.Last() - firedFrame) <= 2,
			$"aviso en el fotograma {warningFrame}, disparo en el {firedFrame}; resaltado del {highlighted.FirstOrDefault()} al {highlighted.LastOrDefault()}");
		Check($"el aviso resaltado dura {F(shoot.WarnTime)} s (WarnTime)", Math.Abs(highlighted.Count - warnFrames) <= 2,
			$"{highlighted.Count} fotogramas resaltados (esperado {warnFrames} ± 2) = {F(highlighted.Count * _dt)} s");
		Check("el material refleja el estado en cada fotograma", samples.All(s => s.Highlighted == s.MaterialsOn),
			$"{samples.Count(s => s.Highlighted != s.MaterialsOn)} fotogramas en los que el material no coincide con el estado");
		await Dispose(world);
	}

	private async Task ExploderFuseByState()
	{
		GD.Print("\n--- Explosivo: mecha de 0.8 s medida por estado ---");
		Node3D world = NewWorld();
		NewPlayer(world, new Vector3(1.6f, 0.5f, 0));
		EnemyBase exploder = NewEnemy(world, EnemyKind.Exploder, new Vector3(0, 0.5f, 0));
		FuseBehavior fuse = exploder.Behavior<FuseBehavior>();
		EnemyVisual visual = exploder.Visual;
		HealthComponent health = HealthComponent.FindIn(exploder);
		int litFrame = -1, explodedFrame = -1;
		fuse.FuseLit += () => litFrame = litFrame < 0 ? _frame : litFrame;
		fuse.Exploded += _ => explodedFrame = explodedFrame < 0 ? _frame : explodedFrame;

		List<(int Frame, bool Highlighted, bool Active, bool Flashing, bool MaterialsOn)> samples = new();
		bool damaged = false;
		for (int i = 0; i < 120 && explodedFrame < 0; i++)
		{
			await Frames(1);
			if (litFrame >= 0 && !damaged && _frame - litFrame >= 20)
			{
				damaged = true;
				health.TakeDamage(1.0f);
			}

			if (explodedFrame < 0 && IsInstanceValid(exploder))
			{
				samples.Add((_frame, visual.Highlighted, visual.WarningActive, visual.HitFlashing, visual.ModelMaterials.All(m => m.EmissionEnabled)));
			}
		}

		int fuseFrames = explodedFrame - litFrame;
		int expected = Mathf.RoundToInt(fuse.FuseTime / _dt);
		Check($"la mecha dura {F(fuse.FuseTime)} s (FuseTime)", litFrame >= 0 && Math.Abs(fuseFrames - expected) <= 2, $"{fuseFrames} fotogramas entre FuseLit y Exploded (esperado {expected} ± 2) = {F(fuseFrames * _dt)} s");

		List<(int Frame, bool Highlighted, bool Active, bool Flashing, bool MaterialsOn)> inFuse = samples.Where(s => s.Frame >= litFrame).ToList();
		List<int> runs = new();
		for (int i = 0; i < inFuse.Count;)
		{
			int j = i;
			while (j < inFuse.Count && inFuse[j].Highlighted == inFuse[i].Highlighted)
			{
				j++;
			}

			runs.Add(j - i);
			i = j;
		}

		int blinkFrames = Mathf.RoundToInt(fuse.BlinkInterval / _dt);
		List<int> inner = runs.Skip(1).Take(Math.Max(0, runs.Count - 2)).ToList();
		Check($"el aviso parpadea cada {F(fuse.BlinkInterval)} s (BlinkInterval)", runs.Count >= 6 && inner.Count > 0 && inner.All(run => Math.Abs(run - blinkFrames) <= 1),
			$"{runs.Count} tramos de {string.Join(", ", runs)} fotogramas (esperado {blinkFrames} ± 1)");
		Check("el aviso está activo durante toda la mecha (incluidos los huecos del parpadeo)", inFuse.All(s => s.Active), $"{inFuse.Count(s => !s.Active)} fotogramas sin aviso activo");
		Check("un golpe en mitad de la mecha no cuela el destello", damaged && inFuse.All(s => !s.Flashing), $"vida tras el golpe {F(health.CurrentHealth)}; destellos {inFuse.Count(s => s.Flashing)}");
		Check("el material refleja el estado en cada fotograma", inFuse.All(s => s.Highlighted == s.MaterialsOn), $"{inFuse.Count(s => s.Highlighted != s.MaterialsOn)} fotogramas en los que el material no coincide con el estado");
		await Dispose(world);
	}

	// --- ayudas ----------------------------------------------------------------------------------------------------

	private static IEnumerable<MeshInstance3D> Meshes(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is MeshInstance3D mesh && mesh.Mesh != null)
			{
				yield return mesh;
			}

			foreach (MeshInstance3D nested in Meshes(child))
			{
				yield return nested;
			}
		}
	}

	private static IEnumerable<Vector3> Vertices(MeshInstance3D mesh, string material = null)
	{
		for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
		{
			if (material != null && mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName != material)
			{
				continue;
			}

			foreach (Vector3 vertex in mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
			{
				yield return mesh.GlobalTransform * vertex;
			}
		}
	}

	private static Aabb Bounds(Node root, out float radius)
	{
		bool first = true;
		Aabb box = new();
		radius = 0.0f;
		Vector3 origin = ((Node3D)root.GetParent()).GlobalPosition;
		foreach (MeshInstance3D mesh in Meshes(root))
		{
			foreach (Vector3 vertex in Vertices(mesh))
			{
				box = first ? new Aabb(vertex, Vector3.Zero) : box.Expand(vertex);
				first = false;
				radius = Mathf.Max(radius, new Vector2(vertex.X - origin.X, vertex.Z - origin.Z).Length());
			}
		}

		return box;
	}

	private static Vector3 Centroid(MeshInstance3D mesh, string material)
	{
		Vector3 sum = Vector3.Zero;
		int count = 0;
		foreach (Vector3 vertex in Vertices(mesh, material))
		{
			sum += vertex;
			count++;
		}

		return count > 0 ? sum / count : mesh.GlobalPosition;
	}

	private static Vector3 Horizontal(Vector3 point, EnemyBase enemy)
	{
		Vector3 offset = point - enemy.GlobalPosition;
		offset.Y = 0.0f;
		return offset.LengthSquared() > 0.0001f ? offset.Normalized() : Vector3.Zero;
	}

	// Hacia dónde mira el modelo, medido en el árbol: el marcador del frente de cada modelo (la placa de la cara del Básico,
	// el cañón del Tirador y los ojos del Explosivo), visto desde el origen del enemigo.
	private static Vector3 FrontDirection(EnemyKind kind, EnemyBase enemy)
	{
		MeshInstance3D Part(string name) => (MeshInstance3D)enemy.FindChild(name, true, false);
		Vector3 marker = kind switch
		{
			EnemyKind.Chaser => Part("Face").GlobalTransform * Part("Face").GetAabb().GetCenter(),
			EnemyKind.Shooter => Part("Cannon").GlobalTransform * Part("Cannon").GetAabb().GetCenter(),
			_ => Centroid(Part("Body"), "Mat_DarkFeatures"),
		};
		return Horizontal(marker, enemy);
	}

	private static Vector3 BackDirection(EnemyBase enemy)
	{
		return Horizontal(Centroid((MeshInstance3D)enemy.FindChild("Body", true, false), "Mat_WindingKey"), enemy);
	}

	private Node3D NewWorld()
	{
		Node3D world = new();
		AddChild(world);
		return world;
	}

	private static PlayerController NewPlayer(Node3D world, Vector3 position)
	{
		PlayerController player = GD.Load<PackedScene>(PlayerPath).Instantiate<PlayerController>();
		HealthComponent.FindIn(player).MaxHealth = 1.0e6f;
		player.Position = position;
		world.AddChild(player);
		return player;
	}

	private static EnemyBase NewEnemy(Node3D world, EnemyKind kind, Vector3 position)
	{
		EnemyBase enemy = EnemyKinds.Scene(kind).Instantiate<EnemyBase>();
		enemy.Position = position;
		world.AddChild(enemy);
		return enemy;
	}

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_frame++;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private async Task Dispose(Node3D world)
	{
		world.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private void Check(string name, bool ok, string detail)
	{
		GD.Print($"  [{(ok ? "OK" : "FALLO")}] {name}: {detail}");
		if (!ok)
		{
			_failures.Add(name);
		}
	}

	private static string F(double value)
	{
		return value.ToString("0.00", CultureInfo.InvariantCulture);
	}
}
