using Godot;

namespace AIVoxelStudio.Core.Mc;

// 平坦地面：固定剖面（与原版经典超平坦一致：基岩 1 + 泥土 3 + 草 1），按玩家位置懒生成区块
public sealed class McFlatGround
{
    private const int SectionSize = McSection.Size;
    private static readonly int BottomSection = McWorld.FloorDiv(McWorld.MinY, SectionSize);

    private readonly McWorld _world;
    private readonly List<(McBlockState State, int Height)> _layers = new();
    private readonly HashSet<Vector2I> _known = new();

    public int TopY { get; private set; } = McWorld.MinY - 1;
    public bool HasGround { get; private set; }
    public IReadOnlyList<(string Id, int Height)> LayerIds => McWorldGenerator.DefaultLayers;

    private McFlatGround(McWorld world)
    {
        _world = world;
    }

    public static McFlatGround Create(McWorld world, McAssets assets)
    {
        var ground = new McFlatGround(world);
        ground.HasGround = DetectGround(world);
        if (ground.HasGround) ground.Apply(assets);

        foreach (var section in world.Sections)
            ground._known.Add(new Vector2I(section.Coord.X, section.Coord.Z));
        return ground;
    }

    // 空白地图（没有默认地面）不参与懒生成；带地面的世界底层一定有基岩
    private static bool DetectGround(McWorld world)
    {
        foreach (var section in world.Sections)
        {
            if (section.Coord.Y != BottomSection) continue;
            foreach (var state in section.Palette)
                if (state.Id == "minecraft:bedrock") return true;
        }
        return false;
    }

    private void Apply(McAssets assets)
    {
        int y = McWorld.MinY;
        foreach (var (id, height) in McWorldGenerator.DefaultLayers)
        {
            _layers.Add((assets.DefaultState(id), height));
            y += height;
        }
        TopY = y - 1;
    }

    // 在玩家周围补地面；只生成没记录过的区块，单次最多 maxChunks 个，由近到远
    public int EnsureAround(Vector3 centerMeters, float meters, int radius, int maxChunks)
    {
        if (!HasGround || maxChunks <= 0) return 0;

        float scale = meters <= 0 ? 1f : meters;
        int blockX = Mathf.FloorToInt(centerMeters.X / scale);
        int blockZ = Mathf.FloorToInt(centerMeters.Z / scale);
        int centerX = McWorld.FloorDiv(blockX, SectionSize);
        int centerZ = McWorld.FloorDiv(blockZ, SectionSize);

        var pending = new List<Vector2I>();
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            var coord = new Vector2I(centerX + dx, centerZ + dz);
            if (!_known.Contains(coord)) pending.Add(coord);
        }

        pending.Sort((a, b) =>
        {
            int da = (a.X - centerX) * (a.X - centerX) + (a.Y - centerZ) * (a.Y - centerZ);
            int db = (b.X - centerX) * (b.X - centerX) + (b.Y - centerZ) * (b.Y - centerZ);
            return da.CompareTo(db);
        });

        int built = 0;
        foreach (var coord in pending)
        {
            if (built >= maxChunks) break;
            if (EnsureChunk(coord.X, coord.Y)) built++;
        }

        return built;
    }

    private bool EnsureChunk(int cx, int cz)
    {
        _known.Add(new Vector2I(cx, cz));

        int baseX = cx * SectionSize;
        int baseZ = cz * SectionSize;
        int worldY = McWorld.MinY;
        foreach (var (state, height) in _layers)
        {
            for (int i = 0; i < height && worldY <= McWorld.MaxY; i++, worldY++)
            {
                var coord = new Vector3I(cx, McWorld.FloorDiv(worldY, SectionSize), cz);
                var section = _world.GetOrCreateSection(coord);
                int ly = worldY - coord.Y * SectionSize;
                for (int lz = 0; lz < SectionSize; lz++)
                for (int lx = 0; lx < SectionSize; lx++)
                {
                    if (section.Get(lx, ly, lz).IsAir)
                        section.Set(lx, ly, lz, state);
                }
            }
        }

        _world.NotifyRegionChanged(baseX, McWorld.MinY, baseZ,
            baseX + SectionSize - 1, TopY, baseZ + SectionSize - 1);
        return true;
    }
}
