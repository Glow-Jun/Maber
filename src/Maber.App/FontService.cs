using System.Security.Cryptography;
using Maber.Core;

namespace Maber.App;

/// <summary>本地字体的导入、移除与 Web 访问地址</summary>
public static class FontService
{
    public const string Host = "fonts.localhost";

    public static void EnsureDirectory() => Directory.CreateDirectory(MaberPaths.FontsDirectory);

    /// <summary>导入字体文件：复制到应用数据目录，返回带族名的记录</summary>
    public static (CustomFont? Font, string? Error) Import(string sourcePath)
    {
        if (!FontFileReader.IsSupported(sourcePath))
            return (null, "只支持 .ttf / .otf / .ttc 字体文件");

        var family = FontFileReader.TryReadFamilyName(sourcePath);
        if (string.IsNullOrWhiteSpace(family))
            return (null, "无法读取字体信息，文件可能已损坏");

        try
        {
            EnsureDirectory();
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)))[..16].ToLowerInvariant();
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            var target = Path.Combine(MaberPaths.FontsDirectory, hash + ext);
            if (!File.Exists(target)) File.Copy(sourcePath, target);
            return (new CustomFont(target, family.Trim()), null);
        }
        catch (Exception ex)
        {
            return (null, $"导入失败：{ex.Message}");
        }
    }

    public static void Remove(CustomFont font)
    {
        try
        {
            if (File.Exists(font.File)) File.Delete(font.File);
        }
        catch
        {
            // 文件被占用时保留，下次启动 SettingsHost 会剔除失效项
        }
    }

    public static string UrlFor(CustomFont font)
        => $"http://{Host}/{Uri.EscapeDataString(Path.GetFileName(font.File))}";
}
