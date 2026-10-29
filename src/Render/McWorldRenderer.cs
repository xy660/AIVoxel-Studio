using System.Diagnostics;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Mc;
using Godot;

namespace AIVoxelStudio.Render;

// 我的世界 section 渲染器：每个 16³ section 一个 MeshInstance + 合并碰撞
public partial class McWorldRenderer : Node3D, IWorldRenderer
{
    private const int SectionSize = McSection.Size;
    private const double RebuildBudgetMs = 6.0;

    private readonly Dictionary<Vector3I, SectionView> _views = new();
    private readonly HashSet<Vector3I> _dirty = new();
    private readonly Queue<Vector3I> _queue = new();

    private McWorld _world;
    private McAssets _assets;
    private float _meters = 1f;
    private StandardMaterial3D _opaqueMaterial;
    private StandardMaterial3D _translucentMaterial;

    private sealed class SectionView
    {
        public MeshInstance3D Mesh;
        public StaticBody3D Body;
        public CollisionShape3D Shape;
    }

    public Node3D Node => this;

    public override void _Ready()
    {
        _opaqueMaterial = new StandardMaterial3D
        {
            AlbedoTexture = _assets.Atlas,
            AlbedoColor = Colors.White,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.5f,
            Roughness = 0.95f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        };

        _translucentMaterial = new StandardMaterial3D
        {
            AlbedoTexture = _assets.Atlas,
            AlbedoColor = Colors.White,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.95f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        };
    }

    public void Attach(McWorld world, McAssets assets, float blockMeters)
    {
        _world = world;
        _assets = assets;
        _meters = blockMeters;
        _world.BlockChanged += OnBlockChanged;
    }

    public void BuildAllImmediately()
    {
        foreach (var view in _views.Values) view.Mesh.QueueFree();
        _views.Clear();
        _dirty.Clear();
        _queue.Clear();
        foreach (var section in _world.Sections) Rebuild(section.Coord);
    }

    private void OnBlockChanged(int x, int y, int z)
    {
        var coord = McWorld.SectionCoordOf(x, y, z);
        MarkDirty(coord);

        int lx = McWorld.LocalOf(x, SectionSize);
        int ly = McWorld.LocalOf(y, SectionSize);
        int lz = McWorld.LocalOf(z, SectionSize);
        if (lx == 0) MarkDirty(coord + new Vector3I(-1, 0, 0));
        if (lx == SectionSize - 1) MarkDirty(coord + new Vector3I(1, 0, 0));
        if (ly == 0) MarkDirty(coord + new Vector3I(0, -1, 0));
        if (ly == SectionSize - 1) MarkDirty(coord + new Vector3I(0, 1, 0));
        if (lz == 0) MarkDirty(coord + new Vector3I(0, 0, -1));
        if (lz == SectionSize - 1) MarkDirty(coord + new Vector3I(0, 0, 1));
    }

    private void MarkDirty(Vector3I coord)
    {
        if (_dirty.Add(coord)) _queue.Enqueue(coord);
    }

    public override void _Process(double delta)
    {
        if (_queue.Count == 0) return;

        var watch = Stopwatch.StartNew();
        while (_queue.Count > 0)
        {
            var coord = _queue.Dequeue();
            _dirty.Remove(coord);
            Rebuild(coord);
            if (watch.Elapsed.TotalMilliseconds >= RebuildBudgetMs) break;
        }
    }

    private void Rebuild(Vector3I coord)
    {
        var section = _world.GetSection(coord);
        var result = section == null || section.IsEmpty
            ? null
            : McMesher.Build(_world, _assets, section, _meters);

        if (result == null || (!result.HasOpaque && !result.HasTranslucent))
        {
            RemoveView(coord);
            return;
        }

        if (!_views.TryGetValue(coord, out var view))
        {
            view = new SectionView { Mesh = new MeshInstance3D() };
            AddChild(view.Mesh);
            _views[coord] = view;
        }

        view.Mesh.Position = new Vector3(coord.X, coord.Y, coord.Z) * SectionSize * _meters;

        var mesh = new ArrayMesh();
        if (result.HasOpaque)
            AddSurface(mesh, result.OpaqueVertices, result.OpaqueNormals, result.OpaqueUvs, result.OpaqueIndices);
        if (result.HasTranslucent)
            AddSurface(mesh, result.TranslucentVertices, result.TranslucentNormals, result.TranslucentUvs,
                result.TranslucentIndices);

        view.Mesh.Mesh = mesh;
        int surface = 0;
        if (result.HasOpaque) view.Mesh.SetSurfaceOverrideMaterial(surface++, _opaqueMaterial);
        if (result.HasTranslucent) view.Mesh.SetSurfaceOverrideMaterial(surface, _translucentMaterial);

        if (!result.HasCollision)
        {
            RemoveCollision(view);
            return;
        }

        if (view.Body == null)
        {
            view.Body = new StaticBody3D();
            view.Shape = new CollisionShape3D();
            view.Body.AddChild(view.Shape);
            AddChild(view.Body);
        }

        view.Shape.Shape = new ConcavePolygonShape3D { Data = result.Collision.ToArray() };
    }

    private static void AddSurface(ArrayMesh mesh, List<Vector3> vertices, List<Vector3> normals,
        List<Vector2> uvs, List<int> indices)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private void RemoveView(Vector3I coord)
    {
        if (!_views.TryGetValue(coord, out var view)) return;
        view.Mesh.QueueFree();
        RemoveCollision(view);
        _views.Remove(coord);
    }

    private static void RemoveCollision(SectionView view)
    {
        if (view.Body == null) return;
        view.Body.QueueFree();
        view.Body = null;
        view.Shape = null;
    }

    public override void _ExitTree()
    {
        if (_world != null) _world.BlockChanged -= OnBlockChanged;
    }
}
