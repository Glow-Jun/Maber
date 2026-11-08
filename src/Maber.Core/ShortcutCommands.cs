namespace Maber.Core;

/// <summary>可自定义快捷键的命令清单；默认绑定同时也是用户重置的回落值</summary>
public sealed record ShortcutCommand(string Id, string Label, string DefaultGesture);

public static class ShortcutCommands
{
    public const string Open = "open";
    public const string Save = "save";
    public const string ToggleMode = "toggleMode";
    public const string Outline = "outline";
    public const string Find = "find";
    public const string Sidebar = "sidebar";
    public const string QuickOpen = "quickOpen";
    public const string Settings = "settings";

    public static readonly IReadOnlyList<ShortcutCommand> All = new[]
    {
        new ShortcutCommand(Open, "打开文件", "Ctrl+O"),
        new ShortcutCommand(Save, "保存", "Ctrl+S"),
        new ShortcutCommand(ToggleMode, "阅读 / 源码", "Ctrl+E"),
        new ShortcutCommand(Outline, "大纲", "Ctrl+K"),
        new ShortcutCommand(Find, "页内查找", "Ctrl+F"),
        new ShortcutCommand(Sidebar, "显示 / 隐藏侧栏", "Ctrl+B"),
        new ShortcutCommand(QuickOpen, "快速打开", "Ctrl+P"),
        new ShortcutCommand(Settings, "设置", "Ctrl+,"),
    };

    public static bool IsKnown(string? id)
        => All.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ShortcutCommand? ById(string id)
        => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>用户覆盖优先；没有或非法时回落默认绑定</summary>
    public static string Resolve(IReadOnlyDictionary<string, string>? overrides, string id)
    {
        if (overrides is not null && overrides.TryGetValue(id, out var custom) &&
            ShortcutGesture.TryNormalize(custom, out var normalized))
            return normalized;
        return ById(id)?.DefaultGesture ?? "";
    }

    /// <summary>默认绑定 + 覆盖，得到命令 id → 组合键的完整表</summary>
    public static Dictionary<string, string> ResolveAll(IReadOnlyDictionary<string, string>? overrides)
        => All.ToDictionary(c => c.Id, c => Resolve(overrides, c.Id), StringComparer.OrdinalIgnoreCase);
}
