using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.Core.Mc;

// 默认平坦地面：地面顶面与纯色模式一致在 y = 0
public static class McWorldGenerator
{
    public const int SurfaceY = -1;
    private const int ChunkRadius = 3;

    public static void GenerateArea(McWorld world, McAssets assets)
    {
        var bedrock = assets.DefaultState("minecraft:bedrock");
        var stone = assets.DefaultState("minecraft:stone");
        var dirt = assets.DefaultState("minecraft:dirt");
        var grass = assets.DefaultState("minecraft:grass_block");

        for (int cx = -ChunkRadius; cx <= ChunkRadius; cx++)
        for (int cz = -ChunkRadius; cz <= ChunkRadius; cz++)
        for (int sectionY = McWorld.MinY >> 4; sectionY <= SurfaceY >> 4; sectionY++)
        {
            var section = world.GetOrCreateSection(new Godot.Vector3I(cx, sectionY, cz));
            int baseY = sectionY * McSection.Size;
            for (int ly = 0; ly < McSection.Size; ly++)
            {
                int worldY = baseY + ly;
                if (worldY > SurfaceY) continue;
                var state = worldY == McWorld.MinY ? bedrock
                    : worldY == SurfaceY ? grass
                    : worldY >= SurfaceY - 2 ? dirt
                    : stone;
                for (int lz = 0; lz < McSection.Size; lz++)
                for (int lx = 0; lx < McSection.Size; lx++)
                    section.Set(lx, ly, lz, state);
            }
        }
    }
}
