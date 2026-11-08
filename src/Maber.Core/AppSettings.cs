namespace Maber.Core;

/// <summary>
/// 全局设置（跨窗口共享）。字段取值为受控枚举键，归一化后保证 UI 与渲染内核拿到合法值。
/// </summary>
public sealed record AppSettings
{
    public const int MaxRecentFiles = 20;

    /// <summary>设置结构版本：2 起默认浅色，旧版本（跟随系统）一次性迁移</summary>
    public const int SettingsSchema = 2;

    // 默认 0：反序列化旧文件时用于识别「没有 schema 字段」；Normalize 会写到当前版本
    public int Schema { get; init; }
    public string Theme { get; init; } = "light";
    public string FontFamily { get; init; } = "default";
    public double FontSize { get; init; } = 16;
    public double LineHeight { get; init; } = 1.75;
    public double ContentWidth { get; init; } = 46;
    public List<string> RecentFiles { get; init; } = new();

    /// <summary>越界值夹回范围、未知键回落默认，避免坏配置把排版打乱</summary>
    public AppSettings Normalize() => this with
    {
        Schema = SettingsSchema,
        Theme = Theme is "system" or "light" or "dark" ? Theme : "light",
        FontFamily = FontFamily is "yahei" or "simsun" or "kaiti" or "mono" ? FontFamily : "default",
        FontSize = Math.Clamp(FontSize, 12, 24),
        LineHeight = Math.Clamp(LineHeight, 1.4, 2.2),
        ContentWidth = Math.Clamp(ContentWidth, 30, 70),
        RecentFiles = NormalizeRecent(RecentFiles ?? new()),
    };

    /// <summary>去空、绝对化、忽略大小写去重、剔除不存在项、截断上限（保持输入顺序）</summary>
    public static List<string> NormalizeRecent(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string full;
            try { full = Path.GetFullPath(raw); } catch { continue; }
            if (!File.Exists(full) || !seen.Add(full)) continue;
            list.Add(full);
            if (list.Count == MaxRecentFiles) break;
        }
        return list;
    }
}
