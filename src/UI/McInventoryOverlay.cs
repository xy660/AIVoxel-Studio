using Godot;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.UI;

// 我的世界背包：搜索并点击方块放入当前选中的快捷格（布局见 McInventoryOverlay.tscn）
public partial class McInventoryOverlay : BlockInventoryOverlay
{
	private const int MaxResults = 200;

	private readonly List<McBlockDef> _filtered = new();

	private McAssets _assets;
	private LineEdit _search;
	private ItemList _list;

	protected override void Setup()
	{
		var palette = (McBlockPalette)Palette;
		_assets = palette.Assets;
		SetTitle("背包");

		_search = Body.GetNode<LineEdit>("Search");
		_search.TextChanged += ApplyFilter;

		_list = Body.GetNode<ItemList>("List");
		_list.ItemClicked += (index, _, button) =>
		{
			if ((MouseButton)button == MouseButton.Left) Choose((int)index);
		};

		ApplyFilter("");
	}

	private void ApplyFilter(string query)
	{
		query = (query ?? "").Trim().ToLowerInvariant();
		_filtered.Clear();
		_list.Clear();

		foreach (var def in _assets.Blocks)
		{
			if (query.Length > 0 &&
				!def.Id.Contains(query, StringComparison.OrdinalIgnoreCase) &&
				!def.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
				!_assets.DisplayName(def.Id).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

			_filtered.Add(def);
			int index = _list.AddItem($"{_assets.DisplayName(def.Id)}  —  {def.Id}");
			_list.SetItemMetadata(index, def.Id);

			var state = _assets.DefaultState(def.Id);
			var icon = state != null ? _assets.Icon(state) : null;
			if (icon != null) _list.SetItemIcon(index, icon);

			if (_filtered.Count >= MaxResults) break;
		}

		SetStatus(_filtered.Count >= MaxResults
			? $"结果较多，仅显示前 {MaxResults} 条，请继续输入缩小范围"
			: $"共 {_filtered.Count} 个结果，单击放入当前快捷格");
	}

	private void Choose(int index)
	{
		if (index < 0 || index >= _filtered.Count) return;
		var def = _filtered[index];
		var state = _assets.DefaultState(def.Id);
		if (state == null) return;

		var palette = (McBlockPalette)Palette;
		if (!palette.ReplaceSlot(palette.SelectedIndex, state)) return;
		SetStatus($"第 {palette.SelectedIndex + 1} 格 → {_assets.DisplayName(state.Id)}（{state.FullString}）");
	}
}
