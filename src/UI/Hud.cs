using Godot;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Player;

namespace AIVoxelStudio.UI;

public partial class Hud : CanvasLayer
{
	private const int MaxSlots = 9;

	private readonly List<Panel> _slotPanels = new();
	private readonly List<TextureRect> _slotIcons = new();
	private PlayerController _player;
	private IBlockPalette _palette;
	private Label _statLabel;
	private Label _blockLabel;

	public event Action InventoryRequested;
	public IBlockPalette Palette => _palette;

	public override void _Ready()
	{
		GetNode<Control>("HudRoot").Theme = UiTheme.Create();
		_statLabel = GetNode<Label>("HudRoot/Stats/StatsBox/StatLabel");
		_blockLabel = GetNode<Label>("HudRoot/Stats/StatsBox/BlockLabel");

		for (int i = 0; i < MaxSlots; i++)
		{
			int index = i;
			var panel = GetNode<Panel>($"HudRoot/BottomCenter/Hotbar/Slot{i + 1}");
			panel.GuiInput += ev =>
			{
				if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
					Select(index);
			};
			_slotPanels.Add(panel);

			var icon = new TextureRect
			{
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false,
			};
			icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			icon.OffsetLeft = 4;
			icon.OffsetTop = 4;
			icon.OffsetRight = -4;
			icon.OffsetBottom = -4;
			panel.AddChild(icon);
			panel.MoveChild(icon, 0);
			_slotIcons.Add(icon);

			var number = panel.GetNodeOrNull<Label>("Number");
			if (number != null)
			{
				number.AddThemeColorOverride("font_outline_color", Colors.White);
				number.AddThemeConstantOverride("outline_size", 2);
			}
		}

		Refresh();
	}

	public void Bind(PlayerController player, IBlockPalette palette)
	{
		if (_palette != null) _palette.Changed -= Refresh;
		_player = player;
		_palette = palette;
		if (_palette != null) _palette.Changed += Refresh;
		Refresh();
	}

	public void Select(int index)
	{
		_palette?.Select(index);
	}

	private void Refresh()
	{
		if (_palette == null) return;

		for (int i = 0; i < _slotPanels.Count; i++)
		{
			bool visible = i < _palette.Slots.Count;
			_slotPanels[i].Visible = visible;
			if (!visible) continue;
			bool selected = i == _palette.SelectedIndex;
			var slot = _palette.Slots[i];
			_slotPanels[i].TooltipText = $"{i + 1}  {slot.Label}";
			_slotPanels[i].AddThemeStyleboxOverride("panel", SlotStyle(slot.Swatch, selected));
			_slotIcons[i].Texture = slot.Icon;
			_slotIcons[i].Visible = slot.Icon != null;
		}

		if (_blockLabel != null) _blockLabel.Text = $"方块：{_palette.DescribeSelected()}";
	}

	public override void _UnhandledInput(InputEvent e)
	{
		// AI 面板 / 背包等模态打开时输入被禁用，屏蔽快捷栏操作
		if (_player != null && !_player.InputEnabled) return;

		if (e is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.E)
			{
				InventoryRequested?.Invoke();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key9)
			{
				Select((int)(key.Keycode - Key.Key1));
				return;
			}
		}

		if (e is InputEventMouseButton { Pressed: true } button && _palette != null)
		{
			if (button.ButtonIndex == MouseButton.WheelUp) Select(_palette.SelectedIndex - 1);
			else if (button.ButtonIndex == MouseButton.WheelDown) Select(_palette.SelectedIndex + 1);
		}
	}

	public override void _Process(double delta)
	{
		if (_player == null || _statLabel == null) return;
		var pos = _player.GlobalPosition;
		_statLabel.Text = $"位置 {pos.X:0.00}, {pos.Y:0.00}, {pos.Z:0.00} m   模式 {(_player.Flying ? "飞行" : "行走")}   FPS {Engine.GetFramesPerSecond()}";
	}

	private static StyleBoxFlat SlotStyle(Color color, bool selected)
	{
		var fill = color.A > 0.01f ? color : new Color(0.55f, 0.55f, 0.6f, 0.35f);
		var style = new StyleBoxFlat { BgColor = fill };
		style.SetBorderWidthAll(selected ? 3 : 1);
		style.BorderColor = selected ? Colors.White : new Color(0f, 0f, 0f, 0.7f);
		style.SetCornerRadiusAll(3);
		return style;
	}
}
