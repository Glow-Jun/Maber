using System.Text;

namespace Maber.Core;

/// <summary>
/// 组合键的规范化与校验。统一格式为「Ctrl+Alt+Shift+Key」，
/// 只接受带至少一个修饰键的组合，避免吞掉普通输入。
/// </summary>
public static class ShortcutGesture
{
    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = "Enter",
        ["Space"] = "Space",
        ["Home"] = "Home",
        ["End"] = "End",
        ["PageUp"] = "PageUp",
        ["PageDown"] = "PageDown",
        ["Left"] = "Left",
        ["Right"] = "Right",
        ["Up"] = "Up",
        ["Down"] = "Down",
        ["Insert"] = "Insert",
        ["Delete"] = "Delete",
        ["Backspace"] = "Backspace",
    };

    private const string Punctuation = ",./;'[]\\-=`";

    public static bool TryNormalize(string? raw, out string gesture)
    {
        gesture = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var ctrl = false;
        var alt = false;
        var shift = false;
        string? key = null;

        foreach (var part in raw.Split('+', StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0) return false;
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    continue;
                case "alt":
                    alt = true;
                    continue;
                case "shift":
                    shift = true;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    return false; // 保留给系统，不参与应用快捷键
            }

            if (key is not null) return false; // 出现了第二个非修饰键
            if (!TryNormalizeKey(part, out key)) return false;
        }

        if (key is null || (!ctrl && !alt && !shift)) return false;

        var sb = new StringBuilder();
        if (ctrl) sb.Append("Ctrl+");
        if (alt) sb.Append("Alt+");
        if (shift) sb.Append("Shift+");
        sb.Append(key);
        gesture = sb.ToString();
        return true;
    }

    /// <summary>拆解规范化后的组合键；失败返回全 false / 空键</summary>
    public static (bool Ctrl, bool Alt, bool Shift, string Key) Parse(string? gesture)
    {
        if (!TryNormalize(gesture, out var normalized)) return (false, false, false, "");
        var parts = normalized.Split('+');
        var key = parts[^1];
        return (parts.Contains("Ctrl"), parts.Contains("Alt"), parts.Contains("Shift"), key);
    }

    public static bool IsPressed(bool ctrl, bool alt, bool shift, string expectedGesture)
    {
        var (ec, ea, es, key) = Parse(expectedGesture);
        return ec == ctrl && ea == alt && es == shift && key.Length > 0;
    }

    private static bool TryNormalizeKey(string token, out string key)
    {
        key = "";
        if (token.Length == 1)
        {
            var c = token[0];
            if (char.IsAsciiLetter(c)) { key = char.ToUpperInvariant(c).ToString(); return true; }
            if (char.IsAsciiDigit(c)) { key = c.ToString(); return true; }
            if (Punctuation.Contains(c)) { key = c.ToString(); return true; }
            return false;
        }

        if (NamedKeys.TryGetValue(token, out var named)) { key = named; return true; }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') &&
            int.TryParse(token[1..], out var fn) && fn is >= 1 and <= 12)
        {
            key = "F" + fn;
            return true;
        }

        return false;
    }
}
