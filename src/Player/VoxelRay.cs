using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Player;

public static class VoxelRay
{
    public static bool Cast(IPlayerWorld world, Vector3 origin, Vector3 direction, float maxDistance, out Vector3I hitBlock, out Vector3I placeCell) =>
        Cast(world.IsSolid, origin, direction, maxDistance, out hitBlock, out placeCell);

    // 通用取格：occupied 判定某个格子是否可命中（不同模式对“非空气”的定义不同）
    public static bool Cast(Func<int, int, int, bool> occupied, Vector3 origin, Vector3 direction, float maxDistance, out Vector3I hitBlock, out Vector3I placeCell)
    {
        hitBlock = default;
        placeCell = default;

        var dir = direction.Normalized();
        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);

        int stepX = Math.Sign(dir.X);
        int stepY = Math.Sign(dir.Y);
        int stepZ = Math.Sign(dir.Z);

        float tDeltaX = stepX != 0 ? Math.Abs(1f / dir.X) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? Math.Abs(1f / dir.Y) : float.PositiveInfinity;
        float tDeltaZ = stepZ != 0 ? Math.Abs(1f / dir.Z) : float.PositiveInfinity;

        float tMaxX = stepX > 0 ? (x + 1 - origin.X) / dir.X : stepX < 0 ? (origin.X - x) / -dir.X : float.PositiveInfinity;
        float tMaxY = stepY > 0 ? (y + 1 - origin.Y) / dir.Y : stepY < 0 ? (origin.Y - y) / -dir.Y : float.PositiveInfinity;
        float tMaxZ = stepZ > 0 ? (z + 1 - origin.Z) / dir.Z : stepZ < 0 ? (origin.Z - z) / -dir.Z : float.PositiveInfinity;

        var previous = new Vector3I(x, y, z);
        bool hasPrevious = false;

        for (int i = 0; i < 512; i++)
        {
            var cell = new Vector3I(x, y, z);
            if (occupied(x, y, z))
            {
                hitBlock = cell;
                placeCell = hasPrevious ? previous : cell;
                return true;
            }

            previous = cell;
            hasPrevious = true;

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                if (tMaxX > maxDistance) break;
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxZ)
            {
                if (tMaxY > maxDistance) break;
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                if (tMaxZ > maxDistance) break;
                z += stepZ;
                tMaxZ += tDeltaZ;
            }
        }

        return false;
    }
}
