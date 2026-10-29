using System.Text;

namespace AIVoxelStudio.Core.Mc;

// 我的世界方块状态：命名空间 ID + 有序 properties；渲染/碰撞标记由素材库解析后写入
public sealed class McBlockState : IEquatable<McBlockState>
{
    public const string AirId = "minecraft:air";

    public static readonly McBlockState Air = new(AirId, Array.Empty<KeyValuePair<string, string>>());

    private readonly string _key;

    public McBlockState(string id, IEnumerable<KeyValuePair<string, string>> properties)
    {
        Id = NormalizeId(id);
        var sorted = properties
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value ?? ""))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToArray();
        Properties = sorted;

        var builder = new StringBuilder(Id);
        if (sorted.Length > 0)
        {
            builder.Append('[');
            for (int i = 0; i < sorted.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(sorted[i].Key).Append('=').Append(sorted[i].Value);
            }
            builder.Append(']');
        }
        _key = builder.ToString();
    }

    public string Id { get; }
    public IReadOnlyList<KeyValuePair<string, string>> Properties { get; }

    /// <summary>用于存档、缓存与比较的规范字符串。</summary>
    public string FullString => _key;

    public bool IsAir => Id == AirId;

    // 以下标记由 McAssets 在解析时写入；存档读回时同样经素材库解析
    public bool IsSolid { get; internal set; }
    public bool IsOpaqueCube { get; internal set; }
    public bool IsFluid { get; internal set; }
    public bool IsCross { get; internal set; }
    public bool HasCutoutFaces { get; internal set; }

    public string GetProperty(string name)
    {
        foreach (var pair in Properties)
            if (pair.Key == name) return pair.Value;
        return null;
    }

    public bool Equals(McBlockState other) => other != null && _key == other._key;
    public override bool Equals(object obj) => obj is McBlockState other && Equals(other);
    public override int GetHashCode() => _key.GetHashCode();
    public override string ToString() => _key;

    public static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return AirId;
        id = id.Trim();
        return id.Contains(':') ? id : "minecraft:" + id;
    }

    public static McBlockState Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Air;
        text = text.Trim();
        int bracket = text.IndexOf('[');
        if (bracket < 0) return new McBlockState(text, Array.Empty<KeyValuePair<string, string>>());

        int end = text.LastIndexOf(']');
        if (end <= bracket) return new McBlockState(text, Array.Empty<KeyValuePair<string, string>>());

        string id = text[..bracket];
        string body = text[(bracket + 1)..end];
        var properties = new List<KeyValuePair<string, string>>();
        foreach (var token in body.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0) continue;
            properties.Add(new KeyValuePair<string, string>(token[..eq].Trim(), token[(eq + 1)..].Trim()));
        }
        return new McBlockState(id, properties);
    }
}
