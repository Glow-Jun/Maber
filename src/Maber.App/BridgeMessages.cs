namespace Maber.App;

/// <summary>与 src/Maber.Web/src/types.ts 的消息协议逐字段对齐的唯一权威。</summary>
public static class BridgeMessages
{
    // 宿主 → Web
    public const string LoadDocument = "loadDocument";
    public const string SetMode = "setMode";
    public const string SetTheme = "setTheme";
    public const string SetTypography = "setTypography";
    public const string SetEmptyState = "setEmptyState";
    public const string ScrollToHeading = "scrollToHeading";
    public const string Find = "find";

    // Web → 宿主
    public const string Ready = "ready";
    public const string Accelerator = "accelerator";
    public const string OutlineChanged = "outlineChanged";
    public const string CheckboxToggled = "checkboxToggled";
    public const string ContentChanged = "contentChanged";
    public const string OpenLink = "openLink";
    public const string OpenWikilink = "openWikilink";
    public const string Diag = "diag";
}
