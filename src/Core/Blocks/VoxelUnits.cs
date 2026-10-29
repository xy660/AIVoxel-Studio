using Godot;

namespace AIVoxelStudio.Core;

public static class VoxelUnits
{
    public const float BlockMeters = 0.125f;
    public const float BlocksPerMeter = 8f;

    //方块坐标到世界坐标转换（方块格子<->世界坐标）
    public static Vector3 BlockToWorld(Vector3 block) => block * BlockMeters;
    public static Vector3 WorldToBlock(Vector3 world) => world * BlocksPerMeter;
}
