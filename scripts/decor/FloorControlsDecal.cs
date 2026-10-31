using Godot;

/// <summary>
/// Los controles pintados a creyón en el piso de la sala de INICIO (assets/ui/controles_piso.png): una imagen plana sobre el suelo,
/// un quad horizontal con transparencia, sin colisión, a 2 cm del suelo (evita el parpadeo con el piso) y con la parte superior de la
/// imagen hacia el norte (-Z), que es "arriba" para la cámara. Se dibuja antes que lo demás (RenderPriority negativo, y a ras de suelo
/// queda bajo Noah, los enemigos y los objetos). Si la textura no existe no se crea nada y no pasa nada.
///
/// Mide 7,2 x 2,7 m (la proporción 8:3 de la imagen), centrado: deja libres las puertas (huecos de ±2,3 m a menos de 2 m de cada pared) y las
/// zonas de llegada (cuadrados de ±1,5 m a 2,5 m de cada pared). FreeDecorPlanner reserva ese rectángulo (con ReserveMargin) en el
/// inicio, haya textura o no, para que la decoración no dependa de si el archivo existe.
/// </summary>
public static class FloorControlsDecal
{
	public const string NodeName = "FloorControls";

	/// <summary>Altura sobre el suelo (m): suficiente para no parpadear con el piso, imperceptible al ojo.</summary>
	public const float Height = 0.02f;

	/// <summary>Margen (m) que la decoración exenta deja alrededor del cartel.</summary>
	public const float ReserveMargin = 0.3f;

	/// <summary>Tamaño del cartel en metros (X, Z).</summary>
	public static readonly Vector2 Size = new(7.2f, 2.7f);

	/// <summary>Ruta de la textura (las pruebas la apuntan a otra para comprobar que su ausencia no rompe nada).</summary>
	public static string TexturePath { get; set; } = "res://assets/ui/controles_piso.png";

	/// <summary>Rectángulo del cartel en coordenadas de la sala (X, Z), centrado en la sala.</summary>
	public static Rect2 Footprint => new(-Size / 2.0f, Size);

	/// <summary>Rectángulo que la decoración exenta no debe pisar: el cartel más su margen.</summary>
	public static Rect2 Reserved => Footprint.Grow(ReserveMargin);

	/// <summary>Crea el cartel como hijo de parent. Devuelve null (sin error) si la textura no existe.</summary>
	public static MeshInstance3D Build(Node3D parent)
	{
		if (!ResourceLoader.Exists(TexturePath) || ResourceLoader.Load<Texture2D>(TexturePath) is not Texture2D texture)
		{
			return null;
		}

		StandardMaterial3D material = new()
		{
			AlbedoTexture = texture,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Roughness = 1.0f,
			RenderPriority = -1,
		};
		MeshInstance3D decal = new()
		{
			Name = NodeName,
			// Plano horizontal con la normal hacia arriba. En un PlaneMesh la parte superior de la textura queda en -Z (norte).
			Mesh = new PlaneMesh { Size = Size },
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = new Vector3(0.0f, Height, 0.0f),
		};
		parent.AddChild(decal);
		return decal;
	}
}
