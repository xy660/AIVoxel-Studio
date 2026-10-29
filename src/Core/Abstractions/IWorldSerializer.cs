using AIVoxelStudio.Core;

namespace AIVoxelStudio.Core.Abstractions;

// 每个模式绑定自己的世界容器，读写自己的文件格式；调用方不关心存储细节
public interface IWorldSerializer
{
    BuildKind Kind { get; }
    bool Load(string saveName);
    void Save(string saveName);
}
