using System.Text.Json.Nodes;
using AIVoxelStudio.Core.Mc;
using Godot;

namespace AIVoxelStudio.AI.Tools;

// 我的世界形状工具：与纯色模式同名同参数，方块参数为 blockId + properties（可选）
public static class McShapeTools
{
    private const int MaxBlocksPerCall = 200_000;

    public static void RegisterAll(ToolRegistry registry)
    {
        registry.RegisterTool(new PutLineTool());
        registry.RegisterTool(new PutCurveTool());
        registry.RegisterTool(new FillBallTool());
        registry.RegisterTool(new ConstructBallTool());
        registry.RegisterTool(new FillEllipseTool());
        registry.RegisterTool(new ConstructEllipseTool());
        registry.RegisterTool(new FillCylinderTool());
        registry.RegisterTool(new ConstructCylinderTool());
        registry.RegisterTool(new ConstructBoxTool());
    }

    private static McToolContext M(ToolContext context) =>
        context as McToolContext ??
        throw new InvalidOperationException("当前模式不支持我的世界工具");

    private sealed class ShapeWriter
    {
        private readonly McToolContext _context;
        private readonly McBlockState _state;
        private readonly Dictionary<string, string> _explicitProps;
        private readonly List<Vector3I> _cells = new();

        public int Changed { get; private set; }

        public ShapeWriter(McToolContext context, McBlockState state, Dictionary<string, string> explicitProps)
        {
            _context = context;
            _state = state;
            _explicitProps = explicitProps;
        }

        public void Put(int x, int y, int z)
        {
            if (y < McWorld.MinY || y > McWorld.MaxY) return;
            if (!_context.World.SetBlock(x, y, z, _state, false)) return;
            Changed++;
            _cells.Add(new Vector3I(x, y, z));
        }

        public void Finish(Vector3I min, Vector3I max)
        {
            if (Changed > 0)
            {
                // 先铺后算：形状写完后，再按邻居补全栅栏 / 玻璃板 / 围墙等连接属性
                if (McConnections.Applies(_state))
                {
                    foreach (var cell in _cells)
                    {
                        var current = _context.World.GetBlock(cell.X, cell.Y, cell.Z);
                        if (current.Id != _state.Id) continue;
                        var adjusted = McConnections.Adjust(
                            _context.World, _context.Assets, cell.X, cell.Y, cell.Z, current, _explicitProps);
                        if (!adjusted.Equals(current))
                            _context.World.SetBlock(cell.X, cell.Y, cell.Z, adjusted, false);
                    }
                }

                _context.World.NotifyRegionChanged(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
            }
        }
    }

    private static ToolResult Report(ShapeWriter writer, string label) =>
        ToolResult.Success($"{label}：写入 {writer.Changed} 格");

    private static ToolResult FillShape(
        McToolContext context,
        McBlockState state,
        Dictionary<string, string> explicitProps,
        Vector3I min,
        Vector3I max,
        Func<int, int, int, bool> inside,
        bool hollow,
        string label)
    {
        long volume = ((long)max.X - min.X + 1) * ((long)max.Y - min.Y + 1) * ((long)max.Z - min.Z + 1);
        if (volume > (long)MaxBlocksPerCall * 8)
            return ToolResult.Failure($"{label} 的包围盒过大（{volume:F0} 格），请缩小尺寸或拆分为多次调用");

        int minY = Math.Max(min.Y, McWorld.MinY);
        int maxY = Math.Min(max.Y, McWorld.MaxY);

        long count = 0;
        for (int y = minY; y <= maxY; y++)
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        {
            if (!inside(x, y, z)) continue;
            if (hollow && !IsSurface(x, y, z, inside)) continue;
            if (++count > MaxBlocksPerCall)
                return ToolResult.Failure($"{label} 需要写入超过 {MaxBlocksPerCall} 格，请缩小尺寸或拆分为多次调用");
        }

        var writer = new ShapeWriter(context, state, explicitProps);
        for (int y = minY; y <= maxY; y++)
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        {
            if (!inside(x, y, z)) continue;
            if (hollow && !IsSurface(x, y, z, inside)) continue;
            writer.Put(x, y, z);
        }

        writer.Finish(min, max);
        return Report(writer, label);
    }

