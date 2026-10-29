namespace AIVoxelStudio.AI.Tools;

// 每个模式提供自己的工具集；注册顺序必须固定，保证 tools schema 前缀稳定
public interface IToolProvider
{
    void Register(ToolRegistry registry);
}
