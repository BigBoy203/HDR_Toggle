using System.Diagnostics;
using HdrToggle.Rules;

namespace HdrToggle.Monitoring;

/// <summary>
/// Polls the process list every 2 seconds and raises events when a profiled
/// game starts or when its last matching process exits.
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Func<IReadOnlyList<GameProfile>> _profilesProvider;
    private readonly Func<bool> _isPaused;
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    public event Action<GameProfile>? GameStarted;
    public event Action<GameProfile>? GameStopped;

    public ProcessWatcher(Func<IReadOnlyList<GameProfile>> profilesProvider, Func<bool> isPaused)
    {
        _profilesProvider = profilesProvider;
        _isPaused = isPaused;
        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    /// <summary>Process names (no extension) currently running, matched case-insensitively.</summary>
    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            finally { p.Dispose(); }
        }
        return names;
    }

    public void Poll()
    {
        if (_isPaused())
            return;

        List<GameProfile> started = new(), stopped = new();
        HashSet<string>? names = null;

        foreach (var profile in _profilesProvider())
        {
            if (!profile.Enabled || string.IsNullOrEmpty(profile.ProcessName))
                continue;

            names ??= GetRunningProcessNames();
            bool isRunning = names.Contains(profile.ProcessName);
            bool wasRunning = _running.Contains(profile.ProcessName);

            if (isRunning && !wasRunning)
            {
                _running.Add(profile.ProcessName);
                started.Add(profile);
            }
            else if (!isRunning && wasRunning)
            {
                _running.Remove(profile.ProcessName);
                stopped.Add(profile);
            }
        }

        foreach (var p in started) GameStarted?.Invoke(p);
        foreach (var p in stopped) GameStopped?.Invoke(p);
    }

    public void Dispose() => _timer.Dispose();
}
