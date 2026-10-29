using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.Core.Mc;

public static class McAssetsLocator
{
    public static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return "";
        root = root.Trim().TrimEnd('\\', '/');
        var nested = Path.Combine(root, ".minecraft");
        if (!Directory.Exists(Path.Combine(root, "versions")) && Directory.Exists(Path.Combine(nested, "versions")))
            return nested;
        return root;
    }

    public static string GuessRoot()
    {
        var candidates = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) candidates.Add(Path.Combine(appData, ".minecraft"));
        candidates.Add(@"D:\MC\.minecraft");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) candidates.Add(Path.Combine(home, ".minecraft"));

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(Path.Combine(candidate, "versions"))) return candidate;
        }
        return "";
    }

    public static List<string> ListVersions(string root)
    {
        root = NormalizeRoot(root);
        var versionsDir = Path.Combine(root, "versions");
        if (!Directory.Exists(versionsDir)) return new List<string>();

        return Directory.GetDirectories(versionsDir)
            .Where(dir => File.Exists(Path.Combine(dir, Path.GetFileName(dir) + ".jar")))
            .Select(Path.GetFileName)
            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveVersion(string root, string preferred)
    {
        var versions = ListVersions(root);
        if (versions.Count == 0) return "";

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = versions.FirstOrDefault(v => string.Equals(v, preferred, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return versions[0];
    }

    public static string VersionJar(string root, string version)
    {
        root = NormalizeRoot(root);
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(version)) return "";
        var path = Path.Combine(root, "versions", version, version + ".jar");
        return File.Exists(path) ? path : "";
    }
}
