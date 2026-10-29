using Godot;

namespace AIVoxelStudio.UI;

public static class UiTheme
{
    public static Theme Create()
    {
        var theme = new Theme();
        var font = new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Segoe UI", "sans-serif" },
        };
        theme.DefaultFont = font;
        theme.DefaultFontSize = 14;
        return theme;
    }
}
