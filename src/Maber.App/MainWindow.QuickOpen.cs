using Maber.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Maber.App;

public sealed partial class MainWindow
{
    private sealed record QuickItem(string Title, string Subtitle, string Path);

    private static readonly Dictionary<string, (DateTime At, List<string> Files)> QuickDirCache =
        new(StringComparer.OrdinalIgnoreCase);

    private List<QuickItem> _quickItems = new();
    private List<string> _quickScanned = new();
    private string _quickQuery = "";

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".obsidian", ".trash", "$RECYCLE.BIN",
    };

    private void ShowQuickOpen()
    {
        if (!_ready) return;
        QuickOpenPopup.HorizontalOffset = Math.Max(0, (Root.ActualWidth - 560) / 2 - 12);
        QuickOpenPopup.VerticalOffset = 64;
        QuickOpenBox.Text = "";
        _quickQuery = "";
        _quickItems = new();
        _quickScanned = new();
        QuickOpenList.Items.Clear();
        QuickOpenPopup.IsOpen = true;
        QuickOpenBox.Focus(FocusState.Programmatic);
        RebuildQuickItems();
        _ = ScanCurrentDirectoryAsync();
    }

    private void QuickOpenBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _quickQuery = QuickOpenBox.Text;
        RebuildQuickItems();
    }

    private void QuickOpenBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                QuickOpenPopup.IsOpen = false;
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                OpenSelectedQuickItem();
                e.Handled = true;
                break;
            case VirtualKey.Down:
                MoveQuickSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveQuickSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveQuickSelection(int delta)
    {
        if (QuickOpenList.Items.Count == 0) return;
        var index = QuickOpenList.SelectedIndex;
        index = Math.Clamp(index + delta, 0, QuickOpenList.Items.Count - 1);
        if (index < 0) index = 0;
        QuickOpenList.SelectedIndex = index;
        QuickOpenList.ScrollIntoView(QuickOpenList.Items[index]);
    }

    private void OpenSelectedQuickItem()
    {
        var index = QuickOpenList.SelectedIndex >= 0 ? QuickOpenList.SelectedIndex : 0;
        if (index >= _quickItems.Count) return;
        OpenQuickItem(_quickItems[index]);
    }

    private void QuickOpenList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StackPanel { Tag: QuickItem item }) OpenQuickItem(item);
    }

    private async void OpenQuickItem(QuickItem item)
    {
        QuickOpenPopup.IsOpen = false;
        if (!File.Exists(item.Path))
        {
            ShowBar("文件已不存在");
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        await LoadFileAsync(item.Path);
    }

    private void RebuildQuickItems()
    {
        var query = _quickQuery.Trim().Trim('"');
        var items = new List<(QuickItem Item, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 输入的是可解析路径时优先置顶
        var direct = ResolveQuickPath(query);
        if (direct is not null)
        {
            items.Add((new QuickItem(Path.GetFileName(direct), "打开路径 · " + Path.GetDirectoryName(direct), direct), 1000));
            seen.Add(direct);
        }

        foreach (var path in SettingsHost.Current.RecentFiles)
        {
            if (!seen.Add(path)) continue;
            var score = Math.Max(Score(Path.GetFileName(path), query), Score(path, query) - 20);
            if (score < 0) continue;
            items.Add((new QuickItem(Path.GetFileName(path), Path.GetDirectoryName(path) ?? "", path), score + 100));
        }

        foreach (var path in _quickScanned)
        {
            if (!seen.Add(path)) continue;
            var score = Math.Max(Score(Path.GetFileName(path), query), Score(path, query) - 20);
            if (score < 0) continue;
            items.Add((new QuickItem(Path.GetFileName(path), Path.GetDirectoryName(path) ?? "", path), score));
        }

        _quickItems = items
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(60)
            .Select(x => x.Item)
            .ToList();

        QuickOpenList.Items.Clear();
        foreach (var item in _quickItems)
        {
            var stack = new StackPanel { Spacing = 1, Padding = new Thickness(2, 2, 2, 2), Tag = item };
            stack.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Subtitle,
                FontSize = 11,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            QuickOpenList.Items.Add(stack);
        }

        if (_quickItems.Count > 0)
        {
            QuickOpenList.SelectedIndex = 0;
            QuickOpenHint.Text = "↑↓ 选择 · Enter 打开 · Esc 关闭";
        }
        else
        {
            QuickOpenHint.Text = query.Length == 0
                ? "输入文件名、路径关键词，或直接粘贴完整路径"
                : "没有匹配的文件";
        }
    }

    private string? ResolveQuickPath(string raw)
    {
        if (raw.Length == 0) return null;
        try
        {
            if (Path.IsPathRooted(raw) && File.Exists(raw) && IsQuickOpenFile(raw))
                return Path.GetFullPath(raw);
            if (_doc is not null)
            {
                var dir = Path.GetDirectoryName(_doc.Value.Path);
                if (dir is not null)
                {
                    var candidate = Path.GetFullPath(Path.Combine(dir, raw));
                    if (File.Exists(candidate) && IsQuickOpenFile(candidate)) return candidate;
                }
            }
        }
        catch
        {
            // 路径非法时当作普通关键词
        }
        return null;
    }

    private static bool IsQuickOpenFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ScanCurrentDirectoryAsync()
    {
        var docPath = _doc is null ? null : _doc.Value.Path;
        if (docPath is null) return;
        var dir = Path.GetDirectoryName(docPath);
        if (dir is null) return;

        if (QuickDirCache.TryGetValue(dir, out var cached) &&
            (DateTime.UtcNow - cached.At).TotalSeconds < 60)
        {
            _quickScanned = cached.Files;
            RebuildQuickItems();
            return;
        }

        List<string> files;
        try
        {
            files = await Task.Run(() => ScanDirectory(dir, depth: 2, cap: 2000));
        }
        catch
        {
            return;
        }
        QuickDirCache[dir] = (DateTime.UtcNow, files);
        if (!QuickOpenPopup.IsOpen) return;
        _quickScanned = files;
        RebuildQuickItems();
    }

    private static List<string> ScanDirectory(string root, int depth, int cap)
    {
        var result = new List<string>();
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0 && result.Count < cap)
        {
            var (dir, level) = queue.Dequeue();
            try
            {
                // 只扫描 Markdown；txt 仍可通过路径直接打开，避免把临时目录里的文本全列出来
                foreach (var pattern in new[] { "*.md", "*.markdown" })
                {
                    foreach (var file in Directory.EnumerateFiles(dir, pattern))
                    {
                        result.Add(file);
                        if (result.Count >= cap) break;
                    }
                    if (result.Count >= cap) break;
                }
            }
            catch
            {
                continue; // 无权限目录跳过
            }

            if (level >= depth) continue;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (SkipDirs.Contains(Path.GetFileName(sub))) continue;
                    queue.Enqueue((sub, level + 1));
                }
            }
            catch
            {
                // 同上
            }
        }
        return result;
    }

    /// <summary>0 以上为匹配分：前缀 &gt; 包含 &gt; 子序列；-1 表示不匹配</summary>
    private static int Score(string text, string query)
    {
        if (query.Length == 0) return 1;
        var t = text.ToLowerInvariant();
        var q = query.ToLowerInvariant();
        if (t.StartsWith(q, StringComparison.Ordinal)) return 100;
        var index = t.IndexOf(q, StringComparison.Ordinal);
        if (index >= 0) return 80 - Math.Min(index, 40);

        var ti = 0;
        var qi = 0;
        var gaps = 0;
        while (ti < t.Length && qi < q.Length)
        {
            if (t[ti] == q[qi]) qi++;
            else gaps++;
            ti++;
        }
        if (qi < q.Length) return -1;
        return Math.Max(5, 60 - gaps);
    }
}
