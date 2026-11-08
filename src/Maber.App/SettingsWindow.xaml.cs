using System.Runtime.InteropServices;
using Maber.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Maber.App;

/// <summary>
/// 独立设置窗口（单例）。左侧分类导航 + 右侧内容区，视觉与主窗口共用 Paper & Ink。
/// 页面内容在 SettingsWindow.Pages.cs 中构建。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    private readonly List<NavItem> _nav = new();
    private string _page = "appearance";
    private string _appliedTheme = "";
    private DispatcherQueueTimer? _saveTimer;

    private sealed record NavItem(string Key, Button Button, Border Bar, TextBlock Text);

    public static void Open()
    {
        if (_instance is null)
        {
            _instance = new SettingsWindow();
            _instance.Closed += (_, _) => _instance = null;
        }
        if (_instance.AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        _instance.Activate();
    }

    public SettingsWindow()
    {
        InitializeComponent();
        Title = "设置";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyTheme();
        SetInitialSize();
        BuildNav();
        SelectPage(_page);
        SettingsHost.Changed += OnSettingsChanged;
        Root.KeyDown += OnRecordingKeyDown;
        Closed += (_, _) =>
        {
            SettingsHost.Changed -= OnSettingsChanged;
            SettingsHost.Persist();
        };
        App.TrackWindow(this);
    }

    private void SetInitialSize()
    {
        try
        {
            var work = DisplayArea.Primary.WorkArea;
            var w = Math.Min(880, work.Width);
            var h = Math.Min(720, work.Height);
            AppWindow.Resize(new SizeInt32(w, h));
            AppWindow.Move(new PointInt32(
                work.X + (work.Width - w) / 2,
                work.Y + (work.Height - h) / 2));
        }
        catch { /* 尺寸失败不影响功能 */ }
    }

    // ---------- 主题 ----------
    private void ApplyTheme()
    {
        _appliedTheme = SettingsHost.ResolveTheme();
        if (Content is FrameworkElement root)
            root.RequestedTheme = _appliedTheme == "light" ? ElementTheme.Light : ElementTheme.Dark;
        ApplyCaptionButtonTheme();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var theme = SettingsHost.ResolveTheme();
        if (theme != _appliedTheme)
        {
            ApplyTheme();
            BuildNav();
            BuildPage();
            return;
        }
        UpdatePreview();
    }

    private void ApplyCaptionButtonTheme()
    {
        try
        {
            var bar = AppWindow.TitleBar;
            var light = _appliedTheme == "light";
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

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var border = light ? 0x00F5F9FA : 0x00131414;
            _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        }
        catch { /* 个别系统版本不可用时忽略 */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // ---------- 导航 ----------
    private void BuildNav()
    {
        NavPanel.Children.Clear();
        _nav.Clear();
        AddNav("appearance", "外观");
        AddNav("reading", "阅读");
        AddNav("shortcuts", "快捷键");
        AddNav("files", "文件与最近");
        AddNav("about", "关于");
    }

    private void AddNav(string key, string label)
    {
        var bar = new Border
        {
            Width = 2,
            Height = 14,
            CornerRadius = new CornerRadius(1),
            Background = AccentBrush,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryBrush,
        };
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(bar);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        var button = new Button
        {
            Content = content,
            Style = Style("SettingsNavButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => SelectPage(key);
        NavPanel.Children.Add(button);
        _nav.Add(new NavItem(key, button, bar, text));
    }

    private void SelectPage(string key)
    {
        _page = key;
        foreach (var item in _nav)
        {
            var selected = item.Key == key;
            item.Bar.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            item.Text.Foreground = selected ? PrimaryBrush : SecondaryBrush;
            item.Button.Background = selected ? HoverBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        BuildPage();
    }

    // ---------- 通用构建 ----------
    private Style Style(string key) => (Style)Application.Current.Resources[key];

    private SolidColorBrush PrimaryBrush => new(_appliedTheme == "light"
        ? Windows.UI.Color.FromArgb(255, 0x1F, 0x1E, 0x1C)
        : Windows.UI.Color.FromArgb(255, 0xE8, 0xE6, 0xDC));

    private SolidColorBrush SecondaryBrush => new(_appliedTheme == "light"
        ? Windows.UI.Color.FromArgb(255, 0x6B, 0x67, 0x5F)
        : Windows.UI.Color.FromArgb(255, 0xA8, 0xA4, 0x9B));

    private SolidColorBrush AccentBrush => new(_appliedTheme == "light"
        ? Windows.UI.Color.FromArgb(255, 0xC9, 0x64, 0x42)
        : Windows.UI.Color.FromArgb(255, 0xE0, 0x8A, 0x6B));

    private SolidColorBrush HoverBrush => new(_appliedTheme == "light"
        ? Windows.UI.Color.FromArgb(0x0D, 0x1F, 0x1E, 0x1C)
        : Windows.UI.Color.FromArgb(0x0F, 0xE8, 0xE6, 0xDC));

    private SolidColorBrush BorderBrushValue => new(_appliedTheme == "light"
        ? Windows.UI.Color.FromArgb(255, 0xE6, 0xE2, 0xD8)
        : Windows.UI.Color.FromArgb(255, 0x2A, 0x28, 0x25));

    private SolidColorBrush SurfaceBrush => new(_appliedTheme == "light"
        ? Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF)
        : Windows.UI.Color.FromArgb(255, 0x1B, 0x1A, 0x18));

    private void AddPageHeader(string title, string desc)
    {
        PageHost.Children.Add(new TextBlock { Text = title, Style = Style("SettingsPageTitleStyle") });
        PageHost.Children.Add(new TextBlock { Text = desc, Style = Style("SettingsPageDescStyle") });
    }

    private void AddSection(string title)
        => PageHost.Children.Add(new TextBlock { Text = title, Style = Style("SettingsSectionStyle") });

    private StackPanel NewCard()
    {
        var stack = new StackPanel { Spacing = 2 };
        PageHost.Children.Add(new Border
        {
            Style = Style("SettingsCardStyle"),
            Child = stack,
        });
        return stack;
    }

    private void AddRow(StackPanel card, string label, string? desc, FrameworkElement control)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, Style = Style("SettingsLabelStyle") });
        if (!string.IsNullOrEmpty(desc))
            text.Children.Add(new TextBlock { Text = desc, Style = Style("SettingsDescStyle") });

        var grid = new Grid { ColumnSpacing = 24, Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        card.Children.Add(grid);
    }

    private void AddStackRow(StackPanel card, string label, string? desc, FrameworkElement control)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 9, 0, 9) };
        stack.Children.Add(new TextBlock { Text = label, Style = Style("SettingsLabelStyle") });
        if (!string.IsNullOrEmpty(desc))
            stack.Children.Add(new TextBlock { Text = desc, Style = Style("SettingsDescStyle") });
        stack.Children.Add(control);
        card.Children.Add(stack);
    }

    private ComboBox AddCombo(StackPanel card, string label, string? desc,
        IEnumerable<(string Label, string Value)> items, string current, Action<string> changed)
    {
        var list = items.ToList();
        var box = new ComboBox { Style = Style("SettingsComboBoxStyle") };
        foreach (var (text, value) in list)
            box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        var index = list.FindIndex(i => i.Value == current);
        box.SelectedIndex = index < 0 ? 0 : index;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem item && item.Tag is string value)
                changed(value);
        };
        AddRow(card, label, desc, box);
        return box;
    }

    private ToggleSwitch AddToggle(StackPanel card, string label, string? desc, bool isOn, Action<bool> changed)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            MinWidth = 0,
            OnContent = "",
            OffContent = "",
            Style = Style("SettingsToggleStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        AddRow(card, label, desc, toggle);
        return toggle;
    }

    private void AddSlider(StackPanel card, string label, string? desc,
        double min, double max, double step, double current, string format,
        Action<double> changed)
    {
        var value = new TextBlock
        {
            Style = Style("SettingsValueStyle"),
            Text = string.Format(format, current),
            MinWidth = 58,
            TextAlignment = TextAlignment.Right,
        };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = current,
            Width = 200,
            Style = Style("SettingsSliderStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = string.Format(format, e.NewValue);
            changed(e.NewValue);
        };
        var box = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.Children.Add(slider);
        box.Children.Add(value);
        AddRow(card, label, desc, box);
    }

    // ---------- 写入 ----------
    private void Commit(Func<AppSettings, AppSettings> change)
    {
        SettingsHost.Update(change);
        DebouncePersist();
    }

    private void DebouncePersist()
    {
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(250);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SettingsHost.Persist();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
