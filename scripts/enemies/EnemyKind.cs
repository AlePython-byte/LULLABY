using System.Collections.Generic;
using Godot;

/// <summary>
/// Tipos de enemigo. El valor numérico es el que guardan las plantillas y RoomData en SpawnKinds, así que un valor
/// existente no se puede cambiar ni reutilizar: los tipos nuevos se añaden al final.
/// </summary>
public enum EnemyKind
{
	Chaser = 0,
	Shooter = 1,
	Exploder = 2,
	Flyer = 3,
}

/// <summary>
/// Lista central de tipos de enemigo: nombre, color provisional (se aplica a la parte gráfica mientras no haya modelo
/// final) y escena. Añadir un tipo = un valor nuevo en EnemyKind, una entrada aquí y su escena.
/// </summary>
public static class EnemyKinds
{
	public readonly record struct Info(EnemyKind Kind, string Name, Color Color, string ScenePath);

	private static readonly Info[] All =
	{
		new(EnemyKind.Chaser, "Básico", new Color(0.45f, 0.12f, 0.12f), "res://scenes/enemies/BasicEnemy.tscn"),
		new(EnemyKind.Shooter, "Tirador", new Color(0.42f, 0.22f, 0.58f), "res://scenes/enemies/ShooterEnemy.tscn"),
		new(EnemyKind.Exploder, "Explosivo", new Color(0.9f, 0.45f, 0.08f), "res://scenes/enemies/ExploderEnemy.tscn"),
		new(EnemyKind.Flyer, "Volador", new Color(0.12f, 0.62f, 0.62f), "res://scenes/enemies/FlyerEnemy.tscn"),
	};

	public static IReadOnlyList<Info> List => All;

	public static bool IsValid(int value)
	{
		return value >= 0 && value < All.Length;
	}

	public static Info Get(EnemyKind kind)
	{
		return All[(int)kind];
	}

	public static PackedScene Scene(EnemyKind kind)
	{
		return GD.Load<PackedScene>(Get(kind).ScenePath);
	}
}
