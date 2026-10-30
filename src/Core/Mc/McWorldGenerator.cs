namespace AIVoxelStudio.Core.Mc;

// 默认平坦地面：与原版经典超平坦一致（1 基岩 + 3 泥土 + 1 草方块），贴世界底部生成，给上方留出更多高度
public static class McWorldGenerator
{
    public const int SurfaceY = McWorld.MinY + 4; // -60：草方块所在层
    private const int ChunkRadius = 3;

    public static void GenerateArea(McWorld world, McAssets assets)
    {
        var bedrock = assets.DefaultState("minecraft:bedrock");
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
                    : dirt;
                for (int lz = 0; lz < McSection.Size; lz++)
                for (int lx = 0; lx < McSection.Size; lx++)
                    section.Set(lx, ly, lz, state);
            }
        }
    }
}
