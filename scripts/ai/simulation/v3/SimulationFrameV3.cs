using System;
using Godot;

/// <summary>Traza opcional para diagnosticos; no participa en decisiones ni genera etiquetas.</summary>
public sealed class SimulationFrameV3
{
	public readonly record struct EnemyFrame(EnemyKind Kind, Vector2 Position, float Health, int RecoilFrames,
		DashBehavior.DashState DashState, Vector2 DashDirection, float DashTimer, DigBehavior.DigPhase DigPhase,
		float DigTime, bool Vulnerable, bool ForcedEmerge, bool DigWarned, bool DigFired);
	public int Frame { get; init; }
	public Vector2I Cell { get; init; }
	public Vector2 Player { get; init; }
	public EnemyFrame[] Enemies { get; init; }
	public int ProjectileCount { get; init; }
}

public sealed partial class LevelSimulatorV3
{
	public event Action<SimulationFrameV3> FrameCompleted;

	private void PublishFrame()
	{
		if (FrameCompleted == null) return;
		SimulationFrameV3.EnemyFrame[] enemies = new SimulationFrameV3.EnemyFrame[_enemies.Count];
		for (int i = 0; i < enemies.Length; i++)
		{
			Enemy enemy = _enemies[i];
			enemies[i] = new(enemy.Stats.Kind, enemy.Position, enemy.Health, enemy.RecoilFrames,
				enemy.DashState, enemy.DashDirection, enemy.DashTimer, enemy.DigPhase, enemy.DigTime,
				!enemy.Underground, enemy.ForcedEmerge, enemy.DigWarned, enemy.DigFired);
		}
		FrameCompleted(new SimulationFrameV3 { Frame = _frame, Cell = _cell, Player = _player,
			Enemies = enemies, ProjectileCount = _projectiles.Count });
	}
}
