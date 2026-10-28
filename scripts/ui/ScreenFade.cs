using System.Threading.Tasks;
using Godot;

public partial class ScreenFade : CanvasLayer
{
	private ColorRect _rect;

	public override void _Ready()
	{
		_rect = GetNode<ColorRect>("Rect");
	}

	// alpha 1 = pantalla en negro, 0 = transparente.
	public async Task FadeTo(float alpha, float duration)
	{
		Tween tween = CreateTween();
		tween.TweenProperty(_rect, "modulate:a", alpha, duration);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}
