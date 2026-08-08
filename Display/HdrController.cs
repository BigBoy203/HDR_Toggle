using static HdrToggle.Display.DisplayApi;

namespace HdrToggle.Display;

/// <summary>A connected display and its current HDR capability/state.</summary>
public sealed class DisplayInfo
{
    public required string DevicePath { get; init; }   // stable across reboots; used as rule key
    public required string FriendlyName { get; init; }
    public string GdiDeviceName { get; init; } = "";   // e.g. \\.\DISPLAY1 — matches Screen.DeviceName
    internal LUID AdapterId { get; init; }
    internal uint TargetId { get; init; }
    public bool SupportsHdr { get; init; }
    public bool HdrEnabled { get; init; }
}

/// <summary>Enumerates displays and gets/sets per-display HDR using the CCD API.</summary>
public static class HdrController
{
    public static List<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();

        int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes);
        if (err != ERROR_SUCCESS)
            throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: {err}");

        var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
        var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
        err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
        if (err != ERROR_SUCCESS)
            throw new InvalidOperationException($"QueryDisplayConfig failed: {err}");

        for (int i = 0; i < numPaths; i++)
        {
            var target = paths[i].targetInfo;

            var nameReq = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = MakeHeader<DISPLAYCONFIG_TARGET_DEVICE_NAME>(GET_TARGET_NAME, target.adapterId, target.id),
            };
            if (DisplayConfigGetDeviceInfo(ref nameReq) != ERROR_SUCCESS)
                continue;

            var sourceReq = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = MakeHeader<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(GET_SOURCE_NAME, paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id),
            };
            string gdiName = DisplayConfigGetDeviceInfo(ref sourceReq) == ERROR_SUCCESS ? sourceReq.viewGdiDeviceName : "";

            bool supportsHdr = false, hdrEnabled = false;

            var info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
            {
                header = MakeHeader<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(GET_ADVANCED_COLOR_INFO_2, target.adapterId, target.id),
            };
            if (DisplayConfigGetDeviceInfo(ref info2) == ERROR_SUCCESS)
            {
                supportsHdr = info2.HighDynamicRangeSupported;
                hdrEnabled = info2.HighDynamicRangeUserEnabled;
            }
            else
            {
                // Pre-24H2 fallback
                var info1 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = MakeHeader<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(GET_ADVANCED_COLOR_INFO, target.adapterId, target.id),
                };
                if (DisplayConfigGetDeviceInfo(ref info1) == ERROR_SUCCESS)
                {
                    supportsHdr = info1.AdvancedColorSupported && !info1.AdvancedColorForceDisabled;
                    hdrEnabled = info1.AdvancedColorEnabled;
                }
            }

            string name = string.IsNullOrWhiteSpace(nameReq.monitorFriendlyDeviceName)
                ? $"Display {displays.Count + 1}"
                : nameReq.monitorFriendlyDeviceName;

            displays.Add(new DisplayInfo
            {
                DevicePath = nameReq.monitorDevicePath,
                FriendlyName = name,
                GdiDeviceName = gdiName,
                AdapterId = target.adapterId,
                TargetId = target.id,
                SupportsHdr = supportsHdr,
                HdrEnabled = hdrEnabled,
            });
        }

        return displays;
    }

    /// <summary>Sets HDR for a display. Returns true on success.</summary>
    public static bool SetHdr(DisplayInfo display, bool enable)
    {
        var hdrReq = new DISPLAYCONFIG_SET_HDR_STATE
        {
            header = MakeHeader<DISPLAYCONFIG_SET_HDR_STATE>(SET_HDR_STATE, display.AdapterId, display.TargetId),
            value = enable ? 1u : 0u,
        };
        if (DisplayConfigSetDeviceInfo(ref hdrReq) == ERROR_SUCCESS)
            return true;

        // Pre-24H2 fallback
        var acReq = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            header = MakeHeader<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(SET_ADVANCED_COLOR_STATE, display.AdapterId, display.TargetId),
            value = enable ? 1u : 0u,
        };
        return DisplayConfigSetDeviceInfo(ref acReq) == ERROR_SUCCESS;
    }

    /// <summary>Sets HDR for the display with the given device path (if connected and HDR-capable). Returns true on success.</summary>
    public static bool SetHdrByPath(string devicePath, bool enable)
    {
        var display = GetDisplays().FirstOrDefault(d => d.DevicePath == devicePath && d.SupportsHdr);
        return display is not null && SetHdr(display, enable);
    }

    /// <summary>Current HDR on/off state of every HDR-capable display, keyed by device path.</summary>
    public static Dictionary<string, bool> SnapshotStates() =>
        GetDisplays().Where(d => d.SupportsHdr).ToDictionary(d => d.DevicePath, d => d.HdrEnabled);
}
