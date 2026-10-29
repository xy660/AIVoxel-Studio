using System.Text.Json.Nodes;

namespace AIVoxelStudio.AI.Tools;

// 所有工具的统一入口：默认同步实现 Run，异步工具覆写 RunAsync
internal abstract class ToolBase : ITool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonObject ParametersSchema { get; }

    public Task<ToolResult> ExecuteAsync(JsonObject args, ToolContext context) => RunAsync(args, context);

    protected virtual Task<ToolResult> RunAsync(JsonObject args, ToolContext context) =>
        Task.FromResult(Run(args, context));

    protected virtual ToolResult Run(JsonObject args, ToolContext context) =>
        ToolResult.Failure("工具未实现");
}

internal static class ToolSchema
{
    public static JsonObject Schema(params (string Name, string Type, string Desc, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, desc, isRequired) in properties)
        {
            props[name] = new JsonObject { ["type"] = type, ["description"] = desc };
            if (isRequired) required.Add(name);
        }

        var result = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Count > 0) result["required"] = required;
        return result;
    }
}
