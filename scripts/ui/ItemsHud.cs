using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Objetos obtenidos durante la partida, en la columna izquierda bajo los corazones: un ícono por objeto (ItemIcons) con su nombre en
/// texto pequeño y un contador si se repite. Sin ícono, la línea se muestra solo con texto. Las curaciones se consumen al instante: no
/// se quedan en la lista; si tienen ícono, se muestra un instante y se desvanece.
/// </summary>
public partial class ItemsHud : CanvasLayer
{
	/// <summary>Lado del ícono en pantalla (el arte es de 128 px; se reduce con filtro Nearest).</summary>
	public const float IconSide = 48.0f;
	public const float HealFlashSeconds = 1.1f;

	private const int NameFontSize = 14;
	private const int FallbackFontSize = 18;

	[Export] public PlayerItems Inventory { get; set; }
	private Label _label;
	private VBoxContainer _rows;
	private readonly Dictionary<string, Row> _listed = new(System.StringComparer.Ordinal);

	private sealed class Row
	{
		public HBoxContainer Root;
		public TextureRect Icon;
		public Label Name;
		public string DisplayName;
		public int Count;
	}

	/// <summary>Filas visibles de la lista (sin contar el destello de una curación).</summary>
	public int RowCount => _listed.Count;

	/// <summary>Contenedor vertical de las filas (para las pruebas y la captura).</summary>
	public VBoxContainer Rows => _rows;

	public int CountOf(string itemId) => _listed.TryGetValue(itemId, out Row row) ? row.Count : 0;

	public bool HasIcon(string itemId) => _listed.TryGetValue(itemId, out Row row) && row.Icon != null;

	public TextureRect IconOf(string itemId) => _listed.TryGetValue(itemId, out Row row) ? row.Icon : null;

	public string TextOf(string itemId) => _listed.TryGetValue(itemId, out Row row) ? row.Name.Text : null;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
		_rows = GetNode<VBoxContainer>("Rows");
		if (Inventory != null)
		{
			Inventory.InventoryChanged += OnInventoryChanged;
			Inventory.ItemCollected += OnItemCollected;
		}

		SetItems(Inventory?.CollectedItems, animate: false);
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Inventory))
		{
			Inventory.InventoryChanged -= OnInventoryChanged;
			Inventory.ItemCollected -= OnItemCollected;
		}
	}

	private void OnInventoryChanged() => SetItems(Inventory.CollectedItems, animate: true);

	/// <summary>Redibuja la lista. Las curaciones no se listan; los objetos repetidos se agrupan con contador.</summary>
	public void SetItems(IReadOnlyList<ItemDefinition> items, bool animate = true)
	{
		List<IGrouping<string, ItemDefinition>> groups = (items ?? new List<ItemDefinition>())
			.Where(item => item != null && item.Effect != ItemEffect.Heal)
			.GroupBy(item => item.Id, System.StringComparer.Ordinal)
			.ToList();

		foreach (string id in _listed.Keys.Where(id => groups.All(group => group.Key != id)).ToList())
		{
			_rows.RemoveChild(_listed[id].Root);
			_listed[id].Root.QueueFree();
			_listed.Remove(id);
		}

		foreach (IGrouping<string, ItemDefinition> group in groups)
		{
			int count = group.Count();
			if (!_listed.TryGetValue(group.Key, out Row row))
			{
				row = BuildRow(group.First());
				_listed[group.Key] = row;
				_rows.AddChild(row.Root);
			}
			else if (row.Count == count)
			{
				continue;
			}

			row.Count = count;
			row.Name.Text = row.DisplayName + (count > 1 ? $" ×{count}" : "");
			if (animate) Pop(row.Icon);
		}

		_label.Text = _listed.Count == 0 ? "Objetos: —" : "Objetos";
	}

	private void OnItemCollected(ItemDefinition item)
	{
		if (item.Effect != ItemEffect.Heal) return;
		Texture2D texture = ItemIcons.For(item.Id);
		if (texture == null) return;

		// La curación ya se aplicó: el ícono aparece un instante al final de la columna y se desvanece.
		Row flash = BuildRow(item);
		flash.Root.Name = "Flash_" + item.Id;
		_rows.AddChild(flash.Root);
		Pop(flash.Icon);
		Tween tween = flash.Root.CreateTween();
		tween.TweenInterval(HealFlashSeconds * 0.6);
		tween.TweenProperty(flash.Root, "modulate:a", 0.0f, HealFlashSeconds * 0.4);
		tween.TweenCallback(Callable.From(flash.Root.QueueFree));
	}

	private static Row BuildRow(ItemDefinition item)
	{
		Texture2D texture = ItemIcons.For(item.Id);
		Row row = new()
		{
			DisplayName = item.DisplayName,
			Root = new HBoxContainer { Name = "Row_" + item.Id, MouseFilter = Control.MouseFilterEnum.Ignore },
			Name = new Label { Name = "Name", MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center },
		};
		row.Root.AddThemeConstantOverride("separation", 8);
		row.Name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
		row.Name.AddThemeConstantOverride("outline_size", 4);

		if (texture != null)
		{
			row.Icon = new TextureRect
			{
				Name = "Icon",
				Texture = texture,
				CustomMinimumSize = new Vector2(IconSide, IconSide),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				PivotOffset = new Vector2(IconSide, IconSide) / 2.0f,
			};
			row.Root.AddChild(row.Icon);
			row.Name.AddThemeFontSizeOverride("font_size", NameFontSize);
		}
		else
		{
			// Respaldo: sin ícono, la línea de texto de siempre.
			row.Name.AddThemeFontSizeOverride("font_size", FallbackFontSize);
		}

		row.Name.Text = item.DisplayName;
		row.Root.AddChild(row.Name);
		return row;
	}

	// Aparece un poco más pequeño, se pasa un instante de su tamaño y se asienta. Discreto (~0,25 s).
	private static void Pop(TextureRect icon)
	{
		if (icon == null || !icon.IsInsideTree()) return;
		icon.Scale = new Vector2(0.6f, 0.6f);
		icon.Modulate = new Color(1, 1, 1, 0.4f);
		Tween tween = icon.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(icon, "scale", new Vector2(1.12f, 1.12f), 0.12).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(icon, "modulate:a", 1.0f, 0.12);
		tween.Chain().TweenProperty(icon, "scale", Vector2.One, 0.12).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}
}
