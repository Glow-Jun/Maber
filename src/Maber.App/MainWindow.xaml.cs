using System.Text.Json;
using System.Text.RegularExpressions;
using Maber.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Maber.App;

public sealed partial class MainWindow : Window
{
    private readonly FileDocumentService _files = new();
    private readonly ExternalFileWatcher _watcher = new();

    private LoadedDocument? _doc;          // 当前文件（含编码信息）
    private string _currentText = "";      // 壳侧权威源文本
    private bool _dirty;                   // 相对磁盘的内容
    private bool _sourceMode;
    private bool _ready;                   // Web 内核已可收消息
    private List<(int level, string text, int line)> _outline = new();
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly Regex TaskMarker = new(@"\[( |x|X)\]");

    // Web 端 onHostMessage 监听器注册后才会处理消息；就绪前发的 loadDocument 会丢，
    // 因此用 ready/NavigationCompleted 双信号握手，等待后再发首条文档。
    private readonly TaskCompletionSource _webReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly bool _totalMode;

    public MainWindow(string? initialPath = null, bool totalMode = false)
    {
        _totalMode = totalMode;
        Perf($"window:ctor {(initialPath is null ? "main" : $"wikilink {Path.GetFileName(initialPath)}")} total={totalMode}");
        InitializeComponent();
        Title = "Maber";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ModeBadge.Text = "阅读";
        ApplyChromeTheme();
        if (_totalMode)
        {
            SidebarToggle.Visibility = Visibility.Visible;
            Sidebar.Visibility = Visibility.Visible;
            EmptyState.Subtitle = "从左侧最近文件选择，或按 Ctrl+O 打开";
        }

        // 所有窗口都关掉后退出进程，避免无窗口的后台残留
        Interlocked.Increment(ref LiveWindows);
        Closed += (_, _) =>
        {
            SettingsHost.Changed -= OnSettingsChanged;
            _watcher.Dispose();
            if (Interlocked.Decrement(ref LiveWindows) == 0) Environment.Exit(0);
        };
        SettingsHost.Changed += OnSettingsChanged;

        Web.WebMessageReceived += OnWebMessage;
        Web.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                Perf($"web:navigation-failed {e.WebErrorStatus}");
                ShowBar($"渲染内核加载失败（{e.WebErrorStatus}）");
                _webReady.TrySetResult(); // 放行初始化流程，避免 await 永久挂起
                return;
            }
            _ready = true;
            _webReady.TrySetResult();
            Perf("web:navigation-completed");
            SendTheme();
            SendTypography();
        };
        // 关闭查找框时清掉页内高亮
        FindPopup.Closed += (_, _) =>
        {
            if (_ready) Post(new { type = BridgeMessages.Find, query = "", direction = 1 });
        };
        _watcher.ExternalChanged += (_, _) => DispatcherQueue.TryEnqueue(OnExternalChanged);
        _watcher.ExternalDeleted += (_, _) => DispatcherQueue.TryEnqueue(() => ShowBar("文件已被外部删除"));
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) SendTheme(); };

        // XAML 侧快捷键只在焦点落在 XAML 元素（如查找框）时生效；
        // 焦点在 WebView2 内时由内核捕获按键后经 "accelerator" 消息分发到同一张表
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden; // 关掉悬停弹 "Ctrl+O" tooltip
        _accelerators = new Dictionary<string, Action>
        {
            ["o"] = () => _ = PickOpenFileAsync(),
            ["s"] = () => _ = SaveAsync(),
            ["e"] = ToggleMode,
            ["k"] = ShowOutline,
            ["f"] = ShowFind,
            ["b"] = ToggleSidebar,
        };
        foreach (var (key, action) in _accelerators)
            AddAccelerator((VirtualKey)char.ToUpper(key[0]), action);

        var path = initialPath ?? Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(p => File.Exists(p));
        // 窗口首次激活（可见）后再初始化 WebView2：不可见时创建控制器是已知慢路径（可达数秒）
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated || _initStarted) return;
            _initStarted = true;
            Perf("window:activated");
            SetDefaultWindowSize();
            _ = InitializeAsync(path);
        };
    }

    private static int LiveWindows;
    private bool _initStarted;

    // ---------- 性能日志（Debug 构建默认开；Release 需设 MABER_PERF=1）----------
    private static readonly bool PerfEnabled =
