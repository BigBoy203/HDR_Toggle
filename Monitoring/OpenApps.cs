using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HdrToggle.Monitoring;

/// <summary>A running program with a window of its own — a candidate for the tray's quick add.</summary>
public sealed record OpenApp(string Name, string ExePath, string ProcessName, string WindowTitle);

/// <summary>
/// Lists the programs that have a visible window open, leaving out Windows' own shell
/// pieces and this app. This is what the tray flyout offers to add as a game.
/// </summary>
public static class OpenApps
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Windowed processes that are never a game, even though they have a window.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp", "Taskmgr", "dwm",
        "SecurityHealthSystray", "CalculatorApp", "WindowsTerminal", "conhost", "cmd", "powershell",
        "pwsh", "msedgewebview2",
    };

    /// <summary>
    /// One entry per program (not per process), sorted by name. <paramref name="exclude"/>
    /// takes process names to leave out — the games already in the library.
    /// </summary>
    public static List<OpenApp> List(ISet<string> exclude)
    {
        var result = new Dictionary<string, OpenApp>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.MainWindowHandle == IntPtr.Zero)
                    continue;
                string title = p.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                string name = p.ProcessName;
                if (Ignored.Contains(name) || exclude.Contains(name) || result.ContainsKey(name))
                    continue;

                string? path = ImagePath(p.Id);
                if (path is null || path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                result[name] = new OpenApp(FriendlyName(path), path, name, title);
            }
            catch
            {
                // Gone already, or not ours to look at.
            }
            finally
            {
                p.Dispose();
            }
        }

        return result.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The .exe behind a process. Asks for the limited query right only, which is granted
    /// for far more processes than <see cref="Process.MainModule"/> can read.
    /// </summary>
    private static string? ImagePath(int pid)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The program's own description ("Grand Theft Auto IV"), else its file name.</summary>
    private static string FriendlyName(string path)
    {
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description) && description.Length <= 60)
                return description;
        }
        catch
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
