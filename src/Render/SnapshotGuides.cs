using Godot;

namespace AIVoxelStudio.Render;

// 预览图坐标辅助：目标块高亮线框 + 坐标文字 + 目标块同高度的 10×10 地面网格
public static class SnapshotGuides
{
    private const int GridRadius = 5;

    private static readonly Color HighlightColor = new(1f, 0.55f, 0.1f);
    private static readonly Color LabelColor = new(1f, 0.86f, 0.3f);
    private static readonly Color GridColor = new(0.25f, 0.85f, 1f, 0.6f);

    public static Node3D Create(Vector3I cell, float meters)
    {
        var root = new Node3D { Name = "SnapshotGuides" };
        root.AddChild(BuildHighlight(cell, meters));
        root.AddChild(BuildGrid(cell, meters));
        root.AddChild(BuildLabel(cell, meters));
        return root;
    }

    private static MeshInstance3D BuildHighlight(Vector3I cell, float meters)
    {
        var mesh = new ImmediateMesh();
        var material = LineMaterial(HighlightColor, noDepthTest: true);
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        // 两层略微外扩的同色线框，让高亮看起来更粗
        AddBox(mesh, cell, meters, meters * 0.004f);
        AddBox(mesh, cell, meters, meters * 0.02f);

        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static MeshInstance3D BuildGrid(Vector3I cell, float meters)
    {
        var mesh = new ImmediateMesh();
        var material = LineMaterial(GridColor, noDepthTest: false);
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        float y = (cell.Y + 1) * meters + meters * 0.002f;
        float fromX = (cell.X - GridRadius) * meters;
        float toX = (cell.X + GridRadius + 1) * meters;
        float fromZ = (cell.Z - GridRadius) * meters;
        float toZ = (cell.Z + GridRadius + 1) * meters;

        for (int i = 0; i <= GridRadius * 2; i++)
        {
            float x = fromX + i * meters;
            AddLine(mesh, new Vector3(x, y, fromZ), new Vector3(x, y, toZ));
            float z = fromZ + i * meters;
            AddLine(mesh, new Vector3(fromX, y, z), new Vector3(toX, y, z));
        }

        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static Label3D BuildLabel(Vector3I cell, float meters)
    {
        return new Label3D
        {
            Text = $"({cell.X},{cell.Y},{cell.Z})",
            Position = new Vector3((cell.X + 0.5f) * meters, (cell.Y + 1.35f) * meters, (cell.Z + 0.5f) * meters),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 64,
            PixelSize = meters * 0.006f,
            Modulate = LabelColor,
            OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
            OutlineSize = 14,
            RenderPriority = 2,
        };
    }

    internal static StandardMaterial3D LineMaterial(Color color, bool noDepthTest) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = noDepthTest,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        DisableReceiveShadows = true,
    };

    private static void AddBox(ImmediateMesh mesh, Vector3I cell, float meters, float expand)
    {
        float x0 = cell.X * meters - expand, x1 = (cell.X + 1) * meters + expand;
        float y0 = cell.Y * meters - expand, y1 = (cell.Y + 1) * meters + expand;
        float z0 = cell.Z * meters - expand, z1 = (cell.Z + 1) * meters + expand;

        var p000 = new Vector3(x0, y0, z0);
        var p100 = new Vector3(x1, y0, z0);
        var p110 = new Vector3(x1, y0, z1);
        var p010 = new Vector3(x0, y0, z1);
        var p001 = new Vector3(x0, y1, z0);
        var p101 = new Vector3(x1, y1, z0);
        var p111 = new Vector3(x1, y1, z1);
        var p011 = new Vector3(x0, y1, z1);

        AddLine(mesh, p000, p100);
        AddLine(mesh, p100, p110);
        AddLine(mesh, p110, p010);
        AddLine(mesh, p010, p000);

        AddLine(mesh, p001, p101);
        AddLine(mesh, p101, p111);
        AddLine(mesh, p111, p011);
        AddLine(mesh, p011, p001);

        AddLine(mesh, p000, p001);
        AddLine(mesh, p100, p101);
        AddLine(mesh, p110, p111);
        AddLine(mesh, p010, p011);
    }

    internal static void AddLine(ImmediateMesh mesh, Vector3 a, Vector3 b)
    {
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
    }
}
