using Godot;

/// <summary>
/// Oyente de los sonidos 3D, colgado del soporte de la cámara (que está en el centro de la sala, a nivel de suelo). La cámara alta
/// queda a ~30 m de todo, así que oír desde ella daría una distancia casi constante; desde el centro de la sala la distancia y la
/// panorámica de cada sonido son coherentes con lo que se ve.
/// </summary>
public partial class CurrentAudioListener : AudioListener3D
{
	public override void _Ready()
	{
		MakeCurrent();
	}
}
