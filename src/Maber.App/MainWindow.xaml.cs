using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maber.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
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

    private readonly ReadingStateStore _readingStore = new();
    private readonly Dictionary<string, ReadingMark> _readingMarks;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _readingSaveTimer;

    // Web 端 onHostMessage 监听器注册后才会处理消息；就绪前发的 loadDocument 会丢，
    // 因此用 ready/NavigationCompleted 双信号握手，等待后再发首条文档。
    private readonly TaskCompletionSource _webReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly bool _totalMode;
    private bool _sidebarOpen = true;
    private ToggleButton? _currentSidebarTab;
    private Storyboard? _sidebarStory;
    private string? _pendingAnchor;   // 新窗口加载完成后要跳转的标题锚点

    public MainWindow(string? initialPath = null, bool totalMode = false, string? anchor = null)
    {
        _totalMode = totalMode;
        _pendingAnchor = anchor;
        Perf($"window:ctor {(initialPath is null ? "main" : $"wikilink {Path.GetFileName(initialPath)}")} total={totalMode}");
        InitializeComponent();
        _readingMarks = _readingStore.Load();
        _currentSidebarTab = TabOutline;
        TabStrip.Loaded += (_, _) => MoveTabUnderline(TabOutline, false);
        TabStrip.SizeChanged += (_, _) => MoveTabUnderline(_currentSidebarTab ?? TabOutline, false);
        Title = "Maber";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyAppIcon();
        TitleText.Text = "Maber";
        ModeBadge.Text = "";
        ApplyChromeTheme();
        UpdateDateLabel();
        if (_totalMode)
        {
            var width = SettingsHost.Current.SidebarWidth;
            _sidebarOpen = SettingsHost.Current.SidebarOpenByDefault;
            SidebarToggle.Visibility = Visibility.Visible;
            Sidebar.Width = width;
            SidebarSpace.Width = width;
            if (_sidebarOpen)
            {
                Sidebar.Visibility = Visibility.Visible;
                SidebarSpace.Visibility = Visibility.Visible;
            }
        }

        Closed += (_, _) =>
        {
            SettingsHost.Changed -= OnSettingsChanged;
            _watcher.Dispose();
            SaveWindowBounds();
            SaveReadingMarks();
        };
        SettingsHost.Changed += OnSettingsChanged;
        App.TrackWindow(this); // 必须在清理/落盘之后注册，保证最后一个窗口退出前完成保存
        SetupDragDrop();

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
            SendShortcuts();
        };
        // 关闭查找框时清掉页内高亮
        FindPopup.Closed += (_, _) =>
        {
            if (_ready) Post(new { type = BridgeMessages.Find, query = "", direction = 1 });
        };
        _watcher.ExternalChanged += (_, _) => DispatcherQueue.TryEnqueue(OnExternalChanged);
        _watcher.ExternalDeleted += (_, _) => DispatcherQueue.TryEnqueue(() => ShowBar("文件已被外部删除"));
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            ApplyChromeTheme(); // 跟随系统时，系统主题可能在后台已切换
            UpdateDateLabel();  // 跨零点后回到窗口时刷新
            SendTheme();
        };

        // XAML 侧快捷键只在焦点落在 XAML 元素（如查找框）时生效；
        // 焦点在 WebView2 内时由内核捕获按键后经 "accelerator" 消息分发到同一张表
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden; // 关掉悬停弹 "Ctrl+O" tooltip
        _commandActions = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            [ShortcutCommands.Open] = () => _ = PickOpenFileAsync(),
            [ShortcutCommands.Save] = () => _ = SaveAsync(),
            [ShortcutCommands.ToggleMode] = ToggleMode,
            [ShortcutCommands.Outline] = ShowOutline,
            [ShortcutCommands.Find] = ShowFind,
            [ShortcutCommands.Sidebar] = ToggleSidebar,
            [ShortcutCommands.QuickOpen] = ShowQuickOpen,
            [ShortcutCommands.Settings] = SettingsWindow.Open,
        };
        RebuildAccelerators();

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
            // 完整模式优先恢复上次关闭时的窗口位置（仍需与当前显示器有交集）
            if (_totalMode && SettingsHost.Current.RememberWindowBounds &&
                SettingsHost.Current.Window is { } saved)
            {
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
                    new Windows.Graphics.PointInt32(saved.X + 40, saved.Y + 40),
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
                var intersects = saved.X < area.X + area.Width && saved.X + saved.Width > area.X
                             && saved.Y < area.Y + area.Height && saved.Y + saved.Height > area.Y;
                if (intersects)
                {
                    AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                        saved.X, saved.Y, saved.Width, saved.Height));
                    if (saved.Maximized && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                        presenter.Maximize();
                    Perf($"window:restored {saved.Width}x{saved.Height} at {saved.X},{saved.Y} max={saved.Maximized}");
                    return;
                }
            }

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
                wLogical = Math.Min(wLogical, work.Height / scale / ratio);
                hLogical = wLogical * ratio;
            }
            var w = (int)(wLogical * scale);
            var h = (int)(hLogical * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            Perf($"window:size {w}x{h} scale={scale:0.##} workArea={work.Width}x{work.Height}");
        }
        catch { /* 尺寸设置失败不影响功能 */ }
    }

    /// <summary>关闭时记住窗口位置；最大化时保留上次的还原尺寸</summary>
    private void SaveWindowBounds()
    {
        if (!_totalMode || !SettingsHost.Current.RememberWindowBounds) return;
        try
        {
            var maximized = AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
                { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized };
            var bounds = maximized && SettingsHost.Current.Window is { } previous
                ? previous with { Maximized = true }
                : new WindowBounds(
                    AppWindow.Position.X, AppWindow.Position.Y,
                    AppWindow.Size.Width, AppWindow.Size.Height, maximized);
            SettingsHost.Update(x => x with { Window = bounds });
            SettingsHost.Persist();
        }
        catch { /* 位置记忆失败不影响关闭 */ }
    }

    /// <summary>滚动位置是高频状态，去抖后落盘</summary>
    private void DebounceReadingSave()
    {
        if (_readingSaveTimer is null)
        {
            _readingSaveTimer = DispatcherQueue.CreateTimer();
            _readingSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
            _readingSaveTimer.IsRepeating = false;
            _readingSaveTimer.Tick += (_, _) => SaveReadingMarks();
        }
        _readingSaveTimer.Stop();
        _readingSaveTimer.Start();
    }

    private void SaveReadingMarks() => _readingStore.Save(_readingMarks);

    private Dictionary<string, Action> _commandActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyboardAccelerator> _activeAccelerators = new();

    /// <summary>按当前设置重建 XAML 快捷键；改动快捷键后立即调用</summary>
    private void RebuildAccelerators()
    {
        if (Content is not Grid root) return;
        foreach (var accelerator in _activeAccelerators)
            root.KeyboardAccelerators.Remove(accelerator);
        _activeAccelerators.Clear();

        foreach (var (id, gesture) in ShortcutCommands.ResolveAll(SettingsHost.Current.Shortcuts))
        {
            if (!_commandActions.TryGetValue(id, out var action)) continue;
            var (ctrl, alt, shift, keyName) = ShortcutGesture.Parse(gesture);
            var key = ParseVirtualKey(keyName);
            if (key is null) continue;

            var modifiers = VirtualKeyModifiers.None;
            if (ctrl) modifiers |= VirtualKeyModifiers.Control;
            if (alt) modifiers |= VirtualKeyModifiers.Menu;
            if (shift) modifiers |= VirtualKeyModifiers.Shift;
            if (modifiers == VirtualKeyModifiers.None) continue;

            var accelerator = new KeyboardAccelerator { Modifiers = modifiers, Key = key.Value };
            accelerator.Invoked += (_, args) => { args.Handled = true; action(); };
            root.KeyboardAccelerators.Add(accelerator);
            _activeAccelerators.Add(accelerator);
        }
    }

    private static VirtualKey? ParseVirtualKey(string name)
    {
        if (name.Length == 1)
        {
            var c = name[0];
            if (char.IsAsciiLetter(c)) return (VirtualKey)('A' + char.ToUpperInvariant(c) - 'A');
            if (char.IsAsciiDigit(c)) return (VirtualKey)('0' + (c - '0'));
            return (int)c switch
            {
                186 => (VirtualKey)186, 187 => (VirtualKey)187, 188 => (VirtualKey)188,
                189 => (VirtualKey)189, 190 => (VirtualKey)190, 191 => (VirtualKey)191,
                192 => (VirtualKey)192, 219 => (VirtualKey)219, 220 => (VirtualKey)220,
                221 => (VirtualKey)221, 222 => (VirtualKey)222,
                _ => null,
            };
        }

        return name switch
        {
            "Enter" => VirtualKey.Enter,
            "Space" => VirtualKey.Space,
            "Home" => VirtualKey.Home,
            "End" => VirtualKey.End,
            "PageUp" => VirtualKey.PageUp,
            "PageDown" => VirtualKey.PageDown,
            "Left" => VirtualKey.Left,
            "Right" => VirtualKey.Right,
            "Up" => VirtualKey.Up,
            "Down" => VirtualKey.Down,
            "Insert" => VirtualKey.Insert,
            "Delete" => VirtualKey.Delete,
            "Backspace" => VirtualKey.Back,
            _ when name.StartsWith('F') && int.TryParse(name[1..], out var fn) && fn is >= 1 and <= 12
                => (VirtualKey)((int)VirtualKey.F1 + fn - 1),
            _ => null,
        };
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
        // 本地导入字体：独立域映射到应用数据目录，供 CSS @font-face 读取
        FontService.EnsureDirectory();
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            FontService.Host, MaberPaths.FontsDirectory,
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
        if (path is null) { SendEmptyState(true); return; }
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
        LoadedDocument doc;
        try
        {
            doc = await _files.LoadAsync(path);
        }
        catch (BinaryFileException)
        {
            ShowBar("这不是文本文件，已拒绝渲染");
            return;
        }
        catch (Exception ex)
        {
            Perf($"open:error {ex.GetType().Name} {ex.Message}");
            ShowBar($"打开文件失败：{ex.Message}");
            return;
        }

        _doc = doc;
        _currentText = doc.Text;
        _dirty = false;
        SettingsHost.RecordRecent(doc.Path);
        _outline = new();          // 新大纲到达前不要展示上一个文件的
        if (_totalMode) RefreshSidebarOutline();
        _watcher.Attach(path);
        TitleText.Text = Path.GetFileName(path);
        // 文档内相对资源的伺服根目录（WebResourceRequested 处理器读取）
        _contentDir = Path.GetDirectoryName(Path.GetFullPath(path));
        _contentRoot = FindContentRoot(_contentDir);
        PostDocument();
        if (SettingsHost.Current.RestoreReadingPosition &&
            _readingMarks.TryGetValue(doc.Path, out var mark) && mark.Ratio > 0.001)
            Post(new { type = BridgeMessages.RestoreReadingPosition, ratio = mark.Ratio });
    }

    /// <summary>统一的文档下发：所有路径都带 contentBase，并把两侧模式状态拉回阅读态</summary>
    private void PostDocument()
    {
        if (_doc is null) return;
        _sourceMode = false;
        ModeBadge.Text = "阅读";
        SendEmptyState(false);
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
    private string? _contentRoot;   // 资源根：优先 Obsidian vault（含 .obsidian 的祖先），否则向上最多 6 层

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
            var rootDir = _contentRoot ?? dir;
            if (env is null || dir is null || rootDir is null)
            {
                e.Response = env?.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var uri = new Uri(e.Request.Uri);
            var requestPath = Uri.UnescapeDataString(uri.AbsolutePath);
            // main.ts 给以 / 开头的图片加 @root/ 前缀，表示从内容根（vault 根）解析
            var fromRoot = requestPath.StartsWith("/@root/", StringComparison.OrdinalIgnoreCase);
            var rel = (fromRoot ? requestPath["/@root/".Length..] : requestPath.TrimStart('/'))
                .Replace('/', Path.DirectorySeparatorChar);
            var baseDir = fromRoot ? rootDir : dir;
            var full = Path.GetFullPath(Path.Combine(baseDir, rel));
            // 允许 ../ 与 vault 根引用，但不得越出内容根
            if (!IsWithin(full, rootDir) || !File.Exists(full))
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
    // 在新窗口打开链接目标；解析支持绝对路径、相对路径与 #标题锚点
    private void OpenWikilink(string target, string? anchor = null)
    {
        if (_doc is null) return;
        if (string.IsNullOrWhiteSpace(target))
        {
            ScrollToAnchor(anchor); // [[#标题]]：当前文档内跳转
            return;
        }
        var resolved = ResolveWikilink(_doc.Value.Path, target);
        if (resolved is null) { ShowBar($"未找到链接目标「{target}」"); return; }
        Perf($"wikilink:clicked target={Path.GetFileName(resolved)} anchor={anchor}");
        var window = new MainWindow(resolved, anchor: anchor);
        Perf("wikilink:ctor-done");
        window.Activate();
        Perf("wikilink:activate-called");
    }

    /// <summary>本地链接：解码中文/空格，支持相对/绝对路径与 #锚点</summary>
    private void OpenLocalMarkdownLink(string href)
    {
        if (_doc is null || string.IsNullOrWhiteSpace(href)) return;
        var raw = href.Trim();
        var hash = raw.IndexOf('#');
        string anchor;
        try
        {
            anchor = hash >= 0 ? Uri.UnescapeDataString(raw[(hash + 1)..]).Trim() : "";
        }
        catch (UriFormatException)
        {
            anchor = raw[(hash + 1)..].Trim();
        }
        var pathPart = hash >= 0 ? raw[..hash] : raw;
        string clean;
        try
        {
            clean = Uri.UnescapeDataString(pathPart).Trim();
        }
        catch (UriFormatException)
        {
            clean = pathPart.Trim();
        }
        clean = clean.Split('?')[0];
        if (clean.Length == 0)
        {
            ScrollToAnchor(anchor); // [标题](#标题)
            return;
        }
        if (!clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            !clean.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            ShowBar($"暂不支持打开「{href}」");
            return;
        }
        try
        {
            var target = clean.Replace('/', Path.DirectorySeparatorChar);
            string full;
            if (clean.StartsWith('/') && _contentRoot is not null)
                full = Path.GetFullPath(Path.Combine(_contentRoot, target.TrimStart(Path.DirectorySeparatorChar)));
            else if (Path.IsPathRooted(target))
                full = Path.GetFullPath(target);
            else
                full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_doc.Value.Path)!, target));
            if (!File.Exists(full)) { ShowBar($"未找到链接目标「{href}」"); return; }
            if (string.Equals(Path.GetFullPath(full), Path.GetFullPath(_doc.Value.Path),
                    StringComparison.OrdinalIgnoreCase))
            {
                ScrollToAnchor(anchor); // 指向当前文件：只跳转，不开新窗口
                return;
            }
            var window = new MainWindow(full, anchor: anchor);
            window.Activate();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            ShowBar($"链接无法解析「{href}」");
        }
    }

    /// <summary>按标题文本匹配大纲行号并滚动；新窗口在大纲到达后由 OutlineChanged 调用</summary>
    private void ScrollToAnchor(string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor)) return;
        var line = FindOutlineLine(anchor);
        if (line is null) { ShowBar($"未找到标题「{anchor}」"); return; }
        Post(new { type = BridgeMessages.ScrollToHeading, line = line.Value });
    }

    private int? FindOutlineLine(string anchor)
    {
        var want = anchor.Trim().TrimStart('#').Trim();
        if (want.Length == 0) return null;
        foreach (var item in _outline)
            if (string.Equals(item.text.Trim(), want, StringComparison.OrdinalIgnoreCase))
                return item.line;
        foreach (var item in _outline)
            if (item.text.Contains(want, StringComparison.OrdinalIgnoreCase))
                return item.line;
        return null;
    }

    /// <summary>资源根：优先含 .obsidian 的祖先目录，否则从当前目录向上最多 6 层</summary>
    private static string? FindContentRoot(string? dir)
    {
        if (dir is null) return null;
        var current = Path.GetFullPath(dir);
        string? highest = current;
        for (var depth = 0; depth < 6 && current is not null; depth++)
        {
            if (Directory.Exists(Path.Combine(current, ".obsidian"))) return current;
            highest = current;
            current = Path.GetDirectoryName(current);
        }
        return highest;
    }

    /// <summary>规范化后判断路径是否位于根目录内（含根本身）</summary>
    private static bool IsWithin(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
            var promptMode = SettingsHost.Current.ExternalChangeMode == "prompt";
            if (!_dirty && !promptMode)
            {
                _doc = fresh; _currentText = fresh.Text;
                PostDocument();
            }
            else
            {
                ShowBar(_dirty
                    ? "文件已被外部修改，你有未保存的更改"
                    : "文件已被外部修改，点击加载最新内容");
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
                    SendShortcuts();
                    if (_totalMode && _doc is null) SendEmptyState(true); // Slim 等文档加载，避免水印闪一下
                    break;
                case BridgeMessages.Accelerator:
                    var gesture = json.RootElement.GetProperty("gesture").GetString() ?? "";
                    var commandId = ShortcutCommands.ResolveAll(SettingsHost.Current.Shortcuts)
                        .FirstOrDefault(kv => kv.Value == gesture).Key;
                    if (commandId is not null && _commandActions.TryGetValue(commandId, out var action))
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
                case BridgeMessages.ReadingPosition:
                    if (_doc is not null)
                    {
                        _readingMarks[_doc.Value.Path] = new ReadingMark(
                            json.RootElement.GetProperty("ratio").GetDouble(),
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        DebounceReadingSave();
                    }
                    break;
                case BridgeMessages.OutlineChanged:
                    _outline = json.RootElement.GetProperty("items").EnumerateArray()
                        .Select(i => (i.GetProperty("level").GetInt32(), i.GetProperty("text").GetString() ?? "", i.GetProperty("line").GetInt32()))
                        .ToList();
                    if (_totalMode) RefreshSidebarOutline();
                    if (_pendingAnchor is { } pending)
                    {
                        _pendingAnchor = null;
                        ScrollToAnchor(pending);
                    }
                    break;
                case BridgeMessages.OpenLink:
                    var href = json.RootElement.GetProperty("href").GetString() ?? "";
                    if (href.StartsWith("http://") || href.StartsWith("https://"))
                        _ = Launcher.LaunchUriAsync(new Uri(href));
                    else
                        OpenLocalMarkdownLink(href);
                    break;
                case BridgeMessages.OpenWikilink:
                    OpenWikilink(
                        json.RootElement.GetProperty("target").GetString() ?? "",
                        json.RootElement.TryGetProperty("anchor", out var anchorEl) ? anchorEl.GetString() : null);
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
        if (Content is FrameworkElement root) root.RequestedTheme = CurrentElementTheme;
        ApplyCaptionButtonTheme();
    }

    /// <summary>根元素与 ContentDialog 都按这个主题渲染（弹层不继承根元素 RequestedTheme）</summary>
    private static ElementTheme CurrentElementTheme => SettingsHost.Current.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// 系统标题栏按钮不跟随元素级 RequestedTheme，需显式上色；
    /// 背景透明，露出 Root 的主题底色，切主题时整条标题栏与内容一致。
    /// </summary>
    private void ApplyCaptionButtonTheme()
    {
        try
        {
            var bar = AppWindow.TitleBar;
            var light = SettingsHost.ResolveTheme() == "light";
            var fg = light
                ? Windows.UI.Color.FromArgb(255, 0x1F, 0x1E, 0x1C)
                : Windows.UI.Color.FromArgb(255, 0xE8, 0xE6, 0xDC);
            var inactiveFg = light
                ? Windows.UI.Color.FromArgb(255, 0x9A, 0x95, 0x8B)
                : Windows.UI.Color.FromArgb(255, 0x72, 0x6E, 0x66);
            var hoverBg = light
                ? Windows.UI.Color.FromArgb(255, 0xF0, 0xED, 0xE4)
                : Windows.UI.Color.FromArgb(255, 0x26, 0x24, 0x20);
            var pressedBg = light
                ? Windows.UI.Color.FromArgb(255, 0xE9, 0xE5, 0xDA)
                : Windows.UI.Color.FromArgb(255, 0x2E, 0x2B, 0x26);

            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = fg;
            bar.ButtonHoverForegroundColor = fg;
            bar.ButtonPressedForegroundColor = fg;
            bar.ButtonInactiveForegroundColor = inactiveFg;
            bar.ButtonHoverBackgroundColor = hoverBg;
            bar.ButtonPressedBackgroundColor = pressedBg;
            bar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            // DWM 窗口描边也跟随系统，不处理会在浅色下留一圈深色边（Win11 才支持，失败无副作用）
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var border = light ? 0x00F5F9FA : 0x00131414; // COLORREF = 0x00BBGGRR
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch { /* 标题栏定制在个别系统版本不可用时不影响功能 */ }
    }

    private const int DwmwaBorderColor = 34;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>窗口/任务栏图标（发布版从 Assets 读取）</summary>
    private void ApplyAppIcon()
    {
        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "Maber.ico");
            if (File.Exists(ico)) AppWindow.SetIcon(ico);
        }
        catch { /* 图标缺失不影响功能 */ }
    }

    private void OnSettingsChanged(object? s, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyChromeTheme();
        ApplyLayoutSettings();
        RebuildAccelerators();
        SendTheme();
        SendTypography();
        SendShortcuts();
        RefreshRecentList();
    });

    /// <summary>侧栏宽度等需要立刻反映到布局的设置</summary>
    private void ApplyLayoutSettings()
    {
        if (!_totalMode) return;
        var width = SettingsHost.Current.SidebarWidth;
        Sidebar.Width = width;
        SidebarSpace.Width = width;
    }

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
            customFontFamily = s.CustomFontFamily,
            fontSize = s.FontSize,
            lineHeight = s.LineHeight,
            contentWidth = s.ContentWidth,
            codeFontFamily = s.CodeFontFamily,
            codeFontSize = s.CodeFontSize,
            renderMath = s.RenderMath,
            renderMermaid = s.RenderMermaid,
            fontFaces = s.CustomFonts.Select(f => new
            {
                family = f.Family,
                url = FontService.UrlFor(f),
            }),
        });
    }

    /// <summary>把当前组合键下发给 Web 内核，用于匹配后上报 accelerator</summary>
    private void SendShortcuts()
    {
        if (!_ready) return;
        var resolved = ShortcutCommands.ResolveAll(SettingsHost.Current.Shortcuts);
        Post(new
        {
            type = BridgeMessages.SetShortcuts,
            shortcuts = resolved.Select(kv => new { id = kv.Key, gesture = kv.Value }),
        });
    }

    private void SendEmptyState(bool visible)
    {
        if (!_ready) return;
        Post(new { type = BridgeMessages.SetEmptyState, visible });
    }

    private void OnOpenSettings(object s, RoutedEventArgs e) => SettingsWindow.Open();

    // ---------- 拖放 ----------
    /// <summary>把 md / markdown / txt 拖进窗口直接打开</summary>
    private void SetupDragDrop()
    {
        Root.AllowDrop = true;
        Root.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "打开";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        };
        Root.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var file = items.OfType<StorageFile>().FirstOrDefault(f =>
                {
                    var ext = Path.GetExtension(f.Path);
                    return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
                });
                if (file is null) { ShowBar("只支持拖入 .md / .markdown / .txt 文件"); return; }
                if (!await ConfirmDiscardAsync()) return;
                await LoadFileAsync(file.Path);
            }
            catch (Exception ex)
            {
                Perf($"drop:error {ex.GetType().Name} {ex.Message}");
                ShowBar("打开拖入的文件失败");
            }
            finally
            {
                deferral.Complete();
            }
        };
    }
    // ---------- 侧栏 ----------
    /// <summary>底栏日期：11月8日 | 星期日</summary>
    private void UpdateDateLabel()
    {
        var now = DateTime.Now;
        var week = "日一二三四五六"[(int)now.DayOfWeek];
        DateText.Text = $"{now.Month}月{now.Day}日 | 星期{week}";
    }

    private void OnToggleSidebar(object s, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar() => SetSidebarOpen(!_sidebarOpen);

    /// <summary>侧栏作为抽屉整体向左滑出，主面板不参与布局变化</summary>
    private void SetSidebarOpen(bool open)
    {
        if (!_totalMode || _sidebarOpen == open) return;
        _sidebarOpen = open;
        _sidebarStory?.Stop();

        if (open)
        {
            if (Sidebar.Visibility != Visibility.Visible)
            {
                Sidebar.Visibility = Visibility.Visible;
                SidebarTransform.X = -Sidebar.Width;
            }
            if (SidebarSpace.Visibility != Visibility.Visible)
            {
                SidebarSpace.Visibility = Visibility.Visible;
                SidebarSpace.Width = 0;
            }
        }

        var duration = TimeSpan.FromMilliseconds(open ? 220 : 180);
        var easing = new CubicEase
        {
            EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn
        };
        var slide = new DoubleAnimation
        {
            To = open ? 0 : -Sidebar.Width,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, SidebarTransform);
        Storyboard.SetTargetProperty(slide, "X");

        var space = new DoubleAnimation
        {
            To = open ? Sidebar.Width : 0,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true, // Width 是布局属性，不开依赖动画会被直接忽略
        };
        Storyboard.SetTarget(space, SidebarSpace);
        Storyboard.SetTargetProperty(space, "Width");

        var story = new Storyboard();
        story.Children.Add(slide);
        story.Children.Add(space);
        story.Completed += (_, _) =>
        {
            _sidebarStory = null;
            if (!_sidebarOpen)
            {
                Sidebar.Visibility = Visibility.Collapsed;
                SidebarTransform.X = 0;
                SidebarSpace.Visibility = Visibility.Collapsed;
                SidebarSpace.Width = Sidebar.Width;
            }
        };
        _sidebarStory = story;
        story.Begin();
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
            _sidebarStory?.Stop();
            _sidebarOpen = true;
            Sidebar.Visibility = Visibility.Visible;
            SidebarTransform.X = 0;
            SidebarSpace.Visibility = Visibility.Visible;
            SidebarSpace.Width = Sidebar.Width;
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

    private void OnTabPointerEntered(object sender, PointerRoutedEventArgs e)
        => SetTabHover(sender, 1);

    private void OnTabPointerExited(object sender, PointerRoutedEventArgs e)
        => SetTabHover(sender, 0);

    /// <summary>悬停底色由指针事件显式开关，移开必然复位</summary>
    private void SetTabHover(object sender, double opacity)
    {
        if (ReferenceEquals(sender, TabOutline)) TabOutlineHover.Opacity = opacity;
        else if (ReferenceEquals(sender, TabRecent)) TabRecentHover.Opacity = opacity;
        else if (ReferenceEquals(sender, TabCheat)) TabCheatHover.Opacity = opacity;
    }

    private void SelectSidebarTab(ToggleButton tab)
    {
        TabOutline.IsChecked = ReferenceEquals(tab, TabOutline);
        TabRecent.IsChecked = ReferenceEquals(tab, TabRecent);
        TabCheat.IsChecked = ReferenceEquals(tab, TabCheat);
        _currentSidebarTab = tab;
        MoveTabUnderline(tab, animate: true);
        SidebarOutline.Visibility = tab == TabOutline ? Visibility.Visible : Visibility.Collapsed;
        SidebarRecent.Visibility = tab == TabRecent ? Visibility.Visible : Visibility.Collapsed;
        CheatScroll.Visibility = tab == TabCheat ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>单条下划线在三个标签之间滑动，避免逐条淡入淡出的跳变</summary>
    private void MoveTabUnderline(ToggleButton tab, bool animate)
    {
        if (TabStrip.ActualWidth <= 0 || tab.ActualWidth <= 0) return;
        var center = tab.TransformToVisual(TabStrip)
            .TransformPoint(new Point(tab.ActualWidth / 2, 0)).X;
        var target = center - TabUnderline.Width / 2;

        if (!animate)
        {
            TabUnderlineTransform.X = target;
            return;
        }

        var slide = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, TabUnderlineTransform);
        Storyboard.SetTargetProperty(slide, "X");
        var story = new Storyboard();
        story.Children.Add(slide);
        story.Begin();
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
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                FontSize = 13,
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
            AutomationProperties.SetName(stack, Path.GetFileName(path));
            AutomationProperties.SetHelpText(stack, path);
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
            RequestedTheme = CurrentElementTheme,
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
        FindPopup.HorizontalOffset = Math.Max(0, Root.ActualWidth - 470);
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
            SendFind(shiftDown ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            FindPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnFindNext(object s, RoutedEventArgs e) => SendFind(1);

    private void OnFindModeChanged(object sender, RoutedEventArgs e) => SendFind(1);

    private void SendFind(int direction)
    {
        var query = FindBox.Text;
        Post(new
        {
            type = BridgeMessages.Find,
            query,
            direction,
            wholeWord = FindWholeWord.IsChecked == true,
        });
        if (query.Length == 0) return;
        SettingsHost.Update(s => s with
        {
            FindHistory = new[] { query }
                .Concat(s.FindHistory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList(),
        });
        SettingsHost.Persist();
    }

    private void OnFindHistoryOpening(object? sender, object e)
    {
        FindHistoryFlyout.Items.Clear();
        foreach (var query in SettingsHost.Current.FindHistory)
        {
            var item = new MenuFlyoutItem { Text = query };
            item.Click += (_, _) =>
            {
                FindBox.Text = query;
                SendFind(1);
            };
            FindHistoryFlyout.Items.Add(item);
        }
        if (FindHistoryFlyout.Items.Count == 0)
            FindHistoryFlyout.Items.Add(new MenuFlyoutItem { Text = "暂无搜索历史", IsEnabled = false });
    }
}
