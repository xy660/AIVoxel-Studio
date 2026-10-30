using Godot;
using System.Text.Json.Nodes;
using AIVoxelStudio.Core;

namespace AIVoxelStudio.AI.Tools;

public static class BuiltinTools
{
    public static void RegisterAll(ToolRegistry registry)
    {
        registry.RegisterTool(new PutBlockTool());
        registry.RegisterTool(new FillBoxTool());
        registry.RegisterTool(new ConstructBoxTool());
        registry.RegisterTool(new PutLineTool());
        registry.RegisterTool(new PutCurveTool());
        registry.RegisterTool(new FillBallTool());
        registry.RegisterTool(new ConstructBallTool());
        registry.RegisterTool(new FillEllipseTool());
        registry.RegisterTool(new ConstructEllipseTool());
        registry.RegisterTool(new FillCylinderTool());
        registry.RegisterTool(new ConstructCylinderTool());
        registry.RegisterTool(new GetBlockTool());
        registry.RegisterTool(new QueryBlockBatchTool());
        registry.RegisterTool(new VoxelCopyRegionTool());
    }

    private static VoxelToolContext V(ToolContext context) =>
        context as VoxelToolContext ??
        throw new InvalidOperationException("当前模式不支持纯色体素工具");

    private sealed class Writer
    {
        private readonly VoxelToolContext _context;
        private readonly BlockInfo _info;
        public int Attempted { get; private set; }
        public int Changed { get; private set; }

        public Writer(VoxelToolContext context, BlockInfo info)
        {
            _context = context;
            _info = info;
        }

        public void Put(int x, int y, int z)
        {
            Attempted++;
            if (_context.Editor.Set(x, y, z, _info)) Changed++;
        }
    }

    private static ToolResult Report(Writer writer, string label)
    {
        string tail = writer.Changed == 0 ? "（目标处已存在相同方块）" : "";
        return ToolResult.Success($"{label}：写入 {writer.Changed} 块{tail}");
    }

    private static ToolResult FillShape(
        VoxelToolContext context,
        Vector3I min,
        Vector3I max,
        Func<int, int, int, bool> inside,
        bool hollow,
        BlockInfo info,
        string label)
    {
        int minX = min.X, maxX = max.X;
        int minY = min.Y, maxY = max.Y;
        int minZ = min.Z, maxZ = max.Z;

        double volume = ((double)maxX - minX + 1) * ((double)maxY - minY + 1) * ((double)maxZ - minZ + 1);
        if (volume > VoxelEditService.MaxBlocksPerCall * 8L)
            return ToolResult.Failure($"{label} 的包围盒过大（{volume:F0} 格），请缩小尺寸或拆分为多次调用");

        long count = 0;
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        for (int z = minZ; z <= maxZ; z++)
        {
            if (!inside(x, y, z)) continue;
            if (hollow && !IsSurface(x, y, z, inside)) continue;
            if (++count > VoxelEditService.MaxBlocksPerCall)
                return ToolResult.Failure($"{label} 需要写入超过 {VoxelEditService.MaxBlocksPerCall} 块，请缩小尺寸或拆分为多次调用");
        }

        var writer = new Writer(context, info);
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        for (int z = minZ; z <= maxZ; z++)
        {
            if (!inside(x, y, z)) continue;
            if (hollow && !IsSurface(x, y, z, inside)) continue;
            writer.Put(x, y, z);
        }

        return Report(writer, label);
    }

    private static bool IsSurface(int x, int y, int z, Func<int, int, int, bool> inside) =>
        !inside(x + 1, y, z) || !inside(x - 1, y, z) ||
        !inside(x, y + 1, z) || !inside(x, y - 1, z) ||
        !inside(x, y, z + 1) || !inside(x, y, z - 1);

