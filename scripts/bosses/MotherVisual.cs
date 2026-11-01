using System.Collections.Generic;
using Godot;

/// <summary>Animación de las piezas rígidas; los avisos tienen prioridad sobre el movimiento ambiental.</summary>
public partial class MotherVisual : Node
{
	[Export] public float EntranceTextTime { get; set; } = 3.0f;
	[Export] public float BobHeight { get; set; } = 0.06f;
	[Export] public float BobSpeed { get; set; } = 2.5f;
	[Export] public float ArmWarningAngle { get; set; } = 35.0f;
	[Export] public float WarningWidth { get; set; } = 2.4f;
	[Export] public float DeathEffectTime { get; set; } = 0.7f;
	[Export] public float FramingHeight { get; set; } = 4.5f;
	[Export] public float PhaseTextTime { get; set; } = 2.5f;
	[Export] public float PhaseBlinkInterval { get; set; } = 0.1f;
	[Export] public float RingGapLaneLength { get; set; } = 7.0f;
	[Export] public float RingGapLaneWidth { get; set; } = 1.8f;

	// Identidad sostenida de cada fase (aspecto del modelo, no avisos): se mezcla con LookBlendTime al cambiar de fase.
	[ExportGroup("Fases")]
	[Export] public float LookBlendTime { get; set; } = 0.6f;
	[Export] public Color PhaseTwoTint { get; set; } = new(1.0f, 0.55f, 0.12f);
	[Export] public float PhaseTwoTintStrength { get; set; } = 0.55f;
	[Export] public Color PhaseTwoEyes { get; set; } = new(1.0f, 0.78f, 0.25f);
	[Export] public float PhaseTwoEyeEnergy { get; set; } = 2.0f;
	[Export] public Color PhaseThreeTint { get; set; } = new(0.62f, 0.03f, 0.05f);
	[Export] public float PhaseThreeTintStrength { get; set; } = 0.75f;
	[Export] public Color PhaseThreeEyes { get; set; } = new(1.0f, 0.12f, 0.08f);
	[Export] public float PhaseThreeEyeEnergy { get; set; } = 4.0f;

	/// <summary>Respiración de la fase 3: amplitud relativa de la escala del modelo y su frecuencia (Hz). Solo visual: la colisión no cambia.</summary>
	[Export] public float BreathAmplitude { get; set; } = 0.04f;
	[Export] public float BreathFrequency { get; set; } = 1.1f;

	private readonly MeshInstance3D[] _spiralGaps = new MeshInstance3D[MotherSpiral.Arms];
	private readonly MeshInstance3D[] _spiralArms = new MeshInstance3D[MotherSpiral.Arms];
	private MeshInstance3D _ring;
	private MeshInstance3D _gapLane;
	private BoxMesh _gapMesh;
	private StandardMaterial3D _ringMaterial;
	private float _phraseUntil;
	private bool _blinking;
	private EnemyBase _enemy;
	private MotherBehavior _behavior;
	private Node3D _visual;
	private Label3D _phrase;
	private MeshInstance3D _lane;
	private BoxMesh _laneMesh;
	private readonly Dictionary<string, Node3D> _parts = new();
	private readonly Dictionary<string, Transform3D> _rest = new();
	private float _age;
	private HealthComponent _health;
	private Node3D _player;

	// Aspecto actual (se acerca al de la fase) y base del nodo Visual.
	private Color _tint = Colors.White;
	private float _tintStrength;
	private Color _eyes = Colors.White;
	private float _eyeEnergy;
	private float _breath;
	private Vector3 _visualScale = Vector3.One;

