using System.Text;

namespace Maber.Core;

public readonly record struct DecodeResult(string Text, string EncodingName, bool HasBom);

/// <summary>打开的路径不是文本文件（图片、压缩包等），由壳层提示并拒绝渲染。</summary>
public sealed class BinaryFileException : Exception
{
    public BinaryFileException() : base("文件包含二进制内容") { }
}

public static class EncodingDetector
{
    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                         throwOnInvalidBytes: true);

    /// <summary>
    /// 轻量二进制判定：前 8KB 内出现 NUL，或不可打印控制字符占比超过 2%。
    /// 文本文件（含 GBK）不会命中，避免把二进制内容按 GBK 解成整页乱码。
    /// </summary>
    public static bool IsLikelyBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return false;
        var sample = bytes[..Math.Min(bytes.Length, 8192)];
        var controls = 0;
        foreach (var b in sample)
        {
            if (b == 0) return true;
            if (b < 0x09 || (b > 0x0D && b < 0x20)) controls++;
        }
        return controls * 100 > sample.Length * 2;
    }

    public static DecodeResult Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return new DecodeResult(string.Empty, "utf-8", false);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new DecodeResult(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8", true);

        // UTF-16 BOM：Windows 记事本等仍会产出，不识别就会被 GBK 强解成乱码
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new DecodeResult(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le", true);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new DecodeResult(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be", true);

        try
        {
            return new DecodeResult(StrictUtf8.GetString(bytes), "utf-8", false);
        }
        catch (DecoderFallbackException)
        {
            return new DecodeResult(Encoding.GetEncoding("GBK").GetString(bytes), "gbk", false);
        }
    }

    public static byte[] Encode(string text, string encodingName, bool hasBom)
    {
        Encoding enc = encodingName switch
        {
            "gbk" => Encoding.GetEncoding("GBK"),
            "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        var body = enc.GetBytes(text);
        if (!hasBom) return body;
        var preamble = enc is UTF8Encoding ? [0xEF, 0xBB, 0xBF] : enc.GetPreamble();
        return [.. preamble, .. body];
    }
}
