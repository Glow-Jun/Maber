using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"maber-settings-{Guid.NewGuid():N}");
    private readonly string _file;

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new SettingsStore(_file).Load();

        Assert.Equal("system", s.Theme);
        Assert.Equal("default", s.FontFamily);
        Assert.Equal(16, s.FontSize);
        Assert.Equal(1.75, s.LineHeight);
        Assert.Equal(46, s.ContentWidth);
        Assert.Empty(s.RecentFiles);
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var recent = Touch("笔记.md");
        var saved = new AppSettings
        {
            Theme = "dark", FontFamily = "mono", FontSize = 18,
            LineHeight = 1.9, ContentWidth = 52, RecentFiles = new() { recent },
        };
        var store = new SettingsStore(_file);
        store.Save(saved);

        var loaded = store.Load();

        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("mono", loaded.FontFamily);
        Assert.Equal(18, loaded.FontSize);
        Assert.Equal(1.9, loaded.LineHeight);
        Assert.Equal(52, loaded.ContentWidth);
        Assert.Equal(new[] { recent }, loaded.RecentFiles);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(_file, "{ not json");
        var s = new SettingsStore(_file).Load();
        Assert.Equal("system", s.Theme);
        Assert.Equal(16, s.FontSize);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var s = new AppSettings
        {
            Theme = "pink", FontFamily = "comic", FontSize = 99,
            LineHeight = 0.5, ContentWidth = 200,
        }.Normalize();

        Assert.Equal("system", s.Theme);
        Assert.Equal("default", s.FontFamily);
        Assert.Equal(24, s.FontSize);
        Assert.Equal(1.4, s.LineHeight);
        Assert.Equal(70, s.ContentWidth);
    }

    [Fact]
    public void NormalizeRecent_DedupesPrunesAndCaps()
    {
        var files = Enumerable.Range(0, 25).Select(i => Touch($"n{i}.md")).ToList();
        var input = new List<string> { files[0], files[0].ToUpperInvariant() };
        input.Add(Path.Combine(_dir, "missing.md"));
        input.AddRange(files.Skip(1));

        var list = AppSettings.NormalizeRecent(input);

        Assert.Equal(AppSettings.MaxRecentFiles, list.Count);
        Assert.Equal(files.Take(AppSettings.MaxRecentFiles), list);
    }

    [Fact]
    public void NormalizeRecent_KeepsInputOrder()
    {
        var a = Touch("a.md");
        var b = Touch("b.md");
        Assert.Equal(new[] { b, a }, AppSettings.NormalizeRecent(new[] { b, a }));
    }
}