    private static bool IsSurface(int x, int y, int z, Func<int, int, int, bool> inside) =>
        !inside(x + 1, y, z) || !inside(x - 1, y, z) ||
        !inside(x, y + 1, z) || !inside(x, y - 1, z) ||
        !inside(x, y, z + 1) || !inside(x, y, z - 1);

    private static ToolResult DrawLine(
        McToolContext context,
        Vector3I a,
        Vector3I b,
        McBlockState state,
        Dictionary<string, string> explicitProps,
        string label)
    {
        int steps = Math.Max(Math.Abs(b.X - a.X), Math.Max(Math.Abs(b.Y - a.Y), Math.Abs(b.Z - a.Z)));
        if (steps + 1 > MaxBlocksPerCall)
            return ToolResult.Failure($"{label} 超过单次上限 {MaxBlocksPerCall} 格，请缩短线段");

        var writer = new ShapeWriter(context, state, explicitProps);
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            writer.Put(
                (int)Math.Round(a.X + (b.X - a.X) * t),
                (int)Math.Round(a.Y + (b.Y - a.Y) * t),
                (int)Math.Round(a.Z + (b.Z - a.Z) * t));
        }

        writer.Finish(MinOf(a, b), MaxOf(a, b));
        return Report(writer, label);
    }

    private static Vector3I MinOf(Vector3I a, Vector3I b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Vector3I MaxOf(Vector3I a, Vector3I b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private sealed class PutLineTool : ToolBase
    {
        public override string Name => "PutLine";
        public override string Description => "在两个整数坐标之间画一条 3D 直线（粗细为一个方块）。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("x1", "integer", "起点 X", true),
            ("y1", "integer", "起点 Y", true),
            ("z1", "integer", "起点 Z", true),
            ("x2", "integer", "终点 X", true),
            ("y2", "integer", "终点 Y", true),
            ("z2", "integer", "终点 Z", true),
            ("blockId", "string", "线条方块 ID", true),
            ("properties", "object", "方块属性对象", false));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);

            return DrawLine(ctx,
                new Vector3I(v[0], v[1], v[2]),
                new Vector3I(v[3], v[4], v[5]), state, explicitProps, "PutLine");
        }
    }

    private sealed class PutCurveTool : ToolBase
    {
        public override string Name => "PutCurve";
        public override string Description => "沿给定的一系列整数坐标点依次连线，绘制折线/曲线。";

        public override JsonObject ParametersSchema => ToolSchema.Schema(
            ("points", "array", "点数组，至少两个，元素为 {x,y,z}", true),
            ("blockId", "string", "线条方块 ID", true),
            ("properties", "object", "方块属性对象", false));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (args["points"] is not JsonArray array || array.Count < 2)
                return ToolResult.Failure("points 必须是至少包含两个点的数组");
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out var error))
                return ToolResult.Failure(error);

            var points = new List<Vector3I>();
            foreach (var node in array)
            {
                if (node is not JsonObject point)
                    return ToolResult.Failure("points 中每个元素都必须是 {x,y,z} 对象");
                if (!McTools.TryInts(point, new[] { "x", "y", "z" }, out var v, out error))
                    return ToolResult.Failure(error);
                points.Add(new Vector3I(v[0], v[1], v[2]));
            }

            var writer = new ShapeWriter(ctx, state, explicitProps);
            for (int i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                int steps = Math.Max(Math.Abs(b.X - a.X), Math.Max(Math.Abs(b.Y - a.Y), Math.Abs(b.Z - a.Z)));
                for (int step = 0; step <= steps; step++)
                {
                    double t = steps == 0 ? 0 : (double)step / steps;
                    writer.Put(
                        (int)Math.Round(a.X + (b.X - a.X) * t),
                        (int)Math.Round(a.Y + (b.Y - a.Y) * t),
                        (int)Math.Round(a.Z + (b.Z - a.Z) * t));
                }
            }

            Vector3I min = points[0], max = points[0];
            foreach (var point in points)
            {
                min = MinOf(min, point);
                max = MaxOf(max, point);
            }
            writer.Finish(min, max);
            return Report(writer, $"PutCurve ({points.Count} 点)");
        }
    }

    private sealed class FillBallTool : ToolBase
    {
        public override string Name => "FillBall";
        public override string Description => "以 (x,y,z) 为球心、radius 为半径填充实心球体。";

        public override JsonObject ParametersSchema => BallSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x", "y", "z", "radius" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);
            if (v[3] < 0) return ToolResult.Failure("radius 不能为负数");

            long r2 = (long)v[3] * v[3];
            return FillShape(ctx, state, explicitProps,
                new Vector3I(v[0] - v[3], v[1] - v[3], v[2] - v[3]),
                new Vector3I(v[0] + v[3], v[1] + v[3], v[2] + v[3]),
                (x, y, z) =>
                {
                    long dx = x - v[0], dy = y - v[1], dz = z - v[2];
                    return dx * dx + dy * dy + dz * dz <= r2;
                },
                false, $"FillBall 半径 {v[3]}");
        }
    }

    private sealed class ConstructBallTool : ToolBase
    {
        public override string Name => "ConstructBall";
        public override string Description => "建造空心球壳：只放置球体表面一层方块。";

        public override JsonObject ParametersSchema => BallSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x", "y", "z", "radius" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);
            if (v[3] < 0) return ToolResult.Failure("radius 不能为负数");

            long r2 = (long)v[3] * v[3];
            Func<int, int, int, bool> inside = (x, y, z) =>
            {
                long dx = x - v[0], dy = y - v[1], dz = z - v[2];
                return dx * dx + dy * dy + dz * dz <= r2;
            };

            return FillShape(ctx, state, explicitProps,
                new Vector3I(v[0] - v[3], v[1] - v[3], v[2] - v[3]),
                new Vector3I(v[0] + v[3], v[1] + v[3], v[2] + v[3]),
                inside, true, $"ConstructBall 半径 {v[3]}");
        }
    }

    private sealed class FillEllipseTool : ToolBase
    {
        public override string Name => "FillEllipse";
        public override string Description => "以 (x,y,z) 为中心、三个轴向半径填充实心椭球体。";

        public override JsonObject ParametersSchema => EllipseSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x", "y", "z", "radiusX", "radiusY", "radiusZ" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);
            if (v[3] <= 0 || v[4] <= 0 || v[5] <= 0) return ToolResult.Failure("三个半径都必须大于 0");

            return Ellipsoid(ctx, v, state, explicitProps, false);
        }
    }

    private sealed class ConstructEllipseTool : ToolBase
    {
        public override string Name => "ConstructEllipse";
        public override string Description => "建造空心椭球壳：只放置椭球表面一层方块。";

        public override JsonObject ParametersSchema => EllipseSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x", "y", "z", "radiusX", "radiusY", "radiusZ" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);
            if (v[3] <= 0 || v[4] <= 0 || v[5] <= 0) return ToolResult.Failure("三个半径都必须大于 0");

            return Ellipsoid(ctx, v, state, explicitProps, true);
        }
    }

    private static ToolResult Ellipsoid(
        McToolContext context, int[] v, McBlockState state, Dictionary<string, string> explicitProps, bool hollow)
    {
        double rx = v[3], ry = v[4], rz = v[5];
        Func<int, int, int, bool> inside = (x, y, z) =>
        {
            double nx = (x - v[0]) / rx, ny = (y - v[1]) / ry, nz = (z - v[2]) / rz;
            return nx * nx + ny * ny + nz * nz <= 1.0;
        };

        return FillShape(context, state, explicitProps,
            new Vector3I(v[0] - v[3], v[1] - v[4], v[2] - v[5]),
            new Vector3I(v[0] + v[3], v[1] + v[4], v[2] + v[5]),
            inside, hollow, hollow ? "ConstructEllipse" : "FillEllipse");
    }

    private sealed class FillCylinderTool : ToolBase
    {
        public override string Name => "FillCylinder";
        public override string Description => "沿 Y 轴填充实心圆柱：(x,y,z) 为底面中心，radius 为半径，height 为高度（向上）。";

        public override JsonObject ParametersSchema => CylinderSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x", "y", "z", "radius", "height" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);
            if (v[3] < 0 || v[4] <= 0) return ToolResult.Failure("radius 不能为负，height 必须大于 0");

            return Cylinder(ctx, v, state, explicitProps, false);
        }
    }

    private sealed class ConstructCylinderTool : ToolBase
    {
        public override string Name => "ConstructCylinder";
        public override string Description => "建造空心圆柱壳：只保留侧面与上下底面的一层方块。";

        public override JsonObject ParametersSchema => CylinderSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x", "y", "z", "radius", "height" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);
            if (v[3] < 0 || v[4] <= 0) return ToolResult.Failure("radius 不能为负，height 必须大于 0");

            return Cylinder(ctx, v, state, explicitProps, true);
        }
    }

    private static ToolResult Cylinder(
        McToolContext context, int[] v, McBlockState state, Dictionary<string, string> explicitProps, bool hollow)
    {
        long r2 = (long)v[3] * v[3];
        int top = v[1] + v[4] - 1;
        Func<int, int, int, bool> inside = (x, y, z) =>
        {
            if (y < v[1] || y > top) return false;
            long dx = x - v[0], dz = z - v[2];
            return dx * dx + dz * dz <= r2;
        };

        return FillShape(context, state, explicitProps,
            new Vector3I(v[0] - v[3], v[1], v[2] - v[3]),
            new Vector3I(v[0] + v[3], top, v[2] + v[3]),
            inside, hollow, hollow ? "ConstructCylinder" : "FillCylinder");
    }

    private sealed class ConstructBoxTool : ToolBase
    {
        public override string Name => "ConstructBox";
        public override string Description => "建造空心长方体：只保留长方体的六个面（外壳），内部保持原样。";

        public override JsonObject ParametersSchema => BoxSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            var ctx = M(context);
            if (!McTools.TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!McTools.TryState(ctx, args, out var state, out var explicitProps, out error))
                return ToolResult.Failure(error);

            int minX = Math.Min(v[0], v[3]), maxX = Math.Max(v[0], v[3]);
            int minY = Math.Min(v[1], v[4]), maxY = Math.Max(v[1], v[4]);
            int minZ = Math.Min(v[2], v[5]), maxZ = Math.Max(v[2], v[5]);

            return FillShape(ctx, state, explicitProps,
                new Vector3I(minX, minY, minZ), new Vector3I(maxX, maxY, maxZ),
                (x, y, z) => x >= minX && x <= maxX && y >= minY && y <= maxY && z >= minZ && z <= maxZ,
                true, "ConstructBox");
        }
    }

    private static JsonObject BoxSchema() => ToolSchema.Schema(
        ("x1", "integer", "起点 X", true),
        ("y1", "integer", "起点 Y", true),
        ("z1", "integer", "起点 Z", true),
        ("x2", "integer", "终点 X（闭区间）", true),
        ("y2", "integer", "终点 Y（闭区间）", true),
        ("z2", "integer", "终点 Z（闭区间）", true),
        ("blockId", "string", "使用的方块 ID", true),
        ("properties", "object", "方块属性对象", false));

    private static JsonObject BallSchema() => ToolSchema.Schema(
        ("x", "integer", "球心 X", true),
        ("y", "integer", "球心 Y", true),
        ("z", "integer", "球心 Z", true),
        ("radius", "integer", "半径（方块数）", true),
        ("blockId", "string", "使用的方块 ID", true),
        ("properties", "object", "方块属性对象", false));

    private static JsonObject EllipseSchema() => ToolSchema.Schema(
        ("x", "integer", "中心 X", true),
        ("y", "integer", "中心 Y", true),
        ("z", "integer", "中心 Z", true),
        ("radiusX", "integer", "X 轴半径", true),
        ("radiusY", "integer", "Y 轴半径", true),
        ("radiusZ", "integer", "Z 轴半径", true),
        ("blockId", "string", "使用的方块 ID", true),
        ("properties", "object", "方块属性对象", false));

    private static JsonObject CylinderSchema() => ToolSchema.Schema(
        ("x", "integer", "底面中心 X", true),
        ("y", "integer", "底面 Y", true),
        ("z", "integer", "底面中心 Z", true),
        ("radius", "integer", "半径（方块数）", true),
        ("height", "integer", "高度（方块数）", true),
        ("blockId", "string", "使用的方块 ID", true),
        ("properties", "object", "方块属性对象", false));
}
