using Godot;

namespace AIVoxelStudio.Core.Mc;

// 玻璃板 / 铁栏杆 / 栅栏 / 围墙等含 north/south/east/west 连接属性的方块：按四邻自动连通
public static class McConnections
{
    private static readonly (string Key, int Dx, int Dz)[] Directions =
    {
        ("north", 0, -1),
        ("south", 0, 1),
        ("west", -1, 0),
        ("east", 1, 0),
    };

    public static bool Applies(McBlockState state) =>
        state != null && !state.IsAir &&
        state.GetProperty("north") != null && state.GetProperty("south") != null &&
        state.GetProperty("east") != null && state.GetProperty("west") != null;

    // 按邻居算好连接属性；explicitProps 中显式给出的方向保持调用方的值
    public static McBlockState Adjust(
        McWorld world, McAssets assets, int x, int y, int z, McBlockState state,
        IReadOnlyDictionary<string, string> explicitProps = null)
    {
        if (world == null || assets == null || !Applies(state)) return state;

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in state.Properties) properties[pair.Key] = pair.Value;

        foreach (var (key, dx, dz) in Directions)
        {
            if (explicitProps != null && explicitProps.ContainsKey(key)) continue;
            properties[key] = Connects(world, state.Id, x + dx, y, z + dz) ? "true" : "false";
        }

        return assets.ResolveState(state.Id, properties) ?? state;
    }

    // 邻居变化后只重算相邻的同类方块，避免级联改动其他方块
    public static void RefreshAround(McWorld world, McAssets assets, int x, int y, int z)
    {
        if (world == null || assets == null) return;

        foreach (var (_, dx, dz) in Directions)
        {
            int nx = x + dx, nz = z + dz;
            var neighbor = world.GetBlock(nx, y, nz);
            if (!Applies(neighbor)) continue;
            var adjusted = Adjust(world, assets, nx, y, nz, neighbor);
            if (!adjusted.Equals(neighbor)) world.SetBlock(nx, y, nz, adjusted);
        }
    }

    // 邻居同种方块，或是完整实心方块时才连接（楼梯/台阶等不完整方块不连，近似原版）
    private static bool Connects(McWorld world, string id, int x, int y, int z)
    {
        var neighbor = world.GetBlock(x, y, z);
        if (neighbor.IsAir) return false;
        return neighbor.Id == id || neighbor.IsSolid || neighbor.IsOpaqueCube;
    }
}