	public override void _Ready()
	{
		_enemy = GetParent<EnemyBase>();
		_behavior = _enemy.GetNode<MotherBehavior>("Attacks");
		_visual = _enemy.GetNode<Node3D>("Visual");
		_visualScale = _visual.Scale;
		_phrase = _enemy.GetNode<Label3D>("EntranceText");
		_phrase.Text = MotherTexts.Entrance;
		foreach (string name in new[] { "Body", "Head", "ArmL", "ArmR", "Base", "Eyes" })
		{
			Node3D part = _visual.GetNodeOrNull<Node3D>($"Model/{name}");
			if (part == null) { GD.PushError($"The Mother: falta la pieza {name}."); continue; }
			_parts[name] = part;
			_rest[name] = part.Transform;
		}
		_laneMesh = new BoxMesh();
		_lane = new MeshInstance3D
		{
			Name = "DashWarning", TopLevel = true, Mesh = _laneMesh, Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1.0f, 0.35f, 0.12f, 0.6f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		};
		AddChild(_lane);

		// Aviso del anillo: círculo en el suelo a la distancia de nacimiento y franja azulada sobre el hueco por el que escapar.
		_ringMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(1.0f, 0.2f, 0.15f, 0.7f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		_ring = new MeshInstance3D
		{
			Name = "RingWarning", TopLevel = true, Visible = false, MaterialOverride = _ringMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Mesh = new TorusMesh { InnerRadius = _behavior.RingSpawnDistance - 0.12f, OuterRadius = _behavior.RingSpawnDistance + 0.12f },
		};
		_gapMesh = new BoxMesh { Size = new Vector3(RingGapLaneWidth, 0.025f, RingGapLaneLength) };
		_gapLane = new MeshInstance3D
		{
			Name = "RingGapWarning", TopLevel = true, Visible = false, Mesh = _gapMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.4f, 0.8f, 1.0f, 0.5f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		};
		AddChild(_ring);
		AddChild(_gapLane);

		// Aviso de la espiral: los tres huecos de escape (azules) y por dónde arranca cada brazo (rojo).
		StandardMaterial3D gapMaterial = new()
		{
			AlbedoColor = new Color(0.4f, 0.8f, 1.0f, 0.5f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		StandardMaterial3D armMaterial = new()
		{
			AlbedoColor = new Color(1.0f, 0.2f, 0.15f, 0.7f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		for (int i = 0; i < MotherSpiral.Arms; i++)
		{
			_spiralGaps[i] = NewSpiralMark($"SpiralGapWarning{i}", new Vector3(RingGapLaneWidth, 0.025f, RingGapLaneLength), gapMaterial);
			_spiralArms[i] = NewSpiralMark($"SpiralArmWarning{i}", new Vector3(0.15f, 0.03f, RingGapLaneLength), armMaterial);
		}

		_behavior.PhaseChanged += OnPhaseChanged;
		_health = HealthComponent.FindIn(_enemy);
		_health.Died += OnDied;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(_behavior)) _behavior.PhaseChanged -= OnPhaseChanged;
		if (IsInstanceValid(_health)) _health.Died -= OnDied;
	}

	private void OnPhaseChanged(int phase)
	{
		_phrase.Text = phase >= 3 ? MotherTexts.PhaseThree : MotherTexts.PhaseTwo;
		_phraseUntil = _age + PhaseTextTime;
	}

	/// <summary>Aspecto al que tiende la fase dada: color y fuerza del tinte, color y energía de los ojos y amplitud de la respiración.</summary>
	public (Color Tint, float TintStrength, Color Eyes, float EyeEnergy, float Breath) LookFor(int phase) => phase switch
	{
		>= 3 => (PhaseThreeTint, PhaseThreeTintStrength, PhaseThreeEyes, PhaseThreeEyeEnergy, BreathAmplitude),
		2 => (PhaseTwoTint, PhaseTwoTintStrength, PhaseTwoEyes, PhaseTwoEyeEnergy, 0.0f),
		_ => (Colors.White, 0.0f, Colors.White, 0.0f, 0.0f),
	};

	// Acerca el aspecto actual al de la fase (suavizado exponencial: ~95 % en LookBlendTime segundos) y lo aplica. La respiración solo
	// escala el nodo Visual.
	private void UpdateLook(float delta)
	{
		(Color tint, float strength, Color eyes, float eyeEnergy, float breath) = LookFor(_behavior.Phase);
		float k = 1.0f - Mathf.Exp(-3.0f * delta / Mathf.Max(0.05f, LookBlendTime));

		bool settled = Mathf.Abs(_tintStrength - strength) < 0.001f && Mathf.Abs(_eyeEnergy - eyeEnergy) < 0.01f;
		if (!settled)
		{
			// Al salir de "sin aspecto" los colores parten ya del de la fase, para que solo se mezcle la fuerza y no el matiz.
			if (strength > 0.0f && _tintStrength < 0.01f) _tint = tint;
			if (eyeEnergy > 0.0f && _eyeEnergy < 0.01f) _eyes = eyes;
			_tint = _tint.Lerp(strength > 0.0f ? tint : _tint, k);
			_eyes = _eyes.Lerp(eyeEnergy > 0.0f ? eyes : _eyes, k);
			_tintStrength = Mathf.Lerp(_tintStrength, strength, k);
			_eyeEnergy = Mathf.Lerp(_eyeEnergy, eyeEnergy, k);
			if (Mathf.Abs(_tintStrength - strength) < 0.001f && Mathf.Abs(_eyeEnergy - eyeEnergy) < 0.01f)
			{
				(_tintStrength, _eyeEnergy) = (strength, eyeEnergy);
			}

			_enemy.Visual?.SetModelLook(_tint, _tintStrength, IsEye, _eyes, _eyeEnergy);
		}

		_breath = Mathf.Lerp(_breath, breath, k);
		float pulse = _breath > 0.0005f ? 1.0f + _breath * Mathf.Sin(_age * Mathf.Tau * BreathFrequency) : 1.0f;
		_visual.Scale = _visualScale * pulse;
	}

	private bool IsEye(MeshInstance3D mesh) => _parts.TryGetValue("Eyes", out Node3D eyes) && (eyes == mesh || eyes.IsAncestorOf(mesh));

	public override void _Process(double delta)
	{
		_age += (float)delta;
		UpdateLook((float)delta);
		_phrase.Visible = _age < EntranceTextTime || _age < _phraseUntil;
		if (_behavior.PhaseChangeActive)
		{
			// Parpadeo con el color de aviso mientras es invulnerable.
			_enemy.Visual?.SetHighlight((int)(_age / Mathf.Max(0.01f, PhaseBlinkInterval)) % 2 == 0);
			_blinking = true;
		}
		else if (_blinking)
		{
			_enemy.Visual?.SetHighlight(false);
			_blinking = false;
		}
		if (!IsInstanceValid(_player)) _player = GetTree().GetFirstNodeInGroup(GameNames.PlayerGroup) as Node3D;
		Vector3 direction = _behavior.DashWarningActive
			|| _behavior.State == MotherBehavior.AttackState.Dash
			? _behavior.DashDirection : (_player != null ? _player.GlobalPosition - _enemy.GlobalPosition : Vector3.Forward);
		direction.Y = 0.0f;
		if (direction.LengthSquared() > 0.0001f)
			_visual.Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.X, -direction.Z), 0.0f);

		float bob = Mathf.Sin(_age * BobSpeed) * BobHeight;
		bool warning = _behavior.ShotWarningActive || _behavior.DashWarningActive || _behavior.RingWarningActive
			|| _behavior.PhaseChangeActive || _behavior.SpiralWarningActive || _behavior.SpiralActive;
		foreach (string name in _parts.Keys)
		{
			Node3D part = _parts[name];
			part.Transform = _rest[name];
			if (name != "Base") part.Position += Vector3.Up * bob;
			if (name == "Head" || name == "Eyes") part.RotateX(Mathf.Sin(_age * BobSpeed * 0.5f) * 0.04f);
			if (name == "ArmL" || name == "ArmR")
			{
				float angle = warning ? Mathf.DegToRad(ArmWarningAngle) : Mathf.Sin(_age * BobSpeed) * 0.07f;
				part.RotateZ(name == "ArmL" ? -angle : angle);
			}
		}
		_lane.Visible = _behavior.DashWarningActive;
		if (_lane.Visible)
		{
			Vector3 start = _enemy.Position;
			Vector3 travel = MotherArena.LimitVelocity(start, _behavior.DashDirection * _behavior.DashDistance,
				_behavior.ArenaSize, _behavior.ArenaMargin, 1.0f);
			float length = travel.Length();
			_laneMesh.Size = new Vector3(WarningWidth, 0.025f, Mathf.Max(0.01f, length));
			_lane.GlobalPosition = _enemy.GlobalPosition + travel * 0.5f;
			_lane.GlobalPosition = _lane.GlobalPosition with { Y = 0.04f };
			_lane.Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.X, -direction.Z), 0.0f);
		}

		bool spiralWarning = _behavior.SpiralWarningActive;
		_ring.Visible = _behavior.RingWarningActive || spiralWarning;
		_gapLane.Visible = _behavior.RingWarningActive;
		if (_ring.Visible)
		{
			_ring.GlobalPosition = _enemy.GlobalPosition with { Y = 0.05f };
			_ringMaterial.AlbedoColor = new Color(1.0f, 0.2f, 0.15f, 0.45f + 0.25f * Mathf.Sin(_age * 18.0f));
		}

		if (_gapLane.Visible)
		{
			Vector3 gap = _behavior.RingGapDirection;
			_gapLane.GlobalPosition = (_enemy.GlobalPosition + gap * (_behavior.RingSpawnDistance + RingGapLaneLength * 0.5f))
				with { Y = 0.045f };
			_gapLane.Rotation = new Vector3(0.0f, Mathf.Atan2(-gap.X, -gap.Z), 0.0f);
		}

		for (int i = 0; i < MotherSpiral.Arms; i++)
		{
			_spiralGaps[i].Visible = _spiralArms[i].Visible = spiralWarning;
			if (!spiralWarning) continue;
			Vector3 gapCenter = _behavior.SpiralGapDirection.Rotated(Vector3.Up, Mathf.DegToRad(MotherSpiral.ArmSpacingDegrees * i));
			PlaceSpiralMark(_spiralGaps[i], gapCenter, 0.045f);
			PlaceSpiralMark(_spiralArms[i], MotherSpiral.Direction(i, 0, _behavior.SpiralGapDirection, _behavior.SpiralReverse), 0.05f);
		}
	}

