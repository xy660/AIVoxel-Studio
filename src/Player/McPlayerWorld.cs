using Godot;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.Player;

public sealed class McPlayerWorld : IPlayerWorld
{
    private readonly McWorld _world;
    private readonly McBlockPalette _palette;
    private readonly McAssets _assets;

    public McPlayerWorld(McWorld world, McBlockPalette palette, McAssets assets)
    {
        _world = world;
        _palette = palette;
        _assets = assets;
    }

    public bool IsSolid(int x, int y, int z) => _world.IsSolid(x, y, z);

    public void BreakAt(Vector3I cell) => McBlockPlacer.Break(_world, _assets, cell.X, cell.Y, cell.Z);

    public bool TryPlaceAt(Vector3I cell)
    {
        var state = _palette.SelectedState;
        if (state == null || state.IsAir) return false;
        return McBlockPlacer.Place(_world, _assets, cell.X, cell.Y, cell.Z, state);
    }

    public bool TryPickBlock(Vector3I cell) =>
        _palette.ReplaceSlot(_palette.SelectedIndex, _world.GetBlock(cell.X, cell.Y, cell.Z));
}
