using Godot;

public sealed partial class LevelSimulatorV3
{
	// DashBehavior.TryGetVelocity: la direccion se fija al EMPEZAR el aviso.
	private bool DashVelocity(Enemy enemy, Vector2 toPlayer, out Vector2 velocity)
	{
		GameplayParametersV3.DashStats dash = enemy.Stats.Dash;
		velocity = Vector2.Zero;
		if (float.IsNaN(enemy.DashCooldown)) enemy.DashCooldown = dash.FirstDelay;
		switch (enemy.DashState)
		{
			case DashBehavior.DashState.Waiting:
				enemy.DashCooldown -= _dt;
				float distance = toPlayer.Length();
				if (enemy.DashCooldown > 0.0f || distance < dash.MinDistance || distance > dash.MaxDistance || distance < 0.0001f)
					return false;
				enemy.DashState = DashBehavior.DashState.Warning;
				enemy.DashTimer = dash.WarnTime;
				enemy.DashDirection = toPlayer / distance;
				return true;
			case DashBehavior.DashState.Warning:
				enemy.DashTimer -= _dt;
				if (enemy.DashTimer <= 0.0f)
				{
					enemy.DashState = DashBehavior.DashState.Dashing;
					enemy.DashTimer = dash.DashTime;
					velocity = enemy.DashDirection * dash.DashSpeed;
				}
				return true;
			default:
				enemy.DashTimer -= _dt;
				velocity = enemy.DashDirection * dash.DashSpeed;
				return true;
		}
	}

	private static void DashAfterMove(Enemy enemy, bool hitPlayer, Vector2 realVelocity)
	{
		if (enemy.DashState != DashBehavior.DashState.Dashing) return;
		GameplayParametersV3.DashStats dash = enemy.Stats.Dash;
		if (hitPlayer || realVelocity.Length() < dash.DashSpeed * dash.BlockedFraction || enemy.DashTimer <= 0.0f)
		{
			enemy.DashState = DashBehavior.DashState.Waiting;
			enemy.DashCooldown = dash.Interval;
		}
	}

	private Vector2 DigVelocity(Enemy enemy)
	{
		if (!enemy.Underground) return Vector2.Zero;
		GameplayParametersV3.DigStats dig = enemy.Stats.Dig;
		Vector2 away = enemy.Position - _player;
		Vector2 direction = away.LengthSquared() > 0.0001f ? away.Normalized() : Vector2.Right;
		float orbit = Mathf.DegToRad(dig.OrbitDegrees);
		// La rotacion de Vector3 sobre Up tiene el signo contrario a Vector2.Rotated en XZ.
		direction = direction.Rotated(-orbit);
		Vector2 target = _player + direction * (dig.EmergeDistance / Mathf.Max(Mathf.Cos(orbit), 0.2f));
		Vector2 half = DigInnerHalf(dig);
		target = target.Clamp(-half, half);
		Vector2 toTarget = target - enemy.Position;
		float distance = toTarget.Length();
		return distance > 0.05f
			? toTarget / distance * Mathf.Min(enemy.Stats.Speed, distance / Mathf.Max(_dt, 0.0001f))
			: Vector2.Zero;
	}

	private Vector2 DigInnerHalf(GameplayParametersV3.DigStats dig)
	{
		return (_room.Size / 2.0f - new Vector2(dig.WallMargin, dig.WallMargin)).Max(Vector2.Zero);
	}

	private void DigAfterMove(Enemy enemy)
	{
		GameplayParametersV3.DigStats dig = enemy.Stats.Dig;
		enemy.DigTime += _dt;
		switch (enemy.DigPhase)
		{
			case DigBehavior.DigPhase.Out:
				if (!enemy.DigFired && !enemy.DigWarned && enemy.DigTime >= dig.FireDelay - dig.WarnTime)
					enemy.DigWarned = true;
				if (!enemy.DigFired && enemy.DigTime >= dig.FireDelay)
				{
					enemy.DigFired = true;
					if (dig.Projectile != null)
					{
						Vector2 toPlayer = _player - enemy.Position;
						Vector2 direction = toPlayer.LengthSquared() > 0.0001f ? toPlayer.Normalized() : new Vector2(0.0f, -1.0f);
						_newProjectiles.Add(new Projectile { Position = enemy.Position + direction * dig.Projectile.SpawnDistance,
							Direction = direction, Stats = dig.Projectile });
					}
				}
				if (enemy.DigTime >= dig.OutTime) EnterDig(enemy, DigBehavior.DigPhase.Sinking);
				break;
			case DigBehavior.DigPhase.Sinking:
				if (enemy.DigTime >= dig.SinkTime) EnterDig(enemy, DigBehavior.DigPhase.Underground);
				break;
			case DigBehavior.DigPhase.Underground:
				if (enemy.DigTime >= dig.UndergroundTime && enemy.Position.DistanceTo(_player) >= dig.MinEmergeDistance)
				{
					EnterDig(enemy, DigBehavior.DigPhase.Emerging);
				}
				else if (enemy.DigTime >= dig.MaxUndergroundTime)
				{
					Vector2 half = DigInnerHalf(dig);
					// Mismo orden y comparacion estricta de FarthestPoint: NO, NE, SE, SO.
					Vector2[] corners = { -half, new(half.X, -half.Y), half, new(-half.X, half.Y) };
					Vector2 best = corners[0];
					float bestDistance = -1.0f;
					foreach (Vector2 corner in corners)
					{
						float distance = corner.DistanceSquaredTo(_player);
						if (distance > bestDistance) { best = corner; bestDistance = distance; }
					}
					enemy.Position = best;
					enemy.ForcedEmerge = true;
					EnterDig(enemy, DigBehavior.DigPhase.Emerging);
				}
				break;
			case DigBehavior.DigPhase.Emerging:
				if (enemy.DigTime >= dig.EmergeTime)
				{
					EnterDig(enemy, DigBehavior.DigPhase.Out);
					enemy.DigWarned = false;
					enemy.DigFired = false;
				}
				break;
		}
	}

	private static void EnterDig(Enemy enemy, DigBehavior.DigPhase phase)
	{
		enemy.DigPhase = phase;
		enemy.DigTime = 0.0f;
	}
}
