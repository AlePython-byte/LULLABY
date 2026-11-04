using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Comprueba los íconos del HUD de objetos (ItemsHud + ItemIcons). Modo headless:
///   godot --headless --fixed-fps 60 --path . res://tests/ItemsHudTest.tscn
///   1. El mapa cubre los 5 objetos con textura de 128×128 (el corazón del suelo no tiene ícono) y las retiene.
///   2. Cada fila lleva su ícono de 48 px con filtro Nearest y el nombre en texto pequeño; las filas se apilan en vertical sin solaparse.
///   3. El mismo objeto dos veces: una sola fila con contador ×2.
///   4. Las curaciones no quedan en la lista; si tienen ícono se muestra un destello que se desvanece y se libera.
///   5. La animación de aparición termina asentada en escala 1 y opacidad 1.
///   6. Respaldo: sin ícono la línea es solo texto, sin errores.
///   7. Liberar el HUD (con animaciones en curso) o el nivel entero no deja errores, y las texturas siguen válidas tras recolectar la basura.
/// Sale con código 0 si todo pasa.
/// </summary>
public partial class ItemsHudTest : Node
{
	private int _checks;
	private ItemCatalog _catalog;

	public override async void _Ready()
	{
		try
		{
			GD.Print($"=== ItemsHudTest ({(OS.IsDebugBuild() ? "Debug" : "Release")}) ===");
			ItemIcons.Reset();
			_catalog = GD.Load<ItemCatalog>("res://resources/items/Catalog.tres");
			CheckMap();
			await CheckRows();
			await CheckHeal();
			await CheckFallback();
			await CheckFree();
			await CheckRetention();
			GD.Print($"ItemsHudTest: {_checks} comprobaciones. RESULTADO: OK");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("RESULTADO: FALLO");
			await BossTestHarness.CollectWrappers(GetTree());
			GetTree().Quit(1);
		}
		finally
		{
			ItemIcons.Folder = ItemIcons.DefaultFolder;
		}
	}

	private void Require(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidDataException(message);
	}

	// El corazón del suelo no está en el catálogo del tesoro: se carga su recurso.
	private ItemDefinition Item(string id) => id == "corazon" ? GD.Load<ItemDefinition>("res://resources/items/corazon.tres") : _catalog.Find(id);

	private ItemsHud NewHud()
	{
		ItemsHud hud = GD.Load<PackedScene>("res://scenes/ui/ItemsHud.tscn").Instantiate<ItemsHud>();
		AddChild(hud);
		return hud;
	}

	private void CheckMap()
	{
		GD.Print("\n--- Mapa ---");
		string[] ids = { "chupete", "biberon", "fotografia_rota", "manta_vieja", "oso_peluche" };
		Require(ItemIcons.Files.Count == 5 && ids.All(ItemIcons.Files.ContainsKey), "el mapa tiene exactamente los 5 objetos");
		foreach (string id in ids)
		{
			Texture2D texture = ItemIcons.For(id);
			Require(texture != null && texture.GetSize() == new Vector2(128, 128), $"{id}: textura de 128×128");
			Require(ItemIcons.For(id) == texture, $"{id}: se carga una sola vez");
		}

		Require(ItemIcons.Held.Count == 5, "las 5 texturas quedan retenidas");
		Require(ItemIcons.For("corazon") == null && ItemIcons.For("inexistente") == null && ItemIcons.For(null) == null, "sin ícono asignado devuelve null");
		Require(ids.All(id => _catalog.Find(id) != null), "los 5 ids existen en el catálogo");
	}

