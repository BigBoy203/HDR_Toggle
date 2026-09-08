using System.Runtime.InteropServices;

namespace HdrToggle.Display;

/// <summary>
/// Reads and sets a display's refresh rate through the GDI display-mode API.
///
/// Nothing here touches the game itself — the app can't limit a process's frame rate
/// from the outside. What it can do is lower the ceiling: anything that presents in
/// sync with the display (V-Sync on, or a fullscreen/borderless swapchain the
/// compositor paces) can't run faster than the panel refreshes. Dropping a 144 Hz
/// monitor to 60 Hz for the duration of a game is the practical way to keep an engine
/// whose physics is tied to frame rate inside the range it was written for.
/// </summary>
public static class RefreshRateController
{
    private const int ENUM_CURRENT_SETTINGS = -1;

    private const uint DM_BITSPERPEL = 0x00040000;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;
    private const uint DM_DISPLAYFREQUENCY = 0x00400000;

    private const uint DM_INTERLACED = 0x00000002; // dmDisplayFlags

    private const uint CDS_UPDATEREGISTRY = 0x00000001;
    private const uint CDS_TEST = 0x00000002;

    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    private static DEVMODE NewDevMode() => new()
    {
        dmDeviceName = "",
        dmFormName = "",
        dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
    };

    private static bool TryGetCurrentMode(string gdiDeviceName, out DEVMODE mode)
    {
        mode = NewDevMode();
        return !string.IsNullOrEmpty(gdiDeviceName)
            && EnumDisplaySettingsEx(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref mode, 0);
    }

    /// <summary>Refresh rate the display is running at right now, in Hz; 0 if it can't be read.</summary>
    public static int GetCurrentRate(string gdiDeviceName) =>
        TryGetCurrentMode(gdiDeviceName, out var mode) ? (int)mode.dmDisplayFrequency : 0;

    /// <summary>
    /// Refresh rates the display offers at its current resolution and colour depth,
    /// ascending. Only those are listed: a cap must never change the resolution out
    /// from under the game.
    /// </summary>
    public static List<int> GetAvailableRates(string gdiDeviceName)
    {
        var rates = new SortedSet<int>();
        if (!TryGetCurrentMode(gdiDeviceName, out var current))
            return new List<int>();

        for (int i = 0; ; i++)
        {
            // dmSize is an input to the call, so the struct is rebuilt every time.
            var mode = NewDevMode();
            if (!EnumDisplaySettingsEx(gdiDeviceName, i, ref mode, 0))
                break;
            if (mode.dmPelsWidth != current.dmPelsWidth || mode.dmPelsHeight != current.dmPelsHeight
                || mode.dmBitsPerPel != current.dmBitsPerPel)
                continue;
            if ((mode.dmDisplayFlags & DM_INTERLACED) != 0)
                continue;
            // 0 and 1 are the driver's "hardware default" placeholders, not real rates.
            if (mode.dmDisplayFrequency > 1)
                rates.Add((int)mode.dmDisplayFrequency);
        }

        // The rate in use is always a legal choice, even if it never came back from the
        // enumeration (some drivers hide the custom/overclocked mode they are running).
        if (current.dmDisplayFrequency > 1)
            rates.Add((int)current.dmDisplayFrequency);

        return rates.ToList();
    }

    /// <summary>
    /// Switches the display to <paramref name="hz"/>, keeping resolution and colour
    /// depth as they are. Returns true if the display is already there or got there.
    /// </summary>
    public static bool SetRate(string gdiDeviceName, int hz)
    {
        if (hz <= 0 || !TryGetCurrentMode(gdiDeviceName, out var mode))
            return false;
        if (mode.dmDisplayFrequency == (uint)hz)
            return true;

        mode.dmDisplayFrequency = (uint)hz;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;

        // Ask first: a rate the panel can't take would otherwise be a black screen.
        if (ChangeDisplaySettingsEx(gdiDeviceName, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL)
            return false;

        return ChangeDisplaySettingsEx(gdiDeviceName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>Switches the display with the given monitor device path (if connected). Returns true on success.</summary>
    public static bool SetRateByPath(string devicePath, int hz)
    {
        var display = HdrController.GetDisplays().FirstOrDefault(d => d.DevicePath == devicePath);
        return display is not null && SetRate(display.GdiDeviceName, hz);
    }

    /// <summary>Current refresh rate of every display that reports one, keyed by device path.</summary>
    public static Dictionary<string, int> SnapshotRates() =>
        HdrController.GetDisplays().Where(d => d.RefreshHz > 0).ToDictionary(d => d.DevicePath, d => d.RefreshHz);
}
