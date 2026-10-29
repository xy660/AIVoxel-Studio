using System.Text.Json;
using Godot;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.App;

public sealed class GameSettings
{
    public string ApiBaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public float Temperature { get; set; } = 0.7f;
    /// <summary>思考强度："": 关闭；low / medium / high / max。</summary>
    public string ReasoningEffort { get; set; } = "high";
    /// <summary>额外请求参数（JSON 对象），用于供应商特有的开关。</summary>
    public string ExtraBodyJson { get; set; } = "";
    public int MaxToolRounds { get; set; } = 30;
    public int MaxSnapshotsPerRequest { get; set; } = 5;
    public string SaveName { get; set; } = "world";
    public BuildKind BuildKind { get; set; } = BuildKind.Voxel;
    public bool GenerateGround { get; set; } = true;
    public float MouseSensitivity { get; set; } = 0.0022f;
    public string McAssetsPath { get; set; } = "";
    public string McVersion { get; set; } = "";

    private static string FilePath => ProjectSettings.GlobalizePath("user://settings.json");

    public static GameSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(FilePath));
                if (settings != null)
                {
                    settings.McAssetsPath = McAssetsLocator.NormalizeRoot(settings.McAssetsPath);
                    if (string.IsNullOrWhiteSpace(settings.McAssetsPath))
                        settings.McAssetsPath = McAssetsLocator.GuessRoot();
                    if (string.IsNullOrWhiteSpace(settings.McVersion))
                        settings.McVersion = McAssetsLocator.ResolveVersion(settings.McAssetsPath, "");
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to load settings: {ex.Message}");
        }
        return new GameSettings();
    }

    public void Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to save settings: {ex.Message}");
        }
    }
}
