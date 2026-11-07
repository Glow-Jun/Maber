using System.Text;

namespace Maber.Core;

public readonly record struct DecodeResult(string Text, string EncodingName, bool HasBom);

public static class EncodingDetector
{
    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                         throwOnInvalidBytes: true);

    public static DecodeResult Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return new DecodeResult(string.Empty, "utf-8", false);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new DecodeResult(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8", true);

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
        var enc = encodingName == "gbk"
            ? Encoding.GetEncoding("GBK")
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var body = enc.GetBytes(text);
        if (!hasBom) return body;
        var preamble = enc is UTF8Encoding ? [0xEF, 0xBB, 0xBF] : enc.GetPreamble();
        return [.. preamble, .. body];
    }
}
