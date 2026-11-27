using Microsoft.Win32;

namespace HdrToggle;

/// <summary>Manages the "start with Windows" registry entry (HKCU Run key, no admin needed).</summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HdrToggle";

    /// <summary><c>--tray</c> keeps the window closed, so the app starts in the tray.</summary>
    private static string Command(bool hidden) =>
        $"\"{Updater.ExePath}\"" + (hidden ? " --tray" : "");

    public static void SetEnabled(bool enabled, bool hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, Command(hidden));
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Re-writes the entry if enabled (keeps the path current if the exe moved).</summary>
    public static void Sync(bool enabled, bool hidden)
    {
        try { SetEnabled(enabled, hidden); }
        catch (Exception ex) { Rules.Logger.Log($"Autostart sync failed: {ex.Message}"); }
    }
}
