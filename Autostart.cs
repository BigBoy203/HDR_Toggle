using Microsoft.Win32;

namespace HdrToggle;

/// <summary>Manages the "start with Windows" registry entry (HKCU Run key, no admin needed).</summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HdrToggle";

    private static string Command => $"\"{Application.ExecutablePath}\" --tray";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Re-writes the entry if enabled (keeps the path current if the exe moved).</summary>
    public static void Sync(bool enabled)
    {
        try { SetEnabled(enabled); }
        catch (Exception ex) { Rules.Logger.Log($"Autostart sync failed: {ex.Message}"); }
    }
}
