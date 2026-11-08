using System.Buffers.Binary;
using System.Text;

namespace Maber.Core;

/// <summary>
/// 从字体文件读取族名（name 表的 nameID 16 / 1），供导入本地字体时展示与 CSS 使用。
/// 支持 .ttf / .otf / .ttc（取集合中的第一个字体）。
/// </summary>
public static class FontFileReader
{
    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryReadFamilyName(string path)
    {
        try
        {
            return ReadFamilyName(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    internal static string? ReadFamilyName(byte[] data)
    {
        if (data.Length < 12) return null;

        var sfnt = 0L;
        if (Tag(data, 0) == "ttcf")
        {
            if (data.Length < 16) return null;
            var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
            if (count == 0) return null;
            sfnt = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
        }

        if (sfnt < 0 || sfnt + 12 > data.Length) return null;
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)sfnt + 4));

        long nameTable = -1;
        long nameLength = -1;
        for (var i = 0; i < numTables; i++)
        {
            var record = sfnt + 12 + i * 16;
            if (record + 16 > data.Length) break;
            if (Tag(data, (int)record) != "name") continue;
            nameTable = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)record + 8));
            nameLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)record + 12));
            break;
        }

        if (nameTable < 0 || nameLength <= 6 || nameTable + nameLength > data.Length) return null;
        return ReadNameTable(data, (int)nameTable, (int)nameLength);
    }

    private static string? ReadNameTable(byte[] data, int start, int length)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(start + 2));
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(start + 4));

        string? best = null;
        var bestScore = -1;
        for (var i = 0; i < count; i++)
        {
            var record = start + 6 + i * 12;
            if (record + 12 > start + length || record + 12 > data.Length) break;

            var platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record));
            var language = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 4));
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 6));
            if (nameId is not (1 or 16)) continue;
            if (platform is not (0 or 1 or 3)) continue;

            var bytes = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 8));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 10));
            var begin = start + stringOffset + offset;
            if (begin + bytes > start + length || begin + bytes > data.Length) continue;

            var value = platform == 1
                ? Encoding.Latin1.GetString(data, begin, bytes)
                : Encoding.BigEndianUnicode.GetString(data, begin, bytes);
            value = value.Replace("\0", "").Trim();
            if (value.Length == 0) continue;

            // 优先排版族名（16），其次 Windows 英文（0x409），再按平台兜底
            var score = (nameId == 16 ? 100 : 0)
                      + (platform == 3 ? 20 : platform == 0 ? 10 : 0)
                      + (language == 0x409 ? 5 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = value;
            }
        }
        return best;
    }

    private static string Tag(byte[] data, int offset)
        => offset + 4 <= data.Length ? Encoding.ASCII.GetString(data, offset, 4) : "";
}
