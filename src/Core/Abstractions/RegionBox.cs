using Godot;

namespace AIVoxelStudio.Core.Abstractions;

// 闭区间的方块包围盒，Min/Max 都含在内
public readonly struct RegionBox
{
    public RegionBox(Vector3I min, Vector3I max)
    {
        Min = min;
        Max = max;
    }

    public Vector3I Min { get; }
    public Vector3I Max { get; }
    public Vector3I Size => new(Max.X - Min.X + 1, Max.Y - Min.Y + 1, Max.Z - Min.Z + 1);

    public static RegionBox FromCorners(Vector3I a, Vector3I b) => new(
        new Vector3I(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
        new Vector3I(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));

    public bool Contains(int x, int y, int z) =>
        x >= Min.X && x <= Max.X && y >= Min.Y && y <= Max.Y && z >= Min.Z && z <= Max.Z;

    public bool Contains(Vector3I cell) => Contains(cell.X, cell.Y, cell.Z);

    public long Volume => (long)Size.X * Size.Y * Size.Z;

    public override string ToString() => $"({Min.X},{Min.Y},{Min.Z})-({Max.X},{Max.Y},{Max.Z})";
}
