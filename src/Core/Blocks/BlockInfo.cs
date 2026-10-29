using Godot;

namespace AIVoxelStudio.Core;

public readonly struct BlockInfo
{
    public readonly byte Type;
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public BlockInfo(byte type, byte r, byte g, byte b)
    {
        Type = type;
        R = r;
        G = g;
        B = b;
    }

    public static readonly BlockInfo Air = new((byte)BlockType.Air, 0, 0, 0);

    public static BlockInfo FromRgb(byte r, byte g, byte b) => new((byte)BlockType.Color, r, g, b);
    public static BlockInfo FromColor(Color c) => FromRgb((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255));
    public static BlockInfo Wall() => new((byte)BlockType.AirWall, 0, 0, 0);

    public bool IsAir => Type == (byte)BlockType.Air;
    public bool IsSolid => Type != (byte)BlockType.Air;
    public bool IsVisible => Type == (byte)BlockType.Color;

    public Color ToColor() => new(R / 255f, G / 255f, B / 255f);

    public bool SameVisual(in BlockInfo other) => Type == other.Type && R == other.R && G == other.G && B == other.B;

    public string Describe() => Type switch
    {
        (byte)BlockType.Air => "air",
        (byte)BlockType.AirWall => "air_wall",
        _ => $"color({R},{G},{B})",
    };
}
