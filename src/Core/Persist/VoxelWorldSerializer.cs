using Godot;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Core;

// 纯色体素存档：voxg 头 + 64³ chunk
public sealed class VoxelWorldSerializer : IWorldSerializer
{
    private readonly World _world;

    public VoxelWorldSerializer(World world) => _world = world;

    public BuildKind Kind => BuildKind.Voxel;

    public void Save(string saveName)
    {
        Directory.CreateDirectory(SaveManager.SaveDir(saveName));
        var path = SaveManager.ChunkFile(saveName);
        var temp = path + ".tmp";

        using (var stream = new FileStream(temp, FileMode.Create, System.IO.FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(SaveManager.Magic);
            writer.Write(SaveManager.Version);
            writer.Write((int)BuildKind.Voxel);
            var chunks = _world.Chunks.ToList();
            writer.Write(chunks.Count);

            var buffer = new byte[Chunk.Size3 * 4];
            foreach (var chunk in chunks)
            {
                writer.Write(chunk.Coord.X);
                writer.Write(chunk.Coord.Y);
                writer.Write(chunk.Coord.Z);

                for (int i = 0; i < Chunk.Size3; i++)
                {
                    var b = chunk.Blocks[i];
                    int o = i * 4;
                    buffer[o] = b.Type;
                    buffer[o + 1] = b.R;
                    buffer[o + 2] = b.G;
                    buffer[o + 3] = b.B;
                }
                writer.Write(buffer);
            }
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    public bool Load(string saveName)
    {
        var path = SaveManager.ChunkFile(saveName);
        if (!File.Exists(path)) return false;

        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != SaveManager.Magic) return false;
        if (reader.ReadInt32() != SaveManager.Version) return false;
        if (reader.ReadInt32() != (int)BuildKind.Voxel) return false;

        int count = reader.ReadInt32();
        if (count < 0 || count > 200_000) return false;

        _world.Clear();
        var buffer = new byte[Chunk.Size3 * 4];
        for (int c = 0; c < count; c++)
        {
            var coord = new Vector3I(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            ReadExact(reader, buffer);
            var chunk = new Chunk(coord);
            for (int i = 0; i < Chunk.Size3; i++)
            {
                int o = i * 4;
                chunk.Blocks[i] = new BlockInfo(buffer[o], buffer[o + 1], buffer[o + 2], buffer[o + 3]);
            }
            _world.AddChunk(chunk);
        }
        return true;
    }

    private static void ReadExact(BinaryReader reader, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = reader.Read(buffer, read, buffer.Length - read);
            if (n <= 0) throw new EndOfStreamException("ChunkData.dat is truncated");
            read += n;
        }
    }
}
