using System.Text.Json.Nodes;

namespace AIVoxelStudio.AI.Tools;

internal sealed class GetUserInfoTool : ToolBase
{
    public override string Name => "GetUserInfo";
    public override string Description =>
        "获取当前用户上下文：玩家所在方块坐标、玩家当前选中的方块、已加载区域数量、当前建模模式。" +
        "这些信息会变化，需要定位时调用。";

    public override JsonObject ParametersSchema => ToolSchema.Schema();

    protected override ToolResult Run(JsonObject args, ToolContext context)
    {
        var player = context.PlayerBlock();
        string selected = context.SelectedBlock();
        return ToolResult.Success(
            $"模式={context.ModeName}；玩家方块坐标=({player.X},{player.Y},{player.Z})；" +
            $"选中方块={selected}；区域数={context.RegionCount()}");
    }
}
