namespace AIVoxelStudio.Core;

public sealed class VoxelEditService
{
    public const int MaxBlocksPerCall = 200_000;

    private readonly World _world;

    public VoxelEditService(World world)
    {
        _world = world;
    }

    public bool Set(int x, int y, int z, in BlockInfo info) => _world.SetBlock(x, y, z, info);
}
