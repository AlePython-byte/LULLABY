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
}
