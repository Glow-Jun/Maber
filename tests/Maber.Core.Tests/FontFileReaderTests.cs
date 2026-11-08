using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class FontFileReaderTests
{
    [Fact]
    public void TryReadFamilyName_ReadsInstalledWindowsFont()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(path)) return; // 非 Windows 环境跳过

        var family = FontFileReader.TryReadFamilyName(path);

        Assert.False(string.IsNullOrWhiteSpace(family));
        Assert.Contains("Arial", family, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadFamilyName_ReturnsNullForGarbage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maber-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        try
        {
            Assert.Null(FontFileReader.TryReadFamilyName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("a.ttf", true)]
    [InlineData("a.OTF", true)]
    [InlineData("a.ttc", true)]
    [InlineData("a.woff", false)]
    [InlineData("a.md", false)]
    public void IsSupported_ChecksExtension(string file, bool expected)
        => Assert.Equal(expected, FontFileReader.IsSupported(file));
}
