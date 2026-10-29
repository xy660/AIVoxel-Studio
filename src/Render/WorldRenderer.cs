using System.Diagnostics;
using Godot;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Render;

public partial class WorldRenderer : Node3D, IWorldRenderer
{
    public Node3D Node => this;

    private const int SectionSize = SectionMesher.Size;
    private const int SectionsPerChunkAxis = Chunk.Size / SectionSize;
    private const float SectionMeters = SectionSize * VoxelUnits.BlockMeters;
    private const double RebuildBudgetMs = 6.0;

    private World _world;
    private StandardMaterial3D _material;
    private readonly Dictionary<Vector3I, SectionView> _views = new();
    private readonly HashSet<Vector3I> _dirtySet = new();
    private readonly Queue<Vector3I> _dirtyQueue = new();

    private sealed class SectionView
    {
        public MeshInstance3D Mesh;
        public StaticBody3D Body;
        public CollisionShape3D Shape;
    }

    public override void _Ready()
    {
        _material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White,
            Roughness = 0.95f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        };
    }

    public void Attach(World world)
    {
        if (_world != null) _world.BlockChanged -= OnBlockChanged;
        ClearAll();
        _world = world;
        _world.BlockChanged += OnBlockChanged;
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_world == null) return;
        foreach (var chunk in _world.Chunks)
        {
            for (int sx = 0; sx < SectionsPerChunkAxis; sx++)
            for (int sy = 0; sy < SectionsPerChunkAxis; sy++)
            for (int sz = 0; sz < SectionsPerChunkAxis; sz++)
            {
                var coord = chunk.Coord * SectionsPerChunkAxis + new Vector3I(sx, sy, sz);
                MarkDirty(coord);
            }
        }
    }

    public void ClearAll()
    {
        foreach (var view in _views.Values)
        {
            view.Mesh?.QueueFree();
            view.Body?.QueueFree();
        }
        _views.Clear();
        _dirtySet.Clear();
        _dirtyQueue.Clear();
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        MarkDirty(SectionCoordOf(x, y, z));
        MarkDirty(SectionCoordOf(x + 1, y, z));
        MarkDirty(SectionCoordOf(x - 1, y, z));
        MarkDirty(SectionCoordOf(x, y + 1, z));
        MarkDirty(SectionCoordOf(x, y - 1, z));
        MarkDirty(SectionCoordOf(x, y, z + 1));
        MarkDirty(SectionCoordOf(x, y, z - 1));
    }

    private static Vector3I SectionCoordOf(int x, int y, int z) =>
        new(World.FloorDiv(x, SectionSize), World.FloorDiv(y, SectionSize), World.FloorDiv(z, SectionSize));

    private void MarkDirty(Vector3I coord)
    {
        if (_dirtySet.Add(coord)) _dirtyQueue.Enqueue(coord);
    }

    public override void _Process(double delta)
    {
        if (_dirtyQueue.Count == 0) return;
        var watch = Stopwatch.StartNew();
        while (_dirtyQueue.Count > 0 && watch.Elapsed.TotalMilliseconds < RebuildBudgetMs)
        {
            var coord = _dirtyQueue.Dequeue();
            _dirtySet.Remove(coord);
            RebuildSection(coord);
        }
    }

    public void BuildAllImmediately()
    {
        int guard = 0;
        while (_dirtyQueue.Count > 0 && guard++ < 200_000)
        {
            var coord = _dirtyQueue.Dequeue();
            _dirtySet.Remove(coord);
            RebuildSection(coord);
        }
    }

    private void RebuildSection(Vector3I coord)
    {
        var chunkCoord = new Vector3I(
            World.FloorDiv(coord.X, SectionsPerChunkAxis),
            World.FloorDiv(coord.Y, SectionsPerChunkAxis),
            World.FloorDiv(coord.Z, SectionsPerChunkAxis));

        if (_world == null || !_world.HasChunk(chunkCoord))
        {
            if (_views.Remove(coord, out var stale))
            {
                stale.Mesh?.QueueFree();
                stale.Body?.QueueFree();
            }
            return;
        }

        var result = SectionMesher.Build(_world, coord);
        var view = GetOrCreateView(coord);

        if (result.HasVisibleGeometry)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = result.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = result.Normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = result.Colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = result.Indices.ToArray();

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            view.Mesh.Mesh = mesh;
        }
        else
        {
            view.Mesh.Mesh = null;
        }

        if (result.HasCollision)
        {
            var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
            shape.SetFaces(result.CollisionFaces.ToArray());
            view.Shape.Shape = shape;
            view.Shape.Disabled = false;
        }
        else
        {
            view.Shape.Shape = null;
            view.Shape.Disabled = true;
        }
    }

    private SectionView GetOrCreateView(Vector3I coord)
    {
        if (_views.TryGetValue(coord, out var view)) return view;

        var pos = new Vector3(coord.X * SectionMeters, coord.Y * SectionMeters, coord.Z * SectionMeters);
        view = new SectionView
        {
            Mesh = new MeshInstance3D { Position = pos, MaterialOverride = _material },
            Body = new StaticBody3D { Position = pos },
            Shape = new CollisionShape3D(),
        };
        view.Body.AddChild(view.Shape);
        AddChild(view.Mesh);
        AddChild(view.Body);

        _views[coord] = view;
        return view;
    }
}