	private MeshInstance3D NewSpiralMark(string name, Vector3 size, StandardMaterial3D material)
	{
		MeshInstance3D mark = new()
		{
			Name = name, TopLevel = true, Visible = false, Mesh = new BoxMesh { Size = size }, MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(mark);
		return mark;
	}

	// Pone una marca alargada en el suelo que parte de la distancia de nacimiento del anillo en la dirección dada.
	private void PlaceSpiralMark(MeshInstance3D mark, Vector3 direction, float height)
	{
		mark.GlobalPosition = (_enemy.GlobalPosition + direction * (_behavior.RingSpawnDistance + RingGapLaneLength * 0.5f)) with { Y = height };
		mark.Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.X, -direction.Z), 0.0f);
	}

	private void OnDied()
	{
		// El cuerpo desaparece por EnemyBase; una copia exclusivamente visual termina el efecto fuera de él.
		Node3D ghost = (Node3D)_visual.Duplicate();
		_enemy.GetParent().AddChild(ghost);
		ghost.GlobalTransform = _visual.GlobalTransform;
		_visual.Visible = false;
		Tween tween = ghost.CreateTween();
		tween.TweenProperty(ghost, "scale", Vector3.One * 0.01f, Mathf.Max(0.05f, DeathEffectTime));
		tween.TweenCallback(Callable.From(ghost.QueueFree));
	}
}
