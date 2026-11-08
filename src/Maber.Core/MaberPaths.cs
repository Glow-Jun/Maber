namespace Maber.Core;

/// <summary>应用数据目录约定，壳与 Core 共用</summary>
public static class MaberPaths
{
    public static string LocalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maber");

    public static string SettingsFile => Path.Combine(LocalDirectory, "settings.json");

    public static string ReadingStateFile => Path.Combine(LocalDirectory, "reading-state.json");

    public static string FontsDirectory => Path.Combine(LocalDirectory, "fonts");
}
