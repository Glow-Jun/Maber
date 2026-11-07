using Maber.Core;

namespace Maber.App;

/// <summary>
/// 应用级设置单例：所有窗口共享同一份内存状态；修改后广播，由各窗口重新下发主题与排版。
/// </summary>
public static class SettingsHost
{
    private static readonly SettingsStore Store = new();

    public static AppSettings Current { get; private set; } = Store.Load();

    public static event EventHandler? Changed;

    public static void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current).Normalize();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Persist() => Store.Save(Current);

    /// <summary>最近文件置顶：去重、剔除失效、上限 20，立即落盘</summary>
    public static void RecordRecent(string path)
    {
        var next = new List<string> { path };
        next.AddRange(Current.RecentFiles);
        Current = Current with { RecentFiles = AppSettings.NormalizeRecent(next) };
        Persist();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>system 偏好时读系统注册表；light/dark 直接返回用户选择</summary>
    public static string ResolveTheme()
    {
        if (Current.Theme is "light" or "dark") return Current.Theme;
        var light = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int v && v == 1;
        return light ? "light" : "dark";
    }
}
