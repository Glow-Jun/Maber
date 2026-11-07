using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class ExternalFileWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"maber-w-{Guid.NewGuid():N}");
    public ExternalFileWatcherTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static Task<bool> WaitAsync(TaskCompletionSource tcs, int ms)
    {
        var done = Task.WhenAny(tcs.Task, Task.Delay(ms));
        return done.ContinueWith(_ => tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task External_Change_FiresEvent()
    {
        var path = Path.Combine(_dir, "note.md");
        await File.WriteAllTextAsync(path, "v1");
        using var w = new ExternalFileWatcher { PollIntervalMs = 50 };
        var tcs = new TaskCompletionSource();
        w.ExternalChanged += (_, _) => tcs.TrySetResult();
        w.Attach(path);

        await File.WriteAllTextAsync(path, "v2 external");
        Assert.True(await WaitAsync(tcs, 5000), "事件未在 5 秒内触发");
    }

    [Fact]
    public async Task External_Delete_FiresEvent()
    {
        var path = Path.Combine(_dir, "gone.md");
        await File.WriteAllTextAsync(path, "x");
        using var w = new ExternalFileWatcher { PollIntervalMs = 50 };
        var tcs = new TaskCompletionSource();
        w.ExternalDeleted += (_, _) => tcs.TrySetResult();
        w.Attach(path);

        File.Delete(path);
        Assert.True(await WaitAsync(tcs, 5000), "删除事件未触发");
    }

    [Fact]
    public void Detach_StopsWatching()
    {
        var path = Path.Combine(_dir, "quiet.md");
        File.WriteAllText(path, "v1");
        var w = new ExternalFileWatcher { PollIntervalMs = 50 };
        var fired = false;
        w.ExternalChanged += (_, _) => fired = true;
        w.Attach(path);
        w.Detach();
        File.WriteAllText(path, "v2");
        Thread.Sleep(300);
        Assert.False(fired);
    }
}
