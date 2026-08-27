using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HdrToggle.Rules;

namespace HdrToggle.UI;

/// <summary>
/// Builds tray icons that badge the app icon with a status dot, so a glance at the
/// tray says whether the app is searching, holding rules on a running game, lifted
/// for a peek, or paused. Drawn at runtime — no extra .ico files to keep in sync.
/// </summary>
internal static class TrayIcons
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon ForState(Icon baseIcon, AppState state) => state switch
    {
        AppState.Paused => Build(baseIcon, Theme.TextDim, faded: true),
        AppState.Peeking => Build(baseIcon, Theme.Accent, faded: false),
        AppState.Active => Build(baseIcon, Theme.Good, faded: false),
        _ => Build(baseIcon, Theme.Idle, faded: false),
    };

    /// <summary>App icon plus a corner dot; <paramref name="faded"/> dims the art for "paused".</summary>
    private static Icon Build(Icon baseIcon, Color badge, bool faded)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawArt(g, baseIcon, size, faded);
            DrawBadge(g, size, badge);
        }

        // GetHicon hands back an unmanaged HICON we own; clone into a managed icon
        // with its own handle, then release ours.
        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawArt(Graphics g, Icon baseIcon, int size, bool faded)
    {
        // Pick the .ico frame closest to the tray size before scaling, so small icons stay sharp.
        using var sized = new Icon(baseIcon, size, size);
        using var art = sized.ToBitmap();
        var target = new Rectangle(0, 0, size, size);

        if (!faded)
        {
            g.DrawImage(art, target);
            return;
        }

        var matrix = new ColorMatrix { Matrix33 = 0.40f };
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        g.DrawImage(art, target, 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, attributes);
    }

    private static void DrawBadge(Graphics g, int size, Color badge)
    {
        // Inset by the ring width so the badge isn't clipped by the bitmap edge.
        float ring = size * 0.06f;
        float diameter = size * 0.44f;
        var dot = new RectangleF(size - diameter - ring, size - diameter - ring, diameter, diameter);

        // A dark ring lifts the dot off whatever the icon art is doing underneath.
        using (var ringBrush = new SolidBrush(Theme.Bg))
        {
            g.FillEllipse(ringBrush, RectangleF.Inflate(dot, ring, ring));
        }
        using var fill = new SolidBrush(badge);
        g.FillEllipse(fill, dot);
    }
}
