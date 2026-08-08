using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HdrToggle.UI;

/// <summary>Dark card/tile theme shared by the custom controls.</summary>
internal static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#14161D");
    public static readonly Color Sidebar = ColorTranslator.FromHtml("#181B24");
    public static readonly Color Card = ColorTranslator.FromHtml("#1F2430");
    public static readonly Color CardHover = ColorTranslator.FromHtml("#252B3A");
    public static readonly Color CardSelected = ColorTranslator.FromHtml("#2A3145");
    public static readonly Color Field = ColorTranslator.FromHtml("#262C3B");
    public static readonly Color Border = ColorTranslator.FromHtml("#2C3342");
    public static readonly Color Accent = ColorTranslator.FromHtml("#FFA028");
    public static readonly Color AccentSoft = ColorTranslator.FromHtml("#5A4520");
    public static readonly Color Text = ColorTranslator.FromHtml("#ECEEF4");
    public static readonly Color TextDim = ColorTranslator.FromHtml("#98A0B0");
    public static readonly Color Danger = ColorTranslator.FromHtml("#E5484D");
    public static readonly Color Good = ColorTranslator.FromHtml("#46C878");
    public static readonly Color TrackOff = ColorTranslator.FromHtml("#3A4152");

    public static readonly Font Base = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Header = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Section = new("Segoe UI Semibold", 12f);
    public static readonly Font Title = new("Segoe UI Semibold", 14.25f);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Makes the native title bar dark (Windows 10 20H1+).</summary>
    public static void EnableDarkTitleBar(IntPtr handle)
    {
        int on = 1;
        _ = DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }
}
