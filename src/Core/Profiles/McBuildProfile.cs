using Godot;
using AIVoxelStudio.AI;
using AIVoxelStudio.AI.Tools;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Export;
using AIVoxelStudio.Core.Mc;
using AIVoxelStudio.Player;
using AIVoxelStudio.Render;

namespace AIVoxelStudio.Core.Profiles;

public sealed class McBuildProfile : IBuildProfile
{
    private readonly McWorld _world = new();
    private readonly McAssets _assets;
    private readonly McBlockPalette _palette;
    private Action<Action<RegionBox>> _enterSelectRegion;

    public McBuildProfile(McAssets assets)
    {
        _assets = assets;
        _palette = new McBlockPalette(assets);
        Tools = new McToolProvider();
        Serializer = new McWorldSerializer(_world, assets);
        Exporters = new IExportManager[]
        {
            new McWorldExportManager(_world, _assets),
            new McStructureExportManager(_world, _assets),
        };
    }

    public McAssets Assets => _assets;
    public McBlockPalette McPalette => _palette;
    public McWorld World => _world;

    public BuildKind Kind => BuildKind.Minecraft;
    public string DisplayName => "我的世界";
    public string PromptPath => "res://Prompts/mc/system_prompt.md";
    public float BlockMeters => 1f;
    public float ReachMeters => 5f;
    public IToolProvider Tools { get; }
    public IWorldSerializer Serializer { get; }
    public IReadOnlyList<IExportManager> Exporters { get; }
    public IBlockPalette Palette => _palette;
    public int RegionCount => _world.SectionCount;

    public IPlayerWorld CreatePlayerWorld() => new McPlayerWorld(_world, _palette, _assets);

    public IWorldRenderer CreateRenderer()
    {
        var renderer = new McWorldRenderer();
        renderer.Attach(_world, _assets, BlockMeters);
        return renderer;
    }

    public void GenerateDefaultWorld() => McWorldGenerator.GenerateArea(_world, _assets);

    public Vector3 FindSpawn(bool blank)
    {
        if (blank) return new Vector3(0.5f, 1f, 0.5f);

        int groundY = McWorldGenerator.SurfaceY;
        for (int scan = McWorld.MaxY; scan >= McWorld.MinY; scan--)
        {
            if (_world.IsSolid(0, scan, 0))
            {
                groundY = scan;
                break;
            }
        }

        return new Vector3(0.5f, (groundY + 1) * BlockMeters + 0.05f, 0.5f);
    }

    public Vector3I? PickCell(Vector3 origin, Vector3 direction, float maxDistance)
    {
        bool Occupied(int x, int y, int z) => !_world.GetBlock(x, y, z).IsAir;
        return VoxelRay.Cast(Occupied, origin, direction, maxDistance, out var hit, out _) ? hit : null;
    }

    public void AttachRegionSelector(Action<Action<RegionBox>> enter) => _enterSelectRegion = enter;

    public void EnterSelectRegionMode(Action<RegionBox> callback) => _enterSelectRegion?.Invoke(callback);

    public ToolContext CreateToolContext(ToolContextEnvironment environment) => new McToolContext
    {
        World = _world,
        Assets = _assets,
        PlayerBlock = environment.PlayerBlock,
        SelectedBlock = () => _palette.DescribeSelected(),
        RegionCount = () => _world.SectionCount,
        RenderSnapshot = environment.RenderSnapshot,
        SaveAttachment = environment.SaveAttachment,
        AskUser = environment.AskUser,
        BlockMeters = BlockMeters,
        ModeName = DisplayName,
    };
}
