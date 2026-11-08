using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class ReadingStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"maber-reading-{Guid.NewGuid():N}");
    private readonly string _file;

    public ReadingStateStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "reading-state.json");
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(new ReadingStateStore(_file).Load());
    }

    [Fact]
    public void SaveLoad_RoundTripsAndClamps()
    {
        var path = Path.Combine(_dir, "a.md");
        var store = new ReadingStateStore(_file);
        store.Save(new Dictionary<string, ReadingMark>
        {
            [path] = new ReadingMark(1.7, 100),
        });

        var loaded = store.Load();
        Assert.Equal(1, loaded[path].Ratio);
    }

    [Fact]
    public void Save_KeepsMostRecentEntries()
    {
        var marks = Enumerable.Range(0, ReadingStateStore.MaxEntries + 20)
            .ToDictionary(i => Path.Combine(_dir, $"n{i}.md"), i => new ReadingMark(0.5, i));
        var store = new ReadingStateStore(_file);
        store.Save(marks);

        var loaded = store.Load();
        Assert.Equal(ReadingStateStore.MaxEntries, loaded.Count);
        Assert.DoesNotContain(Path.Combine(_dir, "n0.md"), loaded.Keys);
        Assert.Contains(Path.Combine(_dir, $"n{marks.Count - 1}.md"), loaded.Keys);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(_file, "{ nope");
        Assert.Empty(new ReadingStateStore(_file).Load());
    }
}
