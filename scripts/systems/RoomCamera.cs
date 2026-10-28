using Godot;

public partial class RoomCamera : Node3D
{
	// Grados de inclinación del soporte (negativo = mira hacia abajo).
	[Export] public float Pitch { get; set; } = -60.0f;

	// Factor de holgura sobre el encuadre exacto (1.05 = 5 % de margen).
	[Export] public float Margin { get; set; } = 1.05f;
	[Export] public float MinDistance { get; set; } = 8.0f;

	private Camera3D _camera;
	private Vector2 _roomSize;
	private float _wallHeight;
	private float _wallThickness;
	private bool _framed;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		GetViewport().SizeChanged += Refit;
	}

	public override void _ExitTree()
	{
		GetViewport().SizeChanged -= Refit;
	}

	public void Frame(Vector2 roomSize, float wallHeight, float wallThickness)
	{
		_roomSize = roomSize;
		_wallHeight = wallHeight;
		_wallThickness = wallThickness;
		_framed = true;
		Refit();
	}

	// La distancia se calcula sobre las 8 esquinas de la caja suelo + paredes: la parte alta de la pared cercana
	// entra en el cálculo, de modo que no se sale por abajo de la pantalla ni deja cortado el suelo.
	private void Refit()
	{
		if (!_framed)
		{
			return;
		}

		RotationDegrees = new Vector3(Pitch, 0.0f, 0.0f);

		Basis inverse = new Basis(Vector3.Right, Mathf.DegToRad(Pitch)).Inverse();
		Vector2 viewport = GetViewport().GetVisibleRect().Size;
		float tanVertical = Mathf.Tan(Mathf.DegToRad(_camera.Fov) / 2.0f);
		float tanHorizontal = tanVertical * viewport.X / viewport.Y;

		float hx = _roomSize.X / 2.0f + _wallThickness;
		float hz = _roomSize.Y / 2.0f + _wallThickness;
		float distance = MinDistance;

		foreach (float y in new[] { 0.0f, _wallHeight })
		{
			foreach (float sx in new[] { -1.0f, 1.0f })
			{
				foreach (float sz in new[] { -1.0f, 1.0f })
				{
					// Coordenadas del punto en el espacio del soporte; la cámara mira hacia -Z local.
					Vector3 q = inverse * new Vector3(sx * hx, y, sz * hz);
					float needed = Mathf.Max(Mathf.Abs(q.X) / tanHorizontal, Mathf.Abs(q.Y) / tanVertical);
					distance = Mathf.Max(distance, q.Z + Margin * needed);
				}
			}
		}

		_camera.Position = new Vector3(0.0f, 0.0f, distance);
	}
}
