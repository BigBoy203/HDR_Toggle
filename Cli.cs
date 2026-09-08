using System.Runtime.InteropServices;
using HdrToggle.Display;

namespace HdrToggle;

/// <summary>
/// Minimal command-line mode for testing the display layer:
///   HdrToggle --list                 list displays, HDR state and refresh rates
///   HdrToggle --set &lt;index&gt; on|off   toggle HDR on a display
///   HdrToggle --rate &lt;index&gt; &lt;hz&gt;    switch a display's refresh rate
///   HdrToggle --fps &lt;exe&gt; [n|off]    set the NVIDIA per-game frame rate limit; with no
///                                    value, dumps every setting on the game's profile
/// </summary>
internal static class Cli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public static void Run(string[] args)
    {
        if (!AttachConsole(-1))
            AllocConsole();

        try
        {
            if (args[0] == "--fps")
            {
                RunFps(args);
                return;
            }

            var displays = HdrController.GetDisplays();

            if (args[0] == "--list")
            {
                Console.WriteLine();
                for (int i = 0; i < displays.Count; i++)
                {
                    var d = displays[i];
                    Console.WriteLine($"[{i}] {d.FriendlyName}");
                    Console.WriteLine($"    Path:     {d.DevicePath}");
                    Console.WriteLine($"    HDR:      {(d.SupportsHdr ? (d.HdrEnabled ? "supported, ON" : "supported, OFF") : "not supported")}");
                    var rates = RefreshRateController.GetAvailableRates(d.GdiDeviceName);
                    Console.WriteLine($"    Refresh:  {(d.RefreshHz > 0 ? d.RefreshHz + " Hz" : "unknown")}"
                        + (rates.Count > 0 ? $" (available: {string.Join(", ", rates)})" : ""));
                }
            }
            else if (args[0] == "--set" && args.Length >= 3 && int.TryParse(args[1], out int idx)
                     && idx >= 0 && idx < displays.Count)
            {
                bool on = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
                bool ok = HdrController.SetHdr(displays[idx], on);
                Console.WriteLine();
                Console.WriteLine($"Set HDR {(on ? "ON" : "OFF")} for {displays[idx].FriendlyName}: {(ok ? "success" : "FAILED")}");
                var after = HdrController.GetDisplays();
                if (idx < after.Count)
                    Console.WriteLine($"State now: {(after[idx].HdrEnabled ? "ON" : "OFF")}");
            }
            else if (args[0] == "--rate" && args.Length >= 3 && int.TryParse(args[1], out int rateIdx)
                     && rateIdx >= 0 && rateIdx < displays.Count && int.TryParse(args[2], out int hz))
            {
                var display = displays[rateIdx];
                bool ok = RefreshRateController.SetRate(display.GdiDeviceName, hz);
                Console.WriteLine();
                Console.WriteLine($"Set {hz} Hz for {display.FriendlyName}: {(ok ? "success" : "FAILED")}");
                Console.WriteLine($"Rate now: {RefreshRateController.GetCurrentRate(display.GdiDeviceName)} Hz");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Usage: HdrToggle --list | --set <index> on|off | --rate <index> <hz>"
                    + " | --fps <exe> [n|off]");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
        }
        Console.Out.Flush();
    }

    /// <summary>
    /// Reads or writes the NVIDIA driver's per-game frame rate limit, so the thing that
    /// actually caps a game that ignores V-Sync can be checked without the GUI.
    /// </summary>
    private static void RunFps(string[] args)
    {
        Console.WriteLine();
        if (!NvidiaFrameLimiter.IsAvailable)
        {
            Console.WriteLine($"NVIDIA frame rate limiting unavailable: {NvidiaFrameLimiter.UnavailableReason}");
            return;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: HdrToggle --fps <exe> [n|off]      e.g. HdrToggle --fps GTAIV.exe 60");
            return;
        }

        string exe = Path.GetFileName(args[1]);

        if (args.Length >= 3)
        {
            bool off = args[2].Equals("off", StringComparison.OrdinalIgnoreCase) || args[2] == "0";
            if (!off && !int.TryParse(args[2], out _))
            {
                Console.WriteLine($"'{args[2]}' is not a frame rate. Use a number, or 'off'.");
                return;
            }
            int fps = off ? 0 : int.Parse(args[2]);
            NvidiaFrameLimiter.TrySetLimit(exe, fps, out string message);
            Console.WriteLine(message);
        }

        Console.WriteLine(NvidiaFrameLimiter.GetLimit(exe) switch
        {
            null => $"Could not read the driver limit for {exe}",
            0 => $"{exe}: no driver frame rate limit",
            int limit => $"{exe}: driver frame rate limit is {limit} FPS",
        });

        // The full dump is what identifies the setting the control panel really writes.
        Console.WriteLine();
        Console.WriteLine(NvidiaFrameLimiter.DescribeProfile(exe));
    }
}
