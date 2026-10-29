using Godot;
using AIVoxelStudio.Core;

namespace AIVoxelStudio.Render;

public static class SectionMesher
{
    public const int Size = 16;
    private const int Pad = Size + 2;

    public sealed class Result
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color> Colors = new();
        public readonly List<int> Indices = new();
        public readonly List<Vector3> CollisionFaces = new();

        public bool HasVisibleGeometry => Indices.Count > 0;
        public bool HasCollision => CollisionFaces.Count > 0;
    }

    public static Result Build(World world, Vector3I sectionCoord)
    {
        var result = new Result();
        var origin = sectionCoord * Size;
        var cache = BuildCache(world, origin);

        bool hasSolid = false;
        bool hasVisible = false;
        for (int x = 0; x < Size && !(hasSolid && hasVisible); x++)
        for (int y = 0; y < Size && !(hasSolid && hasVisible); y++)
        for (int z = 0; z < Size; z++)
        {
            var b = cache[x + 1, y + 1, z + 1];
            hasSolid |= b.IsSolid;
            hasVisible |= b.IsVisible;
            if (hasSolid && hasVisible) break;
        }

        if (hasVisible) BuildVisibleGreedy(cache, result);
        if (hasSolid) BuildCollision(cache, result);
        ScaleToMeters(result);
        return result;
    }

    private static void ScaleToMeters(Result result)
    {
        for (int i = 0; i < result.Vertices.Count; i++) result.Vertices[i] *= VoxelUnits.BlockMeters;
        for (int i = 0; i < result.CollisionFaces.Count; i++) result.CollisionFaces[i] *= VoxelUnits.BlockMeters;
    }

    private static BlockInfo[,,] BuildCache(World world, Vector3I origin)
    {
        var cache = new BlockInfo[Pad, Pad, Pad];
        for (int x = -1; x <= Size; x++)
        for (int y = -1; y <= Size; y++)
        for (int z = -1; z <= Size; z++)
        {
            cache[x + 1, y + 1, z + 1] = world.GetBlock(origin.X + x, origin.Y + y, origin.Z + z);
        }
        return cache;
    }

    private static readonly int[] AxisU = { 1, 2, 0 };
    private static readonly int[] AxisV = { 2, 0, 1 };
    private static readonly float[] PositiveShade = { 0.93f, 1.00f, 0.90f };
    private static readonly float[] NegativeShade = { 0.88f, 0.72f, 0.82f };

    private static void BuildVisibleGreedy(BlockInfo[,,] cache, Result result)
    {
        var mask = new int[Size * Size];
        for (int axis = 0; axis < 3; axis++)
        {
            int uAxis = AxisU[axis];
            int vAxis = AxisV[axis];
            for (int side = 0; side < 2; side++)
            {
                int sign = side == 0 ? 1 : -1;
                float shade = side == 0 ? PositiveShade[axis] : NegativeShade[axis];

                for (int slice = 0; slice < Size; slice++)
                {
                    Array.Fill(mask, -1);
                    bool any = false;

                    for (int v = 0; v < Size; v++)
                    for (int u = 0; u < Size; u++)
                    {
                        var p = MakePos(axis, slice, uAxis, u, vAxis, v);
                        var block = cache[p.X + 1, p.Y + 1, p.Z + 1];
                        if (!block.IsVisible) continue;

                        var n = MakePos(axis, slice + sign, uAxis, u, vAxis, v);
                        if (cache[n.X + 1, n.Y + 1, n.Z + 1].IsVisible) continue;

                        mask[v * Size + u] = (block.R << 16) | (block.G << 8) | block.B;
                        any = true;
                    }

                    if (!any) continue;
                    EmitGreedy(mask, axis, slice, sign, uAxis, vAxis, shade, result);
                }
            }
        }
    }

    private static void EmitGreedy(int[] mask, int axis, int slice, int sign, int uAxis, int vAxis, float shade, Result result)
    {
        for (int v0 = 0; v0 < Size; v0++)
        for (int u0 = 0; u0 < Size; u0++)
        {
            int key = mask[v0 * Size + u0];
            if (key < 0) continue;

            int w = 1;
            while (u0 + w < Size && mask[v0 * Size + u0 + w] == key) w++;

            int h = 1;
            while (v0 + h < Size)
            {
                bool rowMatches = true;
                for (int u = u0; u < u0 + w; u++)
                {
                    if (mask[(v0 + h) * Size + u] != key) { rowMatches = false; break; }
                }
                if (!rowMatches) break;
                h++;
            }

            for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
                mask[(v0 + row) * Size + u0 + col] = -1;

            float plane = slice + (sign > 0 ? 1 : 0);
            var linear = new Color(
                ((key >> 16) & 0xFF) / 255f,
                ((key >> 8) & 0xFF) / 255f,
                (key & 0xFF) / 255f).SrgbToLinear();
            var color = new Color(linear.R * shade, linear.G * shade, linear.B * shade);

            var normal = Vector3.Zero;
            SetComp(ref normal, axis, sign);

            var p00 = Vector3.Zero;
            SetComp(ref p00, axis, plane);
            SetComp(ref p00, uAxis, u0);
            SetComp(ref p00, vAxis, v0);

            var stepU = Vector3.Zero;
            SetComp(ref stepU, uAxis, w);
            var stepV = Vector3.Zero;
            SetComp(ref stepV, vAxis, h);

            AddQuad(result, p00, p00 + stepU, p00 + stepU + stepV, p00 + stepV, normal, color, sign > 0);
        }
    }

    private static readonly Vector3I[] FaceOffsets =
    {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

    private static void BuildCollision(BlockInfo[,,] cache, Result result)
    {
        for (int x = 0; x < Size; x++)
        for (int y = 0; y < Size; y++)
        for (int z = 0; z < Size; z++)
        {
            if (!cache[x + 1, y + 1, z + 1].IsSolid) continue;

            for (int f = 0; f < FaceOffsets.Length; f++)
            {
                var off = FaceOffsets[f];
                if (cache[x + 1 + off.X, y + 1 + off.Y, z + 1 + off.Z].IsSolid) continue;

                Vector3 p00, p10, p11, p01;
                if (off == new Vector3I(1, 0, 0))
                {
                    p00 = new Vector3(x + 1, y, z);
                    p10 = new Vector3(x + 1, y, z + 1);
                    p11 = new Vector3(x + 1, y + 1, z + 1);
                    p01 = new Vector3(x + 1, y + 1, z);
                }
                else if (off == new Vector3I(-1, 0, 0))
                {
                    p00 = new Vector3(x, y, z + 1);
                    p10 = new Vector3(x, y, z);
                    p11 = new Vector3(x, y + 1, z);
                    p01 = new Vector3(x, y + 1, z + 1);
                }
                else if (off == new Vector3I(0, 1, 0))
                {
                    p00 = new Vector3(x, y + 1, z);
                    p10 = new Vector3(x + 1, y + 1, z);
                    p11 = new Vector3(x + 1, y + 1, z + 1);
                    p01 = new Vector3(x, y + 1, z + 1);
                }
                else if (off == new Vector3I(0, -1, 0))
                {
                    p00 = new Vector3(x, y, z + 1);
                    p10 = new Vector3(x + 1, y, z + 1);
                    p11 = new Vector3(x + 1, y, z);
                    p01 = new Vector3(x, y, z);
                }
                else if (off == new Vector3I(0, 0, 1))
                {
                    p00 = new Vector3(x + 1, y, z + 1);
                    p10 = new Vector3(x, y, z + 1);
                    p11 = new Vector3(x, y + 1, z + 1);
                    p01 = new Vector3(x + 1, y + 1, z + 1);
                }
                else
                {
                    p00 = new Vector3(x, y, z);
                    p10 = new Vector3(x + 1, y, z);
                    p11 = new Vector3(x + 1, y + 1, z);
                    p01 = new Vector3(x, y + 1, z);
                }

                AddCollisionQuad(result, p00, p10, p11, p01);
            }
        }
    }

    private static Vector3I MakePos(int axis, int axisValue, int uAxis, int uValue, int vAxis, int vValue)
    {
        var p = Vector3I.Zero;
        switch (axis)
        {
            case 0: p.X = axisValue; break;
            case 1: p.Y = axisValue; break;
            default: p.Z = axisValue; break;
        }
        switch (uAxis)
        {
            case 0: p.X = uValue; break;
            case 1: p.Y = uValue; break;
            default: p.Z = uValue; break;
        }
        switch (vAxis)
        {
            case 0: p.X = vValue; break;
            case 1: p.Y = vValue; break;
            default: p.Z = vValue; break;
        }
        return p;
    }

    private static void SetComp(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }

    private static void AddQuad(Result result, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Color color, bool clockwiseFront)
    {
        int baseIndex = result.Vertices.Count;
        result.Vertices.Add(p0);
        result.Vertices.Add(p1);
        result.Vertices.Add(p2);
        result.Vertices.Add(p3);
        for (int i = 0; i < 4; i++)
        {
            result.Normals.Add(normal);
            result.Colors.Add(color);
        }

        if (clockwiseFront)
        {
            result.Indices.Add(baseIndex);
            result.Indices.Add(baseIndex + 2);
            result.Indices.Add(baseIndex + 1);
            result.Indices.Add(baseIndex);
            result.Indices.Add(baseIndex + 3);
            result.Indices.Add(baseIndex + 2);
        }
        else
        {
            result.Indices.Add(baseIndex);
            result.Indices.Add(baseIndex + 1);
            result.Indices.Add(baseIndex + 2);
            result.Indices.Add(baseIndex);
            result.Indices.Add(baseIndex + 2);
            result.Indices.Add(baseIndex + 3);
        }
    }

    private static void AddCollisionQuad(Result result, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        result.CollisionFaces.Add(p0);
        result.CollisionFaces.Add(p1);
        result.CollisionFaces.Add(p2);
        result.CollisionFaces.Add(p0);
        result.CollisionFaces.Add(p2);
        result.CollisionFaces.Add(p3);
    }
}
