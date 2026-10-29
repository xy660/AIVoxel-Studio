using System.Text;
using AIVoxelStudio.Core.Abstractions;
using Godot;

namespace AIVoxelStudio.Core.Mc;

// 我的世界存档：mcvg 头 + 16³ section（palette + packed indices，YZX）
public sealed class McWorldSerializer : IWorldSerializer
{
    private const int Version = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("mcvg");

    private readonly McWorld _world;
    private readonly McAssets _assets;

    public McWorldSerializer(McWorld world, McAssets assets)
    {
        _world = world;
        _assets = assets;
    }

    public BuildKind Kind => BuildKind.Minecraft;

    public void Save(string saveName)
    {
        Directory.CreateDirectory(SaveManager.SaveDir(saveName));
        var path = SaveManager.McChunkFile(saveName);
        var temp = path + ".tmp";

        var sections = _world.Sections.Where(s => !s.IsEmpty).ToList();

        using (var stream = new FileStream(temp, FileMode.Create, System.IO.FileAccess.Write))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(_assets.DataVersion);
            writer.Write(sections.Count);

            foreach (var section in sections)
            {
                writer.Write(section.Coord.X);
                writer.Write(section.Coord.Y);
                writer.Write(section.Coord.Z);
                writer.Write(section.Palette.Count);
                foreach (var state in section.Palette) writer.Write(state.FullString);

                int count = section.Palette.Count;
                int bits = count <= 1 ? 0 : Math.Max(4, BitsFor(count));
                writer.Write((byte)bits);

                if (bits == 0)
                {
                    writer.Write(0);
                    continue;
                }

                int perLong = 64 / bits;
                int longCount = (McSection.Size3 + perLong - 1) / perLong;
                var data = new long[longCount];
                for (int i = 0; i < McSection.Size3; i++)
                {
                    int value = section.Blocks[i];
                    data[i / perLong] |= (long)value << ((i % perLong) * bits);
                }
                writer.Write(longCount);
                foreach (var value in data) writer.Write(value);
            }
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    public bool Load(string saveName)
    {
        var path = SaveManager.McChunkFile(saveName);
        if (!File.Exists(path)) return false;

        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);

        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.SequenceEqual(Magic)) return false;
        if (reader.ReadInt32() != Version) return false;
        reader.ReadInt32(); // DataVersion：状态串自解释，读档不依赖版本号

        int sectionCount = reader.ReadInt32();
        if (sectionCount < 0 || sectionCount > 200_000) return false;

        _world.Clear();
        for (int s = 0; s < sectionCount; s++)
        {
            var coord = new Vector3I(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            int paletteCount = reader.ReadInt32();
            if (paletteCount <= 0 || paletteCount > McSection.Size3) return false;

            var palette = new McBlockState[paletteCount];
            for (int i = 0; i < paletteCount; i++)
                palette[i] = _assets.ResolveSerialized(reader.ReadString());

            int bits = reader.ReadByte();
            var section = new McSection(coord);
            section.SetPalette(palette);

            if (bits == 0)
            {
                if (reader.ReadInt32() != 0) return false;
                section.SetRaw(0, 0);
            }
            else
            {
                if (bits < 4 || bits > 12) return false;
                int longCount = reader.ReadInt32();
                int perLong = 64 / bits;
                int expected = (McSection.Size3 + perLong - 1) / perLong;
                if (longCount != expected) return false;

                long mask = (1L << bits) - 1;
                var data = new long[longCount];
                for (int i = 0; i < longCount; i++) data[i] = reader.ReadInt64();

                for (int i = 0; i < McSection.Size3; i++)
                {
                    int value = (int)((data[i / perLong] >> ((i % perLong) * bits)) & mask);
                    if (value >= paletteCount) return false;
                    section.SetRaw(i, (ushort)value);
                }
            }

            _world.AddSection(section);
        }
        return true;
    }

    private static int BitsFor(int count)
    {
        int bits = 0;
        while ((1 << bits) < count) bits++;
        return Math.Max(4, bits);
    }
}
