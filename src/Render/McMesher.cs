using AIVoxelStudio.Core.Mc;
using Godot;

namespace AIVoxelStudio.Render;

public sealed class McMeshResult
{
    public readonly List<Vector3> OpaqueVertices = new();
    public readonly List<Vector3> OpaqueNormals = new();
    public readonly List<Vector2> OpaqueUvs = new();
    public readonly List<int> OpaqueIndices = new();

    public readonly List<Vector3> TranslucentVertices = new();
    public readonly List<Vector3> TranslucentNormals = new();
    public readonly List<Vector2> TranslucentUvs = new();
    public readonly List<int> TranslucentIndices = new();

    public readonly List<Vector3> Collision = new();

    public bool HasOpaque => OpaqueIndices.Count > 0;
    public bool HasTranslucent => TranslucentIndices.Count > 0;
    public bool HasCollision => Collision.Count > 0;
}

// 16³ section 网格：按 cullface 剔除、按不透明/镂空/半透明分层
public static class McMesher
{
    private static readonly (int X, int Y, int Z)[] Offsets =
    {
        (0, 0, -1), (0, 0, 1), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0),
    };

    public static McMeshResult Build(McWorld world, McAssets assets, McSection section, float blockMeters)
    {
        var result = new McMeshResult();
        int baseX = section.Coord.X * McSection.Size;
        int baseY = section.Coord.Y * McSection.Size;
        int baseZ = section.Coord.Z * McSection.Size;

        for (int ly = 0; ly < McSection.Size; ly++)
        for (int lz = 0; lz < McSection.Size; lz++)
        for (int lx = 0; lx < McSection.Size; lx++)
        {
            var state = section.Get(lx, ly, lz);
            if (state.IsAir) continue;

            var baked = assets.Baked(state);
            if (baked.Faces.Count == 0 && !state.IsSolid) continue;

            int wx = baseX + lx, wy = baseY + ly, wz = baseZ + lz;

            foreach (var face in baked.Faces)
            {
                if (face.CullFace != null && ShouldCull(world, state, wx, wy, wz, face.CullFace)) continue;

                if (face.Layer == 2) AddQuad(result.TranslucentVertices, result.TranslucentNormals,
                    result.TranslucentUvs, result.TranslucentIndices, face, lx, ly, lz, blockMeters);
                else AddQuad(result.OpaqueVertices, result.OpaqueNormals, result.OpaqueUvs,
                    result.OpaqueIndices, face, lx, ly, lz, blockMeters);
            }

            if (state.IsSolid) AddCollision(world, result, wx, wy, wz);
        }

        return result;
    }

    private static bool ShouldCull(McWorld world, McBlockState state, int x, int y, int z, string cullFace)
    {
        var (dx, dy, dz) = OffsetOf(cullFace);
        var neighbor = world.GetBlock(x + dx, y + dy, z + dz);
        if (neighbor.IsOpaqueCube) return true;

        // 同种流体之间不绘制内部面
        return state.IsFluid && neighbor.FullString == state.FullString;
    }

    private static (int X, int Y, int Z) OffsetOf(string face) => face switch
    {
        "north" => (0, 0, -1),
        "south" => (0, 0, 1),
        "east" => (1, 0, 0),
        "west" => (-1, 0, 0),
        "up" => (0, 1, 0),
        _ => (0, -1, 0),
    };

    private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
        List<int> indices, McFace face, int lx, int ly, int lz, float meters)
    {
        int start = vertices.Count;
        var offset = new Vector3(lx, ly, lz) * meters;
        // 模型元素坐标是 0..16，换算成方块比例再乘方块米数
        float scale = meters / McAssets.UnitsPerBlock;
        for (int i = 0; i < 4; i++)
        {
            vertices.Add(face.Corners[i] * scale + offset);
            normals.Add(face.Normal);
            uvs.Add(face.Uvs[i]);
        }
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static void AddCollision(McWorld world, McMeshResult result, int x, int y, int z)
    {
        for (int i = 0; i < Offsets.Length; i++)
        {
            var (dx, dy, dz) = Offsets[i];
            if (world.IsSolid(x + dx, y + dy, z + dz)) continue;

            var min = new Vector3(x, y, z);
            var max = min + Vector3.One;
            Vector3 a, b, c, d;
            switch (i)
            {
                case 0: a = new Vector3(min.X, min.Y, min.Z); b = new Vector3(max.X, min.Y, min.Z); c = new Vector3(max.X, max.Y, min.Z); d = new Vector3(min.X, max.Y, min.Z); break;
                case 1: a = new Vector3(min.X, min.Y, max.Z); b = new Vector3(max.X, min.Y, max.Z); c = new Vector3(max.X, max.Y, max.Z); d = new Vector3(min.X, max.Y, max.Z); break;
                case 2: a = new Vector3(max.X, min.Y, min.Z); b = new Vector3(max.X, min.Y, max.Z); c = new Vector3(max.X, max.Y, max.Z); d = new Vector3(max.X, max.Y, min.Z); break;
                case 3: a = new Vector3(min.X, min.Y, max.Z); b = new Vector3(min.X, min.Y, min.Z); c = new Vector3(min.X, max.Y, min.Z); d = new Vector3(min.X, max.Y, max.Z); break;
                case 4: a = new Vector3(min.X, max.Y, min.Z); b = new Vector3(max.X, max.Y, min.Z); c = new Vector3(max.X, max.Y, max.Z); d = new Vector3(min.X, max.Y, max.Z); break;
                default: a = new Vector3(min.X, min.Y, min.Z); b = new Vector3(max.X, min.Y, min.Z); c = new Vector3(max.X, min.Y, max.Z); d = new Vector3(min.X, min.Y, max.Z); break;
            }

            result.Collision.Add(a);
            result.Collision.Add(b);
            result.Collision.Add(c);
            result.Collision.Add(a);
            result.Collision.Add(c);
            result.Collision.Add(d);
        }
    }
}
