using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class FileDocumentServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"maber-test-{Guid.NewGuid():N}");
    private readonly FileDocumentService _svc = new();

    public FileDocumentServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task Load_ReturnsText_AndKeepsFileShareable()
    {
        var path = Path.Combine(_dir, "a.md");
        await File.WriteAllTextAsync(path, "# hello");

        // 同时以 ReadWrite 共享再打开一次，模拟 Obsidian 等其他程序
        using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var doc = await _svc.LoadAsync(path);
        Assert.Equal("# hello", doc.Text);
        Assert.Equal(path, doc.Path);
    }

    [Fact]
    public async Task Save_WritesAtomically_LeavesNoTempFiles()
    {
        var path = Path.Combine(_dir, "b.md");
        await File.WriteAllTextAsync(path, "old");

        await _svc.SaveAsync(path, "new content", "utf-8", false);

        Assert.Equal("new content", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Save_PreservesBom_AndGbk()
    {
        var path = Path.Combine(_dir, "c.md");
        var gbk = System.Text.Encoding.GetEncoding("GBK");
        await File.WriteAllBytesAsync(path, gbk.GetBytes("内容"));

        var doc = await _svc.LoadAsync(path);
        Assert.Equal("gbk", doc.EncodingName);
        doc = doc with { Text = "改过的内容" };
        await _svc.SaveAsync(doc.Path, doc.Text, doc.EncodingName, doc.HasBom);

        Assert.Equal("改过的内容", gbk.GetString(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task Save_NewFile_CreatesIt()
    {
        var path = Path.Combine(_dir, "new.md");
        await _svc.SaveAsync(path, "fresh", "utf-8", false);
        Assert.Equal("fresh", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Load_BinaryFile_Throws()
    {
        var path = Path.Combine(_dir, "fake.md");
        await File.WriteAllBytesAsync(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02 });

        await Assert.ThrowsAsync<BinaryFileException>(() => _svc.LoadAsync(path));
    }
}
