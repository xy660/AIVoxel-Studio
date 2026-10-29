using Godot;

namespace AIVoxelStudio.Core;

public sealed class Chunk
{
    public const int Size = 64;
    public const int Size3 = Size * Size * Size;

    public readonly Vector3I Coord;
    public readonly BlockInfo[] Blocks;

    public Chunk(Vector3I coord)
    {
        Coord = coord;
        Blocks = new BlockInfo[Size3];
    }

    public static int Index(int x, int y, int z) => (x * Size + y) * Size + z;

    public BlockInfo Get(int x, int y, int z) => Blocks[Index(x, y, z)];

    public void Set(int x, int y, int z, in BlockInfo info) => Blocks[Index(x, y, z)] = info;

    public void Fill(in BlockInfo info) => Array.Fill(Blocks, info);

    public Vector3I WorldOrigin => Coord * Size;
}
