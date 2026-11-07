using System.Text.Json;
using System.Text.RegularExpressions;
using Maber.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public MainWindow(string? initialPath = null)
    {
        InitializeComponent();
        Title = "Maber";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ModeBadge.Text = "阅读";

        // 所有窗口都关掉后退出进程，避免无窗口的后台残留
        Interlocked.Increment(ref LiveWindows);
        Closed += (_, _) => { _watcher.Dispose(); if (Interlocked.Decrement(ref LiveWindows) == 0) Environment.Exit(0); };

        Web.WebMessageReceived += OnWebMessage;
        Web.NavigationCompleted += (_, _) =>
        {
            _ready = true;
            _webReady.TrySetResult();
            _ = SendThemeAsync();
        };
        _watcher.ExternalChanged += (_, _) => DispatcherQueue.TryEnqueue(OnExternalChanged);
        _watcher.ExternalDeleted += (_, _) => DispatcherQueue.TryEnqueue(() => ShowBar("文件已被外部删除"));
        Activated += async (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) await SendThemeAsync(); };

        // XAML 侧快捷键只在焦点落在 XAML 元素（如查找框）时生效；
        // 焦点在 WebView2 内时由内核捕获按键后经 "accelerator" 消息分发到同一张表
        _accelerators = new Dictionary<string, Action>
        {
            ["o"] = () => _ = PickOpenFileAsync(),
            ["s"] = () => _ = SaveAsync(),
            ["e"] = ToggleMode,
            ["k"] = ShowOutline,
            ["f"] = ShowFind,
        };
        foreach (var (key, action) in _accelerators)
            AddAccelerator((VirtualKey)char.ToUpper(key[0]), action);

        var path = initialPath ?? Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(p => File.Exists(p));
        _ = InitializeAsync(path);
    }

    private static int LiveWindows;

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
        await Web.EnsureCoreWebView2Async();
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        // 关掉浏览器内置快捷键（Ctrl+F 呼出页内查找、Ctrl+O 呼出打开文件等），统一走我们的分发
        Web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        Web.CoreWebView2.Navigate("https://app.local/index.html");
        await _webReady.Task; // 等 Web 监听器就绪，避免首条 loadDocument 丢失
        if (path is null) { EmptyState.IsOpen = true; return; }
        await LoadFileAsync(path);
    }

    // ---------- 文件 ----------
    private async Task PickOpenFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in new[] { ".md", ".markdown", ".txt" }) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await LoadFileAsync(file.Path);
    }

    private async Task LoadFileAsync(string path)
    {
        EmptyState.IsOpen = false; // 空状态提示（Ctrl+O 教程卡）打开文件后即收起
        _doc = await _files.LoadAsync(path);
        _currentText = _doc.Value.Text;
        _dirty = false;
        _sourceMode = false;
        ModeBadge.Text = "阅读";
        _watcher.Attach(path);
        TitleText.Text = Path.GetFileName(path);
        Post(new { type = BridgeMessages.LoadDocument, name = Path.GetFileName(path), content = _currentText });
    }

    private async Task SaveAsync()
    {
        if (_doc is null) return;
        _watcher.SuspendOnce();
        await _files.SaveAsync(_doc.Value.Path, _currentText, _doc.Value.EncodingName, _doc.Value.HasBom);
        _dirty = false;
    }

    // ---------- wikilink 跳转 ----------
    // 在新窗口打开链接目标；解析支持绝对路径与相对路径
    private async Task OpenWikilinkAsync(string target)
    {
        if (_doc is null || string.IsNullOrWhiteSpace(target)) return;
        var resolved = ResolveWikilink(_doc.Value.Path, target);
        if (resolved is null) { ShowBar($"未找到链接目标「{target}」"); return; }
        var window = new MainWindow(resolved);
        window.Activate();
        await Task.CompletedTask;
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
        // 竞态兜底：读最新内容；与我们手里的权威文本一致则是自写回声
        var fresh = await _files.LoadAsync(_doc.Value.Path);
        if (fresh.Text == _currentText) return;
        if (!_dirty)
        {
            _doc = fresh; _currentText = fresh.Text;
            Post(new { type = BridgeMessages.LoadDocument, name = Path.GetFileName(_doc.Value.Path), content = _currentText });
        }
        else
        {
            ShowBar("文件已被外部修改，你有未保存的更改");
        }
    }

    private async void OnReloadExternal(object s, RoutedEventArgs e)
    {
        ExternalBar.IsOpen = false;
        if (_doc is null) return;
        var fresh = await _files.LoadAsync(_doc.Value.Path);
        _doc = fresh; _dirty = false;
        _currentText = fresh.Text;
        Post(new { type = BridgeMessages.LoadDocument, name = Path.GetFileName(_doc.Value.Path), content = _currentText });
    }

    private void ShowBar(string msg)
    {
        ExternalBar.Title = msg;
        ExternalBar.IsOpen = true;
    }

    // ---------- 桥接 ----------
    private void OnWebMessage(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var json = JsonDocument.Parse(e.WebMessageAsJson);
        var type = json.RootElement.GetProperty("type").GetString();
        switch (type)
        {
            case BridgeMessages.Ready:
                _ready = true;
                _webReady.TrySetResult();
                _ = SendThemeAsync();
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
                break;
            case BridgeMessages.OpenLink:
                var href = json.RootElement.GetProperty("href").GetString() ?? "";
                if (href.StartsWith("http")) _ = Launcher.LaunchUriAsync(new Uri(href));
                break;
            case BridgeMessages.OpenWikilink:
                _ = OpenWikilinkAsync(json.RootElement.GetProperty("target").GetString() ?? "");
                break;
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

    // ---------- 主题跟随系统 ----------
    private async Task SendThemeAsync()
    {
        if (!_ready) return;
        var light = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int v && v == 1;
        Post(new { type = BridgeMessages.SetTheme, theme = light ? "light" : "dark" });
        await Task.CompletedTask;
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

    private void ShowOutline()
    {
        if (!_ready || _doc is null) return;
        OutlineList.Items.Clear();
        foreach (var (level, text, line) in _outline)
        {
            var tb = new TextBlock { Text = text, Tag = line, TextTrimming = TextTrimming.CharacterEllipsis };
            tb.Margin = new Thickness((level - 1) * 14, 4, 0, 4);
            OutlineList.Items.Add(tb);
        }
        OutlinePopup.HorizontalOffset = Math.Max(0, Root.ActualWidth - 300);
        OutlinePopup.VerticalOffset = 44;
        OutlinePopup.IsOpen = true;
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
