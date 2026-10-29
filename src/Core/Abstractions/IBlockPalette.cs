using Godot;

namespace AIVoxelStudio.Core.Abstractions;

public readonly record struct BlockSlot(string Label, Color Swatch, Texture2D Icon = null);

public interface IBlockPalette
{
    IReadOnlyList<BlockSlot> Slots { get; }
    int SelectedIndex { get; }
    event Action Changed;
    void Select(int index);
    string DescribeSelected();
}
