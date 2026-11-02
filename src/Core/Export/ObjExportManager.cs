using System.Globalization;
using System.Text;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;
using Godot;

namespace AIVoxelStudio.Core.Export;

// OBJ 导出：按颜色分组，同色共面的可见面做贪心合并，顶点去重，并附带 MTL 材质
public sealed class ObjExportManager : IExportManager
{
    // 一个方块 0.125 m 宽，是 Minecraft 方块的八分之一
    public const float BlockSizeMeters = VoxelUnits.BlockMeters;

    private static readonly Vector3I[] FaceOffsets =
    {
        new(0, 0, 1), new(0, 0, -1),
        new(0, 1, 0), new(0, -1, 0),
        new(1, 0, 0), new(-1, 0, 0),
    };

    private readonly World _world;
    private readonly bool _rangeOnly;

    public ObjExportManager(World world, bool rangeOnly = false)
    {
        _world = world;
        _rangeOnly = rangeOnly;
    }

    public string Id => _rangeOnly ? "objrange" : "obj";
    public string DisplayName => _rangeOnly ? "导出 OBJ（选区）" : "导出 OBJ";
    public string Extension => "obj";
    public bool IsDirectory => false;
    public bool RequiresRegion => _rangeOnly;

    public Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken token = default) =>
        Task.FromResult(Export(request.Path, request.Range, token));

    private ExportResult Export(string path, RegionBox? range, CancellationToken token)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        bool SolidAt(Vector3I cell) =>
            _world.GetBlock(cell.X, cell.Y, cell.Z).Type == (byte)BlockType.Color &&
            (range == null || range.Value.Contains(cell));

        var groups = new SortedDictionary<int, List<Vector3I>>();
        foreach (var chunk in _world.Chunks.OrderBy(c => c.Coord.X).ThenBy(c => c.Coord.Y).ThenBy(c => c.Coord.Z))
        {
            for (int i = 0; i < Chunk.Size3; i++)
            {
                var block = chunk.Blocks[i];
                if (block.Type != (byte)BlockType.Color) continue;

                int lx = i / (Chunk.Size * Chunk.Size);
                int ly = (i / Chunk.Size) % Chunk.Size;
                int lz = i % Chunk.Size;
                var pos = chunk.WorldOrigin + new Vector3I(lx, ly, lz);
                if (range != null && !range.Value.Contains(pos)) continue;

                int key = (block.R << 16) | (block.G << 8) | block.B;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<Vector3I>();
                    groups[key] = list;
                }
                list.Add(pos);
            }
        }

        string mtlName = Path.GetFileNameWithoutExtension(path) + ".mtl";
        string mtlPath = Path.ChangeExtension(path, ".mtl");

        int faces = 0;
        int vertexCount = 0;
        var vertices = new Dictionary<Vector3I, int>();

        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.Write("# AIVoxelStudio OBJ export\n");
            writer.Write($"# block size: {Fmt(BlockSizeMeters)} m\n");
            writer.Write($"mtllib {mtlName}\n");
            writer.Write("o voxel\n");
            writer.Write("vn 0 0 1\nvn 0 0 -1\nvn 0 1 0\nvn 0 -1 0\nvn 1 0 0\nvn -1 0 0\n");

            foreach (var (key, blocks) in groups)
            {
                token.ThrowIfCancellationRequested();
                string material = MaterialName(key);
                writer.Write($"g {material}\n");
                writer.Write($"usemtl {material}\n");

                for (int direction = 0; direction < FaceOffsets.Length; direction++)
                {
                    var quads = MergeFaces(blocks, direction, SolidAt);
                    foreach (var quad in quads)
                    {
                        var corners = QuadCorners(direction, quad);
                        var indices = new int[corners.Length];
                        for (int i = 0; i < corners.Length; i++)
                        {
                            if (!vertices.TryGetValue(corners[i], out int index))
                            {
                                index = ++vertexCount;
                                vertices[corners[i]] = index;
                                writer.Write($"v {Fmt(corners[i].X * BlockSizeMeters)} " +
                                             $"{Fmt(corners[i].Y * BlockSizeMeters)} " +
                                             $"{Fmt(corners[i].Z * BlockSizeMeters)}\n");
                            }
                            indices[i] = index;
                        }

                        var line = new StringBuilder("f");
                        foreach (int index in indices)
                            line.Append(' ').Append(index).Append("//").Append(direction + 1);
                        line.Append('\n');
                        writer.Write(line.ToString());
                        faces++;
                    }
                }
            }
        }

        WriteMtl(mtlPath, groups, Path.GetFileName(path));
        return new ExportResult(true, $"已导出 {faces} 个面 / {vertexCount} 个顶点");
    }

    private readonly record struct Quad(int Layer, int U, int V, int W, int H);

    // 把同色方块某一方向可见的面按平面切成网格，贪心合并成矩形
    private static List<Quad> MergeFaces(List<Vector3I> blocks, int direction, Func<Vector3I, bool> solidAt)
    {
        var layers = new Dictionary<int, List<(int U, int V)>>();
        var normal = FaceOffsets[direction];

        foreach (var pos in blocks)
        {
            if (solidAt(pos + normal)) continue;

            int layer, u, v;
            switch (direction)
            {
                case 0: // +Z
                case 1: // -Z
                    layer = pos.Z;
                    u = pos.X;
                    v = pos.Y;
                    break;
                case 2: // +Y
                case 3: // -Y
                    layer = pos.Y;
                    u = pos.X;
                    v = pos.Z;
                    break;
                default: // +X / -X
                    layer = pos.X;
                    u = pos.Y;
                    v = pos.Z;
                    break;
            }

            if (!layers.TryGetValue(layer, out var cells))
            {
                cells = new List<(int, int)>();
                layers[layer] = cells;
            }
            cells.Add((u, v));
        }

        var quads = new List<Quad>();
        foreach (var (layer, cells) in layers)
        {
            var remaining = new HashSet<(int U, int V)>(cells);
            var ordered = new List<(int U, int V)>(remaining);
            ordered.Sort(static (a, b) =>
                a.U != b.U ? a.U.CompareTo(b.U) : a.V.CompareTo(b.V));

            foreach (var (u, v) in ordered)
            {
                if (!remaining.Contains((u, v))) continue;

                int width = 1;
                while (remaining.Contains((u + width, v))) width++;

                int height = 1;
                while (true)
                {
                    bool rowComplete = true;
                    for (int i = 0; i < width; i++)
                    {
                        if (remaining.Contains((u + i, v + height))) continue;
                        rowComplete = false;
                        break;
                    }
                    if (!rowComplete) break;
                    height++;
                }

                for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                    remaining.Remove((u + i, v + j));

                quads.Add(new Quad(layer, u, v, width, height));
            }
        }

        return quads;
    }

    // 合并矩形按方向还原成世界坐标的四个角点（逆时针朝外）
    private static Vector3I[] QuadCorners(int direction, Quad quad)
    {
        int x, y, z, w, h;
        switch (direction)
        {
            case 0:
            case 1:
                x = quad.U; y = quad.V; z = quad.Layer;
                w = quad.W; h = quad.H;
                break;
            case 2:
            case 3:
                x = quad.U; z = quad.V; y = quad.Layer;
                w = quad.W; h = quad.H;
                break;
            default:
                y = quad.U; z = quad.V; x = quad.Layer;
                w = quad.W; h = quad.H;
                break;
        }

        return direction switch
        {
            0 => new[]
            {
                new Vector3I(x, y, z + 1), new Vector3I(x + w, y, z + 1),
                new Vector3I(x + w, y + h, z + 1), new Vector3I(x, y + h, z + 1),
            },
            1 => new[]
            {
                new Vector3I(x, y, z), new Vector3I(x, y + h, z),
                new Vector3I(x + w, y + h, z), new Vector3I(x + w, y, z),
            },
            2 => new[]
            {
                new Vector3I(x, y + 1, z), new Vector3I(x, y + 1, z + h),
                new Vector3I(x + w, y + 1, z + h), new Vector3I(x + w, y + 1, z),
            },
            3 => new[]
            {
                new Vector3I(x, y, z), new Vector3I(x + w, y, z),
                new Vector3I(x + w, y, z + h), new Vector3I(x, y, z + h),
            },
            4 => new[]
            {
                new Vector3I(x + 1, y, z), new Vector3I(x + 1, y + w, z),
                new Vector3I(x + 1, y + w, z + h), new Vector3I(x + 1, y, z + h),
            },
            _ => new[]
            {
                new Vector3I(x, y, z), new Vector3I(x, y, z + h),
                new Vector3I(x, y + w, z + h), new Vector3I(x, y + w, z),
            },
        };
    }

    private static void WriteMtl(string path, SortedDictionary<int, List<Vector3I>> groups, string name)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.Write("# AIVoxelStudio MTL export\n");
        writer.Write($"# generated for {name}\n");

        foreach (var key in groups.Keys)
        {
            writer.Write($"newmtl {MaterialName(key)}\n");
            writer.Write("Ka 0 0 0\n");
            writer.Write($"Kd {Fmt(((key >> 16) & 0xFF) / 255f)} " +
                         $"{Fmt(((key >> 8) & 0xFF) / 255f)} " +
                         $"{Fmt((key & 0xFF) / 255f)}\n");
            writer.Write("Ks 0 0 0\n");
            writer.Write("Ns 0\n");
            writer.Write("d 1\n");
            writer.Write("illum 1\n");
        }
    }

    private static string MaterialName(int key) =>
        $"color_{(key >> 16) & 0xFF}_{(key >> 8) & 0xFF}_{key & 0xFF}";

    private static string Fmt(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
