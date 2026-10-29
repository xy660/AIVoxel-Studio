using Godot;

namespace AIVoxelStudio.Core;

public sealed class World
{
    private readonly Dictionary<Vector3I, Chunk> _chunks = new();

    public event Action<int, int, int> BlockChanged;

    public IEnumerable<Chunk> Chunks => _chunks.Values;
    public int ChunkCount => _chunks.Count;

    public static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        if ((value % divisor != 0) && (value < 0)) q--;
        return q;
    }

    public static Vector3I ChunkCoordOf(int x, int y, int z) =>
        new(FloorDiv(x, Chunk.Size), FloorDiv(y, Chunk.Size), FloorDiv(z, Chunk.Size));

    public static int LocalOf(int world, int divisor) => world - FloorDiv(world, divisor) * divisor;

    public bool HasChunk(Vector3I coord) => _chunks.ContainsKey(coord);

    public Chunk GetChunk(Vector3I coord) => _chunks.GetValueOrDefault(coord);

    public Chunk GetOrCreateChunk(Vector3I coord)
    {
        if (_chunks.TryGetValue(coord, out var chunk)) return chunk;
        chunk = new Chunk(coord);
        _chunks[coord] = chunk;
        return chunk;
    }

    public void AddChunk(Chunk chunk) => _chunks[chunk.Coord] = chunk;

    public void Clear() => _chunks.Clear();

    public BlockInfo GetBlock(int x, int y, int z)
    {
        var coord = ChunkCoordOf(x, y, z);
        if (!_chunks.TryGetValue(coord, out var chunk)) return BlockInfo.Air;
        return chunk.Get(LocalOf(x, Chunk.Size), LocalOf(y, Chunk.Size), LocalOf(z, Chunk.Size));
    }

    public bool IsSolid(int x, int y, int z) => GetBlock(x, y, z).IsSolid;

    public bool SetBlock(int x, int y, int z, in BlockInfo info, bool notify = true)
    {
        var coord = ChunkCoordOf(x, y, z);
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            if (info.IsAir) return false;
            chunk = GetOrCreateChunk(coord);
        }

        int lx = LocalOf(x, Chunk.Size);
        int ly = LocalOf(y, Chunk.Size);
        int lz = LocalOf(z, Chunk.Size);
        if (chunk.Get(lx, ly, lz).SameVisual(info)) return false;

        chunk.Set(lx, ly, lz, info);
        if (notify) BlockChanged?.Invoke(x, y, z);
        return true;
    }
}
