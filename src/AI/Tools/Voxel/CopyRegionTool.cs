using System.Text.Json.Nodes;
using AIVoxelStudio.Core;

namespace AIVoxelStudio.AI.Tools;

// 纯色模式：把源闭区间盒内的非空气方块整体拷贝到同尺寸目标盒
internal sealed class VoxelCopyRegionTool : ToolBase
{
    public override string Name => "CopyRegion";
    public override string Description =>
        "把 from 闭区间盒内的方块整体拷贝到同尺寸的 to 盒，用于快速复制重复结构（柱子、窗、装饰件等）。" +
        "只写入非空气方块，目标区域原有方块保留；重叠也安全（先快照再写入）。" +
        "from/to 各给两个角点 {a:{x,y,z}, b:{x,y,z}}，两盒尺寸必须一致，落点偏移 = to 最小角 - from 最小角。";

    public override JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["from"] = ToolArgs.BoxSchema("源盒，闭区间"),
            ["to"] = ToolArgs.BoxSchema("目标盒，尺寸必须与 from 一致，a 为源 a 的对应落点"),
        },
        ["required"] = new JsonArray("from", "to"),
    };

    protected override ToolResult Run(JsonObject args, ToolContext context)
    {
        var ctx = context as VoxelToolContext;
        if (ctx == null) return ToolResult.Failure("当前模式不支持纯色体素工具");

        Godot.Vector3I fromMin = default, fromMax = default, toMin = default, toMax = default;
        string error = null;
        if (!ToolArgs.TryBox(args, "from", out fromMin, out fromMax, out error) ||
            !ToolArgs.TryBox(args, "to", out toMin, out toMax, out error))
            return ToolResult.Failure(error);

        int sx = fromMax.X - fromMin.X + 1;
        int sy = fromMax.Y - fromMin.Y + 1;
        int sz = fromMax.Z - fromMin.Z + 1;
        int tx = toMax.X - toMin.X + 1;
        int ty = toMax.Y - toMin.Y + 1;
        int tz = toMax.Z - toMin.Z + 1;
        if (sx != tx || sy != ty || sz != tz)
            return ToolResult.Failure($"目标盒尺寸 ({tx},{ty},{tz}) 与源盒 ({sx},{sy},{sz}) 不一致，请给出相同尺寸的两个盒");

        long volume = (long)sx * sy * sz;
        if (volume > VoxelEditService.MaxBlocksPerCall)
            return ToolResult.Failure($"源盒体积 {volume} 超过单次上限 {VoxelEditService.MaxBlocksPerCall}，请缩小尺寸或拆分调用");

        var snapshot = new BlockInfo[(int)volume];
        int index = 0;
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
            snapshot[index++] = ctx.World.GetBlock(fromMin.X + x, fromMin.Y + y, fromMin.Z + z);

        int dx = toMin.X - fromMin.X;
        int dy = toMin.Y - fromMin.Y;
        int dz = toMin.Z - fromMin.Z;

        int copied = 0;
        int changed = 0;
        index = 0;
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            var info = snapshot[index++];
            if (info.IsAir) continue;
            copied++;
            if (ctx.Editor.Set(fromMin.X + x + dx, fromMin.Y + y + dy, fromMin.Z + z + dz, info)) changed++;
        }

        return ToolResult.Success(
            $"已拷贝 ({fromMin.X},{fromMin.Y},{fromMin.Z})~({fromMax.X},{fromMax.Y},{fromMax.Z}) " +
            $"→ ({toMin.X},{toMin.Y},{toMin.Z})~({toMax.X},{toMax.Y},{toMax.Z})：" +
            $"非空气 {copied} 格，实际写入 {changed} 格");
    }
}
