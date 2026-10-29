using Godot;

namespace AIVoxelStudio.Core;

public static class WorldGenerator
{
    public const int SurfaceY = -1;
    private const int GroundDepth = 7;
    private const int ChunkRadius = 3;

    private static readonly BlockInfo Grass = BlockInfo.FromRgb(94, 158, 74);
    private static readonly BlockInfo Dirt = BlockInfo.FromRgb(122, 88, 58);
    private static readonly BlockInfo Stone = BlockInfo.FromRgb(124, 126, 132);

    public static void GenerateArea(World world, int chunkRadius = ChunkRadius)
    {
        for (int cx = -chunkRadius; cx <= chunkRadius; cx++)
        for (int cz = -chunkRadius; cz <= chunkRadius; cz++)
        {
            var coord = new Vector3I(cx, World.FloorDiv(SurfaceY, Chunk.Size), cz);
            if (world.HasChunk(coord)) continue;
            var chunk = world.GetOrCreateChunk(coord);
            FillFlat(chunk);
        }
    }

    private static void FillFlat(Chunk chunk)
    {
        int baseY = chunk.Coord.Y * Chunk.Size;
        int bottomY = SurfaceY - GroundDepth;

        for (int lx = 0; lx < Chunk.Size; lx++)
        for (int lz = 0; lz < Chunk.Size; lz++)
        for (int ly = 0; ly < Chunk.Size; ly++)
        {
            int wy = baseY + ly;
            if (wy > SurfaceY || wy < bottomY) continue;
            var info = wy == SurfaceY ? Grass
                : wy > bottomY + 2 ? Dirt
                : Stone;
            chunk.Set(lx, ly, lz, info);
        }
    }
}
