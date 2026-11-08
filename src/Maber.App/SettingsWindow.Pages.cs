using System.Text.Json;
using Maber.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using Windows.System;

namespace Maber.App;

public sealed partial class SettingsWindow
{
    private WebView2? _preview;
    private Border? _previewHost;
    private bool _previewReady;
    private bool _previewInitializing;
    private bool _suppressFontSelection;
    private string? _fontStatus;
    private bool _fontStatusError;

    private string? _recordingCommand;
    private Button? _recordingButton;
    private string? _recordingOriginal;
    private TextBlock? _shortcutErrorText;

    private static readonly JsonSerializerOptions PreviewJson = new(JsonSerializerDefaults.Web);

    private sealed record FontChoice(string Kind, string Value, string Display);

    // ================= 页面分发 =================
    private void BuildPage()
    {
        PageHost.Children.Clear();
        _previewHost = null;
        _shortcutErrorText = null;
        switch (_page)
        {
            case "reading": BuildReadingPage(); break;
            case "shortcuts": BuildShortcutsPage(); break;
            case "files": BuildFilesPage(); break;
            case "about": BuildAboutPage(); break;
            default: BuildAppearancePage(); break;
        }
    }

    // ================= 外观 =================
    private void BuildAppearancePage()
    {
        var s = SettingsHost.Current;
        AddPageHeader("外观", "窗口、侧栏与主题。改动即时生效。");

        AddSection("主题");
        var themeCard = NewCard();
        AddCombo(themeCard, "颜色模式", "默认浅色；阅读界面与设置窗口同步切换。",
            new[] { ("跟随系统", "system"), ("浅色", "light"), ("深色", "dark") },
            s.Theme, value => Commit(x => x with { Theme = value }));

        AddSection("侧栏");
        var sidebarCard = NewCard();
        AddToggle(sidebarCard, "启动时展开侧栏", "仅完整模式（直接启动）生效。",
            s.SidebarOpenByDefault, value => Commit(x => x with { SidebarOpenByDefault = value }));
        AddSlider(sidebarCard, "侧栏宽度", "范围 180–320 px。",
            180, 320, 10, s.SidebarWidth, "{0:0} px",
            value => Commit(x => x with { SidebarWidth = Math.Round(value) }));

        AddSection("窗口");
        var windowCard = NewCard();
        AddToggle(windowCard, "记住窗口大小与位置", "下次启动恢复到上次关闭时的位置。",
            s.RememberWindowBounds, value => Commit(x => x with { RememberWindowBounds = value }));
        var reset = new Button { Content = "重置", Style = Style("SettingsButtonStyle") };
        reset.Click += (_, _) => Commit(x => x with { Window = null });
        AddRow(windowCard, "窗口位置", "清除已保存的位置，下次启动重新居中。", reset);
    }

