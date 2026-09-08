using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HdrToggle.UI;

/// <summary>Dark card/tile theme shared by the custom controls.</summary>
internal static class Theme
{
    // Black and orange, taken from app.ico: its body is #181A26 and its mark runs
    // #FFA028 → #FFBE47. The greys are kept off the blue side of neutral so the orange
    // stays the only colour with any temperature to it.
    public static readonly Color Bg = ColorTranslator.FromHtml("#0A0B0F");
    public static readonly Color Sidebar = ColorTranslator.FromHtml("#07080B");
    public static readonly Color Card = ColorTranslator.FromHtml("#111319");
    public static readonly Color CardHover = ColorTranslator.FromHtml("#191C24");
    public static readonly Color CardSelected = ColorTranslator.FromHtml("#241D12"); // warm, under the accent marker
    public static readonly Color Field = ColorTranslator.FromHtml("#15171F");
    public static readonly Color Border = ColorTranslator.FromHtml("#22252F");

    /// <summary>Top of the hero panel's wash, fading into <see cref="Bg"/>.</summary>
    public static readonly Color HeroTop = ColorTranslator.FromHtml("#171922");
    public static readonly Color Accent = ColorTranslator.FromHtml("#FFA028");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#FFB23E");
    public static readonly Color AccentSoft = ColorTranslator.FromHtml("#4A3517");
    public static readonly Color Text = ColorTranslator.FromHtml("#F1EFEA");
    public static readonly Color TextDim = ColorTranslator.FromHtml("#9A99A2");
    public static readonly Color Danger = ColorTranslator.FromHtml("#E5484D");
    public static readonly Color Good = ColorTranslator.FromHtml("#46C878");
    public static readonly Color TrackOff = ColorTranslator.FromHtml("#32353F");

    /// <summary>Tray dot for "watching". Left blue on purpose: the four status dots have to
    /// stay apart from each other, and amber is already the peek dot.</summary>
    public static readonly Color Idle = ColorTranslator.FromHtml("#5A8DEE");

    public static readonly Font Base = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Header = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Section = new("Segoe UI Semibold", 12f);
    public static readonly Font Title = new("Segoe UI Semibold", 14.25f);

    /// <summary>Vertical two-stop wash, for the hero panel behind a game's name.</summary>
    public static Brush VerticalWash(Rectangle bounds, Color top, Color bottom) =>
        new LinearGradientBrush(bounds with { Y = bounds.Y - 1, Height = bounds.Height + 2 }, top, bottom, 90f);

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
