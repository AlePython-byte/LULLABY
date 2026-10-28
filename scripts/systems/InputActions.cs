using Godot;

public static class InputActions
{
	// Es seguro llamarlo en cada recarga de escena: no duplica teclas ya registradas.
	public static void Register(string action, params Key[] keys)
	{
		if (!InputMap.HasAction(action))
		{
			InputMap.AddAction(action);
		}

		foreach (Key key in keys)
		{
			InputEventKey keyEvent = new() { PhysicalKeycode = key };
			if (!InputMap.ActionHasEvent(action, keyEvent))
			{
				InputMap.ActionAddEvent(action, keyEvent);
			}
		}
	}
}
