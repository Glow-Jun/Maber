using Maber.Core;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Maber.App;

public partial class App : Application
{
    public static Window? Main { get; private set; }
    private static Task<CoreWebView2Environment>? _webEnv;
    private static int _liveWindows;

    public App()
    {
        // 安装到 Program Files 后程序目录不可写；用官方环境变量把 WebView2 缓存放到用户数据目录
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER", Path.Combine(MaberPaths.LocalDirectory, "WebView2"));
        this.InitializeComponent();
    }

    /// <summary>窗口生命周期统一计数：所有窗口关闭后退出进程，避免后台残留</summary>
    public static void TrackWindow(Window window)
    {
        Interlocked.Increment(ref _liveWindows);
        window.Closed += (_, _) =>
        {
            if (Interlocked.Decrement(ref _liveWindows) == 0) Environment.Exit(0);
        };
    }

    /// <summary>所有窗口共享同一个 WebView2 环境（同一浏览器进程，后续窗口打开更快）</summary>
    public static Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
        => _webEnv ??= CoreWebView2Environment.CreateAsync().AsTask();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        this.UnhandledException += (_, e) =>
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "maber-crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] {e.Message}\n{e.Exception}\n\n");
            e.Handled = false;
        };
        // 带文件参数（关联/快捷方式）进 Slim；直接启动进 Total。
        // 直接启动且设置为“恢复上次文件”时，用最近文件作为初始文档，但仍保持 Total。
        var launchPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(p => File.Exists(p));
        var settings = SettingsHost.Current;
        var initialPath = launchPath ?? (settings.StartupBehavior == "last"
            ? settings.RecentFiles.FirstOrDefault(File.Exists)
            : null);
        Main = new MainWindow(initialPath, totalMode: launchPath is null);
        Main.Activate();
        _ = GetWebViewEnvironmentAsync(); // 预热 WebView2 环境
    }
}
