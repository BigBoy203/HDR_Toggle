using System.Runtime.InteropServices;
using HdrToggle.Display;

namespace HdrToggle;

/// <summary>
/// Minimal command-line mode for testing the display layer:
///   HdrToggle --list                 list displays and HDR state
///   HdrToggle --set &lt;index&gt; on|off   toggle HDR on a display
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
            else
            {
                Console.WriteLine();
                Console.WriteLine("Usage: HdrToggle --list | --set <index> on|off");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
        }
        Console.Out.Flush();
    }
}
