using System.IO.Compression;
using Godot;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.Core.Export;

// Java 版结构方块 NBT（.nbt）：palette + blocks，坐标相对选区最小角
public sealed class McStructureExportManager : IExportManager
{
    /// <summary>结构方块单轴上限（20w16a 之后），超出后客户端无法加载。</summary>
    public const int MaxAxisSize = 48;

    private readonly McWorld _world;
    private readonly McAssets _assets;

    public McStructureExportManager(McWorld world, McAssets assets)
    {
        _world = world;
        _assets = assets;
    }

    public string Id => "mcstructure";
    public string DisplayName => "导出结构方块 NBT（选区）";
    public string Extension => "nbt";
    public bool IsDirectory => false;
    public bool RequiresRegion => true;

    public string ValidateRegion(RegionBox region)
    {
        var size = region.Size;
        if (size.X <= MaxAxisSize && size.Y <= MaxAxisSize && size.Z <= MaxAxisSize) return null;
        return $"选区为 {size.X}×{size.Y}×{size.Z}，超过结构方块上限 {MaxAxisSize}×{MaxAxisSize}×{MaxAxisSize} 格，" +
               "导出的 .nbt 可能无法在结构方块中加载（仍可尝试 /place template）。继续导出吗？";
    }

    public Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken token = default) =>
        Task.FromResult(Export(request));

    private ExportResult Export(ExportRequest request)
    {
        if (request.Range is not { } region) return new ExportResult(false, "结构导出需要先选取范围");

        var min = new Vector3I(region.Min.X, System.Math.Max(region.Min.Y, McWorld.MinY), region.Min.Z);
        var max = new Vector3I(region.Max.X, System.Math.Min(region.Max.Y, McWorld.MaxY), region.Max.Z);
        var size = new Vector3I(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1);

        var palette = new List<McBlockState>();
        var paletteIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var blocks = new List<(Vector3I Pos, int State)>((int)System.Math.Min(region.Volume, int.MaxValue));

        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        {
            var state = _world.GetBlock(x, y, z);
            // 原版结构文件不写入空气，粘贴时不会清空目标区域的方块
            if (state.IsAir) continue;

            if (!paletteIndex.TryGetValue(state.FullString, out int index))
            {
                index = palette.Count;
                paletteIndex[state.FullString] = index;
                palette.Add(state);
            }
            blocks.Add((new Vector3I(x - min.X, y - min.Y, z - min.Z), index));
        }

        string dir = Path.GetDirectoryName(request.Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = File.Create(request.Path);
        using var gzip = new GZipStream(stream, CompressionLevel.Optimal);
        using var nbt = new NbtWriter(gzip);

        nbt.BeginRootCompound();
        nbt.PutInt("DataVersion", _assets.DataVersion > 0 ? _assets.DataVersion : 3465);

        // size / pos 在结构文件里是 TAG_List<TAG_Int>（不是 IntArray）
        nbt.BeginList("size", NbtWriter.TagType.Int, 3);
        nbt.PutIntValue(size.X);
        nbt.PutIntValue(size.Y);
        nbt.PutIntValue(size.Z);

        nbt.BeginList("palette", NbtWriter.TagType.Compound, palette.Count);
        foreach (var state in palette)
        {
            nbt.BeginCompoundValue();
            nbt.PutString("Name", state.Id);
            if (state.Properties.Count > 0)
            {
                nbt.BeginCompound("Properties");
                foreach (var pair in state.Properties) nbt.PutString(pair.Key, pair.Value);
                nbt.EndTag();
            }
            nbt.EndTag();
        }

        nbt.BeginList("blocks", NbtWriter.TagType.Compound, blocks.Count);
        foreach (var (pos, stateIndex) in blocks)
        {
            nbt.BeginCompoundValue();
            nbt.BeginList("pos", NbtWriter.TagType.Int, 3);
            nbt.PutIntValue(pos.X);
            nbt.PutIntValue(pos.Y);
            nbt.PutIntValue(pos.Z);
            nbt.PutInt("state", stateIndex);
            nbt.EndTag();
        }

        nbt.BeginList("entities", NbtWriter.TagType.End, 0);
        nbt.EndTag();

        return new ExportResult(true,
            $"已导出结构 {size.X}×{size.Y}×{size.Z}，{blocks.Count} 个方块，{palette.Count} 种状态");
    }
}