    // ================= 阅读 =================
    private void BuildReadingPage()
    {
        var s = SettingsHost.Current;
        AddPageHeader("阅读", "字体、排版与渲染。");

        AddSection("正文字体");
        var bodyCard = NewCard();
        AddStackRow(bodyCard, "字体", "推荐字体、系统已安装字体或导入的字体文件。",
            BuildFontSelector(code: false));

        AddSection("排版");
        var layoutCard = NewCard();
        AddSlider(layoutCard, "字号", "正文基准字号。", 12, 24, 1, s.FontSize, "{0:0} px",
            value => Commit(x => x with { FontSize = Math.Round(value) }));
        AddSlider(layoutCard, "行高", "1.4 紧凑，2.2 宽松。", 1.4, 2.2, 0.05, s.LineHeight, "{0:0.00}",
            value => Commit(x => x with { LineHeight = Math.Round(value, 2) }));
        AddSlider(layoutCard, "阅读宽度", "正文最大宽度。", 30, 70, 1, s.ContentWidth, "{0:0} rem",
            value => Commit(x => x with { ContentWidth = Math.Round(value) }));

        AddSection("代码");
        var codeCard = NewCard();
        AddStackRow(codeCard, "代码字体", "行内代码与代码块使用。", BuildFontSelector(code: true));
        AddSlider(codeCard, "代码字号", "相对正文单独设置。", 10, 20, 1, s.CodeFontSize, "{0:0} px",
            value => Commit(x => x with { CodeFontSize = Math.Round(value) }));

        AddSection("渲染");
        var renderCard = NewCard();
        AddToggle(renderCard, "数学公式", "关闭后 $ 与 $$ 按原文显示。",
            s.RenderMath, value => Commit(x => x with { RenderMath = value }));
        AddToggle(renderCard, "Mermaid 图表", "关闭后 mermaid 代码块按普通代码显示。",
            s.RenderMermaid, value => Commit(x => x with { RenderMermaid = value }));
        AddToggle(renderCard, "恢复阅读位置", "再次打开同一文件时回到上次滚动位置。",
            s.RestoreReadingPosition, value => Commit(x => x with { RestoreReadingPosition = value }));

        if (s.CustomFonts.Count > 0)
        {
            AddSection("已导入字体");
            var fontCard = NewCard();
            foreach (var font in s.CustomFonts.ToList())
            {
                var remove = new Button { Content = "移除", Style = Style("SettingsButtonStyle") };
                remove.Click += (_, _) =>
                {
                    FontService.Remove(font);
                    Commit(x => x with
                    {
                        CustomFonts = x.CustomFonts.Where(f => f.File != font.File).ToList(),
                        FontFamily = x.FontFamily == "custom" && x.CustomFontFamily == font.Family ? "default" : x.FontFamily,
                        CodeFontFamily = x.CodeFontFamily == font.Family ? "mono" : x.CodeFontFamily,
                    });
                    _fontStatus = $"已移除 {font.Family}";
                    _fontStatusError = false;
                    BuildPage();
                };
                AddRow(fontCard, font.Family, Path.GetFileName(font.File), remove);
            }
        }

        AddSection("预览");
        var previewCard = NewCard();
        var host = new Border
        {
            Height = 196,
            CornerRadius = new CornerRadius(8),
            BorderBrush = BorderBrushValue,
            BorderThickness = new Thickness(1),
            Background = SurfaceBrush,
            Margin = new Thickness(0, 8, 0, 10),
        };
        previewCard.Children.Add(host);
        AttachPreview(host);
    }

    private FrameworkElement BuildFontSelector(bool code)
    {
        var stack = new StackPanel { Spacing = 8 };
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var box = new ComboBox { Style = Style("SettingsComboBoxStyle"), Width = 240 };
        var search = new TextBox
        {
            PlaceholderText = "搜索字体",
            Width = 160,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        line.Children.Add(search);
        line.Children.Add(box);

        if (!code)
        {
            var import = new Button { Content = "导入字体…", Style = Style("SettingsButtonStyle") };
            import.Click += OnImportFont;
            line.Children.Add(import);
        }

        var status = new TextBlock
        {
            Text = _fontStatus ?? "",
            FontSize = 11.5,
            Foreground = _fontStatusError ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC0, 0x50, 0x4D)) : SecondaryBrush,
            Visibility = string.IsNullOrEmpty(_fontStatus) ? Visibility.Collapsed : Visibility.Visible,
            TextWrapping = TextWrapping.Wrap,
        };

        var all = BuildFontChoices(code);

        string CurrentValue() => code
            ? SettingsHost.Current.CodeFontFamily
            : SettingsHost.Current.FontFamily is "custom" ? SettingsHost.Current.CustomFontFamily : SettingsHost.Current.FontFamily;

        void Fill(string filter)
        {
            _suppressFontSelection = true;
            var current = CurrentValue();
            box.Items.Clear();
            var query = filter.Trim();
            bool Match(FontChoice c) => query.Length == 0 ||
                c.Display.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                c.Value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

            var presets = all.Where(c => c.Kind == "preset" && Match(c)).ToList();
            var imports = all.Where(c => c.Kind == "import" && Match(c)).ToList();
            var systems = all.Where(c => c.Kind == "system" && Match(c)).ToList();

            void Header(string text) => box.Items.Add(new ComboBoxItem
            {
                Content = text,
                IsEnabled = false,
                FontSize = 11,
                Opacity = 0.55,
            });

            var selectionIndex = -1;
            void AddChoices(string title, List<FontChoice> choices)
            {
                if (choices.Count == 0) return;
                Header(title);
                foreach (var choice in choices)
                {
                    if (selectionIndex < 0 && choice.Value == current) selectionIndex = box.Items.Count;
                    box.Items.Add(new ComboBoxItem { Content = choice.Display, Tag = choice });
                }
            }

            AddChoices("推荐", presets);
            AddChoices("已导入", imports);
            AddChoices("系统字体", systems);
            box.SelectedIndex = selectionIndex;
            _suppressFontSelection = false;
        }