	private async Task CheckRows()
	{
		GD.Print("\n--- Filas ---");
		ItemsHud hud = NewHud();
		Label title = hud.GetNode<Label>("Label");
		Require(hud.RowCount == 0 && title.Text == "Objetos: —", "vacío al empezar");
		hud.SetItems(new[] { Item("oso_peluche"), Item("chupete"), Item("manta_vieja") });
		Require(hud.RowCount == 3 && title.Text == "Objetos", "tres filas");
		TextureRect icon = hud.IconOf("oso_peluche");
		Require(icon != null && icon.TextureFilter == CanvasItem.TextureFilterEnum.Nearest && icon.CustomMinimumSize == new Vector2(48, 48), "ícono de 48 px con filtro Nearest");
		Require(hud.TextOf("oso_peluche") == "Oso de peluche", "el nombre acompaña al ícono");

		// Animación: arranca pequeño y se asienta.
		Require(icon.Scale.X < 1.0f, "al aparecer empieza más pequeño");
		await BossTestHarness.Frames(GetTree(), 40);
		Require(icon.Scale == Vector2.One && Mathf.IsEqualApprox(icon.Modulate.A, 1.0f), $"se asienta en escala 1 ({icon.Scale}, α={icon.Modulate.A})");

		// Apilado vertical sin solaparse, debajo del título y dentro de la pantalla.
		Rect2 previous = default;
		foreach (string id in new[] { "oso_peluche", "chupete", "manta_vieja" })
		{
			Rect2 rect = hud.Rows.GetChildren().OfType<Control>().First(c => c.Name == "Row_" + id).GetRect();
			Require(previous.Size == Vector2.Zero || rect.Position.Y >= previous.End.Y, $"{id} no solapa a la anterior");
			previous = rect;
		}

		Require(hud.Rows.GlobalPosition.Y >= title.GetGlobalRect().End.Y, "las filas quedan debajo del título");
		Require(hud.Rows.GlobalPosition.Y + hud.Rows.GetCombinedMinimumSize().Y <= 648, "caben en una pantalla de 648 px");

		// Contador.
		hud.SetItems(new[] { Item("oso_peluche"), Item("oso_peluche"), Item("chupete") });
		Require(hud.RowCount == 2 && hud.CountOf("oso_peluche") == 2 && hud.TextOf("oso_peluche") == "Oso de peluche ×2", "el mismo objeto dos veces: una fila con ×2");
		Require(hud.CountOf("manta_vieja") == 0, "lo que ya no está se quita");
		hud.SetItems(Array.Empty<ItemDefinition>());
		Require(hud.RowCount == 0 && hud.Rows.GetChildCount() == 0 && title.Text == "Objetos: —", "lista vacía al reiniciar");
		hud.Free();
	}

	private async Task CheckHeal()
	{
		GD.Print("\n--- Curaciones ---");
		ItemsHud hud = NewHud();
		hud.SetItems(new[] { Item("biberon"), Item("corazon"), Item("fotografia_rota") });
		Require(hud.RowCount == 1 && hud.CountOf("biberon") == 0 && hud.CountOf("corazon") == 0, "las curaciones no quedan en la lista");
		hud.Free();

		// Con un jugador real: el biberón muestra un destello breve que se libera solo.
		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		await BossTestHarness.Frames(GetTree(), 4);
		ItemsHud real = level.Root.GetNode<ItemsHud>("ItemsHud");
		PlayerItems items = level.Root.GetNode<PlayerItems>("Player/Items");
		HealthComponent.FindIn(level.Player).TakeDamage(2.0f);
		Require(items.TryCollect(Item("biberon")), "el biberón cura");
		Require(real.RowCount == 0 && real.Rows.GetChildCount() == 1 && real.Rows.GetChild(0).Name == "Flash_biberon", "destello del biberón al final de la columna, fuera de la lista");
		await BossTestHarness.Frames(GetTree(), (int)(ItemsHud.HealFlashSeconds * 60) + 10);
		Require(real.Rows.GetChildCount() == 0, "el destello se desvanece y se libera");
		await BossTestHarness.Frames(GetTree(), 120); // pasa la invulnerabilidad tras el daño anterior
		HealthComponent.FindIn(level.Player).TakeDamage(1.0f);
		Require(HealthComponent.FindIn(level.Player).CurrentHealth < 5.0f, "el jugador recibió daño");
		Require(items.TryCollect(Item("corazon")), "el corazón cura");
		Require(real.Rows.GetChildCount() == 0, "el corazón no tiene ícono: sin destello");
		await BossTestHarness.Close(GetTree(), level);
	}

