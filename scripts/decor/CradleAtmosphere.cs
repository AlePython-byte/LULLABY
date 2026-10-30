using Godot;

/// <summary>
/// Todos los valores de luz y ambiente de The Cradle en UN solo recurso editable desde el inspector de Godot:
/// el entorno (ambiente, niebla, tonemap, fondo) y la luz principal. AtmosphereApplier lo aplica al nivel al empezar.
/// No toca la cámara: solo cambian colores, energías y la orientación de la luz.
/// </summary>
[GlobalClass]
public partial class CradleAtmosphere : Resource
{
	/// <summary>Entorno completo (se edita en línea: ambiente, niebla, tonemap, brillo...).</summary>
	[Export] public Environment Environment { get; set; }

	[ExportGroup("Luz principal")]
	[Export] public Color LightColor { get; set; } = new(1.0f, 0.82f, 0.62f);
	[Export(PropertyHint.Range, "0,4,0.01")] public float LightEnergy { get; set; } = 0.9f;
	[Export] public Vector3 LightRotationDegrees { get; set; } = new(-55.0f, 30.0f, 0.0f);
	[Export] public bool LightShadows { get; set; } = true;
}
