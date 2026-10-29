using Godot;
using AIVoxelStudio.Core;

namespace AIVoxelStudio.UI;

// 纯色体素调色板：点击标准色放入当前快捷格（布局见 VoxelInventoryOverlay.tscn）
public partial class VoxelInventoryOverlay : BlockInventoryOverlay
{
	private VoxelBlockPalette _voxel;

	protected override void Setup()
	{
		_voxel = (VoxelBlockPalette)Palette;
		SetTitle("调色板");

		for (int i = 0; i < _voxel.Catalog.Count; i++)
		{
			var button = Body.GetNodeOrNull<Button>($"Grid/Catalog{i + 1}");
			if (button == null) continue;

			int catalogIndex = i;
			button.Text = _voxel.Catalog[i].Label;
			ApplySwatchStyle(button, _voxel.Catalog[i].Swatch, false);
			button.Pressed += () => Choose(catalogIndex);
		}
	}

	private void Choose(int catalogIndex)
	{
		if (!_voxel.ReplaceSlot(_voxel.SelectedIndex, catalogIndex)) return;
		SetStatus($"第 {_voxel.SelectedIndex + 1} 格 → {_voxel.Catalog[catalogIndex].Label}");
	}
}
