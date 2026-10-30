using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

/// <summary>Pruebas de objetos y conservación de los parámetros base. Ejecutar ItemSystemTest.tscn en headless.</summary>
public partial class ItemSystemTest : Node
{
	private ItemCatalog _catalog;
	private int _checks;

	public override async void _Ready()
	{
		try
		{
			CheckCatalog();
			CheckHealth();
			await CheckPlayerItems();
			await CheckPickups();
			CheckBaseParameters();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			GD.Print($"ItemSystemTest: {_checks} comprobaciones. RESULTADO: OK");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GetTree().Quit(1);
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	private void Near(float actual, float expected, string message) =>
		Require(Mathf.IsEqualApprox(actual, expected), $"{message}: {actual} != {expected}");

	private void CheckCatalog()
	{
		_catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
		Require(_catalog != null && _catalog.IsValid(), "Catálogo válido");
		Require(_catalog.Items.Count == 5 && _catalog.ItemsPerTreasure == 1, "Cinco objetos y un objeto por tesoro por defecto");
		Require(_catalog.Items.Select(item => item.Effect).Distinct().Count() == 5, "Cinco efectos diferentes");
		Require(_catalog.Items.Select(item => item.Color).Distinct().Count() == 5, "Cinco colores diferentes");
		Require(_catalog.Items.Select(item => item.Shape).Distinct().Count() == 5, "Cinco formas diferentes");
		Near(_catalog.Find("biberon").Amount, 2.0f, "Curación");
		Near(_catalog.Find("fotografia_rota").Amount, 0.25f, "Daño");
		Near(_catalog.Find("chupete").Amount, 0.2f, "Cadencia");
		Near(_catalog.Find("oso_peluche").Amount, 1.0f, "Vida máxima");
		Near(_catalog.Find("manta_vieja").Amount, 0.2f, "Resistencia");
		GD.Print("Catálogo y presentación: OK");
	}

	private void CheckBaseParameters()
	{
		foreach (int version in new[] { 1, 2 })
		{
			GameplayParameters parameters = GameplayParameters.FromScenes();
			foreach (EnemyKinds.Info kind in EnemyKinds.List.Take(version == 1 ? 1 : 4))
				parameters.EnemyFor(EnemyKinds.Scene(kind.Kind));
			var actual = parameters.ToDictionary();
			using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(
				$"res://ai-training/datasets/levels_v{version}_labels.meta.json")));
			foreach (JsonProperty property in metadata.RootElement.GetProperty("gameplay").EnumerateObject())
				Require(actual.TryGetValue(property.Name, out double value) && value == property.Value.GetDouble(),
					$"Parámetro histórico v{version}: {property.Name}");
		}
		GD.Print("Parámetros y colisiones históricos v1/v2: OK");
	}

	private void CheckHealth()
	{
		HealthComponent health = new() { MaxHealth = 5.0f };
		int damaged = 0, died = 0, changed = 0;
		health.Damaged += (_, _) => damaged++;
		health.Died += () => died++;
		health.HealthChanged += (_, _) => changed++;
		AddChild(health);
		Near(health.Heal(2.0f), 0.0f, "No cura por encima del máximo");
		health.TakeDamage(4.0f);
		Near(health.Heal(2.0f), 2.0f, "Curación efectiva");
		Near(health.CurrentHealth, 3.0f, "Vida después de curar");
		Near(health.Heal(99.0f), 2.0f, "Curación limitada al máximo");
		Near(health.Heal(float.NaN), 0.0f, "Curación no finita rechazada");
		Near(health.Heal(-1.0f), 0.0f, "Curación negativa rechazada");
		Require(health.AddMaxHealthBonus(1.0f), "Aumento de vida máxima");
		Near(health.MaxHealth, 5.0f, "La vida máxima base no cambia");
		Near(health.EffectiveMaxHealth, 6.0f, "Vida máxima efectiva");
		Near(health.CurrentHealth, 6.0f, "El incremento añade vida actual");
		health.TakeDamage(99.0f);
		Near(health.Heal(2.0f), 0.0f, "Curación no revive");
		Require(!health.AddMaxHealthBonus(1.0f), "La mejora no revive");
		Require(damaged == 2 && died == 1, "Se conservan las señales de daño y muerte");
		health.ResetForNewRun();
		Near(health.EffectiveMaxHealth, 5.0f, "Reinicio elimina la bonificación");
		Near(health.CurrentHealth, 5.0f, "Reinicio recupera la vida base");
		Require(changed == 6, "Se notifican daño, curación, mejora y reinicio al HUD");
		health.Free();
		GD.Print("Curación, máximo por instancia, señales y reinicio: OK");
	}

