using Godot;

namespace AIVoxelStudio.Core.Abstractions;

// 玩家/射线只需要的最小世界能力，voxel 与 MC 各自实现
public interface IPlayerWorld
{
    bool IsSolid(int x, int y, int z);
    void BreakAt(Vector3I cell);
    bool TryPlaceAt(Vector3I cell);

    // 中键拾取：把该格方块写入当前选中的快捷格
    bool TryPickBlock(Vector3I cell);
}
