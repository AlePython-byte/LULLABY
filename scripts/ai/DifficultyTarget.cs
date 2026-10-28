/// <summary>
/// Dificultad objetivo del director. Cada valor corresponde a un percentil de damage_total_mean en el entrenamiento
/// del modelo (25, 50 y 75); los números concretos viven en el JSON del modelo (difficulty_targets).
/// </summary>
public enum DifficultyTarget
{
	Easy,
	Normal,
	Hard,
}
