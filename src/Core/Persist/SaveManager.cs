using Godot;

namespace AIVoxelStudio.Core;

public static class SaveManager
{
    public const int Magic = 0x67786F76;
	public const int Version = 2;

    public static string SavesRoot => ProjectSettings.GlobalizePath("user://saves");

    public static string SaveDir(string saveName) => Path.Combine(SavesRoot, Sanitize(saveName));

    public static string ChunkFile(string saveName) => Path.Combine(SaveDir(saveName), "ChunkData.dat");
    public static string McChunkFile(string saveName) => Path.Combine(SaveDir(saveName), "McChunkData.dat");

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
        var result = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "world" : result;
    }

    public static bool HasSave(string saveName) =>
        File.Exists(ChunkFile(saveName)) || File.Exists(McChunkFile(saveName));

    public static List<string> ListSaves()
    {
        var root = SavesRoot;
        if (!Directory.Exists(root)) return new List<string>();
        return Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "ChunkData.dat")) ||
                        File.Exists(Path.Combine(d, "McChunkData.dat")))
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToList();
    }

    public static BuildKind ReadKind(string saveName)
    {
        if (File.Exists(McChunkFile(saveName))) return BuildKind.Minecraft;
        var path = ChunkFile(saveName);
        if (!File.Exists(path)) return BuildKind.Voxel;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return BuildKind.Voxel;
            int kind = reader.ReadInt32();
            return kind == (int)BuildKind.Minecraft ? BuildKind.Minecraft : BuildKind.Voxel;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to read save kind: {ex.Message}");
            return BuildKind.Voxel;
        }
    }

    public static void DeleteSave(string saveName)
    {
        var dir = SaveDir(saveName);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
