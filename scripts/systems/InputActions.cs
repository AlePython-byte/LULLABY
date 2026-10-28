using Godot;

public static class InputActions
{
	public static void Register(string action, params Key[] keys)
	{
		if (!InputMap.HasAction(action))
		{
			InputMap.AddAction(action);
		}

		foreach (Key key in keys)
		{
			InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
		}
	}
}
