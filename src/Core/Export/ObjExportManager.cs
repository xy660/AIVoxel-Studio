using System.Globalization;
using System.Text;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;
using Godot;

namespace AIVoxelStudio.Core.Export;

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

    // 立方体 8 个角点，按 (x,y,z) 位序排列
    private static readonly Vector3I[] Corners =
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
        new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1),
    };

    // 每个方向的面顶点（逆时针朝外），vn 索引从 1 开始
    private static readonly int[][] FaceVerts =
    {
        new[] { 3, 2, 6, 7 }, // +Z
        new[] { 0, 4, 5, 1 }, // -Z
        new[] { 4, 7, 6, 5 }, // +Y
        new[] { 0, 1, 2, 3 }, // -Y
        new[] { 1, 5, 6, 2 }, // +X
        new[] { 0, 3, 7, 4 }, // -X
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
        Task.FromResult(Export(request.Path, request.Range));

    private ExportResult Export(string path, RegionBox? range)
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

        int faces = 0;
        int vertexBase = 0;
        var sb = new StringBuilder(1 << 20);
        sb.Append("# AIVoxelStudio OBJ export\n");
        sb.Append("# block size: ").Append(Fmt(BlockSizeMeters)).Append(" m\n");
        sb.Append("o voxel\n");
        sb.Append("vn 0 0 1\nvn 0 0 -1\nvn 0 1 0\nvn 0 -1 0\nvn 1 0 0\nvn -1 0 0\n");

        foreach (var (key, blocks) in groups)
        {
            sb.Append("g color_")
                .Append((key >> 16) & 0xFF).Append('_')
                .Append((key >> 8) & 0xFF).Append('_')
                .Append(key & 0xFF).Append('\n');

            foreach (var pos in blocks)
            {
                int mask = 0;
                for (int f = 0; f < FaceOffsets.Length; f++)
                {
                    var n = FaceOffsets[f];
                    if (SolidAt(pos + n)) continue;
                    mask |= 1 << f;
                }
                if (mask == 0) continue;

                int baseIndex = vertexBase + 1;
                for (int c = 0; c < Corners.Length; c++)
                {
                    var corner = Corners[c];
                    sb.Append("v ")
                        .Append(Fmt((pos.X + corner.X) * BlockSizeMeters)).Append(' ')
                        .Append(Fmt((pos.Y + corner.Y) * BlockSizeMeters)).Append(' ')
                        .Append(Fmt((pos.Z + corner.Z) * BlockSizeMeters)).Append('\n');
                }
                vertexBase += Corners.Length;

                for (int f = 0; f < FaceOffsets.Length; f++)
                {
                    if ((mask & (1 << f)) == 0) continue;

                    sb.Append("f");
                    foreach (int c in FaceVerts[f])
                    {
                        sb.Append(' ').Append(baseIndex + c).Append("//").Append(f + 1);
                    }
                    sb.Append('\n');
                    faces++;
                }
            }
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return new ExportResult(true, $"已导出 {faces} 个面");
    }

    private static string Fmt(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
