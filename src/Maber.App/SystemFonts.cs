using System.Runtime.InteropServices;

namespace Maber.App;

/// <summary>枚举系统已安装字体族（GDI，只读取名字，不加载字体文件）</summary>
public static class SystemFonts
{
    private static IReadOnlyList<string>? _cache;

    public static IReadOnlyList<string> All()
    {
        if (_cache is not null) return _cache;
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var hdc = GetDC(IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            try
            {
                var lf = new LOGFONT { lfCharSet = 1, lfFaceName = "" }; // DEFAULT_CHARSET
                EnumFontFamiliesExW(hdc, ref lf, (ref LOGFONT font, IntPtr _, uint _, IntPtr _) =>
                {
                    var name = font.lfFaceName;
                    if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) names.Add(name);
                    return 1;
                }, IntPtr.Zero, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        _cache = names.ToList();
        return _cache;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    private delegate int EnumFontProc(ref LOGFONT font, IntPtr textMetric, uint fontType, IntPtr lParam);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumFontFamiliesExW")]
    private static extern int EnumFontFamiliesExW(IntPtr hdc, ref LOGFONT logfont,
        EnumFontProc callback, IntPtr lParam, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}
