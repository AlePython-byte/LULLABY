using System.Collections.Generic;
using Godot;

/// <summary>
/// Luces cálidas y suaves de cada sala decorada: 1 o 2 OmniLight3D SIN sombras y con rango limitado, para dar profundidad sin
/// saturar. Solo visual. Posición, color y energía salen de SeedMix (semilla de partida y celda): la misma sala, las mismas luces.
///
/// Para comparar rendimiento o apagarlas sin tocar lógica: RoomLights.Enabled = false (o el valor por defecto de abajo). Con ella
/// apagada Build no crea nada.
/// </summary>
public static class RoomLights
{
	/// <summary>Interruptor general. Cambiar este valor por defecto (o ponerlo a false en tiempo de ejecución) las desactiva todas.</summary>
	public static bool Enabled { get; set; } = true;

	public const string NodeName = "Lights";

	/// <summary>Altura de las luces (m), bajo el borde de las paredes (3 m).</summary>
	public const float Height = 2.2f;
	public const float Range = 7.0f;
	public const float MinEnergy = 0.6f;
	public const float MaxEnergy = 0.9f;

	/// <summary>Distancia mínima (m) de las luces a las paredes: quedan en el interior de la sala.</summary>
	public const float EdgeMargin = 3.0f;

	private const ulong Salt = 0x4C494754UL;

	public readonly record struct LightSpec(Vector3 Position, Color Color, float Energy, float Range);

	/// <summary>Plan de las luces de una sala (no depende de Enabled): 1 o 2; con dos, una en cada mitad (oeste y este).</summary>
	public static List<LightSpec> Plan(Vector2 size, Vector2I cell, int gameSeed)
	{
		RandomNumberGenerator random = SeedMix.RandomFor(gameSeed, cell, Salt);
		float reachX = size.X / 2.0f - EdgeMargin;
		float reachZ = size.Y / 2.0f - EdgeMargin;
		int count = random.Randf() < 0.5f ? 1 : 2;

		List<LightSpec> lights = new();
		for (int i = 0; i < count; i++)
		{
			float x = count == 1 ? random.RandfRange(-reachX, reachX) : (i == 0 ? random.RandfRange(-reachX, -1.0f) : random.RandfRange(1.0f, reachX));
			float z = random.RandfRange(-reachZ, reachZ);
			Color color = new(1.0f, random.RandfRange(0.72f, 0.82f), random.RandfRange(0.50f, 0.60f));
			lights.Add(new LightSpec(new Vector3(x, Height, z), color, random.RandfRange(MinEnergy, MaxEnergy), Range));
		}

		return lights;
	}

	/// <summary>Crea el nodo "Lights" con las luces de la sala, o devuelve null si están desactivadas.</summary>
	public static Node3D Build(Node3D room, Vector2 size, Vector2I cell, int gameSeed)
	{
		if (!Enabled)
		{
			return null;
		}

		Node3D root = new() { Name = NodeName };
		room.AddChild(root);
		List<LightSpec> lights = Plan(size, cell, gameSeed);
		for (int i = 0; i < lights.Count; i++)
		{
			root.AddChild(new OmniLight3D
			{
				Name = $"Light{i}",
				Position = lights[i].Position,
				LightColor = lights[i].Color,
				LightEnergy = lights[i].Energy,
				OmniRange = lights[i].Range,
				OmniAttenuation = 1.5f,
				LightSpecular = 0.3f,
				ShadowEnabled = false,
			});
		}

		return root;
	}
}
