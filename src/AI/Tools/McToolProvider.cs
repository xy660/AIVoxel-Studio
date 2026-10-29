namespace AIVoxelStudio.AI.Tools;

public sealed class McToolProvider : IToolProvider
{
    public void Register(ToolRegistry registry)
    {
        CommonTools.RegisterAll(registry);
        McTools.RegisterAll(registry);
    }
}
