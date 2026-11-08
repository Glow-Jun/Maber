namespace Maber.Core;

/// <summary>
/// 全局设置（跨窗口共享）。字段取值为受控枚举键，归一化后保证 UI 与渲染内核拿到合法值。
/// </summary>
public sealed record AppSettings
{
    /// <summary>最近文件硬上限；实例上的 RecentLimit 是用户可调上限</summary>
    public const int MaxRecentFiles = 50;
    public const int DefaultRecentLimit = 20;

    /// <summary>设置结构版本：2 起默认浅色；3 起加入设置窗口、字体、快捷键等字段</summary>
    public const int SettingsSchema = 3;

    // 默认 0：反序列化旧文件时用于识别「没有 schema 字段」；Normalize 会写到当前版本
    public int Schema { get; init; }
    public string Theme { get; init; } = "light";
    public string FontFamily { get; init; } = "default";
    /// <summary>FontFamily == "custom" 时使用的字体族名（系统字体或已导入字体）</summary>
    public string CustomFontFamily { get; init; } = "";
    public double FontSize { get; init; } = 16;
    public double LineHeight { get; init; } = 1.75;
    public double ContentWidth { get; init; } = 46;
    public List<string> RecentFiles { get; init; } = new();

    // ---------- v0.4 ----------
    /// <summary>代码字体："mono" / "default" / 字体族名</summary>
    public string CodeFontFamily { get; init; } = "mono";
    public double CodeFontSize { get; init; } = 13;
    public bool RenderMath { get; init; } = true;
    public bool RenderMermaid { get; init; } = true;

    public bool SidebarOpenByDefault { get; init; } = true;
    public double SidebarWidth { get; init; } = 240;

    public int RecentLimit { get; init; } = DefaultRecentLimit;
    /// <summary>"empty" 空白页 / "last" 恢复上次文件</summary>
    public string StartupBehavior { get; init; } = "empty";
    /// <summary>"auto" 自动重载 / "prompt" 总是提示</summary>
    public string ExternalChangeMode { get; init; } = "auto";
    public bool RestoreReadingPosition { get; init; } = true;
    public bool RememberWindowBounds { get; init; } = true;
    public WindowBounds? Window { get; init; }

    public List<CustomFont> CustomFonts { get; init; } = new();
    /// <summary>命令 id → 组合键覆盖；未出现的命令使用默认绑定</summary>
    public Dictionary<string, string> Shortcuts { get; init; } = new();
    public List<string> FindHistory { get; init; } = new();

    /// <summary>越界值夹回范围、未知键回落默认，避免坏配置把排版打乱</summary>
    public AppSettings Normalize() => this with
    {
        Schema = SettingsSchema,
        Theme = Theme is "system" or "light" or "dark" ? Theme : "light",
        FontFamily = NormalizeFontKey(FontFamily),
        CustomFontFamily = (CustomFontFamily ?? "").Trim(),
        FontSize = Math.Clamp(FontSize, 12, 24),
        LineHeight = Math.Clamp(LineHeight, 1.4, 2.2),
        ContentWidth = Math.Clamp(ContentWidth, 30, 70),
        RecentLimit = Math.Clamp(RecentLimit, 5, MaxRecentFiles),
        RecentFiles = NormalizeRecent(RecentFiles ?? new(), RecentLimit),
        CodeFontFamily = string.IsNullOrWhiteSpace(CodeFontFamily) ? "mono" : CodeFontFamily.Trim(),
        CodeFontSize = Math.Clamp(CodeFontSize, 10, 20),
        SidebarWidth = Math.Clamp(SidebarWidth, 180, 320),
        StartupBehavior = StartupBehavior == "last" ? "last" : "empty",
        ExternalChangeMode = ExternalChangeMode == "prompt" ? "prompt" : "auto",
        Window = NormalizeWindow(Window),
        CustomFonts = NormalizeFonts(CustomFonts),
        Shortcuts = NormalizeShortcuts(Shortcuts),
        FindHistory = (FindHistory ?? new())
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList(),
    };

    /// <summary>字体键：保留预设；"custom" 需要自定义族名，否则回落默认</summary>
    private static string NormalizeFontKey(string key)
    {
        if (key is "yahei" or "wenkai" or "smiley" or "simsun" or "kaiti" or "mono") return key;
        return key == "custom" ? "custom" : "default";
    }

    private static WindowBounds? NormalizeWindow(WindowBounds? bounds)
    {
        if (bounds is null) return null;
        if (bounds.Width < 480 || bounds.Height < 360) return null;
        return bounds with
        {
            Width = Math.Clamp(bounds.Width, 480, 5000),
            Height = Math.Clamp(bounds.Height, 360, 4000),
            X = Math.Clamp(bounds.X, -10000, 10000),
            Y = Math.Clamp(bounds.Y, -10000, 10000),
        };
    }

    private static List<CustomFont> NormalizeFonts(List<CustomFont>? fonts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<CustomFont>();
        foreach (var font in fonts ?? new())
        {
            var file = (font.File ?? "").Trim();
            var family = (font.Family ?? "").Trim();
            if (file.Length == 0 || family.Length == 0 || !seen.Add(file)) continue;
            list.Add(font with { File = file, Family = family });
            if (list.Count == 20) break;
        }
        return list;
    }

    private static Dictionary<string, string> NormalizeShortcuts(Dictionary<string, string>? shortcuts)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, gesture) in shortcuts ?? new())
        {
            if (!ShortcutCommands.IsKnown(id)) continue;
            if (ShortcutGesture.TryNormalize(gesture, out var normalized))
                result[id] = normalized;
        }
        return result;
    }

    /// <summary>去空、绝对化、忽略大小写去重、剔除不存在项、截断上限（保持输入顺序）</summary>
    public static List<string> NormalizeRecent(IEnumerable<string> paths)
        => NormalizeRecent(paths, DefaultRecentLimit);

    public static List<string> NormalizeRecent(IEnumerable<string> paths, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxRecentFiles);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string full;
            try { full = Path.GetFullPath(raw); } catch { continue; }
            if (!File.Exists(full) || !seen.Add(full)) continue;
            list.Add(full);
            if (list.Count == limit) break;
        }
        return list;
    }
}

/// <summary>已导入的字体文件；File 为绝对路径，Family 为字体族名</summary>
public sealed record CustomFont(string File, string Family);

/// <summary>记住的窗口位置与大小（逻辑像素）</summary>
public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);