	private async System.Threading.Tasks.Task CheckPlayerItems()
	{
		PackedScene scene = GD.Load<PackedScene>("res://scenes/player/Player.tscn");
		PlayerController player = scene.Instantiate<PlayerController>();
		PlayerController other = scene.Instantiate<PlayerController>();
		AddChild(player);
		AddChild(other);
		player.SetPhysicsProcess(false);
		other.SetPhysicsProcess(false);
		other.GetNode<PlayerShooter>("Shooter").SetPhysicsProcess(false);
		HealthComponent health = HealthComponent.FindIn(player);
		PlayerHealth protection = player.GetNode<PlayerHealth>("PlayerHealth");
		PlayerShooter shooter = player.GetNode<PlayerShooter>("Shooter");
		PlayerItems items = player.GetNode<PlayerItems>("Items");
		HealthHud hud = GD.Load<PackedScene>("res://scenes/ui/HealthHud.tscn").Instantiate<HealthHud>();
		AddChild(hud);
		int changed = 0;
		items.InventoryChanged += () => changed++;
		Require(!items.TryCollect(_catalog.Find("biberon")), "Biberón no consumido a vida completa");
		health.TakeDamage(3.0f);
		Require(items.TryCollect(_catalog.Find("biberon")), "Biberón recogido");
		Near(health.CurrentHealth, 4.0f, "Biberón recupera dos puntos");
		Require(items.TryCollect(_catalog.Find("fotografia_rota")), "Fotografía recogida");
		Require(!items.TryCollect(_catalog.Find("fotografia_rota")), "Fotografía no se acumula");
		Near(shooter.DamageMultiplier, 1.25f, "Multiplicador de daño");
		Require(items.TryCollect(_catalog.Find("chupete")), "Chupete recogido");
		Near(shooter.EffectiveShotsPerSecond, 3.6f, "Cadencia efectiva");
		Near(shooter.ShotsPerSecond, 3.0f, "Cadencia base intacta");
		Require(items.TryCollect(_catalog.Find("oso_peluche")), "Oso recogido");
		Near(health.EffectiveMaxHealth, 6.0f, "Oso aumenta el máximo efectivo");
		Near(health.CurrentHealth, 5.0f, "Oso añade un punto actual");
		Near(health.MaxHealth, 5.0f, "Oso conserva el máximo base");
		Require(items.TryCollect(_catalog.Find("manta_vieja")), "Manta recogida");
		Near(protection.DamageTakenMultiplier, 0.8f, "Resistencia");
		Require(protection.TakeHit(1.0f), "Golpe con resistencia");
		Near(health.CurrentHealth, 4.2f, "Daño reducido un 20 %");
		Require(hud.GetNode<Label>("Label").Text == $"Vida: {4.2f:0.#}/6", "HUD muestra salud fraccionaria y máximo efectivo");
		Require(!protection.TakeHit(1.0f), "Se conserva la invulnerabilidad");

		Input.ActionPress(PlayerShooter.ShootRight);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Input.ActionRelease(PlayerShooter.ShootRight);
		var tears = GetTree().GetNodesInGroup("tears").OfType<Tear>().ToArray();
		Require(tears.Length > 0, "Disparo real después de mejorar");
		foreach (Tear tear in tears) { Near(tear.Damage, 1.25f, "Daño aplicado a la instancia de lágrima"); tear.Free(); }
		Tear baseTear = shooter.TearScene.Instantiate<Tear>();
		Near(baseTear.Damage, 1.0f, "Recurso de lágrima intacto");
		baseTear.Free();
		Near(other.GetNode<PlayerShooter>("Shooter").DamageMultiplier, 1.0f, "Segundo jugador sin mejoras compartidas");
		Near(HealthComponent.FindIn(other).EffectiveMaxHealth, 5.0f, "Segundo jugador sin vida adicional");
		Require(items.CollectedItems.Count == 5, "Inventario registra los cinco objetos");
		items.ResetForNewRun();
		Require(items.CollectedItems.Count == 0 && changed == 6, "Inventario y señales al reiniciar");
		Near(shooter.DamageMultiplier, 1.0f, "Reinicio restaura daño");
		Near(shooter.EffectiveShotsPerSecond, 3.0f, "Reinicio restaura cadencia");
		Near(protection.DamageTakenMultiplier, 1.0f, "Reinicio restaura resistencia");
		Near(health.CurrentHealth, 5.0f, "Reinicio restaura salud");
		Near(health.EffectiveMaxHealth, 5.0f, "Reinicio restaura máximo");
		Require(hud.GetNode<Label>("Label").Text == "Vida: 5/5", "HUD actualizado al reiniciar");
		hud.Free();
		player.Free();
		other.Free();
		GD.Print("Cinco efectos, disparo real, duplicados, aislamiento por instancia y reinicio: OK");
	}

