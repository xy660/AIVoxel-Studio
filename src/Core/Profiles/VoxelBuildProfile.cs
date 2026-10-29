using Godot;
using AIVoxelStudio.AI;
using AIVoxelStudio.AI.Tools;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Export;
using AIVoxelStudio.Player;
using AIVoxelStudio.Render;

namespace AIVoxelStudio.Core.Profiles;

public sealed class VoxelBuildProfile : IBuildProfile
{
    private readonly World _world = new();
    private readonly VoxelBlockPalette _palette = new();
    private Action<Action<RegionBox>> _enterSelectRegion;

    public VoxelBuildProfile()
    {
        Tools = new VoxelToolProvider();
        Serializer = new VoxelWorldSerializer(_world);
        Exporters = new IExportManager[]
        {
            new ObjExportManager(_world),
            new ObjExportManager(_world, rangeOnly: true),
        };
    }

    public BuildKind Kind => BuildKind.Voxel;
    public string DisplayName => "纯色体素";
    public string PromptPath => "res://Prompts/voxel/system_prompt.md";
    public float BlockMeters => VoxelUnits.BlockMeters;
    public float ReachMeters => 3f;
    public IToolProvider Tools { get; }
    public IWorldSerializer Serializer { get; }
    public IReadOnlyList<IExportManager> Exporters { get; }
    public IBlockPalette Palette => _palette;
    public int RegionCount => _world.ChunkCount;

    public IPlayerWorld CreatePlayerWorld() => new VoxelPlayerWorld(_world, _palette);

    public IWorldRenderer CreateRenderer()
    {
        var renderer = new WorldRenderer();
        renderer.Attach(_world);
        return renderer;
    }

    public void GenerateDefaultWorld() => WorldGenerator.GenerateArea(_world);

    public Vector3 FindSpawn(bool blank)
    {
        if (blank) return new Vector3(0.5f * BlockMeters, 1f, 0.5f * BlockMeters);

        int groundY = -1;
        for (int scan = 63; scan >= -63; scan--)
        {
            if (_world.IsSolid(0, scan, 0))
            {
                groundY = scan;
                break;
            }
        }

        return new Vector3(0.5f * BlockMeters, (groundY + 1) * BlockMeters + 0.05f, 0.5f * BlockMeters);
    }

    public Vector3I? PickCell(Vector3 origin, Vector3 direction, float maxDistance)
    {
        bool Occupied(int x, int y, int z) => _world.GetBlock(x, y, z).Type != 0;
        return VoxelRay.Cast(Occupied, origin, direction, maxDistance, out var hit, out _) ? hit : null;
    }

    public void AttachRegionSelector(Action<Action<RegionBox>> enter) => _enterSelectRegion = enter;

    public void EnterSelectRegionMode(Action<RegionBox> callback) => _enterSelectRegion?.Invoke(callback);

    public ToolContext CreateToolContext(ToolContextEnvironment environment) => new VoxelToolContext
    {
        World = _world,
        Editor = new VoxelEditService(_world),
        SelectedBlockInfo = () => _palette.SelectedBlock,
        PlayerBlock = environment.PlayerBlock,
        SelectedBlock = () => _palette.DescribeSelected(),
        RegionCount = () => _world.ChunkCount,
        RenderSnapshot = environment.RenderSnapshot,
        SaveAttachment = environment.SaveAttachment,
        BlockMeters = BlockMeters,
        ModeName = DisplayName,
    };
}
