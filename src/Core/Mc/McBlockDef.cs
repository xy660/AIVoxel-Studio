using Godot;

namespace AIVoxelStudio.Core.Mc;

public sealed class McBlockDef
{
    public string Id;
    public string Path;
    public string Name;
    public bool IsFluid;
    public bool ForceTranslucent;
    public readonly Dictionary<string, List<string>> PropertyValues = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal);
    public readonly List<McVariant> Variants = new();
    public readonly List<McMultipart> Multipart = new();
}

public sealed class McVariant
{
    public readonly Dictionary<string, string[]> Conditions = new(StringComparer.Ordinal);
    public readonly List<McModelRef> Models = new();
}

public sealed class McMultipart
{
    public McCondition When;
    public readonly List<McModelRef> Models = new();
}

public sealed class McCondition
{
    public readonly Dictionary<string, string[]> Values = new(StringComparer.Ordinal);
    public readonly List<McCondition> Or = new();
    public readonly List<McCondition> And = new();
}

public sealed class McModelRef
{
    public string Model;
    public int X;
    public int Y;
}

public readonly struct McFace
{
    public readonly Vector3[] Corners;
    public readonly Vector2[] Uvs;
    public readonly Vector3 Normal;
    public readonly string CullFace;
    public readonly int Layer; // 0 不透明 / 1 镂空 / 2 半透明

    public McFace(Vector3[] corners, Vector2[] uvs, Vector3 normal, string cullFace, int layer)
    {
        Corners = corners;
        Uvs = uvs;
        Normal = normal;
        CullFace = cullFace;
        Layer = layer;
    }
}

public sealed class McBakedState
{
    public readonly List<McFace> Faces = new();
    public bool IsFullCube;
    public bool IsCross;
    public bool IsFluid;
}
