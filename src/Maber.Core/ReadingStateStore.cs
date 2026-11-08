using System.Text.Json;

namespace Maber.Core;

/// <summary>单个文件的阅读位置：0–1 的滚动比例 + 最后更新时间</summary>
public sealed record ReadingMark(double Ratio, long UpdatedAt);

/// <summary>
/// 阅读位置状态（独立于用户设置）：按文件路径记录滚动比例，超量按更新时间淘汰。
/// </summary>
public sealed class ReadingStateStore
{
    public const int MaxEntries = 200;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath { get; }

    public ReadingStateStore(string? path = null) => FilePath = path ?? MaberPaths.ReadingStateFile;

    public Dictionary<string, ReadingMark> Load()
    {
        var result = new Dictionary<string, ReadingMark>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath)) return result;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ReadingMark>>(
                File.ReadAllText(FilePath), JsonOpts);
            if (loaded is null) return result;
            foreach (var (path, mark) in loaded)
            {
                if (string.IsNullOrWhiteSpace(path) || mark is null) continue;
                if (!double.IsFinite(mark.Ratio)) continue;
                result[path] = mark with { Ratio = Math.Clamp(mark.Ratio, 0, 1) };
            }
        }
        catch
        {
            // 状态文件损坏不影响阅读，当作没有记录
        }
        return result;
    }

    public void Save(IReadOnlyDictionary<string, ReadingMark> marks)
    {
        try
        {
            var trimmed = marks
                .Where(kv => kv.Key.Length > 0 && double.IsFinite(kv.Value.Ratio))
                .OrderByDescending(kv => kv.Value.UpdatedAt)
                .Take(MaxEntries)
                .ToDictionary(kv => kv.Key, kv => kv.Value with { Ratio = Math.Clamp(kv.Value.Ratio, 0, 1) },
                    StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(trimmed, JsonOpts));
            try
            {
                File.Move(tmp, FilePath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }
        catch
        {
            // 阅读位置是尽力而为的状态，写失败不打扰用户
        }
    }
}
