using Godot;

/// <summary>
/// Marcador de aparición: un anillo de crayón sobre el suelo, en el punto donde va a salir un enemigo. Crece mientras dura la espera
/// (GrowTime, que RoomController iguala a SpawnDelay) para que Noah sepa dónde y cuándo saldrán; cuando el enemigo sale se llama a
/// Finish y el anillo se desvanece (FadeTime) y se libera solo. Solo es visual: no tiene colisión ni lógica de juego.
///
/// Trabaja en _Process, así que la pausa lo detiene igual que detiene la espera de la sala. Se construye por código (sin escena) con
/// un anillo y un disco tenue, sin sombras, para no depender de ningún recurso.
/// </summary>
public partial class SpawnMarker : Node3D
{
	/// <summary>Segundos que tarda el anillo en llegar a su tamaño completo.</summary>
	public float GrowTime { get; set; } = 0.7f;

	/// <summary>Segundos que tarda en desvanecerse tras Finish.</summary>
	public float FadeTime { get; set; } = 0.25f;

	/// <summary>Radio exterior del anillo ya crecido, en metros.</summary>
	public float Radius { get; set; } = 0.75f;

	public Color CrayonColor { get; set; } = new(0.86f, 0.36f, 0.30f);

	/// <summary>De 0 (recién creado) a 1 (tamaño completo).</summary>
	public float Progress { get; private set; }

	/// <summary>Finish ya se llamó: el anillo se está desvaneciendo.</summary>
	public bool Finishing { get; private set; }

	private const float StartScale = 0.25f;

	// Generar un toro y un cilindro cuesta del orden de un milisegundo: se hace una vez por radio y se reutiliza. Resolución baja a propósito
	// (el anillo se ve desde arriba, pequeño y semitransparente).
	private static readonly System.Collections.Generic.Dictionary<float, (Mesh Ring, Mesh Disc)> SharedMeshes = new();

	private Node3D _body;
	private StandardMaterial3D _ringMaterial;
	private StandardMaterial3D _discMaterial;
	private float _age;
	private float _fade;

	public override void _Ready()
	{
		_body = new Node3D { Name = "Body" };
		AddChild(_body);

		_ringMaterial = CreateMaterial();
		_discMaterial = CreateMaterial();

		// Anillo: un toro aplanado; el tubo mide una quinta parte del radio. Las mallas se comparten entre todos los marcadores.
		(Mesh ring, Mesh disc) = MeshesFor(Radius);
		_body.AddChild(new MeshInstance3D
		{
			Name = "Ring",
			Mesh = ring,
			MaterialOverride = _ringMaterial,
			Scale = new Vector3(1.0f, 0.15f, 1.0f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		_body.AddChild(new MeshInstance3D
		{
			Name = "Disc",
			Mesh = disc,
			MaterialOverride = _discMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		Apply();
	}

	/// <summary>El enemigo ya salió: el anillo se desvanece y desaparece.</summary>
	public void Finish()
	{
		Finishing = true;
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		if (Finishing)
		{
			_fade += step;
			if (_fade >= FadeTime)
			{
				QueueFree();
				return;
			}
		}
		else
		{
			_age += step;
			Progress = GrowTime > 0.0f ? Mathf.Clamp(_age / GrowTime, 0.0f, 1.0f) : 1.0f;
		}

		Apply();
	}

	private void Apply()
	{
		if (_body == null)
		{
			return;
		}

		// Crece deprisa al principio y se asienta al final; un latido pequeño le da aspecto de dibujo a mano.
		float eased = 1.0f - Mathf.Pow(1.0f - Progress, 2.0f);
		float pulse = Finishing ? 0.0f : 0.04f * Mathf.Sin(_age * 18.0f);
		float scale = Mathf.Lerp(StartScale, 1.0f, eased) + pulse;
		_body.Scale = new Vector3(scale, 1.0f, scale);

		float fadeOut = Finishing && FadeTime > 0.0f ? 1.0f - Mathf.Clamp(_fade / FadeTime, 0.0f, 1.0f) : 1.0f;
		_ringMaterial.AlbedoColor = CrayonColor with { A = (0.35f + 0.5f * Progress) * fadeOut };
		_discMaterial.AlbedoColor = CrayonColor with { A = (0.06f + 0.16f * Progress) * fadeOut };
	}

	private static (Mesh Ring, Mesh Disc) MeshesFor(float radius)
	{
		if (!SharedMeshes.TryGetValue(radius, out (Mesh Ring, Mesh Disc) meshes))
		{
			float tube = radius * 0.12f;
			float inner = radius - 2.0f * tube;
			meshes = (
				new TorusMesh { InnerRadius = inner, OuterRadius = radius, Rings = 24, RingSegments = 8 },
				new CylinderMesh { TopRadius = inner, BottomRadius = inner, Height = 0.005f, RadialSegments = 24, Rings = 1 });
			SharedMeshes[radius] = meshes;
		}

		return meshes;
	}

	private static StandardMaterial3D CreateMaterial()
	{
		return new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.0f),
		};
	}
}
