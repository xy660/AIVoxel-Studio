using System.Text.Json.Nodes;
using Godot;

namespace AIVoxelStudio.AI.Tools;

// 两种建模模式共用的工具
public static class CommonTools
{
    public static void RegisterAll(ToolRegistry registry)
    {
        registry.RegisterTool(new GetUserInfoTool());
        registry.RegisterTool(new RenderSnapshotTool());
    }

    private abstract class ToolBase : ITool
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

    private static JsonObject Schema(params (string Name, string Type, string Desc, bool Required)[] properties)
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

    private sealed class GetUserInfoTool : ToolBase
    {
        public override string Name => "GetUserInfo";
        public override string Description =>
            "获取当前用户上下文：玩家所在方块坐标、玩家当前选中的方块、已加载区域数量、当前建模模式。" +
            "这些信息会变化，需要定位时调用。";

        public override JsonObject ParametersSchema => Schema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var player = context.PlayerBlock();
            string selected = context.SelectedBlock();
            return ToolResult.Success(
                $"模式={context.ModeName}；玩家方块坐标=({player.X},{player.Y},{player.Z})；" +
                $"选中方块={selected}；区域数={context.RegionCount()}");
        }
    }

    private sealed class RenderSnapshotTool : ToolBase
    {
        public override string Name => "RenderSnapshot";
        public override string Description =>
            "从指定相机渲染当前世界的预览图，返回图片供你自检比例、遮挡与位置是否正确。" +
            "相机位于 x/y/z，镜头看向 lx/ly/lz，坐标单位均为方块（可为小数）。" +
            "预览图会保存到对话中可随时回看；每轮请求的截图次数有限，用尽后会被拒绝。直到用户重新发送信息后重置次数";

        public override JsonObject ParametersSchema => Schema(
            ("x", "number", "相机 X（方块坐标）", true),
            ("y", "number", "相机 Y（方块坐标）", true),
            ("z", "number", "相机 Z（方块坐标）", true),
            ("lx", "number", "看向点 X（方块坐标）", true),
            ("ly", "number", "看向点 Y（方块坐标）", true),
            ("lz", "number", "看向点 Z（方块坐标）", true));

        protected override async Task<ToolResult> RunAsync(JsonObject args, ToolContext context)
        {
            if (!TryFloats(args, new[] { "x", "y", "z", "lx", "ly", "lz" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (context.RenderSnapshot == null) return ToolResult.Failure("当前环境不支持渲染预览图");

            var image = await context.RenderSnapshot(
                new Vector3(v[0], v[1], v[2]) * context.BlockMeters,
                new Vector3(v[3], v[4], v[5]) * context.BlockMeters);
            if (image == null) return ToolResult.Failure("渲染预览图失败");

            string name = context.SaveAttachment?.Invoke(image);
            if (string.IsNullOrEmpty(name)) return ToolResult.Failure("预览图保存失败");

            return ToolResult.Success(
                $"已渲染预览图（{image.GetWidth()}×{image.GetHeight()}），下方附带的就是该视角，请据此检查造型。",
                new List<string> { name });
        }
    }

    private static bool TryFloats(JsonObject args, string[] names, out float[] values, out string error)
    {
        values = new float[names.Length];
        error = null;
        for (int i = 0; i < names.Length; i++)
        {
            if (!TryFloat(args, names[i], out values[i]))
            {
                error = $"参数 {names[i]} 缺失或不是数字";
                return false;
            }
        }
        return true;
    }

    private static bool TryFloat(JsonObject obj, string name, out float value)
    {
        value = 0;
        if (obj[name] is not JsonValue node) return false;
        try
        {
            value = (float)node.GetValue<double>();
            return true;
        }
        catch (Exception)
        {
            try
            {
                value = node.GetValue<int>();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
