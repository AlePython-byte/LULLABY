using Godot;

/// <summary>
/// Animación por código de Noah, sin esqueleto ni AnimationPlayer. Actúa sobre el modelo (Model, el hijo de Visual), no
/// sobre Visual: PlayerController fija la rotación completa de Visual cada fotograma (el giro hacia donde se mueve), y
/// así los dos no se pisan. Visual gira, el modelo se balancea dentro de él, y sus ejes son los de Noah: -Z es delante.
///
///   Caminar   rebote vertical (dos por ciclo), balanceo lateral y una leve inclinación hacia delante, con las piernas y
///             los brazos oscilando en contrafase. Todo escala con la velocidad real del jugador: parado, no se mueve.
///   Disparar  compresión breve (se aplasta y se ensancha un poco) cada vez que sale una lágrima. No lee ni toca
///             PlayerShooter: detecta las lágrimas (Tear) que aparecen junto al jugador.
///
/// El parpadeo de invulnerabilidad lo sigue haciendo PlayerHealth sobre Visual, así que alcanza a todas las partes.
/// Las piernas y los brazos se buscan por nombre dentro del modelo (LegL, LegR, ArmL, ArmR; el pivote de cada uno está en
/// su articulación). Si falta alguno, simplemente no se anima. Todos los valores son [Export].
/// </summary>
public partial class PlayerVisualAnimator : Node
{
	[Export] public Node3D Model { get; set; }

	[ExportGroup("Caminar")]
	[Export] public float StepsPerSecond { get; set; } = 3.0f;
	[Export] public float BounceHeight { get; set; } = 0.06f;
	[Export] public float TiltDegrees { get; set; } = 6.0f;
	[Export] public float LeanDegrees { get; set; } = 4.0f;
	[Export] public float LimbSwingDegrees { get; set; } = 24.0f;
	[Export] public float BlendSpeed { get; set; } = 12.0f;

	[ExportGroup("Disparar")]
	[Export] public float ShotSquash { get; set; } = 0.12f;
	[Export] public float ShotWiden { get; set; } = 0.07f;
	[Export] public float ShotDuration { get; set; } = 0.14f;

	private CharacterBody3D _body;
	private PlayerController _controller;
	private SceneTree _tree;
	private Node3D _legL;
	private Node3D _legR;
	private Node3D _armL;
	private Node3D _armR;
	private Vector3 _legLRest;
	private Vector3 _legRRest;
	private Vector3 _armLRest;
	private Vector3 _armRRest;

	private float _movement;
	private float _phase;
	private float _shotElapsed = float.MaxValue;

	public override void _Ready()
	{
		_controller = _body as PlayerController;
		Model ??= _body.GetNodeOrNull<Node3D>("Visual/Noah");
		if (Model == null)
		{
			GD.PushWarning("PlayerVisualAnimator: no encuentra el modelo de Noah; no anima nada.");
			SetProcess(false);
			return;
		}

		_legL = FindPart("LegL", out _legLRest);
		_legR = FindPart("LegR", out _legRRest);
		_armL = FindPart("ArmL", out _armLRest);
		_armR = FindPart("ArmR", out _armRRest);
	}

	public override void _EnterTree()
	{
		_body = GetParent<CharacterBody3D>();
		_tree = GetTree();
		_tree.NodeAdded += OnNodeAdded;
	}

	public override void _ExitTree()
	{
		_tree.NodeAdded -= OnNodeAdded;
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;

		Vector3 velocity = _body.GetRealVelocity();
		velocity.Y = 0.0f;
		float topSpeed = _controller != null && _controller.Speed > 0.0f ? _controller.Speed : 1.0f;
		float target = Mathf.Clamp(velocity.Length() / topSpeed, 0.0f, 1.0f);
		_movement = Mathf.Lerp(_movement, target, 1.0f - Mathf.Exp(-BlendSpeed * step));

		// Un paso por cada media vuelta de la fase: el rebote es |sin| y el balanceo, sin.
		_phase = Mathf.Wrap(_phase + step * StepsPerSecond * Mathf.Pi * _movement, 0.0f, Mathf.Tau);
		float swing = Mathf.Sin(_phase);

		float bounce = Mathf.Abs(swing) * BounceHeight * _movement;
		float roll = swing * Mathf.DegToRad(TiltDegrees) * _movement;
		float lean = -Mathf.DegToRad(LeanDegrees) * _movement; // -X inclina hacia delante (-Z)

		_shotElapsed += step;
		float shot = _shotElapsed < ShotDuration ? Mathf.Sin(Mathf.Pi * _shotElapsed / ShotDuration) : 0.0f;
		float scaleY = 1.0f - ShotSquash * shot;
		float scaleXZ = 1.0f + ShotWiden * shot;

		Model.Position = new Vector3(0.0f, bounce, 0.0f);
		Model.Rotation = new Vector3(lean, 0.0f, roll);
		Model.Scale = new Vector3(scaleXZ, scaleY, scaleXZ);

		float limb = Mathf.DegToRad(LimbSwingDegrees) * swing * _movement;
		Swing(_legR, _legRRest, limb);
		Swing(_legL, _legLRest, -limb);
		Swing(_armR, _armRRest, -limb);
		Swing(_armL, _armLRest, limb);
	}

	private Node3D FindPart(string name, out Vector3 rest)
	{
		Node3D part = Model.FindChild(name, true, false) as Node3D;
		rest = part?.Rotation ?? Vector3.Zero;
		return part;
	}

	private static void Swing(Node3D part, Vector3 rest, float angle)
	{
		if (part != null)
		{
			part.Rotation = new Vector3(rest.X + angle, rest.Y, rest.Z);
		}
	}

	// Cada lágrima del jugador se añade al mismo padre que el jugador (ver PlayerShooter).
	private void OnNodeAdded(Node node)
	{
		if (node is Tear && node.GetParent() == _body.GetParent())
		{
			_shotElapsed = 0.0f;
		}
	}
}
