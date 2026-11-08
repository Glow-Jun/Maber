using System.Text.Json;

namespace Maber.Core;

/// <summary>
/// 设置的 JSON 持久化。读取遇到文件缺失、JSON 损坏、字段越界一律回落默认值，不向调用方抛异常。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath { get; }

    public SettingsStore(string? path = null) => FilePath = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maber", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings().Normalize();
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            // v0.2.0 及更早没有 schema 字段，默认值是「跟随系统」；本版起默认浅色，一次性迁移
            if (loaded.Schema < AppSettings.SettingsSchema && loaded.Theme == "system")
                loaded = loaded with { Theme = "light" };
            return loaded.Normalize();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>临时文件 + 替换，避免写一半留下坏 JSON</summary>
    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".settings.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings.Normalize(), JsonOpts));
        try
        {
            File.Move(tmp, FilePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }
}
