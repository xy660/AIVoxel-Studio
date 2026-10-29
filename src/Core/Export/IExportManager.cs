using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Core.Export;

public sealed class ExportRequest
{
    public string Path { get; init; } = "";
    public string SaveName { get; init; } = "world";

    /// <summary>导出时的出生点（方块坐标），由组合根按玩家位置提供。</summary>
    public Vector3I? Spawn { get; init; }

    /// <summary>选区导出时的包围盒（闭区间）；非选区导出为 null。</summary>
    public RegionBox? Range { get; init; }
}

public sealed record ExportResult(bool Ok, string Message);

public interface IExportManager
{
    string Id { get; }
    string DisplayName { get; }
    string Extension { get; }

    /// <summary>产物是目录（存档）而非单个文件。</summary>
    bool IsDirectory { get; }

    /// <summary>是否需要先进入选区模式。</summary>
    bool RequiresRegion => false;

    /// <summary>选区合法性检查；返回非空文案时 UI 会先弹警示，用户可选择继续。</summary>
    string ValidateRegion(RegionBox region) => null;

    Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken token = default);
}

// 供 UI 使用的导出项：每个 profile 自带自己的导出器，互不引用
public sealed class ExportEntry
{
    public required string Label { get; init; }
    public required string Extension { get; init; }
    public string FilterName { get; init; } = "";
    public bool IsDirectory { get; init; }
    public bool RequiresRegion { get; init; }
    public Func<RegionBox, string> ValidateRegion { get; init; } = _ => null;

    /// <summary>单文件模式的默认目录；为空则用 user://exports。</summary>
    public string DefaultDir { get; init; } = "";

    /// <summary>目录模式下新建的存档文件夹名；单文件模式下作为默认文件名。</summary>
    public string FolderName { get; init; } = "";

    public required Func<string, RegionBox?, CancellationToken, Task<ExportResult>> Run { get; init; }
}
