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

        Assert.Equal("light", s.Theme);
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
        Assert.Equal("light", s.Theme);
        Assert.Equal(16, s.FontSize);
    }

    [Fact]
    public void Load_LegacySystemTheme_MigratesToLight()
    {
        // v0.2.0 的设置文件没有 schema 字段，theme=system 是当时的默认值
        File.WriteAllText(_file, """{"theme":"system","fontSize":16,"recentFiles":[]}""");

        var s = new SettingsStore(_file).Load();

        Assert.Equal("light", s.Theme);
        Assert.Equal(AppSettings.SettingsSchema, s.Schema);
    }

    [Fact]
    public void Load_LegacyExplicitDark_KeepsDark()
    {
        File.WriteAllText(_file, """{"theme":"dark","fontSize":16,"recentFiles":[]}""");
        Assert.Equal("dark", new SettingsStore(_file).Load().Theme);
    }

    [Fact]
    public void Load_CurrentSchemaSystem_KeepsSystem()
    {
        File.WriteAllText(_file, """{"schema":2,"theme":"system","fontSize":16,"recentFiles":[]}""");
        Assert.Equal("system", new SettingsStore(_file).Load().Theme);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var s = new AppSettings
        {
            Theme = "pink", FontFamily = "comic", FontSize = 99,
            LineHeight = 0.5, ContentWidth = 200,
        }.Normalize();

        Assert.Equal("light", s.Theme);
        Assert.Equal("default", s.FontFamily);
        Assert.Equal(24, s.FontSize);
        Assert.Equal(1.4, s.LineHeight);
        Assert.Equal(70, s.ContentWidth);
    }

    [Theory]
    [InlineData("wenkai")]
    [InlineData("smiley")]
    public void Normalize_KeepsOpenSourceFontKeys(string key)
    {
        Assert.Equal(key, new AppSettings { FontFamily = key }.Normalize().FontFamily);
    }

    [Fact]
    public void NormalizeRecent_DedupesPrunesAndCaps()
    {
        var files = Enumerable.Range(0, 25).Select(i => Touch($"n{i}.md")).ToList();
        var input = new List<string> { files[0], files[0].ToUpperInvariant() };
        input.Add(Path.Combine(_dir, "missing.md"));
        input.AddRange(files.Skip(1));

        var list = AppSettings.NormalizeRecent(input);

        Assert.Equal(AppSettings.DefaultRecentLimit, list.Count);
        Assert.Equal(files.Take(AppSettings.DefaultRecentLimit), list);
    }

    [Fact]
    public void NormalizeRecent_HonorsCustomLimit()
    {
        var files = Enumerable.Range(0, 8).Select(i => Touch($"n{i}.md")).ToList();
        var list = AppSettings.NormalizeRecent(files, 3);
        Assert.Equal(files.Take(3), list);
    }

    [Fact]
    public void NormalizeRecent_KeepsInputOrder()
    {
        var a = Touch("a.md");
        var b = Touch("b.md");
        Assert.Equal(new[] { b, a }, AppSettings.NormalizeRecent(new[] { b, a }));
    }

    [Fact]
    public void Normalize_ClampsNewSettings()
    {
        var s = new AppSettings
        {
            SidebarWidth = 999,
            RecentLimit = 1,
            CodeFontSize = 99,
            StartupBehavior = "nope",
            ExternalChangeMode = "nope",
            Window = new WindowBounds(0, 0, 100, 100, false),
            CustomFonts = new()
            {
                new CustomFont("C:\\a.ttf", "A"),
                new CustomFont("C:\\a.ttf", "A2"),
                new CustomFont("", "B"),
            },
            Shortcuts = new()
            {
                ["open"] = "ctrl+shift+o",
                ["missing"] = "Ctrl+Q",
                ["save"] = "NotAKey",
            },
        }.Normalize();

        Assert.Equal(320, s.SidebarWidth);
        Assert.Equal(5, s.RecentLimit);
        Assert.Equal(20, s.CodeFontSize);
        Assert.Equal("empty", s.StartupBehavior);
        Assert.Equal("auto", s.ExternalChangeMode);
        Assert.Null(s.Window);
        Assert.Single(s.CustomFonts);
        Assert.Equal("Ctrl+Shift+O", s.Shortcuts["open"]);
        Assert.False(s.Shortcuts.ContainsKey("missing"));
        Assert.False(s.Shortcuts.ContainsKey("save"));
    }

    [Fact]
    public void SaveLoad_RoundTripsV3Fields()
    {
        var saved = new AppSettings
        {
            FontFamily = "custom",
            CustomFontFamily = "LXGW WenKai",
            CodeFontFamily = "Cascadia Code",
            CodeFontSize = 14,
            RenderMath = false,
            SidebarOpenByDefault = false,
            SidebarWidth = 280,
            RecentLimit = 30,
            StartupBehavior = "last",
            ExternalChangeMode = "prompt",
            RestoreReadingPosition = false,
            RememberWindowBounds = false,
            Window = new WindowBounds(100, 80, 1200, 840, true),
            Shortcuts = new() { ["quickOpen"] = "Ctrl+Shift+P" },
            FindHistory = new() { "alpha", "beta", "alpha" },
        };
        var store = new SettingsStore(_file);
        store.Save(saved);

        var loaded = store.Load();

        Assert.Equal("custom", loaded.FontFamily);
        Assert.Equal("LXGW WenKai", loaded.CustomFontFamily);
        Assert.Equal("Cascadia Code", loaded.CodeFontFamily);
        Assert.Equal(14, loaded.CodeFontSize);
        Assert.False(loaded.RenderMath);
        Assert.False(loaded.SidebarOpenByDefault);
        Assert.Equal(280, loaded.SidebarWidth);
        Assert.Equal(30, loaded.RecentLimit);
        Assert.Equal("last", loaded.StartupBehavior);
        Assert.Equal("prompt", loaded.ExternalChangeMode);
        Assert.False(loaded.RestoreReadingPosition);
        Assert.False(loaded.RememberWindowBounds);
        Assert.Equal(new WindowBounds(100, 80, 1200, 840, true), loaded.Window);
        Assert.Equal("Ctrl+Shift+P", loaded.Shortcuts["quickOpen"]);
        Assert.Equal(new[] { "alpha", "beta" }, loaded.FindHistory);
    }
}
