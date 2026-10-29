using Godot;

namespace AIVoxelStudio.Core.Mc;

// 门/床/高植物等「两格方块」在 MC 里是相邻的两个独立方块，这里集中做配对与原子放置/拆除
public static class McBlockPlacer
{
    private static readonly Dictionary<string, Vector3I> Facings = new(StringComparer.Ordinal)
    {
        ["north"] = new(0, 0, -1),
        ["south"] = new(0, 0, 1),
        ["west"] = new(-1, 0, 0),
        ["east"] = new(1, 0, 0),
    };

    // 找出当前状态对应的另一半：门/高植物按 half（lower/upper），床按 part（foot/head）+ facing
    public static bool TryGetPair(
        McAssets assets, McBlockState state, out Vector3I offset, out McBlockState pair)
    {
        offset = Vector3I.Zero;
        pair = null;
        if (assets == null || state == null || state.IsAir) return false;

        string half = state.GetProperty("half");
        if (half is "lower" or "upper")
        {
            offset = new Vector3I(0, half == "lower" ? 1 : -1, 0);
            pair = WithProperty(assets, state, "half", half == "lower" ? "upper" : "lower");
            return pair != null;
        }

        string part = state.GetProperty("part");
        if (part is "foot" or "head" &&
            Facings.TryGetValue(state.GetProperty("facing") ?? "", out var facing))
        {
            offset = part == "foot" ? facing : -facing;
            pair = WithProperty(assets, state, "part", part == "foot" ? "head" : "foot");
            return pair != null;
        }

        return false;
    }

    public static bool Place(
        McWorld world, McAssets assets, int x, int y, int z, McBlockState state,
        IReadOnlyDictionary<string, string> explicitProps = null)
    {
        if (world == null || state == null || state.IsAir) return false;

        if (!TryGetPair(assets, state, out var offset, out var pair))
        {
            var adjusted = McConnections.Adjust(world, assets, x, y, z, state, explicitProps);
            if (!world.SetBlock(x, y, z, adjusted)) return false;
            McConnections.RefreshAround(world, assets, x, y, z);
            return true;
        }

        int px = x + offset.X, py = y + offset.Y, pz = z + offset.Z;
        if (py < McWorld.MinY || py > McWorld.MaxY) return false;
        if (!CanReplace(world.GetBlock(px, py, pz), pair.Id)) return false;

        world.SetBlock(x, y, z, state);
        world.SetBlock(px, py, pz, pair);
        McConnections.RefreshAround(world, assets, x, y, z);
        McConnections.RefreshAround(world, assets, px, py, pz);
        return true;
    }

    public static bool Break(McWorld world, McAssets assets, int x, int y, int z)
    {
        if (world == null) return false;
        var state = world.GetBlock(x, y, z);
        if (state.IsAir) return false;

        bool changed = world.Break(x, y, z);
        if (TryGetPair(assets, state, out var offset, out var pair))
        {
            int px = x + offset.X, py = y + offset.Y, pz = z + offset.Z;
            if (world.GetBlock(px, py, pz).Equals(pair) && world.Break(px, py, pz))
            {
                changed = true;
                McConnections.RefreshAround(world, assets, px, py, pz);
            }
        }
        McConnections.RefreshAround(world, assets, x, y, z);
        return changed;
    }

    // 目标格可覆盖：空气、同种方块（重复放置），或可被替换的流体/植物
    private static bool CanReplace(McBlockState existing, string id)
    {
        if (existing.IsAir || existing.IsFluid || existing.IsCross) return true;
        return existing.Id == id;
    }

    private static McBlockState WithProperty(McAssets assets, McBlockState state, string key, string value)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in state.Properties)
            properties[pair.Key] = pair.Key == key ? value : pair.Value;
        properties[key] = value;
        return assets.ResolveState(state.Id, properties);
    }
}
