using Godot;

/// <summary>
/// Gira la parte gráfica del enemigo (el nodo hijo "Visual") para que mire al jugador. Yaw puro, solo visual: no toca la
/// física, la colisión ni la lógica del enemigo (ningún comportamiento lee la rotación del Visual) y el simulador no lo
/// ve. El modelo cuelga de Visual con su propio giro fijo de 180° (los .glb de los enemigos miran hacia +Z y el juego
/// usa -Z), así que con yaw 0 el frente queda hacia -Z, igual que PlayerController con Noah.
/// La primera vez se coloca de golpe hacia el jugador (sin barrido al aparecer); después gira a TurnSpeed (suavizado
/// exponencial, independiente de la tasa de fotogramas: 1 - e^(-TurnSpeed · delta)).
/// Va como nodo hijo del enemigo y trabaja en _Process; no es un EnemyBehavior (no decide movimiento).
/// </summary>
public partial class EnemyFacing : Node
{
	[Export] public float TurnSpeed { get; set; } = 10.0f;

	private Node3D _enemy;
	private Node3D _visual;
	private Node3D _player;
	private bool _placed;

	public override void _Ready()
	{
		_enemy = GetParent<Node3D>();
		_visual = _enemy.GetNodeOrNull<Node3D>("Visual");
	}

	public override void _Process(double delta)
	{
		if (_visual == null)
		{
			return;
		}

		if (!IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
			if (_player == null)
			{
				return;
			}
		}

		Vector3 toPlayer = _player.GlobalPosition - _enemy.GlobalPosition;
		toPlayer.Y = 0.0f;
		if (toPlayer.LengthSquared() < 0.0001f)
		{
			return;
		}

		// Mira hacia -Z cuando el yaw es 0 (igual que PlayerController).
		float targetYaw = Mathf.Atan2(-toPlayer.X, -toPlayer.Z);
		float yaw = _placed
			? Mathf.LerpAngle(_visual.Rotation.Y, targetYaw, 1.0f - Mathf.Exp(-TurnSpeed * (float)delta))
			: targetYaw;
		_placed = true;
		_visual.Rotation = new Vector3(0.0f, yaw, 0.0f);
	}
}
