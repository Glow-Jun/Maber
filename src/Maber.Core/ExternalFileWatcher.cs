using System.IO;

namespace Maber.Core;

/// <summary>
/// 监视单个文件的外部修改。FSW 为主、按 LastWriteTimeUtc+Length 轮询兜底
/// （网盘/网络路径上 FSW 不可靠）。自己的写入由调用方比对内容后忽略。
/// </summary>
public sealed class ExternalFileWatcher : IDisposable
{
    public int PollIntervalMs { get; init; } = 2000;

    private FileSystemWatcher? _fsw;
    private Timer? _poll;
    private string? _path;
    private (DateTime Utc, long Len) _last;
    private bool _suspended;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler? ExternalChanged;
    public event EventHandler? ExternalDeleted;

    public void Attach(string path)
    {
        Detach();
        _path = Path.GetFullPath(path);
        _last = Stat(_path);

        var dir = Path.GetDirectoryName(_path)!;
        var name = Path.GetFileName(_path);
        _fsw = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _fsw.Changed += OnFswEvent;
        _fsw.Renamed += OnFswEvent;
        _fsw.EnableRaisingEvents = true;

        _poll = new Timer(_ => _ = PollAsync(), null, PollIntervalMs, PollIntervalMs);
    }

    /// <summary>自己保存前调用，避免把自写当外部修改。</summary>
    public void SuspendOnce()
    {
        _suspended = true;
        if (_path is not null) _last = Stat(_path);
    }

    public void Detach()
    {
        _fsw?.Dispose(); _fsw = null;
        _poll?.Dispose(); _poll = null;
        _path = null;
    }

    private void OnFswEvent(object s, FileSystemEventArgs e) => _ = PollAsync();

    private async Task PollAsync()
    {
        if (_path is null || _suspended) return;
        await _gate.WaitAsync();
        try
        {
            if (_path is null || _suspended) return;
            var now = Stat(_path);
            if (now == _last) return;

            if (now.Utc == default)
            {
                _last = now;
                ExternalDeleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            _last = now;
            ExternalChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            // 短暂忽略 FSW 的连发（编辑器常触发多次 Changed）
            await Task.Delay(100);
            if (_path is not null) _last = Stat(_path);
            _suspended = false;
            _gate.Release();
        }
    }

    private static (DateTime Utc, long Len) Stat(string path)
        => File.Exists(path) ? (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length) : (default, -1);

    public void Dispose() => Detach();
}
