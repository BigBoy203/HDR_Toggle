using System.Runtime.InteropServices;

namespace HdrToggle.UI;

/// <summary>Pulls an .exe's own icon out at a usable size, for the library and the tray flyout.</summary>
internal static class GameIcons
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>The .exe's first icon at <paramref name="size"/> px, or the generic app icon. Never null.</summary>
    public static Image Load(string exePath, int size = 64)
    {
        Image? img = null;
        try
        {
            // High-res extraction first, falling back to the classic 32px API.
            if (SHDefExtractIcon(exePath, 0, 0, out IntPtr hLarge, out IntPtr hSmall, (uint)size) == 0 && hLarge != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(hLarge);
                img = icon.ToBitmap();
                DestroyIcon(hLarge);
                if (hSmall != IntPtr.Zero) DestroyIcon(hSmall);
            }
        }
        catch
        {
        }
        if (img is null)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                img = icon?.ToBitmap();
            }
            catch
            {
            }
        }
        return img ?? SystemIcons.Application.ToBitmap();
    }
}
