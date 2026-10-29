using Godot;

namespace AIVoxelStudio.Core.Abstractions;

public interface IWorldRenderer
{
    Node3D Node { get; }
    void BuildAllImmediately();
}
