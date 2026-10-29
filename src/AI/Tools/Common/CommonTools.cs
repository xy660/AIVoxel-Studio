namespace AIVoxelStudio.AI.Tools;

// 两种建模模式共用的工具：定位、预览截图、向用户提问
public static class CommonTools
{
    public static void RegisterAll(ToolRegistry registry)
    {
        registry.RegisterTool(new GetUserInfoTool());
        registry.RegisterTool(new RenderSnapshotTool());
        registry.RegisterTool(new AskUserTool());
    }
}