        box.SelectionChanged += (_, _) =>
        {
            if (_suppressFontSelection) return;
            if (box.SelectedItem is not ComboBoxItem { Tag: FontChoice choice }) return;
            if (code)
            {
                Commit(x => x with { CodeFontFamily = choice.Value });
            }
            else if (choice.Kind == "preset")
            {
                Commit(x => x with { FontFamily = choice.Value, CustomFontFamily = "" });
            }
            else
            {
                Commit(x => x with { FontFamily = "custom", CustomFontFamily = choice.Value });
            }
        };

        search.TextChanged += (_, _) => Fill(search.Text);
        Fill("");
        stack.Children.Add(line);
        stack.Children.Add(status);
        return stack;
    }

    private List<FontChoice> BuildFontChoices(bool code)
    {
        var list = new List<FontChoice>();
        if (code)
        {
            list.Add(new FontChoice("preset", "mono", "等宽（Cascadia / Consolas）"));
            list.Add(new FontChoice("preset", "default", "跟随正文字体"));
        }
        else
        {
            list.Add(new FontChoice("preset", "default", "系统默认"));
            list.Add(new FontChoice("preset", "yahei", "微软雅黑"));
            list.Add(new FontChoice("preset", "wenkai", "霞鹜文楷（屏幕阅读）"));
            list.Add(new FontChoice("preset", "smiley", "得意黑"));
            list.Add(new FontChoice("preset", "simsun", "宋体"));
            list.Add(new FontChoice("preset", "kaiti", "楷体"));
            list.Add(new FontChoice("preset", "mono", "等宽（Cascadia / Consolas）"));
        }

        foreach (var font in SettingsHost.Current.CustomFonts)
            list.Add(new FontChoice("import", font.Family, font.Family));

        foreach (var name in SystemFonts.All())
            list.Add(new FontChoice("system", name, name));

        return list;
    }

    private async void OnImportFont(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in new[] { ".ttf", ".otf", ".ttc" }) picker.FileTypeFilter.Add(ext);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var (font, error) = FontService.Import(file.Path);
        if (font is null)
        {
            _fontStatus = error ?? "导入失败";
            _fontStatusError = true;
            BuildPage();
            return;
        }

        var already = SettingsHost.Current.CustomFonts.Any(
            f => string.Equals(f.File, font.File, StringComparison.OrdinalIgnoreCase));
        Commit(x => x with
        {
            CustomFonts = already ? x.CustomFonts : x.CustomFonts.Append(font).ToList(),
            FontFamily = "custom",
            CustomFontFamily = font.Family,
        });
        _fontStatus = already ? $"已选择 {font.Family}" : $"已导入 {font.Family}";
        _fontStatusError = false;
        BuildPage();
    }

    // ================= 快捷键 =================
    private void BuildShortcutsPage()
    {
        AddPageHeader("快捷键", "点击组合键后重新录制；Esc 取消。只接受带 Ctrl / Alt / Shift 的组合。");

        var card = NewCard();
        foreach (var command in ShortcutCommands.All)
        {
            var gesture = ShortcutCommands.Resolve(SettingsHost.Current.Shortcuts, command.Id);
            var record = new Button
            {
                Content = gesture,
                Style = Style("SettingsButtonStyle"),
                MinWidth = 120,
            };
            record.Click += (_, _) => BeginRecording(command.Id, record);

            var reset = new Button
            {
                Content = "↺",
                Style = Style("SettingsButtonStyle"),
                Padding = new Thickness(9, 4, 9, 4),
                Visibility = SettingsHost.Current.Shortcuts.ContainsKey(command.Id)
                    ? Visibility.Visible : Visibility.Collapsed,
            };
            ToolTipService.SetToolTip(reset, "恢复默认");
            reset.Click += (_, _) =>
            {
                Commit(x =>
                {
                    var next = new Dictionary<string, string>(x.Shortcuts);
                    next.Remove(command.Id);
                    return x with { Shortcuts = next };
                });
                BuildPage();
            };

            var box = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.Children.Add(record);
            box.Children.Add(reset);
            AddRow(card, command.Label, $"默认 {command.DefaultGesture}", box);
        }

        _shortcutErrorText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC0, 0x50, 0x4D)),
            Margin = new Thickness(2, 8, 0, 0),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };
        PageHost.Children.Add(_shortcutErrorText);

        var restore = new Button
        {
            Content = "全部恢复默认",
            Style = Style("SettingsButtonStyle"),
            Margin = new Thickness(0, 16, 0, 0),
        };
        restore.Click += (_, _) =>
        {
            Commit(x => x with { Shortcuts = new Dictionary<string, string>() });
            BuildPage();
        };
        PageHost.Children.Add(restore);
    }

    private void BeginRecording(string commandId, Button button)
    {
        CancelRecording();
        _recordingCommand = commandId;
        _recordingButton = button;
        _recordingOriginal = button.Content as string;
        button.Content = "按下组合键…";
        button.Focus(FocusState.Programmatic);
        if (_shortcutErrorText is not null) _shortcutErrorText.Visibility = Visibility.Collapsed;
    }

    private void CancelRecording()
    {
        if (_recordingButton is not null && _recordingOriginal is not null)
            _recordingButton.Content = _recordingOriginal;
        _recordingCommand = null;
        _recordingButton = null;
        _recordingOriginal = null;
    }

    private void ShowShortcutError(string message)
    {
        if (_shortcutErrorText is null) return;
        _shortcutErrorText.Text = message;
        _shortcutErrorText.Visibility = Visibility.Visible;
    }

    private void OnRecordingKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_recordingCommand is null) return;
        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            CancelRecording();
            return;
        }

        if (e.Key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            return;

        var ctrl = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);
        var shift = IsDown(VirtualKey.Shift);
        if (!ctrl && !alt && !shift)
        {
            ShowShortcutError("请同时按住 Ctrl、Alt 或 Shift 再按一个键");
            return;
        }

        var token = KeyToken(e.Key);
        if (token is null)
        {
            ShowShortcutError("这个键不支持作为快捷键");
            return;
        }

        var raw = $"{(ctrl ? "Ctrl+" : "")}{(alt ? "Alt+" : "")}{(shift ? "Shift+" : "")}{token}";
        if (!ShortcutGesture.TryNormalize(raw, out var gesture))
        {
            ShowShortcutError("组合键无效");
            return;
        }

        var resolved = ShortcutCommands.ResolveAll(SettingsHost.Current.Shortcuts);
        var conflict = resolved.FirstOrDefault(kv => kv.Key != _recordingCommand && kv.Value == gesture);
        if (conflict.Key is not null)
        {
            ShowShortcutError($"与「{ShortcutCommands.ById(conflict.Key)?.Label ?? conflict.Key}」冲突");
            return;
        }

        var id = _recordingCommand;
        CancelRecording();
        Commit(x =>
        {
            var next = new Dictionary<string, string>(x.Shortcuts) { [id] = gesture };
            return x with { Shortcuts = next };
        });
        BuildPage();
    }

    private static bool IsDown(VirtualKey key)
        => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static string? KeyToken(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
            return ((char)('A' + key - VirtualKey.A)).ToString();
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            return ((char)('0' + key - VirtualKey.Number0)).ToString();
        if (key >= VirtualKey.F1 && key <= VirtualKey.F12)
            return "F" + (key - VirtualKey.F1 + 1);
        return key switch
        {
            VirtualKey.Enter => "Enter",
            VirtualKey.Space => "Space",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Back => "Backspace",
            _ => PunctuationToken((int)key),
        };
    }

    private static string? PunctuationToken(int vk) => vk switch
    {
        186 => ";", 187 => "=", 188 => ",", 189 => "-", 190 => ".", 191 => "/",
        192 => "`", 219 => "[", 220 => "\\", 221 => "]", 222 => "'",
        _ => null,
    };

    // ================= 文件与最近 =================
    private void BuildFilesPage()
    {
        var s = SettingsHost.Current;
        AddPageHeader("文件与最近", "打开、启动与外部修改行为。");

        AddSection("最近文件");
        var recentCard = NewCard();
        AddSlider(recentCard, "保留数量", "最多记录多少个最近文件。", 5, 50, 5, s.RecentLimit, "{0:0} 个",
            value =>
            {
                var limit = (int)Math.Round(value);
                Commit(x => x with
                {
                    RecentLimit = limit,
                    RecentFiles = AppSettings.NormalizeRecent(x.RecentFiles, limit),
                });
            });
        var clear = new Button { Content = "清除列表", Style = Style("SettingsButtonStyle") };
        clear.Click += (_, _) => Commit(x => x with { RecentFiles = new List<string>() });
        AddRow(recentCard, "清除最近文件", "只清除列表，不删除磁盘文件。", clear);

        AddSection("启动");
        var startupCard = NewCard();
        AddCombo(startupCard, "启动时", "直接启动 Maber 时打开什么。",
            new[] { ("空白页", "empty"), ("恢复上次文件", "last") },
            s.StartupBehavior, value => Commit(x => x with { StartupBehavior = value }));

        AddSection("外部修改");
        var externalCard = NewCard();
        AddCombo(externalCard, "文件被其他程序修改时", "自动重载适合边读边写；总是提示更稳妥。",
            new[] { ("自动重载", "auto"), ("总是提示", "prompt") },
            s.ExternalChangeMode, value => Commit(x => x with { ExternalChangeMode = value }));
    }

    // ================= 关于 =================
    private void BuildAboutPage()
    {
        AddPageHeader("关于", "Maber — Markdown 阅读器。");

        var card = NewCard();
        var title = new TextBlock
        {
            Text = "Maber.",
            FontFamily = new FontFamily("Georgia"),
            FontSize = 30,
            Foreground = PrimaryBrush,
            Margin = new Thickness(0, 10, 0, 2),
        };
        card.Children.Add(title);
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.4.0";
        card.Children.Add(new TextBlock
        {
            Text = $"版本 {version} · MIT License",
            FontSize = 12,
            Foreground = SecondaryBrush,
            Margin = new Thickness(0, 0, 0, 10),
        });
        card.Children.Add(new TextBlock
        {
            Text = "专注于随时打开散落在硬盘各处的 Markdown 文档：轻量、快速、即读即走。",
            Style = Style("SettingsDescStyle"),
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddSection("快捷键速览");
        var keysCard = NewCard();
        foreach (var (id, gesture) in ShortcutCommands.ResolveAll(SettingsHost.Current.Shortcuts))
        {
            var command = ShortcutCommands.ById(id);
            var value = new TextBlock
            {
                Text = gesture,
                FontFamily = new FontFamily("Cascadia Code"),
                FontSize = 12,
                Foreground = SecondaryBrush,
            };
            AddRow(keysCard, command?.Label ?? id, null, value);
        }
    }

    // ================= 预览 =================
    private void AttachPreview(Border host)
    {
        _preview ??= new WebView2
        {
            DefaultBackgroundColor = Microsoft.UI.Colors.Transparent,
        };
        if (_preview.Parent is Border old) old.Child = null;
        host.Child = _preview;
        _previewHost = host;
        if (_previewReady) UpdatePreview();
        else _ = InitPreviewAsync();
    }

    private async Task InitPreviewAsync()
    {
        if (_previewInitializing || _preview is null) return;
        _previewInitializing = true;
        try
        {
            var env = await App.GetWebViewEnvironmentAsync();
            await _preview.EnsureCoreWebView2Async(env);
            FontService.EnsureDirectory();
            _preview.CoreWebView2.SetVirtualHostNameToFolderMapping(
                FontService.Host, MaberPaths.FontsDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            _preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _preview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _preview.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) return;
                _previewReady = true;
                UpdatePreview();
            };
            _preview.NavigateToString(PreviewHtml);
        }
        catch
        {
            _previewInitializing = false;
        }
    }

    private async void UpdatePreview()
    {
        if (!_previewReady || _preview?.CoreWebView2 is null) return;
        var s = SettingsHost.Current;
        var light = _appliedTheme == "light";
        var payload = new
        {
            theme = _appliedTheme,
            bg = light ? "#FAF9F5" : "#141413",
            fg = light ? "#1F1E1C" : "#E8E6DC",
            soft = light ? "#6B675F" : "#A8A49B",
            accent = light ? "#C96442" : "#E08A6B",
            border = light ? "#E6E2D8" : "#2A2825",
            codeBg = light ? "#F1EEE6" : "#1E1D1A",
            fontFamily = FontStacks.Body(s.FontFamily, s.CustomFontFamily),
            fontSize = s.FontSize,
            lineHeight = s.LineHeight,
            codeFontFamily = FontStacks.Code(s.CodeFontFamily, s.FontFamily, s.CustomFontFamily),
            codeFontSize = s.CodeFontSize,
            fontFaces = s.CustomFonts.Select(f => new { family = f.Family, url = FontService.UrlFor(f) }),
        };
        try
        {
            await _preview.CoreWebView2.ExecuteScriptAsync(
                $"window.applyPreview({JsonSerializer.Serialize(payload, PreviewJson)});");
        }
        catch { /* 预览失败不影响设置 */ }
    }

    private const string PreviewHtml = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
  :root {
    --bg:#FAF9F5; --fg:#1F1E1C; --soft:#6B675F; --accent:#C96442;
    --border:#E6E2D8; --code-bg:#F1EEE6;
    --font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
    --font-size: 16px; --line-height: 1.75;
    --code-font-family: "Cascadia Code", Consolas, monospace; --code-font-size: 13px;
  }
  html, body { margin:0; height:100%; }
  body {
    background: var(--bg); color: var(--fg);
    font-family: var(--font-family); font-size: var(--font-size);
    line-height: var(--line-height);
    padding: 16px 20px; box-sizing: border-box; overflow: hidden;
  }
  h1 { font-size: 1.3em; margin: 0 0 .45em; line-height: 1.35; }
  p { margin: 0 0 .55em; }
  code {
    background: var(--code-bg); padding: .12em .38em; border-radius: 4px;
    font-family: var(--code-font-family); font-size: var(--code-font-size);
  }
  .dim { color: var(--soft); font-size: .78em; margin-top: .7em; }
  .accent { color: var(--accent); }
</style>
</head>
<body>
  <h1>阅读的质感</h1>
  <p>Maber 专注于随时打开硬盘上的 Markdown 文档，像一张干净的白纸。</p>
  <p>按 <code>Ctrl + O</code> 打开文件，按 <code>Ctrl + P</code> 快速打开，按 <code>Ctrl + F</code> 查找。</p>
  <p class="accent">陶土强调色 · KaTeX 公式 · Mermaid 图表</p>
  <div class="dim">字体预览 · The quick brown fox jumps over the lazy dog. 0123456789</div>
</body>
</html>
""";

    private static class FontStacks
    {
        private const string Fallback = "\"Microsoft YaHei\", \"Segoe UI\", system-ui, sans-serif";

        public static string Body(string key, string custom) => key switch
        {
            "yahei" => $"\"Microsoft YaHei\", \"Segoe UI\", system-ui, sans-serif",
            "wenkai" => "\"霞鹜文楷 GB 屏幕阅读版 R\", \"LXGW WenKai GB Screen R\", \"LXGW WenKai\", \"KaiTi\", serif",
            "smiley" => "\"得意黑\", \"Smiley Sans Oblique\", \"Microsoft YaHei\", \"Segoe UI\", sans-serif",
            "simsun" => "SimSun, \"Songti SC\", serif",
            "kaiti" => "KaiTi, \"Kaiti SC\", serif",
            "mono" => "\"Cascadia Code\", Consolas, \"Microsoft YaHei\", monospace",
            "custom" when !string.IsNullOrWhiteSpace(custom) => Quote(custom) + ", " + Fallback,
            _ => "\"Segoe UI Variable Text\", \"Segoe UI\", " + Fallback,
        };

        public static string Code(string key, string bodyKey, string bodyCustom) => key switch
        {
            "mono" => "\"Cascadia Code\", Consolas, monospace",
            "default" => Body(bodyKey, bodyCustom),
            _ => Quote(key) + ", \"Cascadia Code\", Consolas, monospace",
        };

        private static string Quote(string family)
            => "\"" + family.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
