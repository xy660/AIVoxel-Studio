using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Render;

// 选区模式的世界内预览：两个角点线框 + 包围盒线框 + 坐标/尺寸文字
public sealed partial class RegionGuide : Node3D
{
    private static readonly Color FirstColor = new(1f, 0.6f, 0.15f);
    private static readonly Color SecondColor = new(0.35f, 0.78f, 1f);
    private static readonly Color RegionColor = new(0.4f, 1f, 0.55f);

    private readonly ImmediateMesh _mesh = new();
    private readonly StandardMaterial3D _firstMaterial;
    private readonly StandardMaterial3D _secondMaterial;
    private readonly StandardMaterial3D _regionMaterial;
    private readonly Label3D _firstLabel;
    private readonly Label3D _secondLabel;
    private readonly Label3D _sizeLabel;
    private readonly float _meters;

    private Vector3I? _first;
    private Vector3I? _second;

    public RegionGuide(float meters)
    {
        _meters = meters;
        _firstMaterial = SnapshotGuides.LineMaterial(FirstColor, noDepthTest: true);
        _secondMaterial = SnapshotGuides.LineMaterial(SecondColor, noDepthTest: true);
        _regionMaterial = SnapshotGuides.LineMaterial(RegionColor, noDepthTest: true);

        AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        _firstLabel = CreateLabel(FirstColor);
        _secondLabel = CreateLabel(SecondColor);
        _sizeLabel = CreateLabel(RegionColor);
        _sizeLabel.FontSize = 48;
        AddChild(_firstLabel);
        AddChild(_secondLabel);
        AddChild(_sizeLabel);
    }

    public void SetCorners(Vector3I? first, Vector3I? second)
    {
        _first = first;
        _second = second;
        Rebuild();
    }

    private void Rebuild()
    {
        _mesh.ClearSurfaces();

        if (_first is { } a)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _firstMaterial);
            AddCellBox(a, _meters * 0.01f);
            _mesh.SurfaceEnd();
            PlaceLabel(_firstLabel, a, FirstColor, 1.2f);
        }
        else
        {
            _firstLabel.Visible = false;
        }

        if (_second is { } b)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _secondMaterial);
            AddCellBox(b, _meters * 0.01f);
            _mesh.SurfaceEnd();
            PlaceLabel(_secondLabel, b, SecondColor, 1.2f);
        }
        else
        {
            _secondLabel.Visible = false;
        }

        if (_first is { } lo && _second is { } hi)
        {
            var box = RegionBox.FromCorners(lo, hi);
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _regionMaterial);
            AddBounds(box, _meters * 0.006f);
            _mesh.SurfaceEnd();

            var size = box.Size;
            _sizeLabel.Text = $"{size.X}×{size.Y}×{size.Z}";
            _sizeLabel.Visible = true;
            var center = new Vector3(
                (box.Min.X + box.Max.X + 1) * 0.5f,
                box.Max.Y + 1.2f,
                (box.Min.Z + box.Max.Z + 1) * 0.5f) * _meters;
            _sizeLabel.Position = center;
        }
        else
        {
            _sizeLabel.Visible = false;
        }
    }

    private void AddCellBox(Vector3I cell, float expand)
    {
        AddBounds(new RegionBox(cell, cell), expand);
    }

    private void AddBounds(RegionBox box, float expand)
    {
        float x0 = box.Min.X * _meters - expand, x1 = (box.Max.X + 1) * _meters + expand;
        float y0 = box.Min.Y * _meters - expand, y1 = (box.Max.Y + 1) * _meters + expand;
        float z0 = box.Min.Z * _meters - expand, z1 = (box.Max.Z + 1) * _meters + expand;

        var p000 = new Vector3(x0, y0, z0);
        var p100 = new Vector3(x1, y0, z0);
        var p110 = new Vector3(x1, y0, z1);
        var p010 = new Vector3(x0, y0, z1);
        var p001 = new Vector3(x0, y1, z0);
        var p101 = new Vector3(x1, y1, z0);
        var p111 = new Vector3(x1, y1, z1);
        var p011 = new Vector3(x0, y1, z1);

        SnapshotGuides.AddLine(_mesh, p000, p100);
        SnapshotGuides.AddLine(_mesh, p100, p110);
        SnapshotGuides.AddLine(_mesh, p110, p010);
        SnapshotGuides.AddLine(_mesh, p010, p000);
        SnapshotGuides.AddLine(_mesh, p001, p101);
        SnapshotGuides.AddLine(_mesh, p101, p111);
        SnapshotGuides.AddLine(_mesh, p111, p011);
        SnapshotGuides.AddLine(_mesh, p011, p001);
        SnapshotGuides.AddLine(_mesh, p000, p001);
        SnapshotGuides.AddLine(_mesh, p100, p101);
        SnapshotGuides.AddLine(_mesh, p110, p111);
        SnapshotGuides.AddLine(_mesh, p010, p011);
    }

    private void PlaceLabel(Label3D label, Vector3I cell, Color color, float height)
    {
        label.Text = $"({cell.X},{cell.Y},{cell.Z})";
        label.Visible = true;
        label.Modulate = color;
        label.Position = new Vector3(
            (cell.X + 0.5f) * _meters,
            (cell.Y + height) * _meters,
            (cell.Z + 0.5f) * _meters);
    }

    private Label3D CreateLabel(Color color) => new()
    {
        Visible = false,
        Text = "",
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        FontSize = 64,
        PixelSize = _meters * 0.006f,
        Modulate = color,
        OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
        OutlineSize = 14,
        RenderPriority = 2,
    };
}
