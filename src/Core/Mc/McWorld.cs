using Godot;

namespace AIVoxelStudio.Core.Mc;

// 我的世界世界容器：y ∈ [-64, 319]，section 16³，空 section 不保留
public sealed class McWorld
{
    public const int SectionSize = McSection.Size;
    public const int MinY = -64;
    public const int MaxY = 319;

    private readonly Dictionary<Vector3I, McSection> _sections = new();

    public event Action<int, int, int> BlockChanged;

    public IEnumerable<McSection> Sections => _sections.Values;
    public int SectionCount => _sections.Count;

    public static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        if ((value % divisor != 0) && (value < 0)) q--;
        return q;
    }

    public static int LocalOf(int value, int divisor) => value - FloorDiv(value, divisor) * divisor;

    public static Vector3I SectionCoordOf(int x, int y, int z) =>
        new(FloorDiv(x, SectionSize), FloorDiv(y, SectionSize), FloorDiv(z, SectionSize));

    public bool HasSection(Vector3I coord) => _sections.ContainsKey(coord);
    public McSection GetSection(Vector3I coord) => _sections.GetValueOrDefault(coord);

    public McSection GetOrCreateSection(Vector3I coord)
    {
        if (_sections.TryGetValue(coord, out var section)) return section;
        section = new McSection(coord);
        _sections[coord] = section;
        return section;
    }

    public void AddSection(McSection section) => _sections[section.Coord] = section;

    public void Clear() => _sections.Clear();

    public McBlockState GetBlock(int x, int y, int z)
    {
        if (y < MinY || y > MaxY) return McBlockState.Air;
        var section = GetSection(SectionCoordOf(x, y, z));
        if (section == null) return McBlockState.Air;
        return section.Get(LocalOf(x, SectionSize), LocalOf(y, SectionSize), LocalOf(z, SectionSize));
    }

    public bool IsSolid(int x, int y, int z) => GetBlock(x, y, z).IsSolid;
    public bool IsOpaqueCube(int x, int y, int z) => GetBlock(x, y, z).IsOpaqueCube;

    public bool SetBlock(int x, int y, int z, McBlockState state, bool notify = true)
    {
        if (y < MinY || y > MaxY || state == null) return false;

        var coord = SectionCoordOf(x, y, z);
        if (!_sections.TryGetValue(coord, out var section))
        {
            if (state.IsAir) return false;
            section = GetOrCreateSection(coord);
        }

        bool changed = section.Set(
            LocalOf(x, SectionSize), LocalOf(y, SectionSize), LocalOf(z, SectionSize), state);
        if (!changed) return false;

        if (section.IsEmpty) _sections.Remove(coord);
        if (notify) BlockChanged?.Invoke(x, y, z);
        return true;
    }

    public bool Break(int x, int y, int z) => SetBlock(x, y, z, McBlockState.Air);

    // 批量编辑后按 section 范围通知渲染器刷新（含一圈邻接 section，保证 cullface 正确）
    public void NotifyRegionChanged(int x1, int y1, int z1, int x2, int y2, int z2)
    {
        if (BlockChanged == null) return;

        int sx1 = FloorDiv(Math.Min(x1, x2), SectionSize) - 1;
        int sx2 = FloorDiv(Math.Max(x1, x2), SectionSize) + 1;
        int sy1 = FloorDiv(Math.Min(y1, y2), SectionSize) - 1;
        int sy2 = FloorDiv(Math.Max(y1, y2), SectionSize) + 1;
        int sz1 = FloorDiv(Math.Min(z1, z2), SectionSize) - 1;
        int sz2 = FloorDiv(Math.Max(z1, z2), SectionSize) + 1;

        for (int sy = sy1; sy <= sy2; sy++)
        {
            int y = Math.Clamp(sy * SectionSize, MinY, MaxY);
            for (int sz = sz1; sz <= sz2; sz++)
            for (int sx = sx1; sx <= sx2; sx++)
                BlockChanged.Invoke(sx * SectionSize, y, sz * SectionSize);
        }
    }
}
