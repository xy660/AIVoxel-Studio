using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Core.Mc;

public sealed class McBlockPalette : IBlockPalette
{
    private static readonly string[] DefaultIds =
    {
        "minecraft:stone",
        "minecraft:oak_planks",
        "minecraft:glass",
        "minecraft:oak_log",
        "minecraft:cobblestone",
        "minecraft:bricks",
        "minecraft:grass_block",
        "minecraft:sand",
        "minecraft:water",
    };

    private readonly McAssets _assets;
    private readonly McBlockState[] _states;
    private readonly List<BlockSlot> _slots = new();

    public McBlockPalette(McAssets assets)
    {
        _assets = assets;
        _states = new McBlockState[DefaultIds.Length];

        var fallback = assets.DefaultState("minecraft:stone") ?? McBlockState.Air;
        for (int i = 0; i < DefaultIds.Length; i++)
        {
            var state = assets.DefaultState(DefaultIds[i]) ?? fallback;
            _states[i] = state;
            _slots.Add(new BlockSlot(assets.DisplayName(state.Id), new Color(0.34f, 0.34f, 0.38f), assets.Icon(state)));
        }
    }

    public McAssets Assets => _assets;
    public IReadOnlyList<BlockSlot> Slots => _slots;
    public int SelectedIndex { get; private set; }
    public event Action Changed;

    public McBlockState SelectedState => _states[SelectedIndex];

    public void Select(int index)
    {
        SelectedIndex = Mathf.Wrap(index, 0, _states.Length);
        Changed?.Invoke();
    }

    public bool ReplaceSlot(int index, McBlockState state)
    {
        if (state == null || state.IsAir || index < 0 || index >= _states.Length) return false;
        _states[index] = state;
        _slots[index] = new BlockSlot(_assets.DisplayName(state.Id), _slots[index].Swatch, _assets.Icon(state));
        Changed?.Invoke();
        return true;
    }

    public string DescribeSelected() => _assets.DisplayName(_states[SelectedIndex].Id);
}
