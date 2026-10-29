using Godot;
using AIVoxelStudio.AI;
using AIVoxelStudio.AI.Tools;
using AIVoxelStudio.Core.Export;

namespace AIVoxelStudio.Core.Abstractions;

// 一个建模模式的全部差异点；组合根只认这个接口，业务层不判断 BuildKind
public interface IBuildProfile
{
    BuildKind Kind { get; }
    string DisplayName { get; }
    string PromptPath { get; }
    float BlockMeters { get; }
    float ReachMeters { get; }
    IToolProvider Tools { get; }
    IWorldSerializer Serializer { get; }
    IReadOnlyList<IExportManager> Exporters { get; }
    IBlockPalette Palette { get; }
    int RegionCount { get; }

    IPlayerWorld CreatePlayerWorld();
    IWorldRenderer CreateRenderer();
    void GenerateDefaultWorld();
    Vector3 FindSpawn(bool blank);
    ToolContext CreateToolContext(ToolContextEnvironment environment);

    /// <summary>视线取格：返回第一格非空气方块（origin/direction 用方块单位，maxDistance 为格数）。</summary>
    Vector3I? PickCell(Vector3 origin, Vector3 direction, float maxDistance);

    /// <summary>进入选区模式，用户确认后回调包围盒；取消则不回调。由组合根注入实现。</summary>
    void EnterSelectRegionMode(Action<RegionBox> callback);

    /// <summary>组合根注入选区入口（profile 不依赖输入与 UI）。</summary>
    void AttachRegionSelector(Action<Action<RegionBox>> enter);
}

public sealed class ToolContextEnvironment
{
    public Func<Vector3I> PlayerBlock { get; init; }
    public Func<Vector3, Vector3, Task<Image>> RenderSnapshot { get; init; }
    public Func<Image, string> SaveAttachment { get; init; }
}
