using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.UI;

// 背包/调色板浮层：静态布局见 BlockInventoryOverlay.tscn，子类只绑定目录区数据
public partial class BlockInventoryOverlay : CanvasLayer
{
	private readonly List<Button> _slotButtons = new();
	private Control _root;
	private Label _title;
	private Label _status;

	protected IBlockPalette Palette { get; private set; }
	protected VBoxContainer Body { get; private set; }

	public event Action Closed;
	public bool IsOpen => _root is { Visible: true };

	// 必须在加入场景树前调用
	public void SetPalette(IBlockPalette palette) => Palette = palette;

	public override void _Ready()
	{
		_root = GetNode<Control>("Root");
		_root.Theme = UiTheme.Create();
		_title = GetNode<Label>("Root/Center/Panel/Box/Header/TitleLabel");
		_status = GetNode<Label>("Root/Center/Panel/Box/StatusLabel");
		Body = GetNode<VBoxContainer>("Root/Center/Panel/Box/Body");

		GetNode<Button>("Root/Center/Panel/Box/Header/CloseButton").Pressed += Close;

		var hotbar = GetNode<HBoxContainer>("Root/Center/Panel/Box/Hotbar");
		for (int i = 0; i < hotbar.GetChildCount(); i++)
		{
			if (hotbar.GetChild(i) is not Button button) continue;
			int index = i;
			button.Pressed += () => Palette?.Select(index);
			_slotButtons.Add(button);
		}

		if (Palette == null)
		{
			GD.PushError($"{GetType().Name}: 未设置 Palette");
			return;
		}

		Palette.Changed += RefreshSlots;
		Setup();
		RefreshSlots();
	}

	// 子类在这里绑定目录区节点并填充数据
	protected virtual void Setup() { }

	protected void SetTitle(string text)
	{
		if (_title != null) _title.Text = text;
	}

	protected void SetStatus(string text)
	{
		if (_status != null) _status.Text = text;
	}

	public void Open()
	{
		if (_root == null) return;
		_root.Visible = true;
		RefreshSlots();
	}

	public void Close()
	{
		if (_root == null || !_root.Visible) return;
		_root.Visible = false;
		Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!IsOpen) return;
		if (e is not InputEventKey { Pressed: true, Echo: false } key) return;

		// 搜索框里输入 e 时应正常输入文字
		bool close = key.Keycode == Key.Escape ||
			(key.Keycode == Key.E && GetViewport().GuiGetFocusOwner() is not LineEdit);
		if (!close) return;

		Close();
		GetViewport().SetInputAsHandled();
	}

	public override void _ExitTree()
	{
		if (Palette != null) Palette.Changed -= RefreshSlots;
		base._ExitTree();
	}

	private void RefreshSlots()
	{
		if (Palette == null) return;

		for (int i = 0; i < _slotButtons.Count; i++)
		{
			bool visible = i < Palette.Slots.Count;
			_slotButtons[i].Visible = visible;
			if (!visible) continue;

			var slot = Palette.Slots[i];
			var button = _slotButtons[i];
			var icon = slot.Icon;
			button.Icon = icon;
			button.ExpandIcon = icon != null;
			button.Text = icon != null ? $"{i + 1}" : $"{i + 1} {slot.Label}";
			button.TooltipText = $"第 {i + 1} 格：{slot.Label}";
			ApplySwatchStyle(button, slot.Swatch, i == Palette.SelectedIndex);
		}
	}

	protected static void ApplySwatchStyle(Button button, Color swatch, bool selected)
	{
		var normal = SwatchStyle(swatch, selected);
		button.AddThemeStyleboxOverride("normal", normal);
		button.AddThemeStyleboxOverride("hover", SwatchStyle(swatch.Lightened(0.12f), selected));
		button.AddThemeStyleboxOverride("pressed", normal);
		button.AddThemeStyleboxOverride("focus", normal);

		var text = ReadableTextColor(swatch);
		button.AddThemeColorOverride("font_color", text);
		button.AddThemeColorOverride("font_hover_color", text);
		button.AddThemeColorOverride("font_pressed_color", text);
		button.AddThemeColorOverride("font_focus_color", text);
	}

	private static StyleBoxFlat SwatchStyle(Color swatch, bool selected)
	{
		var fill = swatch.A > 0.05f ? swatch : new Color(0.55f, 0.55f, 0.6f, 0.35f);
		var style = new StyleBoxFlat { BgColor = fill };
		style.SetBorderWidthAll(selected ? 3 : 1);
		style.BorderColor = selected ? Colors.White : new Color(0f, 0f, 0f, 0.7f);
		style.SetCornerRadiusAll(4);
		return style;
	}

	private static Color ReadableTextColor(Color swatch)
	{
		if (swatch.A < 0.5f) return Colors.White;
		float luminance = swatch.R * 0.299f + swatch.G * 0.587f + swatch.B * 0.114f;
		return luminance > 0.62f ? Colors.Black : Colors.White;
	}
}
