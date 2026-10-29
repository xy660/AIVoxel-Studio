using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Core;

public sealed class VoxelBlockPalette : IBlockPalette
{
    private static readonly (string Name, BlockInfo Info)[] Items =
    {
        ("草绿", BlockInfo.FromRgb(94, 158, 74)),
        ("石灰", BlockInfo.FromRgb(124, 126, 132)),
        ("红色", BlockInfo.FromRgb(200, 64, 64)),
        ("橙色", BlockInfo.FromRgb(232, 140, 52)),
        ("黄色", BlockInfo.FromRgb(234, 204, 78)),
        ("蓝色", BlockInfo.FromRgb(70, 112, 210)),
        ("紫色", BlockInfo.FromRgb(150, 84, 200)),
        ("白色", BlockInfo.FromRgb(236, 236, 236)),
        ("空气墙", BlockInfo.Wall()),
    };

    private readonly BlockInfo[] _states;
    private readonly BlockSlot[] _slots;

    public VoxelBlockPalette()
    {
        _states = new BlockInfo[Items.Length];
        _slots = new BlockSlot[Items.Length];
        for (int i = 0; i < Items.Length; i++)
        {
            _states[i] = Items[i].Info;
            _slots[i] = SlotOf(Items[i].Info);
        }

        Catalog = Items.Select(item => new BlockSlot(item.Name, SwatchOf(item.Info))).ToArray();
    }

    /// <summary>调色板里可选的标准方块（固定 9 项）。</summary>
    public IReadOnlyList<BlockSlot> Catalog { get; }

    public IReadOnlyList<BlockSlot> Slots => _slots;
    public int SelectedIndex { get; private set; }
    public event Action Changed;

    public BlockInfo SelectedBlock => _states[SelectedIndex];

    public void Select(int index)
    {
        SelectedIndex = Mathf.Wrap(index, 0, _states.Length);
        Changed?.Invoke();
    }

    public string DescribeSelected() => LabelOf(_states[SelectedIndex]);

    public bool ReplaceSlot(int slot, int catalogIndex)
    {
        if (catalogIndex < 0 || catalogIndex >= Items.Length) return false;
        return ReplaceSlot(slot, Items[catalogIndex].Info);
    }

    // 允许写入任意方块（含中键拾取的自定义颜色）
    public bool ReplaceSlot(int slot, BlockInfo info)
    {
        if (slot < 0 || slot >= _states.Length || info.IsAir) return false;
        _states[slot] = info;
        _slots[slot] = SlotOf(info);
        Changed?.Invoke();
        return true;
    }

    private static string LabelOf(BlockInfo info)
    {
        if (info.Type == (byte)BlockType.AirWall) return "空气墙";
        if (info.IsVisible)
        {
            foreach (var item in Items)
                if (item.Info.SameVisual(info)) return item.Name;
        }
        return $"#{info.R:X2}{info.G:X2}{info.B:X2}";
    }

    private static BlockSlot SlotOf(BlockInfo info) => new(LabelOf(info), SwatchOf(info));

    private static Color SwatchOf(BlockInfo info) =>
        info.IsVisible ? info.ToColor() : new Color(0.55f, 0.55f, 0.6f, 0.35f);
}
