using Godot;

namespace AIVoxelStudio.Core.Mc;

// 16³ section：palette + 每格调色板索引；索引顺序 YZX
public sealed class McSection
{
    public const int Size = 16;
    public const int Size3 = Size * Size * Size;

    private readonly Dictionary<string, ushort> _lookup = new() { [McBlockState.Air.FullString] = 0 };
    private int _nonAir;

    public McSection(Vector3I coord)
    {
        Coord = coord;
    }

    public readonly Vector3I Coord;
    public readonly List<McBlockState> Palette = new() { McBlockState.Air };
    public readonly ushort[] Blocks = new ushort[Size3];

    public int NonAirCount => _nonAir;
    public bool IsEmpty => _nonAir == 0;

    public static int Index(int x, int y, int z) => (y * Size + z) * Size + x;

    public McBlockState Get(int x, int y, int z) => Palette[Blocks[Index(x, y, z)]];

    public bool Set(int x, int y, int z, McBlockState state)
    {
        int index = Index(x, y, z);
        ushort target = ResolvePaletteIndex(state);
        if (Blocks[index] == target) return false;

        if (Blocks[index] == 0 && target != 0) _nonAir++;
        else if (Blocks[index] != 0 && target == 0) _nonAir--;

        Blocks[index] = target;
        return true;
    }

    public void SetRaw(int index, ushort value)
    {
        Blocks[index] = value;
        if (value != 0) _nonAir++;
    }

    // 读档时整体替换调色板（顺序与存档一致，索引 0 为空气）
    public void SetPalette(IReadOnlyList<McBlockState> palette)
    {
        Palette.Clear();
        _lookup.Clear();
        for (int i = 0; i < palette.Count; i++)
        {
            var state = palette[i] ?? McBlockState.Air;
            Palette.Add(state);
            _lookup[state.FullString] = (ushort)i;
        }

        if (Palette.Count == 0)
        {
            Palette.Add(McBlockState.Air);
            _lookup[McBlockState.Air.FullString] = 0;
        }
    }

    private ushort ResolvePaletteIndex(McBlockState state)
    {
        if (state == null || state.IsAir) return 0;
        if (_lookup.TryGetValue(state.FullString, out ushort index)) return index;
        if (Palette.Count >= ushort.MaxValue) return 0;
        index = (ushort)Palette.Count;
        Palette.Add(state);
        _lookup[state.FullString] = index;
        return index;
    }
}
