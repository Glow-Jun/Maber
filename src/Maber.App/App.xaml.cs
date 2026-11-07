using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Maber.App;

public partial class App : Application
{
    public static Window? Main { get; private set; }
    private static Task<CoreWebView2Environment>? _webEnv;

    public App() => this.InitializeComponent();

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
        Main = new MainWindow();
        Main.Activate();
        _ = GetWebViewEnvironmentAsync(); // 预热 WebView2 环境
    }
}
