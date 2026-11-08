using Maber.Core;
using Xunit;

namespace Maber.Core.Tests;

public class ShortcutGestureTests
{
    [Theory]
    [InlineData("ctrl+o", "Ctrl+O")]
    [InlineData("Ctrl+Shift+P", "Ctrl+Shift+P")]
    [InlineData("shift+ctrl+alt+F12", "Ctrl+Alt+Shift+F12")]
    [InlineData("Ctrl+,", "Ctrl+,")]
    [InlineData("Alt+Left", "Alt+Left")]
    public void TryNormalize_AcceptsValidGestures(string raw, string expected)
    {
        Assert.True(ShortcutGesture.TryNormalize(raw, out var gesture));
        Assert.Equal(expected, gesture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+O+P")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Win+O")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+")]
    public void TryNormalize_RejectsInvalidGestures(string raw)
    {
        Assert.False(ShortcutGesture.TryNormalize(raw, out _));
    }

    [Fact]
    public void Parse_SplitsModifiersAndKey()
    {
        var (ctrl, alt, shift, key) = ShortcutGesture.Parse("shift+ctrl+F5");
        Assert.True(ctrl);
        Assert.False(alt);
        Assert.True(shift);
        Assert.Equal("F5", key);
    }

    [Fact]
    public void Resolve_UsesOverrideOrFallsBackToDefault()
    {
        var overrides = new Dictionary<string, string> { ["quickOpen"] = "ctrl+shift+p" };
        Assert.Equal("Ctrl+Shift+P", ShortcutCommands.Resolve(overrides, ShortcutCommands.QuickOpen));
        Assert.Equal("Ctrl+O", ShortcutCommands.Resolve(overrides, ShortcutCommands.Open));
        Assert.Equal("Ctrl+O", ShortcutCommands.Resolve(new Dictionary<string, string>(), ShortcutCommands.Open));
    }

    [Fact]
    public void ResolveAll_ContainsEveryCommandOnce()
    {
        var all = ShortcutCommands.ResolveAll(null);
        Assert.Equal(ShortcutCommands.All.Count, all.Count);
        foreach (var command in ShortcutCommands.All)
            Assert.Equal(command.DefaultGesture, all[command.Id]);
    }
}
