using Godot;

/// <summary>
/// Aplica un CradleAtmosphere al WorldEnvironment y a la luz direccional del nivel al entrar en el árbol. Sin recurso asignado
/// no toca nada (el nivel se ve como siempre). Aplicar de nuevo con Apply() sirve para probar cambios.
/// </summary>
public partial class AtmosphereApplier : Node
{
	[Export] public CradleAtmosphere Atmosphere { get; set; }
	[Export] public WorldEnvironment World { get; set; }
	[Export] public DirectionalLight3D Light { get; set; }

	public override void _Ready()
	{
		Apply();
	}

	public void Apply()
	{
		if (Atmosphere == null)
		{
			return;
		}

		if (World != null && Atmosphere.Environment != null)
		{
			World.Environment = Atmosphere.Environment;
		}

		if (Light != null)
		{
			Light.LightColor = Atmosphere.LightColor;
			Light.LightEnergy = Atmosphere.LightEnergy;
			Light.RotationDegrees = Atmosphere.LightRotationDegrees;
			Light.ShadowEnabled = Atmosphere.LightShadows;
		}
	}
}
