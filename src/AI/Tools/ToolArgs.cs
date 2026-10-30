using System.Text.Json.Nodes;
using Godot;

namespace AIVoxelStudio.AI.Tools;

internal static class ToolArgs
{
    // 供 from/to 等闭区间盒参数使用的 JSON Schema
    public static JsonObject BoxSchema(string description) => new()
    {
        ["type"] = "object",
        ["description"] = description,
        ["properties"] = new JsonObject
        {
            ["a"] = PointSchema("第一个角点（含）"),
            ["b"] = PointSchema("对角点（含）"),
        },
        ["required"] = new JsonArray("a", "b"),
    };

    private static JsonObject PointSchema(string description) => new()
    {
        ["type"] = "object",
        ["description"] = description,
        ["properties"] = new JsonObject
        {
            ["x"] = new JsonObject { ["type"] = "integer" },
            ["y"] = new JsonObject { ["type"] = "integer" },
            ["z"] = new JsonObject { ["type"] = "integer" },
        },
        ["required"] = new JsonArray("x", "y", "z"),
    };

    // 读取闭区间盒 {a:{x,y,z}, b:{x,y,z}} 并归一化为 min/max
    public static bool TryBox(JsonObject args, string name, out Vector3I min, out Vector3I max, out string error)
    {
        min = max = Vector3I.Zero;
        error = null;
        if (args[name] is not JsonObject box)
        {
            error = "参数 " + name + " 缺失，应形如 {\"a\":{\"x\":0,\"y\":0,\"z\":0},\"b\":{...}}";
            return false;
        }
        if (!TryPoint(box["a"], out var a, out error) || !TryPoint(box["b"], out var b, out error))
        {
            error = "参数 " + name + " 的角点 a/b 必须含整数 x、y、z：" + error;
            return false;
        }

        min = new Vector3I(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        max = new Vector3I(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        return true;
    }

    private static bool TryPoint(JsonNode node, out Vector3I point, out string error)
    {
        point = Vector3I.Zero;
        error = null;
        if (node is not JsonObject obj)
        {
            error = "角点必须是对象";
            return false;
        }
        if (!TryInt(obj["x"], out int x) || !TryInt(obj["y"], out int y) || !TryInt(obj["z"], out int z))
        {
            error = "角点缺少整数 x/y/z";
            return false;
        }
        point = new Vector3I(x, y, z);
        return true;
    }

    private static bool TryInt(JsonNode node, out int value)
    {
        value = 0;
        if (node is not JsonValue json) return false;
        try
        {
            value = json.GetValue<int>();
            return true;
        }
        catch (Exception)
        {
            try
            {
                value = (int)json.GetValue<double>();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
