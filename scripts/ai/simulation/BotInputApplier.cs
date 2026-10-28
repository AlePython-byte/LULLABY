using Godot;

/// <summary>
/// Traduce las decisiones de un BotBrain a pulsaciones de las acciones de entrada del juego real
/// (Input.ActionPress / ActionRelease), recordando qué teclas tiene pulsadas.
/// Herramienta de desarrollo: el juego no depende de esta clase.
/// </summary>
public sealed class BotInputApplier
{
	private static readonly string[] MoveActions =
	{
		PlayerController.MoveLeft, PlayerController.MoveRight, PlayerController.MoveUp, PlayerController.MoveDown,
	};

	private readonly bool[] _moving = new bool[4];
	private int _shooting = BotBrain.ShootNone;

	public void Apply(BotAction action)
	{
		bool[] wanted = { action.Move.X < 0.0f, action.Move.X > 0.0f, action.Move.Y < 0.0f, action.Move.Y > 0.0f };
		for (int i = 0; i < MoveActions.Length; i++)
		{
			SetPressed(MoveActions[i], ref _moving[i], wanted[i]);
		}

		if (action.Shoot == _shooting)
		{
			return;
		}

		if (_shooting != BotBrain.ShootNone)
		{
			Input.ActionRelease(PlayerShooter.Actions[_shooting]);
		}

		if (action.Shoot != BotBrain.ShootNone)
		{
			Input.ActionPress(PlayerShooter.Actions[action.Shoot]);
		}

		_shooting = action.Shoot;
	}

	public void ReleaseAll()
	{
		Apply(new BotAction { Move = Vector2.Zero, Shoot = BotBrain.ShootNone });
	}

	private static void SetPressed(string action, ref bool state, bool pressed)
	{
		if (state == pressed)
		{
			return;
		}

		if (pressed)
		{
			Input.ActionPress(action);
		}
		else
		{
			Input.ActionRelease(action);
		}

		state = pressed;
	}
}
