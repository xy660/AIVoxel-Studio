using Godot;

namespace AIVoxelStudio.UI;

public partial class Crosshair : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var center = Size * 0.5f;
        var color = new Color(1f, 1f, 1f, 0.9f);
        const float gap = 3f;
        const float length = 8f;
        DrawLine(new Vector2(center.X - gap - length, center.Y), new Vector2(center.X - gap, center.Y), color, 2f);
        DrawLine(new Vector2(center.X + gap, center.Y), new Vector2(center.X + gap + length, center.Y), color, 2f);
        DrawLine(new Vector2(center.X, center.Y - gap - length), new Vector2(center.X, center.Y - gap), color, 2f);
        DrawLine(new Vector2(center.X, center.Y + gap), new Vector2(center.X, center.Y + gap + length), color, 2f);
    }
}
