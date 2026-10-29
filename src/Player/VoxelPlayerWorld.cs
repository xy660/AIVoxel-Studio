using Godot;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Player;

public sealed class VoxelPlayerWorld : IPlayerWorld
{
    private readonly World _world;
    private readonly VoxelBlockPalette _palette;

    public VoxelPlayerWorld(World world, VoxelBlockPalette palette)
    {
        _world = world;
        _palette = palette;
    }

    public bool IsSolid(int x, int y, int z) => _world.IsSolid(x, y, z);

    public void BreakAt(Vector3I cell) => _world.SetBlock(cell.X, cell.Y, cell.Z, BlockInfo.Air);

    public bool TryPlaceAt(Vector3I cell)
    {
        var info = _palette.SelectedBlock;
        if (info.IsAir) return false;
        _world.SetBlock(cell.X, cell.Y, cell.Z, info);
        return true;
    }

    public bool TryPickBlock(Vector3I cell) =>
        _palette.ReplaceSlot(_palette.SelectedIndex, _world.GetBlock(cell.X, cell.Y, cell.Z));
}