#if DEBUG
        true;
#else
        Environment.GetEnvironmentVariable("MABER_PERF") == "1";
#endif
    private static readonly object PerfLock = new();
    private static void Perf(string msg)
    {
        if (!PerfEnabled) return;
        try
        {
            lock (PerfLock)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "maber-perf.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] T{Environment.CurrentManagedThreadId} {msg}\n");
        }
        catch { /* 日志失败不影响功能 */ }
    }

    /// <summary>
    /// Slim：竖版 4:5，宽基准 720 逻辑像素（高 = 宽 × 5/4 = 900）；
    /// Total：1200×840 的宽版阅读台。两者都夹进显示器工作区。
    /// </summary>
    private void SetDefaultWindowSize()
    {
        try
        {
            var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale < 1) scale = 1;
            var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            double wLogical, hLogical;
            if (_totalMode)
            {
                wLogical = Math.Min(1200, work.Width / scale);
                hLogical = Math.Min(840, work.Height / scale);
            }
            else
            {
                const double ratio = 5.0 / 4.0;              // 高/宽
                wLogical = Math.Min(720.0, work.Width / scale);
                hLogical = Math.Min(wLogical * ratio, work.Height / scale);
                wLogical = hLogical / ratio;                  // 高度被夹矮时收宽度，保住 4:5
            }
            var w = (int)(wLogical * scale);
            var h = (int)(hLogical * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            Perf($"window:size {w}x{h} scale={scale:0.##} workArea={work.Width}x{work.Height}");
        }
        catch { /* 尺寸设置失败不影响功能 */ }
    }

    private Dictionary<string, Action> _accelerators = new();

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var acc = new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = key };
        acc.Invoked += (_, args) => { args.Handled = true; action(); };
        // 挂在根 Grid 上，XAML 焦点范围内任意位置均可触发
        ((Grid)Content).KeyboardAccelerators.Add(acc);
    }

    private async Task InitializeAsync(string? path)
    {
        try
        {
            await InitializeCoreAsync(path);
        }
        catch (Exception ex)
        {
            Perf($"init:exception {ex.GetType().Name} {ex.Message}");
            ShowBar($"初始化失败：{ex.Message}");
        }
    }

    private async Task InitializeCoreAsync(string? path)
    {
        Perf("init:start");
        _env = await App.GetWebViewEnvironmentAsync();
        await Web.EnsureCoreWebView2Async(_env);
        Perf("init:controller-ready");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.localhost", Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        Perf("init:mapped");
        // 文档内相对资源（图片等）：WebResourceRequested 自伺服。
        // 不用 SetVirtualHostNameToFolderMapping——实测首次导航后注册映射不生效
        Web.CoreWebView2.AddWebResourceRequestedFilter(
            $"http://{ContentHost}/*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All); // 必须带 /* 通配
        Web.CoreWebView2.WebResourceRequested += OnContentResourceRequested;
        Perf("init:filter-registered");
        // 关掉浏览器内置快捷键（Ctrl+F 呼出页内查找、Ctrl+O 呼出打开文件等），统一走我们的分发
        Web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        Perf("init:navigate");
        // 主机名用 .localhost 而非 .local：Chromium 会把 .local 走 mDNS 广播解析，等 ~2s 超时才落到虚拟主机映射；
        // .localhost 由浏览器规范保证即时回环解析
        Web.CoreWebView2.Navigate("http://app.localhost/index.html");
        await _webReady.Task; // 等 Web 监听器就绪，避免首条 loadDocument 丢失
        Perf("init:web-ready");
        if (!_ready) return;  // 导航失败时页面是错误页，继续加载文档没有意义
        if (path is null) { EmptyState.IsOpen = true; return; }
        await LoadFileAsync(path);
        Perf("init:document-loaded");
    }

    // ---------- 文件 ----------
    private async Task PickOpenFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in new[] { ".md", ".markdown", ".txt" }) picker.FileTypeFilter.Add(ext);
        try
        {
            var file = await picker.PickSingleFileAsync();
            if (file is not null && await ConfirmDiscardAsync()) await LoadFileAsync(file.Path);
        }
        catch (Exception ex)
        {
            Perf($"open:error {ex.GetType().Name} {ex.Message}");
            ShowBar($"打开文件失败：{ex.Message}");
        }
    }

    private async Task LoadFileAsync(string path)
    {
        EmptyState.IsOpen = false; // 空状态提示（Ctrl+O 教程卡）打开文件后即收起
        _doc = await _files.LoadAsync(path);
        _currentText = _doc.Value.Text;
        _dirty = false;
        SettingsHost.RecordRecent(_doc.Value.Path);
        _outline = new();          // 新大纲到达前不要展示上一个文件的
        if (_totalMode) RefreshSidebarOutline();
        _watcher.Attach(path);
        TitleText.Text = Path.GetFileName(path);
        // 文档内相对资源的伺服根目录（WebResourceRequested 处理器读取）
        _contentDir = Path.GetDirectoryName(Path.GetFullPath(path));
        PostDocument();
    }

    /// <summary>统一的文档下发：所有路径都带 contentBase，并把两侧模式状态拉回阅读态</summary>
    private void PostDocument()
    {
        if (_doc is null) return;
        _sourceMode = false;
        ModeBadge.Text = "阅读";
        Post(new
        {
            type = BridgeMessages.LoadDocument,
            name = Path.GetFileName(_doc.Value.Path),
            content = _currentText,
            contentBase = $"http://{ContentHost}/",
        });
    }

    private const string ContentHost = "md.localhost";
    private Microsoft.Web.WebView2.Core.CoreWebView2Environment? _env;
    private string? _contentDir;

    private static readonly Dictionary<string, string> MimeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
        [".bmp"] = "image/bmp", [".avif"] = "image/avif",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mp3"] = "audio/mpeg",
        [".txt"] = "text/plain", [".json"] = "application/json", [".md"] = "text/markdown",
    };

    /// <summary>伺服当前文档目录下的相对资源；路径规范化并限制在目录内</summary>
    private async void OnContentResourceRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral(); // 处理器内有异步 IO，挂起完成直到响应就绪
        try
        {
            var env = _env;
            var dir = _contentDir; // 快照：切换文档后，旧文档的在途请求不要解析到新目录
            if (env is null || dir is null)
            {
                e.Response = env?.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var uri = new Uri(e.Request.Uri);
            var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            var full = Path.GetFullPath(Path.Combine(dir, rel));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                Perf($"res:404 {rel}");
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            if (new FileInfo(full).Length > MaxResourceBytes)
            {
                Perf($"res:413 {rel}");
                e.Response = env.CreateWebResourceResponse(null, 413, "Payload Too Large", "");
                return;
            }
            var mime = MimeByExt.TryGetValue(Path.GetExtension(full), out var m) ? m : "application/octet-stream";
            var bytes = await File.ReadAllBytesAsync(full);
            var stream = new MemoryStream(bytes).AsRandomAccessStream(); // WinRT 响应要求 IRandomAccessStream
            e.Response = env.CreateWebResourceResponse(stream, 200, "OK",
                $"Content-Type: {mime}\r\nCache-Control: no-store");
        }
        catch (Exception ex)
        {
            Perf($"res:error {ex.Message}");
            try { e.Response = _env?.CreateWebResourceResponse(null, 500, "Internal Error", ""); } catch { /* ignore */ }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private const long MaxResourceBytes = 64 * 1024 * 1024; // 单文件资源上限，避免整读超大文件顶高内存

    private async Task SaveAsync()
    {
        if (_doc is null) return;
        try
        {
            _watcher.SuspendOnce();
            await _files.SaveAsync(_doc.Value.Path, _currentText, _doc.Value.EncodingName, _doc.Value.HasBom);
            _dirty = false;
        }
        catch (Exception ex)
        {
            Perf($"save:error {ex.GetType().Name} {ex.Message}");
            ShowBar($"保存失败：{ex.Message}");
        }
    }

    // ---------- wikilink 跳转 ----------
    // 在新窗口打开链接目标；解析支持绝对路径与相对路径
    private void OpenWikilink(string target)
    {
        if (_doc is null || string.IsNullOrWhiteSpace(target)) return;
        var resolved = ResolveWikilink(_doc.Value.Path, target);
        if (resolved is null) { ShowBar($"未找到链接目标「{target}」"); return; }
        Perf($"wikilink:clicked target={Path.GetFileName(resolved)}");
        var window = new MainWindow(resolved);
        Perf("wikilink:ctor-done");
        window.Activate();
        Perf("wikilink:activate-called");
    }

    /// <summary>标准 Markdown 相对链接（[text](docs/xx.md)）：剥掉 # 锚点后相对当前文件解析，新窗口打开</summary>
    private void OpenLocalMarkdownLink(string href)
    {
        if (_doc is null || string.IsNullOrWhiteSpace(href)) return;
        var clean = href.Split('#')[0].Trim();
        if (clean.Length == 0) return;
        if (!clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            !clean.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            ShowBar($"暂不支持打开「{href}」"); // 非 md 的本地文件（如 pdf）v0.1 不处理
            return;
        }
        try
        {
            var full = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(_doc.Value.Path)!, clean.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full)) { ShowBar($"未找到链接目标「{href}」"); return; }
            var window = new MainWindow(full);
            window.Activate();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            ShowBar($"链接无法解析「{href}」");
        }
    }

    private static string? ResolveWikilink(string currentPath, string target)
    {
        var rel = target.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string[] candidates = [rel, rel + ".md", rel + ".markdown"];
        if (Path.IsPathRooted(rel)) return candidates.FirstOrDefault(File.Exists);

        // 相对路径：先当前目录，再逐级向上（覆盖 Obsidian 风格的 vault 相对引用）
        var dir = Path.GetDirectoryName(currentPath);
        for (var depth = 0; depth < 6 && dir is not null; depth++)
        {
            foreach (var name in candidates)
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private async void OnExternalChanged()
    {
        if (_doc is null) return;
        try
        {
            // 竞态兜底：读最新内容；与我们手里的权威文本一致则是自写回声
            var fresh = await _files.LoadAsync(_doc.Value.Path);
            if (fresh.Text == _currentText) return;
            if (!_dirty)
            {
                _doc = fresh; _currentText = fresh.Text;
                PostDocument();
            }
            else
            {
                ShowBar("文件已被外部修改，你有未保存的更改");
            }
        }
        catch (Exception ex)
        {
            // async void：任何漏出的异常都会掀掉进程
            Perf($"external:error {ex.GetType().Name} {ex.Message}");
            ShowBar("读取外部修改失败，文件可能已被移动或删除");
        }
    }

    private async void OnReloadExternal(object s, RoutedEventArgs e)
    {
        ExternalBar.IsOpen = false;
        if (_doc is null) return;
        try
        {
            _doc = await _files.LoadAsync(_doc.Value.Path);
            _dirty = false;
            _currentText = _doc.Value.Text;
            PostDocument();
        }
        catch (Exception ex)
        {
            Perf($"reload:error {ex.GetType().Name} {ex.Message}");
            ShowBar("重新加载失败，文件可能已被删除");
        }
    }

    private void ShowBar(string msg)
    {
        ExternalBar.Title = msg;
        ExternalBar.IsOpen = true;
    }

    // ---------- 桥接 ----------
    private void OnWebMessage(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            var type = json.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case BridgeMessages.Ready:
                    Perf("web:main-ready"); // 内核 JS 真正跑起来的标志（区别于错误页也会触发的 NavigationCompleted）
                    _ready = true;
                    _webReady.TrySetResult();
                    SendTheme();
                    SendTypography();
                    break;
                case BridgeMessages.Accelerator:
                    if (_accelerators.TryGetValue(json.RootElement.GetProperty("key").GetString() ?? "", out var action))
                        action();
                    break;
                case BridgeMessages.CheckboxToggled:
                    ToggleTaskLine(json.RootElement.GetProperty("line").GetInt32(), json.RootElement.GetProperty("checked").GetBoolean());
                    _ = SaveAsync();
                    break;
                case BridgeMessages.ContentChanged:
                    _currentText = json.RootElement.GetProperty("content").GetString() ?? _currentText;
                    _dirty = true;
                    break;
                case BridgeMessages.OutlineChanged:
                    _outline = json.RootElement.GetProperty("items").EnumerateArray()
                        .Select(i => (i.GetProperty("level").GetInt32(), i.GetProperty("text").GetString() ?? "", i.GetProperty("line").GetInt32()))
                        .ToList();
                    if (_totalMode) RefreshSidebarOutline();
                    break;
                case BridgeMessages.OpenLink:
                    var href = json.RootElement.GetProperty("href").GetString() ?? "";
                    if (href.StartsWith("http://") || href.StartsWith("https://"))
                        _ = Launcher.LaunchUriAsync(new Uri(href));
                    else if (!href.StartsWith("#"))
                        OpenLocalMarkdownLink(href);
                    break;
                case BridgeMessages.OpenWikilink:
                    OpenWikilink(json.RootElement.GetProperty("target").GetString() ?? "");
                    break;
                case BridgeMessages.Diag:
                    Perf($"web:{json.RootElement.GetProperty("text").GetString()}");
                    break;
            }
        }
        catch (Exception ex)
        {
            // 协议消息畸形或内核脚本异常都不应掀掉窗口
            Perf($"bridge:error {ex.GetType().Name} {ex.Message}");
        }
    }

    private void Post(object msg) => Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg, JsonOpts));

    private void ToggleTaskLine(int line, bool checkedNow)
    {
        var lines = new List<string>(_currentText.Split('\n'));
        if (line < 0 || line >= lines.Count) return;
        lines[line] = TaskMarker.Replace(lines[line], checkedNow ? "[x]" : "[ ]", 1);
        _currentText = string.Join("\n", lines);
        _dirty = true;
    }

    // ---------- 主题与设置 ----------
    /// <summary>WinUI 侧（标题栏/弹层/对话框）跟随设置；system 交给系统默认</summary>
    private void ApplyChromeTheme()
    {
        if (Content is not FrameworkElement root) return;
        root.RequestedTheme = SettingsHost.Current.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void OnSettingsChanged(object? s, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyChromeTheme();
        SendTheme();
        SendTypography();
        RefreshRecentList();
    });

    private void SendTheme()
    {
        if (!_ready) return;
        Post(new { type = BridgeMessages.SetTheme, theme = SettingsHost.ResolveTheme() });
    }

    private void SendTypography()
    {
        if (!_ready) return;
        var s = SettingsHost.Current;
        Post(new
        {
            type = BridgeMessages.SetTypography,
            fontFamily = s.FontFamily,
            fontSize = s.FontSize,
            lineHeight = s.LineHeight,
            contentWidth = s.ContentWidth,
        });
    }

    private bool _suppressSettingsEvents;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;

    private async void OnOpenSettings(object s, RoutedEventArgs e) => await ShowSettingsAsync();

    /// <summary>Total Mode 的完整设置：主题 + 排版，改动即时广播到所有窗口</summary>
    private async Task ShowSettingsAsync()
    {
        if (!_totalMode) return;

        var themeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        themeBox.Items.Add(new ComboBoxItem { Content = "跟随系统", Tag = "system" });
        themeBox.Items.Add(new ComboBoxItem { Content = "浅色", Tag = "light" });
        themeBox.Items.Add(new ComboBoxItem { Content = "深色", Tag = "dark" });

        var fontBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        fontBox.Items.Add(new ComboBoxItem { Content = "系统默认", Tag = "default" });
        fontBox.Items.Add(new ComboBoxItem { Content = "微软雅黑", Tag = "yahei" });
        fontBox.Items.Add(new ComboBoxItem { Content = "宋体", Tag = "simsun" });
        fontBox.Items.Add(new ComboBoxItem { Content = "楷体", Tag = "kaiti" });
        fontBox.Items.Add(new ComboBoxItem { Content = "等宽（Cascadia / Consolas）", Tag = "mono" });

        var sizeSlider = new Slider { Minimum = 12, Maximum = 24, StepFrequency = 1 };
        var sizeValue = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var lhSlider = new Slider { Minimum = 1.4, Maximum = 2.2, StepFrequency = 0.05 };
        var lhValue = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var widthSlider = new Slider { Minimum = 30, Maximum = 70, StepFrequency = 1 };
        var widthValue = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };

        void SyncFromSettings()
        {
            _suppressSettingsEvents = true;
            var s = SettingsHost.Current;
            themeBox.SelectedIndex = s.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
            fontBox.SelectedIndex = s.FontFamily switch { "yahei" => 1, "simsun" => 2, "kaiti" => 3, "mono" => 4, _ => 0 };
            sizeSlider.Value = s.FontSize;
            sizeValue.Text = $"{s.FontSize:0} px";
            lhSlider.Value = s.LineHeight;
            lhValue.Text = $"{s.LineHeight:0.00}";
            widthSlider.Value = s.ContentWidth;
            widthValue.Text = $"{s.ContentWidth:0} rem";
            _suppressSettingsEvents = false;
        }

        void Commit(Func<AppSettings, AppSettings> change)
        {
            if (_suppressSettingsEvents) return;
            SettingsHost.Update(change);
            DebounceSave();
        }

        themeBox.SelectionChanged += (_, _) => Commit(s =>
            s with { Theme = (themeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "system" });
        fontBox.SelectionChanged += (_, _) => Commit(s =>
            s with { FontFamily = (fontBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "default" });
        sizeSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue);
            sizeValue.Text = $"{v:0} px";
            Commit(s => s with { FontSize = v });
        };
        lhSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue, 2);
            lhValue.Text = $"{v:0.00}";
            Commit(s => s with { LineHeight = v });
        };
        widthSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue);
            widthValue.Text = $"{v:0} rem";
            Commit(s => s with { ContentWidth = v });
        };

        SyncFromSettings();

        var panel = new StackPanel { Spacing = 16, MinWidth = 340 };
        panel.Children.Add(LabeledRow("主题", themeBox));
        panel.Children.Add(LabeledRow("字体", fontBox));
        panel.Children.Add(SliderRow("字号", sizeSlider, sizeValue));
        panel.Children.Add(SliderRow("行高", lhSlider, lhValue));
        panel.Children.Add(SliderRow("阅读宽度", widthSlider, widthValue));

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "设置",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "完成",
            SecondaryButtonText = "恢复默认",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true; // 恢复默认后留在对话框，方便继续调整
            SettingsHost.Update(_ => new AppSettings());
            SettingsHost.Persist();
            SyncFromSettings();
        };
        await dialog.ShowAsync();
        SettingsHost.Persist(); // 去抖可能还没触发，关闭时兜底落盘
    }

    private static StackPanel LabeledRow(string label, FrameworkElement control)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 });
        row.Children.Add(control);
        return row;
    }

    private static StackPanel SliderRow(string label, Slider slider, TextBlock value)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 });
        head.Children.Add(value);
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(head);
        row.Children.Add(slider);
        return row;
    }

    private void DebounceSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SettingsHost.Persist();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------- 侧栏 ----------
    private void OnToggleSidebar(object s, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        if (!_totalMode) return;
        Sidebar.Visibility = Sidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- 快捷键 ----------
    private void ToggleMode()
    {
        if (!_ready || _doc is null) return;
        _sourceMode = !_sourceMode;
        ModeBadge.Text = _sourceMode ? "源码" : "阅读";
        Post(new { type = BridgeMessages.SetMode, mode = _sourceMode ? "source" : "render" });
    }

    // ---------- 大纲 ----------
    private void OutlineList_ItemClick(object s, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TextBlock tb && tb.Tag is int line)
            Post(new { type = BridgeMessages.ScrollToHeading, line });
        OutlinePopup.IsOpen = false;
    }

    private void SidebarOutline_ItemClick(object s, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TextBlock tb && tb.Tag is int line)
            Post(new { type = BridgeMessages.ScrollToHeading, line });
    }

    private void ShowOutline()
    {
        if (!_ready || _doc is null) return;
        if (_totalMode)
        {
            Sidebar.Visibility = Visibility.Visible;
            SelectSidebarTab(TabOutline);
            RefreshSidebarOutline();
            return;
        }
        FillOutline(OutlineList, _outline, 14);
        OutlinePopup.HorizontalOffset = Math.Max(0, Root.ActualWidth - 300);
        OutlinePopup.VerticalOffset = 44;
        OutlinePopup.IsOpen = true;
    }

    private void RefreshSidebarOutline()
    {
        if (!_totalMode) return;
        FillOutline(SidebarOutline, _outline, 12);
        if (SidebarOutline.Items.Count == 0)
            SidebarOutline.Items.Add(new TextBlock { Text = "当前文档暂无标题", Opacity = 0.6, Margin = new Thickness(8) });
    }

    private static void FillOutline(ListView list, IEnumerable<(int level, string text, int line)> items, double indentStep)
    {
        list.Items.Clear();
        foreach (var (level, text, line) in items)
        {
            var tb = new TextBlock { Text = text, Tag = line, TextTrimming = TextTrimming.CharacterEllipsis };
            tb.Margin = new Thickness((level - 1) * indentStep, 4, 0, 4);
            list.Items.Add(tb);
        }
    }

    // ---------- 侧栏分区 ----------
    private void OnSidebarTab(object sender, RoutedEventArgs e)
    {
        var tab = (ToggleButton)sender;
        SelectSidebarTab(tab);
        if (tab == TabRecent) RefreshRecentList();
    }

    private void SelectSidebarTab(ToggleButton tab)
    {
        TabOutline.IsChecked = ReferenceEquals(tab, TabOutline);
        TabRecent.IsChecked = ReferenceEquals(tab, TabRecent);
        TabCheat.IsChecked = ReferenceEquals(tab, TabCheat);
        SidebarOutline.Visibility = tab == TabOutline ? Visibility.Visible : Visibility.Collapsed;
        SidebarRecent.Visibility = tab == TabRecent ? Visibility.Visible : Visibility.Collapsed;
        CheatScroll.Visibility = tab == TabCheat ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshRecentList()
    {
        if (!_totalMode) return;
        SidebarRecent.Items.Clear();
        var files = SettingsHost.Current.RecentFiles;
        if (files.Count == 0)
        {
            SidebarRecent.Items.Add(new TextBlock { Text = "暂无最近文件", Opacity = 0.6, Margin = new Thickness(8) });
            return;
        }
        foreach (var path in files)
        {
            var stack = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(new TextBlock
            {
                Text = Path.GetDirectoryName(path),
                FontSize = 11,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Tag = path;
            SidebarRecent.Items.Add(stack);
        }
    }

    private async void SidebarRecent_ItemClick(object s, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not StackPanel item || item.Tag is not string path) return;
        if (!File.Exists(path))
        {
            ShowBar("文件已不存在，已从最近列表移除");
            SettingsHost.Update(s => s with { RecentFiles = AppSettings.NormalizeRecent(s.RecentFiles) });
            SettingsHost.Persist();
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        await LoadFileAsync(path);
    }

    /// <summary>切换文件前保护未保存修改（当前文件可能被外部编辑过，不能静默丢）</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "未保存的更改",
            Content = "切换文件会丢失当前未保存的修改。",
            PrimaryButtonText = "放弃更改",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ---------- 查找 ----------
    private void ShowFind()
    {
        if (!_ready || _doc is null) return;
        FindPopup.HorizontalOffset = Math.Max(0, Root.ActualWidth - 340);
        FindPopup.VerticalOffset = 44;
        FindPopup.IsOpen = true;
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();
    }

    private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var shiftDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            Post(new { type = BridgeMessages.Find, query = FindBox.Text, direction = shiftDown ? -1 : 1 });
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            FindPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnFindNext(object s, RoutedEventArgs e)
        => Post(new { type = BridgeMessages.Find, query = FindBox.Text, direction = 1 });
}
