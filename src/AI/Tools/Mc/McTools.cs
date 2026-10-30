using System.Text;
using System.Text.Json.Nodes;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.AI.Tools;

// 我的世界建模工具
public static class McTools
{
    private const int MaxBlocksPerCall = 200_000;
    private const int ListPageSize = 30;

    public static void RegisterAll(ToolRegistry registry)
    {
        registry.RegisterTool(new PutBlockTool());
        registry.RegisterTool(new EraseTool());
        registry.RegisterTool(new FillBoxTool());
        registry.RegisterTool(new ReplaceBlocksTool());
        registry.RegisterTool(new GetBlockTool());
        registry.RegisterTool(new GetBlockBatchTool());
        registry.RegisterTool(new ListBlocksTool());
        registry.RegisterTool(new GetBlockStateTool());
        registry.RegisterTool(new McCopyRegionTool());
        McShapeTools.RegisterAll(registry);
    }

    private static McToolContext M(ToolContext context) =>
        context as McToolContext ??
        throw new InvalidOperationException("当前模式不支持我的世界工具");

    private sealed class PutBlockTool : ToolBase
    {
        public override string Name => "PutBlock";
        public override string Description =>
            "在指定坐标放置一个我的世界方块。blockId 形如 minecraft:oak_stairs；" +
            "properties 可选，键为裸属性名（如 facing、half），未给出的属性自动用默认值。" +
            "门/床/高植物等两格方块会自动补全另一格。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("x", "integer", "方块 X", true),
            ("y", "integer", "方块 Y", true),
            ("z", "integer", "方块 Z", true),
            ("blockId", "string", "方块 ID，如 minecraft:stone", true),
            ("properties", "object", "方块属性对象，如 {\"facing\":\"east\"}", false));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!TryState(ctx, args, out var state, out var explicitProps, out var error)) return ToolResult.Failure(error);
            if (!TryInt(args, "x", out int x) || !TryInt(args, "y", out int y) || !TryInt(args, "z", out int z))
                return ToolResult.Failure("参数 x/y/z 缺失");

            return McBlockPlacer.Place(ctx.World, ctx.Assets, x, y, z, state, explicitProps)
                ? ToolResult.Success($"已放置 {ctx.World.GetBlock(x, y, z).FullString} 于 ({x},{y},{z})")
                : ToolResult.Failure("坐标越界或目标位置被占用");
        }
    }

    private sealed class EraseTool : ToolBase
    {
        public override string Name => "Erase";
        public override string Description => "把指定坐标的方块清除为空气；门/床等两格方块会同时清除另一格。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("x", "integer", "方块 X", true),
            ("y", "integer", "方块 Y", true),
            ("z", "integer", "方块 Z", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!TryInt(args, "x", out int x) || !TryInt(args, "y", out int y) || !TryInt(args, "z", out int z))
                return ToolResult.Failure("参数 x/y/z 缺失");
            return McBlockPlacer.Break(ctx.World, ctx.Assets, x, y, z)
                ? ToolResult.Success($"已清除 ({x},{y},{z})")
                : ToolResult.Failure("该位置本来就没有方块");
        }
    }

    private sealed class FillBoxTool : ToolBase
    {
        public override string Name => "FillBox";
        public override string Description =>
            "用同一种方块填充闭区间长方体（含 x1/x2、y1/y2、z1/z2 两端）。properties 可选。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("x1", "integer", "起点 X", true),
            ("y1", "integer", "起点 Y", true),
            ("z1", "integer", "起点 Z", true),
            ("x2", "integer", "终点 X", true),
            ("y2", "integer", "终点 Y", true),
            ("z2", "integer", "终点 Z", true),
            ("blockId", "string", "方块 ID", true),
            ("properties", "object", "方块属性对象", false));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryState(ctx, args, out var state, out var explicitProps, out error)) return ToolResult.Failure(error);

            int x1 = Math.Min(v[0], v[3]), x2 = Math.Max(v[0], v[3]);
            int y1 = Math.Min(v[1], v[4]), y2 = Math.Max(v[1], v[4]);
            int z1 = Math.Min(v[2], v[5]), z2 = Math.Max(v[2], v[5]);
            long volume = (long)(x2 - x1 + 1) * (y2 - y1 + 1) * (z2 - z1 + 1);
            if (volume > MaxBlocksPerCall)
                return ToolResult.Failure($"体积 {volume} 超过单次上限 {MaxBlocksPerCall}");

            int changed = 0;
            for (int y = y1; y <= y2; y++)
            for (int z = z1; z <= z2; z++)
            for (int x = x1; x <= x2; x++)
                if (ctx.World.SetBlock(x, y, z, state, false)) changed++;

            // 先铺后算：区域内方块都写完后，再按邻居补全连接属性
            if (McConnections.Applies(state))
            {
                for (int y = y1; y <= y2; y++)
                for (int z = z1; z <= z2; z++)
                for (int x = x1; x <= x2; x++)
                {
                    var current = ctx.World.GetBlock(x, y, z);
                    if (current.Id != state.Id) continue;
                    var adjusted = McConnections.Adjust(ctx.World, ctx.Assets, x, y, z, current, explicitProps);
                    if (!adjusted.Equals(current)) ctx.World.SetBlock(x, y, z, adjusted, false);
                }
            }

            ctx.World.NotifyRegionChanged(x1, y1, z1, x2, y2, z2);

            return ToolResult.Success($"已填充 {state.FullString}，体积 {volume}，实际变化 {changed} 格");
        }
    }

    private sealed class ReplaceBlocksTool : ToolBase
    {
        public override string Name => "ReplaceBlocks";
        public override string Description =>
            "在闭区间长方体内，把匹配的方块替换为目标方块。from 传方块 ID 或 *（* 表示任意非空气方块）；" +
            "targetId 与 properties 指定目标状态。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("x1", "integer", "起点 X", true),
            ("y1", "integer", "起点 Y", true),
            ("z1", "integer", "起点 Z", true),
            ("x2", "integer", "终点 X", true),
            ("y2", "integer", "终点 Y", true),
            ("z2", "integer", "终点 Z", true),
            ("from", "string", "匹配的方块 ID，或 *", true),
            ("blockId", "string", "目标方块 ID", true),
            ("properties", "object", "目标方块属性对象", false));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (args["from"] is not JsonValue fromValue || !fromValue.TryGetValue<string>(out var from))
                return ToolResult.Failure("参数 from 缺失");
            if (!TryState(ctx, args, out var target, out var explicitProps, out error)) return ToolResult.Failure(error);

            int x1 = Math.Min(v[0], v[3]), x2 = Math.Max(v[0], v[3]);
            int y1 = Math.Min(v[1], v[4]), y2 = Math.Max(v[1], v[4]);
            int z1 = Math.Min(v[2], v[5]), z2 = Math.Max(v[2], v[5]);
            long volume = (long)(x2 - x1 + 1) * (y2 - y1 + 1) * (z2 - z1 + 1);
            if (volume > MaxBlocksPerCall)
                return ToolResult.Failure($"体积 {volume} 超过单次上限 {MaxBlocksPerCall}");

            bool any = from == "*";
            string matchId = any ? null : McBlockState.NormalizeId(from);
            int changed = 0;
            for (int y = y1; y <= y2; y++)
            for (int z = z1; z <= z2; z++)
            for (int x = x1; x <= x2; x++)
            {
                var current = ctx.World.GetBlock(x, y, z);
                if (current.IsAir) continue;
                if (!any && current.Id != matchId) continue;
                if (ctx.World.SetBlock(x, y, z, target, false)) changed++;
            }

            if (McConnections.Applies(target))
            {
                for (int y = y1; y <= y2; y++)
                for (int z = z1; z <= z2; z++)
                for (int x = x1; x <= x2; x++)
                {
                    var current = ctx.World.GetBlock(x, y, z);
                    if (current.Id != target.Id) continue;
                    var adjusted = McConnections.Adjust(ctx.World, ctx.Assets, x, y, z, current, explicitProps);
                    if (!adjusted.Equals(current)) ctx.World.SetBlock(x, y, z, adjusted, false);
                }
            }

            ctx.World.NotifyRegionChanged(x1, y1, z1, x2, y2, z2);

            return ToolResult.Success($"已替换 {changed} 格为 {target.FullString}");
        }
    }

    private sealed class GetBlockTool : ToolBase
    {
        public override string Name => "GetBlock";
        public override string Description => "查询指定坐标的方块状态字符串与名称。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("x", "integer", "方块 X", true),
            ("y", "integer", "方块 Y", true),
            ("z", "integer", "方块 Z", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!TryInts(args, new[] { "x", "y", "z" }, out var v, out var error))
                return ToolResult.Failure(error);

            var state = ctx.World.GetBlock(v[0], v[1], v[2]);
            if (state.IsAir) return ToolResult.Success($"({v[0]},{v[1]},{v[2]}) 是空气");
            return ToolResult.Success(
                $"({v[0]},{v[1]},{v[2]}) = {state.FullString}（{ctx.Assets.DisplayName(state.Id)}）");
        }
    }

    private sealed class GetBlockBatchTool : ToolBase
    {
        private const int MaxPoints = 200;
        private const int MaxRegionResults = 512;
        private const int MaxRegionVolume = 1_000_000;

        public override string Name => "QueryBlockBatch";
        public override string Description =>
            "批量查询方块，两种用法二选一：points 传坐标点列表（每项 {x,y,z}，最多 200 个）；" +
            $"或给 x1..z2 闭区间扫描区域，只返回非空气方块，最多 {MaxRegionResults} 条。";

        public override JsonObject ParametersSchema
        {
            get
            {
                var schema = ToolSchema.Schema(
                    ("x1", "integer", "区域起点 X（区域模式必填）", false),
                    ("y1", "integer", "区域起点 Y（区域模式必填）", false),
                    ("z1", "integer", "区域起点 Z（区域模式必填）", false),
                    ("x2", "integer", "区域终点 X（区域模式必填）", false),
                    ("y2", "integer", "区域终点 Y（区域模式必填）", false),
                    ("z2", "integer", "区域终点 Z（区域模式必填）", false));
                schema["properties"]["points"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = $"坐标点列表，如 [{{\"x\":1,\"y\":2,\"z\":3}}]，最多 {MaxPoints} 个",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["x"] = new JsonObject { ["type"] = "integer" },
                            ["y"] = new JsonObject { ["type"] = "integer" },
                            ["z"] = new JsonObject { ["type"] = "integer" },
                        },
                        ["required"] = new JsonArray { "x", "y", "z" },
                    },
                };
                return schema;
            }
        }

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);

            if (args["points"] is JsonArray points)
            {
                if (points.Count == 0) return ToolResult.Failure("points 不能为空");
                if (points.Count > MaxPoints) return ToolResult.Failure($"points 最多 {MaxPoints} 个，请分批查询");

                var lines = new List<string>(points.Count);
                foreach (var node in points)
                {
                    if (node is not JsonObject point ||
                        !TryInt(point, "x", out int px) ||
                        !TryInt(point, "y", out int py) ||
                        !TryInt(point, "z", out int pz))
                        return ToolResult.Failure("points 每项都需要整数 x/y/z");
                    lines.Add(Describe(ctx, px, py, pz));
                }
                return ToolResult.Success(string.Join("\n", lines));
            }

            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error + "（也可以改用 points 传坐标点列表）");

            int x1 = Math.Min(v[0], v[3]), x2 = Math.Max(v[0], v[3]);
            int y1 = Math.Clamp(Math.Min(v[1], v[4]), McWorld.MinY, McWorld.MaxY);
            int y2 = Math.Clamp(Math.Max(v[1], v[4]), McWorld.MinY, McWorld.MaxY);
            int z1 = Math.Min(v[2], v[5]), z2 = Math.Max(v[2], v[5]);

            long volume = (long)(x2 - x1 + 1) * (y2 - y1 + 1) * (z2 - z1 + 1);
            if (volume > MaxRegionVolume)
                return ToolResult.Failure($"区域过大（{volume} 格），请缩小范围或改用 points");

            var found = new List<string>();
            bool truncated = false;
            for (int y = y1; y <= y2 && !truncated; y++)
            for (int z = z1; z <= z2 && !truncated; z++)
            for (int x = x1; x <= x2; x++)
            {
                if (ctx.World.GetBlock(x, y, z).IsAir) continue;
                if (found.Count >= MaxRegionResults)
                {
                    truncated = true;
                    break;
                }
                found.Add(Describe(ctx, x, y, z));
            }

            string range = $"({x1},{y1},{z1})-({x2},{y2},{z2})";
            if (found.Count == 0) return ToolResult.Success($"区域 {range} 内没有非空气方块");

            string header = $"区域 {range} 非空气方块 {found.Count} 个" +
                            (truncated ? $"（已达 {MaxRegionResults} 条上限被截断，请缩小范围）" : "") + "：";
            return ToolResult.Success(header + "\n" + string.Join("\n", found));
        }

        private static string Describe(McToolContext ctx, int x, int y, int z)
        {
            var state = ctx.World.GetBlock(x, y, z);
            if (state.IsAir) return $"({x},{y},{z}) 空气";
            return $"({x},{y},{z}) {state.FullString}（{ctx.Assets.DisplayName(state.Id)}）";
        }
    }

    private sealed class ListBlocksTool : ToolBase
    {
        public override string Name => "ListBlocks";
        public override string Description =>
            "搜索可用的我的世界方块。query 可匹配方块 ID 或英文名（留空列出全部），page 从 1 开始，每页 30 条。" +
            "返回结果含方块 ID、名称与默认状态；需要属性细节时再调用 GetBlockState。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("query", "string", "搜索词，可为空", false),
            ("page", "integer", "页码，从 1 开始", false));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            string query = args["query"] is JsonValue queryValue && queryValue.TryGetValue<string>(out var text)
                ? text.Trim().ToLowerInvariant()
                : "";
            int page = TryInt(args, "page", out int requested) ? Math.Max(1, requested) : 1;

            var matches = ctx.Assets.Blocks
                .Where(def => query.Length == 0 ||
                              def.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              def.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int pages = Math.Max(1, (matches.Count + ListPageSize - 1) / ListPageSize);
            page = Math.Min(page, pages);
            var slice = matches.Skip((page - 1) * ListPageSize).Take(ListPageSize).ToList();

            var builder = new StringBuilder();
            builder.Append($"共 {matches.Count} 个方块，第 {page}/{pages} 页：");
            foreach (var def in slice)
            {
                builder.Append('\n').Append(def.Id).Append(" — ").Append(ctx.Assets.DisplayName(def.Id));
                if (def.PropertyValues.Count > 0)
                {
                    builder.Append("（属性：");
                    builder.Append(string.Join(", ", def.PropertyValues.Keys));
                    builder.Append("）");
                }
            }
            return ToolResult.Success(builder.ToString());
        }
    }

    private sealed class GetBlockStateTool : ToolBase
    {
        public override string Name => "GetBlockState";
        public override string Description =>
            "查询某个方块的全部属性、取值与默认值，并给出默认状态字符串。放方块前可用它确认属性写法。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("blockId", "string", "方块 ID，如 minecraft:oak_stairs", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (args["blockId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id))
                return ToolResult.Failure("参数 blockId 缺失");
            id = McBlockState.NormalizeId(id);

            if (!ctx.Assets.IsKnown(id))
                return ToolResult.Failure($"未知方块 {id}，请先调用 ListBlocks 搜索");

            var def = ctx.Assets.Blocks.FirstOrDefault(d => d.Id == id);
            if (def == null) return ToolResult.Failure($"未知方块 {id}");

            var defaultState = ctx.Assets.DefaultState(id);
            var builder = new StringBuilder();
            builder.Append(id).Append(" — ").Append(ctx.Assets.DisplayName(id));
            builder.Append("\n默认状态：").Append(defaultState?.FullString ?? id);

            if (def.PropertyValues.Count == 0)
            {
                builder.Append("\n（方块没有可配置属性）");
            }
            else
            {
                foreach (var pair in def.PropertyValues.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    builder.Append('\n')
                        .Append(pair.Key).Append("：").Append(string.Join("|", pair.Value))
                        .Append("（默认 ").Append(def.Defaults[pair.Key]).Append("）");
                }
            }
            return ToolResult.Success(builder.ToString());
        }
    }

    internal static bool TryState(
        McToolContext context, JsonObject args,
        out McBlockState state, out Dictionary<string, string> explicitProps, out string error)
    {
        state = null;
        explicitProps = null;
        error = null;
        if (args["blockId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id))
        {
            error = "参数 blockId 缺失";
            return false;
        }

        id = McBlockState.NormalizeId(id);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args["properties"] is JsonObject props)
        {
            foreach (var pair in props)
            {
                if (pair.Value is not JsonValue value)
                {
                    error = $"属性 {pair.Key} 不是字符串/布尔值";
                    return false;
                }
                if (value.TryGetValue<string>(out var text)) properties[pair.Key] = text;
                else if (value.TryGetValue<bool>(out bool flag)) properties[pair.Key] = flag ? "true" : "false";
                else if (value.TryGetValue<int>(out int number)) properties[pair.Key] = number.ToString();
                else
                {
                    error = $"属性 {pair.Key} 的值类型不支持";
                    return false;
                }
            }
        }

        explicitProps = properties;
        if (!context.Assets.TryResolveState(id, properties, out state, out error)) return false;
        return true;
    }

    internal static bool TryInt(JsonObject args, string name, out int value)
    {
        value = 0;
        if (args[name] is not JsonValue node) return false;
        if (node.TryGetValue<int>(out value)) return true;
        if (node.TryGetValue<double>(out double number))
        {
            value = (int)Math.Round(number);
            return true;
        }
        return false;
    }

    internal static bool TryInts(JsonObject args, string[] names, out int[] values, out string error)
    {
        values = new int[names.Length];
        error = null;
        for (int i = 0; i < names.Length; i++)
        {
            if (!TryInt(args, names[i], out values[i]))
            {
                error = $"参数 {names[i]} 缺失或不是整数";
                return false;
            }
        }
        return true;
    }
}