	private async System.Threading.Tasks.Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private async System.Threading.Tasks.Task CheckPickups()
	{
		PackedScene pickupScene = GD.Load<PackedScene>("res://scenes/items/ItemPickup.tscn");
		foreach (ItemDefinition definition in _catalog.Items)
		{
			ItemPickup sample = pickupScene.Instantiate<ItemPickup>();
			sample.Definition = definition;
			sample.Position = new Vector3(20.0f, 0.0f, 0.0f);
			AddChild(sample);
			MeshInstance3D mesh = sample.GetNode<MeshInstance3D>("Visual");
			Require(mesh.Mesh != null && ((StandardMaterial3D)mesh.MaterialOverride).AlbedoColor == definition.Color,
				"Forma y color de " + definition.Id);
			Require(sample.GetNode<Label3D>("NameLabel").Text == definition.DisplayName, "Nombre visible de " + definition.Id);
			Require(sample.CollisionLayer == 0 && sample.CollisionMask == CollisionLayers.Player, "Recogible no bloqueante");
			sample.Free();
		}
		PlayerController player = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
		player.Position = new Vector3(-3.0f, 0.5f, 0.0f);
		AddChild(player);
		player.SetPhysicsProcess(false);
		player.GetNode<PlayerShooter>("Shooter").SetPhysicsProcess(false);
		PlayerItems items = player.GetNode<PlayerItems>("Items");
		HealthComponent health = HealthComponent.FindIn(player);
		ItemPickup photo = pickupScene.Instantiate<ItemPickup>();
		photo.Definition = _catalog.Find("fotografia_rota");
		int collected = 0;
		photo.Collected += _ => collected++;
		AddChild(photo);
		await Frames(3);
		Require(items.CollectedItems.Count == 0, "No se recoge a distancia");
		player.GlobalPosition = new Vector3(0.0f, 0.5f, 0.0f);
		await Frames(3);
		Require(!GodotObject.IsInstanceValid(photo) && collected == 1 && items.CollectedItems.Count == 1,
			"Contacto físico recoge y elimina una sola vez");
		ItemPickup bottle = pickupScene.Instantiate<ItemPickup>();
		bottle.Position = new Vector3(10.0f, 0.0f, 0.0f);
		AddChild(bottle);
		Node3D enemy = new();
		AddChild(enemy);
		Require(!bottle.TryCollect(enemy), "Un enemigo no recoge objetos");
		Require(!bottle.TryCollect(player) && !bottle.IsCollected && bottle.Visible, "Biberón permanece a vida completa");
		health.TakeDamage(4.0f);
		bool reentrant = false;
		items.InventoryChanged += () => reentrant = bottle.TryCollect(player);
		Require(bottle.TryCollect(player), "Biberón recogido al necesitarlo");
		Require(!reentrant && !bottle.TryCollect(player), "Recogida protegida frente a callbacks repetidos y reentrantes");
		Near(health.CurrentHealth, 3.0f, "Solo una curación aplicada");
		await Frames(2);
		Require(!GodotObject.IsInstanceValid(bottle), "Eliminación diferida del recogible");
		enemy.Free();
		player.Free();
		GD.Print("Formas, colores, nombres, contacto físico y recogida única: OK");
	}
}
