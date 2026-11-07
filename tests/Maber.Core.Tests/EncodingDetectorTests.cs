using System.Text;
using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class EncodingDetectorTests
{
    [Fact]
    public void Utf8_WithBom_IsDetected()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("# 标题\n"u8.ToArray()).ToArray();
        var r = EncodingDetector.Decode(bytes);
        Assert.True(r.HasBom);
        Assert.Equal("utf-8", r.EncodingName);
        Assert.Equal("# 标题\n", r.Text);
    }

    [Fact]
    public void ValidUtf8_WithoutBom_IsUtf8()
    {
        var r = EncodingDetector.Decode("# 中文内容"u8.ToArray());
        Assert.False(r.HasBom);
        Assert.Equal("utf-8", r.EncodingName);
        Assert.Equal("# 中文内容", r.Text);
    }

    [Fact]
    public void Gbk_FallsBackToGbk()
    {
        var gbk = Encoding.GetEncoding("GBK");
        var bytes = gbk.GetBytes("# 中文内容");
        var r = EncodingDetector.Decode(bytes);
        Assert.Equal("gbk", r.EncodingName);
        Assert.Equal("# 中文内容", r.Text);
    }

    [Fact]
    public void Utf16Le_WithBom_RoundTrips()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("# 标题")).ToArray();
        var r = EncodingDetector.Decode(bytes);
        Assert.True(r.HasBom);
        Assert.Equal("utf-16le", r.EncodingName);
        Assert.Equal("# 标题", r.Text);
        Assert.Equal(bytes, EncodingDetector.Encode(r.Text, r.EncodingName, r.HasBom));
    }

    [Fact]
    public void Utf16Be_WithBom_RoundTrips()
    {
        var enc = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("内容")).ToArray();
        var r = EncodingDetector.Decode(bytes);
        Assert.True(r.HasBom);
        Assert.Equal("utf-16be", r.EncodingName);
        Assert.Equal("内容", r.Text);
        Assert.Equal(bytes, EncodingDetector.Encode(r.Text, r.EncodingName, r.HasBom));
    }

    [Fact]
    public void Empty_IsUtf8Empty()
    {
        var r = EncodingDetector.Decode(Array.Empty<byte>());
        Assert.Equal(string.Empty, r.Text);
    }
}