	private async Task CheckFallback()
	{
		GD.Print("\n--- Respaldo ---");
		ItemIcons.Folder = "res://assets/ui/no_existe/";
		ItemIcons.Reset();
		Require(ItemIcons.For("oso_peluche") == null, "sin archivo no hay textura");
		ItemsHud hud = NewHud();
		hud.SetItems(new[] { Item("oso_peluche"), Item("chupete"), Item("chupete") });
		await BossTestHarness.Frames(GetTree(), 2);
		Require(hud.RowCount == 2 && !hud.HasIcon("oso_peluche") && hud.TextOf("oso_peluche") == "Oso de peluche", "la línea queda solo con texto");
		Require(hud.TextOf("chupete").EndsWith("×2"), "el contador también funciona sin ícono");
		hud.Free();

		// Mezcla: un objeto sin ícono junto a otro con ícono.
		ItemIcons.Folder = ItemIcons.DefaultFolder;
		ItemIcons.Reset();
		ItemsHud mixed = NewHud();
		ItemDefinition noIcon = new() { Id = "sin_icono", DisplayName = "Sin ícono", Effect = ItemEffect.Damage, Amount = 0.1f };
		mixed.SetItems(new[] { Item("oso_peluche"), noIcon });
		Require(mixed.HasIcon("oso_peluche") && !mixed.HasIcon("sin_icono") && mixed.TextOf("sin_icono") == "Sin ícono", "un objeto sin ícono convive con los demás");
		mixed.Free();
	}

	private async Task CheckFree()
	{
		GD.Print("\n--- Liberar ---");
		ItemsHud hud = NewHud();
		hud.SetItems(new[] { Item("oso_peluche"), Item("manta_vieja") });
		hud.Free(); // con las animaciones recién lanzadas
		await BossTestHarness.Frames(GetTree(), 40);
		Require(true, "liberar el HUD con animaciones en curso no falla");

		BossTestHarness.Level level = BossTestHarness.Open(this, BossTestHarness.Seed, unlimitedHealth: false);
		await BossTestHarness.Frames(GetTree(), 4);
		PlayerItems items = level.Root.GetNode<PlayerItems>("Player/Items");
		Require(items.TryCollect(Item("oso_peluche")) && level.Root.GetNode<ItemsHud>("ItemsHud").HasIcon("oso_peluche"), "en el nivel, recoger un objeto añade su ícono");
		items.ResetForNewRun();
		Require(level.Root.GetNode<ItemsHud>("ItemsHud").RowCount == 0, "reiniciar la partida limpia las filas");
		items.TryCollect(Item("chupete"));
		await BossTestHarness.Close(GetTree(), level);
		Require(true, "liberar el nivel con el HUD suscrito no falla");
	}

	private async Task CheckRetention()
	{
		GD.Print("\n--- Retención ---");
		ItemIcons.Reset();
		ItemsHud hud = NewHud();
		hud.SetItems(new[] { Item("oso_peluche"), Item("chupete"), Item("manta_vieja"), Item("fotografia_rota") });
		Require(ItemIcons.For("biberon") != null, "el ícono del biberón también se carga");
		hud.Free();
		await BossTestHarness.CollectWrappers(GetTree());
		Require(ItemIcons.Held.Count == 5 && ItemIcons.Held.All(t => GodotObject.IsInstanceValid(t) && t.GetSize() == new Vector2(128, 128)), "las texturas siguen válidas tras liberar el HUD y recolectar la basura");
		ItemsHud again = NewHud();
		again.SetItems(new[] { Item("oso_peluche") });
		Require(again.HasIcon("oso_peluche"), "un HUD nuevo vuelve a usarlas");
		again.Free();
	}
}
