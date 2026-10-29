using AIVoxelStudio.App;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.Core.Profiles;

// 组合根唯一允许按 BuildKind 选择实现的地方
public static class BuildProfileFactory
{
    public static IBuildProfile Create(BuildKind kind, GameSettings settings) => kind switch
    {
        BuildKind.Voxel => new VoxelBuildProfile(),
        BuildKind.Minecraft => new McBuildProfile(LoadMcAssets(settings)),
        _ => throw new NotSupportedException($"建模类型 {kind} 尚未实现"),
    };

    private static McAssets LoadMcAssets(GameSettings settings)
    {
        var root = McAssetsLocator.NormalizeRoot(settings.McAssetsPath);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "versions")))
            throw new InvalidOperationException("未配置我的世界目录，请先在设置中选择 .minecraft 路径");

        var version = McAssetsLocator.ResolveVersion(root, settings.McVersion);
        if (string.IsNullOrEmpty(version))
            throw new InvalidOperationException("我的世界目录下没有可用版本（versions/*/*.jar）");

        settings.McVersion = version;
        return McAssets.Load(root, version);
    }
}
