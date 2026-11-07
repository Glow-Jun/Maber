namespace Maber.Core;

public readonly record struct LoadedDocument(
    string Path, string Text, string EncodingName, bool HasBom);

public sealed class FileDocumentService
{
    /// <summary>以 FileShare.ReadWrite 打开：我们读的同时，别的程序可以写。</summary>
    public async Task<LoadedDocument> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 64 * 1024, useAsync: true);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms, ct);
        var (text, enc, bom) = EncodingDetector.Decode(ms.ToArray());
        return new LoadedDocument(path, text, enc, bom);
    }

    /// <summary>写临时文件后原子替换，避免写入中断导致文件损坏。</summary>
    public async Task SaveAsync(string path, string text, string encodingName, bool hasBom)
    {
        var bytes = EncodingDetector.Encode(text, encodingName, hasBom);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        await using (var fs = new FileStream(
            tmp, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024, useAsync: true))
        {
            await fs.WriteAsync(bytes);
            await fs.FlushAsync();
        }

        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (IOException)
        {
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            // 替换失败时不留临时文件；成功路径下 tmp 已被移动，此处为空操作
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }
}