    private static ToolResult DrawLine(VoxelToolContext context, Vector3I a, Vector3I b, BlockInfo info, string label)
    {
        int steps = Math.Max(Math.Abs(b.X - a.X), Math.Max(Math.Abs(b.Y - a.Y), Math.Abs(b.Z - a.Z)));
        if (steps + 1 > VoxelEditService.MaxBlocksPerCall)
            return ToolResult.Failure($"{label} 超过单次上限 {VoxelEditService.MaxBlocksPerCall} 块，请缩短线段");

        var writer = new Writer(context, info);
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            int x = (int)Math.Round(a.X + (b.X - a.X) * t);
            int y = (int)Math.Round(a.Y + (b.Y - a.Y) * t);
            int z = (int)Math.Round(a.Z + (b.Z - a.Z) * t);
            writer.Put(x, y, z);
        }
        return Report(writer, label);
    }

    private sealed class PutBlockTool : ToolBase
    {
        public override string Name => "PutBlock";
        public override string Description => "在指定整数坐标放置一个方块，可覆盖原有方块。要删除方块请传 blockType=0。";

        public override JsonObject ParametersSchema => Schema(
            ("x", "integer", "世界 X 坐标", true),
            ("y", "integer", "世界 Y 坐标（高度，向上为正）", true),
            ("z", "integer", "世界 Z 坐标", true),
            ("blockInfo", "block", "要放置的方块", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z" }, out var v, out var error)) return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);

            var writer = new Writer(V(context), info);
            writer.Put(v[0], v[1], v[2]);
            return Report(writer, $"PutBlock ({v[0]},{v[1]},{v[2]})");
        }
    }

    private sealed class FillBoxTool : ToolBase
    {
        public override string Name => "FillBox";
        public override string Description => "用方块填满由两个对角点定义的闭区间长方体区域（实心）。";

        public override JsonObject ParametersSchema => Schema(
            ("x1", "integer", "起点 X", true),
            ("y1", "integer", "起点 Y", true),
            ("z1", "integer", "起点 Z", true),
            ("x2", "integer", "终点 X（闭区间）", true),
            ("y2", "integer", "终点 Y（闭区间）", true),
            ("z2", "integer", "终点 Z（闭区间）", true),
            ("blockInfo", "block", "填充用的方块", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);

            int minX = Math.Min(v[0], v[3]), maxX = Math.Max(v[0], v[3]);
            int minY = Math.Min(v[1], v[4]), maxY = Math.Max(v[1], v[4]);
            int minZ = Math.Min(v[2], v[5]), maxZ = Math.Max(v[2], v[5]);

            return FillShape(V(context), new Vector3I(minX, minY, minZ), new Vector3I(maxX, maxY, maxZ),
                (x, y, z) => true, false, info, "FillBox");
        }
    }

    private sealed class ConstructBoxTool : ToolBase
    {
        public override string Name => "ConstructBox";
        public override string Description => "建造空心长方体：只保留长方体的六个面（外壳），内部保持原样。";

        public override JsonObject ParametersSchema => BoxSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);

            int minX = Math.Min(v[0], v[3]), maxX = Math.Max(v[0], v[3]);
            int minY = Math.Min(v[1], v[4]), maxY = Math.Max(v[1], v[4]);
            int minZ = Math.Min(v[2], v[5]), maxZ = Math.Max(v[2], v[5]);

            return FillShape(V(context), new Vector3I(minX, minY, minZ), new Vector3I(maxX, maxY, maxZ),
                (x, y, z) => x >= minX && x <= maxX && y >= minY && y <= maxY && z >= minZ && z <= maxZ,
                true, info, "ConstructBox");
        }
    }

    private sealed class PutLineTool : ToolBase
    {
        public override string Name => "PutLine";
        public override string Description => "在两个整数坐标之间画一条 3D 直线（粗细为一个方块）。";

        public override JsonObject ParametersSchema => Schema(
            ("x1", "integer", "起点 X", true),
            ("y1", "integer", "起点 Y", true),
            ("z1", "integer", "起点 Z", true),
            ("x2", "integer", "终点 X", true),
            ("y2", "integer", "终点 Y", true),
            ("z2", "integer", "终点 Z", true),
            ("blockInfo", "block", "线条方块", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);

            return DrawLine(V(context),
                new Vector3I(v[0], v[1], v[2]),
                new Vector3I(v[3], v[4], v[5]), info, "PutLine");
        }
    }

    private sealed class PutCurveTool : ToolBase
    {
        public override string Name => "PutCurve";
        public override string Description => "沿给定的一系列整数坐标点依次连线，绘制折线/曲线。";

        public override JsonObject ParametersSchema => Schema(
            ("points", "array", "点数组，至少两个，元素为 {x,y,z}", true),
            ("blockInfo", "block", "线条方块", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (args["points"] is not JsonArray array || array.Count < 2)
                return ToolResult.Failure("points 必须是至少包含两个点的数组");
            if (!TryBlockInfo(args, out var info, out var error)) return ToolResult.Failure(error);

            var points = new List<Vector3I>();
            foreach (var node in array)
            {
                if (node is not JsonObject point)
                    return ToolResult.Failure("points 中每个元素都必须是 {x,y,z} 对象");
                if (!TryInts(point, new[] { "x", "y", "z" }, out var v, out error))
                    return ToolResult.Failure(error);
                points.Add(new Vector3I(v[0], v[1], v[2]));
            }

            var writer = new Writer(V(context), info);
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
            return Report(writer, $"PutCurve ({points.Count} 点)");
        }
    }

    private sealed class FillBallTool : ToolBase
    {
        public override string Name => "FillBall";
        public override string Description => "以 (x,y,z) 为球心、radius 为半径填充实心球体。";

        public override JsonObject ParametersSchema => Schema(
            ("x", "integer", "球心 X", true),
            ("y", "integer", "球心 Y", true),
            ("z", "integer", "球心 Z", true),
            ("radius", "integer", "半径（方块数）", true),
            ("blockInfo", "block", "球体方块", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z", "radius" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);
            if (v[3] < 0) return ToolResult.Failure("radius 不能为负数");

            long r2 = (long)v[3] * v[3];
            return FillShape(V(context),
                new Vector3I(v[0] - v[3], v[1] - v[3], v[2] - v[3]),
                new Vector3I(v[0] + v[3], v[1] + v[3], v[2] + v[3]),
                (x, y, z) =>
                {
                    long dx = x - v[0], dy = y - v[1], dz = z - v[2];
                    return dx * dx + dy * dy + dz * dz <= r2;
                },
                false, info, $"FillBall 半径 {v[3]}");
        }
    }

    private sealed class ConstructBallTool : ToolBase
    {
        public override string Name => "ConstructBall";
        public override string Description => "建造空心球壳：只放置球体表面一层方块。";

        public override JsonObject ParametersSchema => FillBallSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z", "radius" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);
            if (v[3] < 0) return ToolResult.Failure("radius 不能为负数");

            long r2 = (long)v[3] * v[3];
            Func<int, int, int, bool> inside = (x, y, z) =>
            {
                long dx = x - v[0], dy = y - v[1], dz = z - v[2];
                return dx * dx + dy * dy + dz * dz <= r2;
            };

            return FillShape(V(context),
                new Vector3I(v[0] - v[3], v[1] - v[3], v[2] - v[3]),
                new Vector3I(v[0] + v[3], v[1] + v[3], v[2] + v[3]),
                inside, true, info, $"ConstructBall 半径 {v[3]}");
        }
    }

    private sealed class FillEllipseTool : ToolBase
    {
        public override string Name => "FillEllipse";
        public override string Description => "以 (x,y,z) 为中心、三个轴向半径填充实心椭球体。";

        public override JsonObject ParametersSchema => EllipseSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z", "radiusX", "radiusY", "radiusZ" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);
            if (v[3] <= 0 || v[4] <= 0 || v[5] <= 0) return ToolResult.Failure("三个半径都必须大于 0");

            return Ellipsoid(V(context), v, info, false);
        }
    }

    private sealed class ConstructEllipseTool : ToolBase
    {
        public override string Name => "ConstructEllipse";
        public override string Description => "建造空心椭球壳：只放置椭球表面一层方块。";

        public override JsonObject ParametersSchema => EllipseSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z", "radiusX", "radiusY", "radiusZ" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);
            if (v[3] <= 0 || v[4] <= 0 || v[5] <= 0) return ToolResult.Failure("三个半径都必须大于 0");

            return Ellipsoid(V(context), v, info, true);
        }
    }

    private static ToolResult Ellipsoid(VoxelToolContext context, int[] v, BlockInfo info, bool hollow)
    {
        double rx = v[3], ry = v[4], rz = v[5];
        Func<int, int, int, bool> inside = (x, y, z) =>
        {
            double nx = (x - v[0]) / rx, ny = (y - v[1]) / ry, nz = (z - v[2]) / rz;
            return nx * nx + ny * ny + nz * nz <= 1.0;
        };

        return FillShape(context,
            new Vector3I(v[0] - v[3], v[1] - v[4], v[2] - v[5]),
            new Vector3I(v[0] + v[3], v[1] + v[4], v[2] + v[5]),
            inside, hollow, info,
            hollow ? "ConstructEllipse" : "FillEllipse");
    }

    private sealed class FillCylinderTool : ToolBase
    {
        public override string Name => "FillCylinder";
        public override string Description => "沿 Y 轴填充实心圆柱：(x,y,z) 为底面中心，radius 为半径，height 为高度（向上）。";

        public override JsonObject ParametersSchema => CylinderSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z", "radius", "height" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);
            if (v[3] < 0 || v[4] <= 0) return ToolResult.Failure("radius 不能为负，height 必须大于 0");

            return Cylinder(V(context), v, info, false);
        }
    }

    private sealed class ConstructCylinderTool : ToolBase
    {
        public override string Name => "ConstructCylinder";
        public override string Description => "建造空心圆柱壳：只保留侧面与上下底面的一层方块。";

        public override JsonObject ParametersSchema => CylinderSchema();

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z", "radius", "height" }, out var v, out var error))
                return ToolResult.Failure(error);
            if (!TryBlockInfo(args, out var info, out error)) return ToolResult.Failure(error);
            if (v[3] < 0 || v[4] <= 0) return ToolResult.Failure("radius 不能为负，height 必须大于 0");

            return Cylinder(V(context), v, info, true);
        }
    }

    private static ToolResult Cylinder(VoxelToolContext context, int[] v, BlockInfo info, bool hollow)
    {
        long r2 = (long)v[3] * v[3];
        int top = v[1] + v[4] - 1;
        Func<int, int, int, bool> inside = (x, y, z) =>
        {
            if (y < v[1] || y > top) return false;
            long dx = x - v[0], dz = z - v[2];
            return dx * dx + dz * dz <= r2;
        };

        return FillShape(context,
            new Vector3I(v[0] - v[3], v[1], v[2] - v[3]),
            new Vector3I(v[0] + v[3], top, v[2] + v[3]),
            inside, hollow, info,
            hollow ? "ConstructCylinder" : "FillCylinder");
    }

    private sealed class GetBlockTool : ToolBase
    {
        public override string Name => "GetBlock";
        public override string Description => "查询指定坐标的方块信息（类型、颜色、是否实心）。";

        public override JsonObject ParametersSchema => Schema(
            ("x", "integer", "X 坐标", true),
            ("y", "integer", "Y 坐标", true),
            ("z", "integer", "Z 坐标", true));

        protected override ToolResult Run(JsonObject args, ToolContext context)
        {
            if (!TryInts(args, new[] { "x", "y", "z" }, out var v, out var error)) return ToolResult.Failure(error);

            var info = V(context).World.GetBlock(v[0], v[1], v[2]);
            return ToolResult.Success(
                $"({v[0]},{v[1]},{v[2]}) blockType={info.Type} color={{r:{info.R},g:{info.G},b:{info.B}}} " +
                $"solid={info.IsSolid} visible={info.IsVisible}");
        }
    }

    private sealed class QueryBlockBatchTool : ToolBase
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
                var schema = Schema(
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
            var world = V(context).World;

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
                    lines.Add(Describe(world, px, py, pz));
                }
                return ToolResult.Success(string.Join("\n", lines));
            }

            if (!TryInts(args, new[] { "x1", "y1", "z1", "x2", "y2", "z2" }, out var v, out var error))
                return ToolResult.Failure(error + "（也可以改用 points 传坐标点列表）");

            int x1 = Math.Min(v[0], v[3]), x2 = Math.Max(v[0], v[3]);
            int y1 = Math.Min(v[1], v[4]), y2 = Math.Max(v[1], v[4]);
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
                if (world.GetBlock(x, y, z).Type == 0) continue;
                if (found.Count >= MaxRegionResults)
                {
                    truncated = true;
                    break;
                }
                found.Add(Describe(world, x, y, z));
            }

            string range = $"({x1},{y1},{z1})-({x2},{y2},{z2})";
            if (found.Count == 0) return ToolResult.Success($"区域 {range} 内没有非空气方块");

            string header = $"区域 {range} 非空气方块 {found.Count} 个" +
                            (truncated ? $"（已达 {MaxRegionResults} 条上限被截断，请缩小范围）" : "") + "：";
            return ToolResult.Success(header + "\n" + string.Join("\n", found));
        }

        private static string Describe(World world, int x, int y, int z)
        {
            var info = world.GetBlock(x, y, z);
            if (info.Type == 0) return $"({x},{y},{z}) 空气";
            return $"({x},{y},{z}) blockType={info.Type} color={{r:{info.R},g:{info.G},b:{info.B}}} solid={info.IsSolid}";
        }
    }

    private static JsonObject BoxSchema() => Schema(
        ("x1", "integer", "起点 X", true),
        ("y1", "integer", "起点 Y", true),
        ("z1", "integer", "起点 Z", true),
        ("x2", "integer", "终点 X（闭区间）", true),
        ("y2", "integer", "终点 Y（闭区间）", true),
        ("z2", "integer", "终点 Z（闭区间）", true),
        ("blockInfo", "block", "使用的方块", true));

    private static JsonObject FillBallSchema() => Schema(
        ("x", "integer", "球心 X", true),
        ("y", "integer", "球心 Y", true),
        ("z", "integer", "球心 Z", true),
        ("radius", "integer", "半径（方块数）", true),
        ("blockInfo", "block", "使用的方块", true));

    private static JsonObject EllipseSchema() => Schema(
        ("x", "integer", "中心 X", true),
        ("y", "integer", "中心 Y", true),
        ("z", "integer", "中心 Z", true),
        ("radiusX", "integer", "X 轴半径", true),
        ("radiusY", "integer", "Y 轴半径", true),
        ("radiusZ", "integer", "Z 轴半径", true),
        ("blockInfo", "block", "使用的方块", true));

    private static JsonObject CylinderSchema() => Schema(
        ("x", "integer", "底面中心 X", true),
        ("y", "integer", "底面 Y", true),
        ("z", "integer", "底面中心 Z", true),
        ("radius", "integer", "半径（方块数）", true),
        ("height", "integer", "高度（方块数）", true),
        ("blockInfo", "block", "使用的方块", true));

    private static JsonObject Schema(params (string Name, string Type, string Desc, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, desc, isRequired) in properties)
        {
            JsonObject schema;
            switch (type)
            {
                case "block":
                    schema = BlockSchema(desc);
                    break;
                case "point":
                    schema = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = desc,
                        ["properties"] = new JsonObject
                        {
                            ["x"] = new JsonObject { ["type"] = "integer" },
                            ["y"] = new JsonObject { ["type"] = "integer" },
                            ["z"] = new JsonObject { ["type"] = "integer" },
                        },
                        ["required"] = new JsonArray { "x", "y", "z" },
                    };
                    break;
                default:
                    schema = new JsonObject { ["type"] = type, ["description"] = desc };
                    break;
            }
            props[name] = schema;
            if (isRequired) required.Add(name);
        }

        var result = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
        };
        if (required.Count > 0) result["required"] = required;
        return result;
    }

    private static JsonObject BlockSchema(string description) => new()
    {
        ["type"] = "object",
        ["description"] = description,
        ["properties"] = new JsonObject
        {
            ["blockType"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "0=空气（删除），1=颜色方块，2=空气墙（不可见但实心）",
            },
            ["blockColor"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "仅 blockType=1 时有效，RGB 分量取值范围 0-255",
                ["properties"] = new JsonObject
                {
                    ["r"] = new JsonObject { ["type"] = "integer" },
                    ["g"] = new JsonObject { ["type"] = "integer" },
                    ["b"] = new JsonObject { ["type"] = "integer" },
                },
                ["required"] = new JsonArray { "r", "g", "b" },
            },
        },
        ["required"] = new JsonArray { "blockType" },
    };

    private static bool TryBlockInfo(JsonObject args, out BlockInfo info, out string error)
    {
        info = BlockInfo.Air;
        error = null;

        if (args["blockInfo"] is not JsonObject block)
        {
            error = "缺少 blockInfo 参数";
            return false;
        }

        if (!TryInt(block, "blockType", out int type))
        {
            error = "blockInfo.blockType 必须是整数";
            return false;
        }
        if (type < 0 || type > 2)
        {
            error = "blockInfo.blockType 只支持 0=空气 / 1=颜色方块 / 2=空气墙";
            return false;
        }

        byte r = 0, g = 0, b = 0;
        if (type == (int)BlockType.Color)
        {
            if (block["blockColor"] is not JsonObject color ||
                !TryInt(color, "r", out int cr) || !TryInt(color, "g", out int cg) || !TryInt(color, "b", out int cb))
            {
                error = "blockType=1 时必须在 blockInfo.blockColor 中提供整数 r、g、b";
                return false;
            }
            r = (byte)Math.Clamp(cr, 0, 255);
            g = (byte)Math.Clamp(cg, 0, 255);
            b = (byte)Math.Clamp(cb, 0, 255);
        }

        info = new BlockInfo((byte)type, r, g, b);
        return true;
    }

    private static bool TryInt(JsonObject obj, string name, out int value)
    {
        value = 0;
        if (obj[name] is not JsonValue node) return false;
        double number;
        try
        {
            number = node.GetValue<double>();
        }
        catch (Exception)
        {
            try
            {
                number = node.GetValue<int>();
            }
            catch (Exception)
            {
                return false;
            }
        }
        value = (int)Math.Round(number);
        return true;
    }

    private static bool TryInts(JsonObject args, string[] names, out int[] values, out string error)
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
