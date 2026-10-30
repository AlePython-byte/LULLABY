using Godot;

/// <summary>B lleva a la sala del jefe; U le quita 20 de vida. Sin modificadores y solo en depuración.</summary>
public partial class DebugBossControls : Node
{
	[Export] public RoomManager Rooms { get; set; }
	private const string JumpAction = "debug_boss_room";
	private const string DamageAction = "debug_damage_boss";

	public override void _Ready()
	{
		if (!OS.IsDebugBuild()) { SetProcessUnhandledInput(false); return; }
		InputActions.Register(JumpAction, Key.B);
		InputActions.Register(DamageAction, Key.U);
		GD.Print("Depuración del jefe: B va a su sala; U le quita 20 de vida. R reinicia el encuentro con la misma semilla.");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!OS.IsDebugBuild() || @event is not InputEventKey key || key.Echo || key.CtrlPressed || key.AltPressed
			|| key.ShiftPressed || key.MetaPressed || !IsInstanceValid(Rooms)) return;
		if (@event.IsActionPressed(JumpAction, false, true))
		{
			if (Rooms.DebugGoToBoss()) GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed(DamageAction, false, true) && DamageBoss())
			GetViewport().SetInputAsHandled();
	}

	public bool DamageBoss()
	{
		if (!OS.IsDebugBuild() || !IsInstanceValid(Rooms?.CurrentRoom?.BossEnemy)) return false;
		HealthComponent health = Rooms.CurrentRoom.BossEnemy.Health;
		if (health.IsDead) return false;
		health.TakeDamage(20.0f);
		return true;
	}
}
