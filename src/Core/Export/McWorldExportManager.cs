using System.IO.Compression;
using AIVoxelStudio.Core.Mc;
using Godot;

namespace AIVoxelStudio.Core.Export;

// 导出 Java 版世界存档：level.dat(gzip NBT) + region/*.mca，免模组，可直接放进 saves
public sealed class McWorldExportManager : IExportManager
{
    private const int ChunkSize = 16;
    private const int RegionSize = 32;
    private const int SectionCount = (McWorld.MaxY - McWorld.MinY + 1) / ChunkSize;
    private const int HeaderBytes = 8192;
    private const int SectorBytes = 4096;

    private readonly McWorld _world;
    private readonly McAssets _assets;

    public McWorldExportManager(McWorld world, McAssets assets)
    {
        _world = world;
        _assets = assets;
    }

    public string Id => "mcworld";
    public string DisplayName => "导出我的世界存档";
    public string Extension => "";
    public bool IsDirectory => true;

    public Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken token = default) =>
        Task.Run(() => Export(request, token), token);

    private ExportResult Export(ExportRequest request, CancellationToken token)
    {
        try
        {
            return ExportCore(request, token);
        }
        catch (OperationCanceledException)
        {
            return new ExportResult(false, "导出已取消");
        }
        catch (Exception ex)
        {
            GD.PushError($"MC export failed: {ex}");
            return new ExportResult(false, $"导出失败：{ex.Message}");
        }
    }

    private ExportResult ExportCore(ExportRequest request, CancellationToken token)
    {
        string target = request.Path;
        if (string.IsNullOrWhiteSpace(target)) return new ExportResult(false, "未指定导出目录");

        string saveName = SaveManager.Sanitize(request.SaveName);
        int dataVersion = _assets.DataVersion > 0 ? _assets.DataVersion : 3465;
        var spawn = ResolveSpawn(request.Spawn);
        bool blank = _world.SectionCount == 0;

        Directory.CreateDirectory(target);
        PrepareTarget(target);
        token.ThrowIfCancellationRequested();

        WriteLevelDat(Path.Combine(target, "level.dat"), saveName, dataVersion, spawn, blank);

        var chunks = CollectChunks(spawn);
        int regionCount = WriteChunks(Path.Combine(target, "region"), chunks, dataVersion, token);

        File.WriteAllBytes(Path.Combine(target, "session.lock"), new byte[] { 0xE2, 0x98, 0x83 });

        string version = string.IsNullOrWhiteSpace(_assets.VersionName) ? "" : $"（{_assets.VersionName}）";
        return new ExportResult(true,
            $"已导出 {chunks.Count} 个区块 / {regionCount} 个 region{version} → {target}");
    }

    private static void PrepareTarget(string target)
    {
        foreach (var name in new[] { "level.dat", "level.dat_old" })
        {
            string file = Path.Combine(target, name);
            if (File.Exists(file)) File.Delete(file);
        }

        string region = Path.Combine(target, "region");
        if (Directory.Exists(region)) Directory.Delete(region, true);
        Directory.CreateDirectory(region);
    }

    private Vector3I ResolveSpawn(Vector3I? requested)
    {
        if (requested.HasValue)
        {
            var spawn = requested.Value;
            return new Vector3I(spawn.X, Math.Clamp(spawn.Y, McWorld.MinY + 1, McWorld.MaxY), spawn.Z);
        }

        int ground = McWorld.MinY - 1;
        for (int y = McWorld.MaxY; y >= McWorld.MinY; y--)
        {
            if (!_world.IsSolid(0, y, 0)) continue;
            ground = y;
            break;
        }
        return new Vector3I(0, ground + 1, 0);
    }

    // 按 chunk 汇总 section：chunk 坐标与 section 的 X/Z 一致，Y 为 section 的 Y
    private Dictionary<(int X, int Z), Dictionary<int, McSection>> CollectChunks(Vector3I spawn)
    {
        var chunks = new Dictionary<(int X, int Z), Dictionary<int, McSection>>();
        foreach (var section in _world.Sections)
        {
            var key = (section.Coord.X, section.Coord.Z);
            if (!chunks.TryGetValue(key, out var byY))
            {
                byY = new Dictionary<int, McSection>();
                chunks[key] = byY;
            }
            byY[section.Coord.Y] = section;
        }

        var spawnChunk = (McWorld.FloorDiv(spawn.X, ChunkSize), McWorld.FloorDiv(spawn.Z, ChunkSize));
        if (!chunks.ContainsKey(spawnChunk)) chunks[spawnChunk] = new Dictionary<int, McSection>();
        return chunks;
    }

    private int WriteChunks(string regionDir,
        Dictionary<(int X, int Z), Dictionary<int, McSection>> chunks, int dataVersion, CancellationToken token)
    {
        var regions = new Dictionary<(int X, int Z), List<RegionChunk>>();
        foreach (var ((cx, cz), sections) in chunks)
        {
            token.ThrowIfCancellationRequested();

            var key = (McWorld.FloorDiv(cx, RegionSize), McWorld.FloorDiv(cz, RegionSize));
            if (!regions.TryGetValue(key, out var list))
            {
                list = new List<RegionChunk>();
                regions[key] = list;
            }
            list.Add(new RegionChunk(cx, cz, BuildChunkNbt(cx, cz, sections, dataVersion)));
        }

        foreach (var ((rx, rz), regionChunks) in regions)
        {
            token.ThrowIfCancellationRequested();
            WriteRegionFile(Path.Combine(regionDir, $"r.{rx}.{rz}.mca"), regionChunks);
        }
        return regions.Count;
    }

    private byte[] BuildChunkNbt(int cx, int cz, Dictionary<int, McSection> sections, int dataVersion)
    {
        using var buffer = new MemoryStream();
        using (var w = new NbtWriter(buffer))
        {
            w.BeginRootCompound();

            w.PutInt("DataVersion", dataVersion);
            w.PutInt("xPos", cx);
            w.PutInt("zPos", cz);
            w.PutInt("yPos", McWorld.MinY / ChunkSize);
            w.PutLong("LastUpdate", 0);
            w.PutLong("InhabitedTime", 0);
            w.PutByte("isLightOn", 0);
            w.PutString("Status", "minecraft:full");

            w.BeginList("sections", NbtWriter.TagType.Compound, SectionCount);
            for (int i = 0; i < SectionCount; i++)
            {
                int sy = McWorld.MinY / ChunkSize + i;
                WriteSection(w, sy, sections.GetValueOrDefault(sy));
            }

            WriteHeightmaps(w, cx, cz, sections);

            w.BeginList("block_entities", NbtWriter.TagType.Compound, 0);

            w.BeginCompound("structures");
            w.BeginCompound("References");
            w.EndTag();
            w.BeginCompound("starts");
            w.EndTag();
            w.EndTag();

            w.BeginList("PostProcessing", NbtWriter.TagType.Int, 0);

            w.EndTag(); // root
        }
        return buffer.ToArray();
    }

    private static void WriteSection(NbtWriter w, int sectionY, McSection section)
    {
        w.BeginCompoundValue();
        w.PutByte("Y", (byte)(sbyte)sectionY);

        w.BeginCompound("block_states");
        int paletteCount = section?.Palette.Count ?? 1;
        w.BeginList("palette", NbtWriter.TagType.Compound, paletteCount);
        if (section == null)
        {
            w.BeginCompoundValue();
            w.PutString("Name", McBlockState.AirId);
            w.EndTag();
        }
        else
        {
            foreach (var state in section.Palette)
                WritePaletteState(w, state);
        }

        if (paletteCount > 1)
        {
            int bits = Math.Max(4, BitsFor(paletteCount));
            w.PutLongArray("data", PackBlocks(section, bits));
        }
        w.EndTag(); // block_states

        w.BeginCompound("biomes");
        w.BeginList("palette", NbtWriter.TagType.String, 1);
        w.PutStringValue("minecraft:plains");
        w.EndTag(); // biomes

        w.EndTag(); // section
    }

    private static void WritePaletteState(NbtWriter w, McBlockState state)
    {
        w.BeginCompoundValue();
        w.PutString("Name", state.Id);
        if (state.Properties.Count > 0)
        {
            w.BeginCompound("Properties");
            foreach (var pair in state.Properties) w.PutString(pair.Key, pair.Value);
            w.EndTag();
        }
        w.EndTag();
    }

    private static int BitsFor(int paletteCount)
    {
        int bits = 0;
        while ((1 << bits) < paletteCount) bits++;
        return bits;
    }

    // block_states 索引 YZX，不能跨 long
    private static long[] PackBlocks(McSection section, int bits)
    {
        int perLong = 64 / bits;
        var data = new long[(McSection.Size3 + perLong - 1) / perLong];
        for (int i = 0; i < McSection.Size3; i++)
            data[i / perLong] |= (long)section.Blocks[i] << ((i % perLong) * bits);
        return data;
    }

    private void WriteHeightmaps(NbtWriter w, int cx, int cz, Dictionary<int, McSection> sections)
    {
        var motion = new int[256];
        var surface = new int[256];

        int topY = McWorld.MinY - 1;
        if (sections.Count > 0) topY = Math.Min(McWorld.MaxY, sections.Keys.Max() * ChunkSize + ChunkSize - 1);

        for (int z = 0; z < ChunkSize; z++)
        {
            for (int x = 0; x < ChunkSize; x++)
            {
                int wx = cx * ChunkSize + x;
                int wz = cz * ChunkSize + z;
                int surfaceValue = 0;
                int motionValue = 0;
                bool motionFound = false;

                for (int y = topY; y >= McWorld.MinY; y--)
                {
                    var state = _world.GetBlock(wx, y, wz);
                    if (state.IsAir) continue;

                    if (surfaceValue == 0) surfaceValue = y - McWorld.MinY + 1;
                    if (!motionFound && state.IsSolid)
                    {
                        motionValue = y - McWorld.MinY + 1;
                        motionFound = true;
                    }
                    if (motionFound) break;
                }

                int index = z * ChunkSize + x;
                surface[index] = surfaceValue;
                motion[index] = motionValue;
            }
        }

        w.BeginCompound("Heightmaps");
        w.PutLongArray("MOTION_BLOCKING", PackHeightmap(motion));
        w.PutLongArray("WORLD_SURFACE", PackHeightmap(surface));
        w.EndTag();
    }

    // 256 个 9 bit 高度值，7 个/long，共 37 longs
    private static long[] PackHeightmap(int[] values)
    {
        const int bits = 9;
        const int perLong = 64 / bits;
        var data = new long[(values.Length + perLong - 1) / perLong];
        for (int i = 0; i < values.Length; i++)
            data[i / perLong] |= (long)values[i] << ((i % perLong) * bits);
        return data;
    }

    private static void WriteRegionFile(string path, List<RegionChunk> chunks)
    {
        var header = new byte[HeaderBytes];
        int timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var payload = new MemoryStream();
        foreach (var chunk in chunks.OrderBy(c => c.Z).ThenBy(c => c.X))
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(chunk.Nbt, 0, chunk.Nbt.Length);

            int length = 1 + (int)compressed.Length;
            int total = 4 + length;
            int sectors = (total + SectorBytes - 1) / SectorBytes;
            int offsetSector = HeaderBytes / SectorBytes + (int)(payload.Length / SectorBytes);
            if (offsetSector > 0xFFFFFF) throw new InvalidOperationException("region 文件超过 256 MiB 上限");

            WriteInt32(payload, length);
            payload.WriteByte(2); // zlib
            compressed.Position = 0;
            compressed.CopyTo(payload);
            int pad = (SectorBytes - (int)(payload.Length % SectorBytes)) % SectorBytes;
            for (int i = 0; i < pad; i++) payload.WriteByte(0);

            int index = (chunk.X & (RegionSize - 1)) + (chunk.Z & (RegionSize - 1)) * RegionSize;
            int location = index * 4;
            header[location] = (byte)(offsetSector >> 16);
            header[location + 1] = (byte)(offsetSector >> 8);
            header[location + 2] = (byte)offsetSector;
            header[location + 3] = (byte)sectors;
            WriteInt32(header, SectorBytes + location, timestamp);
        }

        using var file = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write);
        file.Write(header, 0, HeaderBytes);
        payload.Position = 0;
        payload.CopyTo(file);
    }

    private void WriteLevelDat(string path, string saveName, int dataVersion, Vector3I spawn, bool blank)
    {
        string versionName = string.IsNullOrWhiteSpace(_assets.VersionName) ? "1.20.1" : _assets.VersionName;

        using var file = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var w = new NbtWriter(gzip);

        w.BeginRootCompound();
        w.BeginCompound("Data");

        w.PutInt("DataVersion", dataVersion);
        // 小写 version 是存档格式版本（Anvil = 19133），客户端缺这个字段会直接报 Unknown data version: 0
        w.PutInt("version", 19133);

        w.BeginCompound("Version");
        w.PutInt("Id", dataVersion);
        w.PutString("Name", versionName);
        w.PutString("Series", "main");
        w.PutByte("Snapshot", 0);
        w.EndTag();

        w.PutString("LevelName", saveName);
        w.PutInt("GameType", 1);
        w.PutInt("hardcore", 0);
        w.PutInt("Difficulty", 1);
        w.PutInt("DifficultyLocked", 0);
        w.PutInt("allowCommands", 1);
        w.PutInt("MapFeatures", 0);
        w.PutInt("initialized", 1);

        w.PutInt("SpawnX", spawn.X);
        w.PutInt("SpawnY", spawn.Y);
        w.PutInt("SpawnZ", spawn.Z);

        w.PutLong("Time", 0);
        w.PutLong("DayTime", 0);
        w.PutLong("LastPlayed", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        w.BeginCompound("DataPacks");
        w.BeginList("Enabled", NbtWriter.TagType.String, 1);
        w.PutStringValue("vanilla");
        w.BeginList("Disabled", NbtWriter.TagType.String, 0);
        w.EndTag(); // DataPacks

        w.BeginCompound("WorldGenSettings");
        w.PutLong("seed", 0);
        w.PutByte("generate_features", 0);
        w.PutByte("bonus_chest", 0);
        w.BeginCompound("dimensions");

        w.BeginCompound("minecraft:overworld");
        w.PutString("type", "minecraft:overworld");
        WriteOverworldGenerator(w, blank);
        w.EndTag();

        WriteNoiseDimension(w, "minecraft:the_nether", "minecraft:nether", "minecraft:multi_noise", "minecraft:nether");
        WriteNoiseDimension(w, "minecraft:the_end", "minecraft:end", "minecraft:the_end", null);

        w.EndTag(); // dimensions
        w.EndTag(); // WorldGenSettings

        w.EndTag(); // Data
        w.EndTag(); // root
    }

    // 默认平坦地形：bedrock 1 + stone 60 + dirt 2 + grass 1，顶面 y = -1
    private static void WriteOverworldGenerator(NbtWriter w, bool blank)
    {
        w.BeginCompound("generator");
        w.PutString("type", "minecraft:flat");
        w.BeginCompound("settings");
        w.PutString("biome", blank ? "minecraft:the_void" : "minecraft:plains");
        w.PutByte("lakes", 0);
        w.PutByte("features", 0);
        w.BeginList("structure_overrides", NbtWriter.TagType.String, 0);

        if (blank)
        {
            w.BeginList("layers", NbtWriter.TagType.Compound, 0);
        }
        else
        {
            w.BeginList("layers", NbtWriter.TagType.Compound, 4);
            WriteFlatLayer(w, "minecraft:bedrock", 1);
            WriteFlatLayer(w, "minecraft:stone", 60);
            WriteFlatLayer(w, "minecraft:dirt", 2);
            WriteFlatLayer(w, "minecraft:grass_block", 1);
        }

        w.EndTag(); // settings
        w.EndTag(); // generator
    }

    private static void WriteFlatLayer(NbtWriter w, string block, int height)
    {
        w.BeginCompoundValue();
        w.PutString("block", block);
        w.PutInt("height", height);
        w.EndTag();
    }

    // noise 生成器必须带 biome_source，否则客户端解析 WorldGenSettings 直接失败
    private static void WriteNoiseDimension(
        NbtWriter w, string key, string settings, string biomeSourceType, string preset)
    {
        w.BeginCompound(key);
        w.PutString("type", key);
        w.BeginCompound("generator");
        w.PutString("type", "minecraft:noise");
        w.PutString("settings", settings);
        w.BeginCompound("biome_source");
        w.PutString("type", biomeSourceType);
        if (preset != null) w.PutString("preset", preset);
        w.EndTag(); // biome_source
        w.EndTag(); // generator
        w.EndTag(); // key
    }

    private static void WriteInt32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private sealed record RegionChunk(int X, int Z, byte[] Nbt);
}
