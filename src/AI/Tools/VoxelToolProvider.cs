namespace AIVoxelStudio.AI.Tools;

public sealed class VoxelToolProvider : IToolProvider
{
    public void Register(ToolRegistry registry)
    {
        CommonTools.RegisterAll(registry);
        BuiltinTools.RegisterAll(registry);
    }
}
