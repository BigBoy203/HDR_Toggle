using HdrToggle.Display;

namespace HdrToggle.Rules;

/// <summary>What the app is doing right now, in the order the tray icon prioritises it.</summary>
public enum AppState
{
    /// <summary>Automation is switched off entirely.</summary>
    Paused,

    /// <summary>Idle, polling for a profiled game to appear.</summary>
    Watching,

    /// <summary>A profiled game is running and its rules are applied.</summary>
    Active,

    /// <summary>Overlays are lifted so the other monitors can be glanced at.</summary>
    Peeking,
}

/// <summary>
/// Applies launch rules when a profiled game starts and exit rules (default:
/// restore the pre-launch HDR state) when the last profiled game stops. Any
/// refresh-rate cap the launch rules applied is always lifted on the way out.
/// </summary>
public sealed class RuleEngine
{
    private readonly AppConfig _config;
    private readonly Action _save;
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly OverlayManager _overlays = new();

    /// <summary>Transient one-off message for the status bar.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Raised whenever <see cref="State"/> may have changed, for the tray icon and UI.</summary>
    public event Action? StateChanged;

    public RuleEngine(AppConfig config, Action save)
    {
        _config = config;
        _save = save;
    }

    public bool GameActive => _active.Count > 0;

    /// <summary>Stops the watcher from acting on anything until switched back on.</summary>
    public bool Paused
    {
        get => _config.Paused;
        set
        {
            if (_config.Paused == value)
                return;
            _config.Paused = value;
            _save();
            Logger.Log(value ? "Automation paused." : "Automation resumed.");
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Master switch for the "while playing" overlays. Turning it off drops them
    /// instantly for a quick look at the other monitors; turning it back on restores
    /// them. Deliberately not persisted — a peek is a moment, not a setting — and it
    /// re-arms itself when the last profiled game exits.
    /// </summary>
    public bool OverlaysEnabled
    {
        get => _overlays.Enabled;
        set
        {
            if (_overlays.Enabled == value)
                return;
            _overlays.Enabled = value;
            StateChanged?.Invoke();
            StatusChanged?.Invoke(value ? "Overlays restored" : "Overlays lifted — press again to restore");
        }
    }

    /// <summary>Flips the overlay master switch; wired to the tray menu and the peek hotkey.</summary>
    public void ToggleOverlays() => OverlaysEnabled = !OverlaysEnabled;

    public AppState State =>
        _config.Paused ? AppState.Paused :
        !OverlaysEnabled ? AppState.Peeking :
        GameActive ? AppState.Active :
        AppState.Watching;

    /// <summary>One-line description of <see cref="State"/> for the status bar, tray tip and menu.</summary>
    public string StatusText
    {
        get
        {
            if (_config.Paused)
                return "Automation paused";

            string running = string.Join(", ", ActiveProfileNames());
            if (!OverlaysEnabled)
                return running.Length > 0 ? $"Overlays lifted — {running} running" : "Overlays lifted";
            if (running.Length > 0)
                return $"Running: {running}";

            int enabled = _config.Profiles.Count(p => p.Enabled);
            return $"Watching {enabled} game{(enabled == 1 ? "" : "s")}";
        }
    }

    /// <summary>Display names of the running profiles, falling back to the process name.</summary>
    private IEnumerable<string> ActiveProfileNames() =>
        _active.Select(name => _config.Profiles
            .FirstOrDefault(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase))?.Name ?? name);

    /// <summary>Re-raises <see cref="StateChanged"/> after the profile list is edited.</summary>
    public void NotifyProfilesChanged() => StateChanged?.Invoke();

    /// <summary>
    /// If the app previously crashed/exited while a game was active, and that game
    /// is no longer running, restore the snapshot it left behind.
    /// </summary>
    public void RecoverOnStartup(Func<string, bool> isProcessRunning)
    {
        if (_config.HdrSnapshot is null && _config.RefreshRateSnapshot is null)
        {
            _config.ActiveGames.Clear();
            return;
        }

        var stillRunning = _config.ActiveGames.Where(isProcessRunning).ToList();
        if (stillRunning.Count > 0)
        {
            // Game still up from a previous app session; keep tracking it.
            foreach (var name in stillRunning)
                _active.Add(name);
            _config.ActiveGames = stillRunning;
            _save();
            Logger.Log($"Recovered active game(s) from previous session: {string.Join(", ", stillRunning)}");
            return;
        }

        Logger.Log("Restoring display snapshot left over from a previous session.");
        RestoreRefreshRates();
        RestoreSnapshot();
        _config.HdrSnapshot = null;
        _config.RefreshRateSnapshot = null;
        _config.ActiveGames.Clear();
        _save();
    }

    public void OnGameStarted(GameProfile profile)
    {
        Logger.Log($"Game started: {profile.Name} ({profile.ProcessName})");

        if (_active.Count == 0)
        {
            try
            {
                _config.HdrSnapshot = HdrController.SnapshotStates();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to snapshot HDR state: {ex.Message}");
                _config.HdrSnapshot = null;
            }

            try
            {
                _config.RefreshRateSnapshot = RefreshRateController.SnapshotRates();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to snapshot refresh rates: {ex.Message}");
                _config.RefreshRateSnapshot = null;
            }
        }

        _active.Add(profile.ProcessName);
        _config.ActiveGames = _active.ToList();
        _save();

        foreach (var (path, action) in profile.LaunchRules)
        {
            if (action == LaunchAction.NoChange)
                continue;
            Apply(path, action == LaunchAction.TurnOn);
        }

        foreach (var (path, hz) in profile.RefreshRateRules)
            ApplyRate(path, hz);

        foreach (var (path, action) in profile.OverlayRules)
            _overlays.Apply(path, action);

        StateChanged?.Invoke();
        StatusChanged?.Invoke($"Applied launch rules for {profile.Name}");
    }

    public void OnGameStopped(GameProfile profile)
    {
        Logger.Log($"Game stopped: {profile.Name} ({profile.ProcessName})");

        _active.Remove(profile.ProcessName);
        _config.ActiveGames = _active.ToList();

        if (_active.Count > 0)
        {
            // Another profiled game is still running; defer restore until it exits.
            _save();
            StateChanged?.Invoke();
            return;
        }

        List<DisplayInfo> displays;
        try
        {
            displays = HdrController.GetDisplays().Where(d => d.SupportsHdr).ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to enumerate displays on game exit: {ex.Message}");
            displays = new();
        }

        foreach (var display in displays)
        {
            var action = profile.ExitRules.GetValueOrDefault(display.DevicePath, ExitAction.RestorePrevious);
            switch (action)
            {
                case ExitAction.NoChange:
                    break;
                case ExitAction.TurnOn:
                    Apply(display.DevicePath, true, display);
                    break;
                case ExitAction.TurnOff:
                    Apply(display.DevicePath, false, display);
                    break;
                case ExitAction.RestorePrevious:
                    if (_config.HdrSnapshot is not null && _config.HdrSnapshot.TryGetValue(display.DevicePath, out bool previous)
                        && previous != display.HdrEnabled)
                    {
                        Apply(display.DevicePath, previous, display);
                    }
                    break;
            }
        }

        // Last: switching HDR can itself change the display mode, so the rate goes back
        // after that has settled. This re-enumerates displays for itself, so the list
        // used above stays valid.
        RestoreRefreshRates();

        _overlays.CloseAll();

        // A peek is meant to last a moment. Re-arm the overlays now that nothing is
        // running, so a hotkey press that was never undone can't silently disable the
        // blackout for the next movie.
        if (!_overlays.Enabled)
        {
            _overlays.Enabled = true;
            Logger.Log("Overlays re-armed after the last game exited.");
        }

        _config.HdrSnapshot = null;
        _config.RefreshRateSnapshot = null;
        _save();
        StateChanged?.Invoke();
        StatusChanged?.Invoke($"Applied exit rules for {profile.Name}");
    }

    private void RestoreSnapshot()
    {
        if (_config.HdrSnapshot is null)
            return;
        foreach (var (path, enabled) in _config.HdrSnapshot)
            Apply(path, enabled);
    }

    /// <summary>
    /// Puts every display back on the refresh rate it had before the first game launched.
    /// A cap is never left behind: unlike HDR there is no "leave it as the game set it"
    /// option, because a monitor silently stuck at 60 Hz is a bug report, not a setting.
    /// </summary>
    private void RestoreRefreshRates()
    {
        if (_config.RefreshRateSnapshot is null)
            return;

        List<DisplayInfo> displays;
        try
        {
            displays = HdrController.GetDisplays();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to enumerate displays to restore refresh rates: {ex.Message}");
            return;
        }

        foreach (var display in displays)
        {
            if (_config.RefreshRateSnapshot.TryGetValue(display.DevicePath, out int previous) && previous != display.RefreshHz)
                ApplyRate(display.DevicePath, previous, display);
        }
    }

    private static void ApplyRate(string devicePath, int hz, DisplayInfo? display = null)
    {
        try
        {
            bool ok = display is not null
                ? RefreshRateController.SetRate(display.GdiDeviceName, hz)
                : RefreshRateController.SetRateByPath(devicePath, hz);
            Logger.Log($"Set {hz} Hz for {devicePath}: {(ok ? "ok" : "failed/not available")}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting refresh rate for {devicePath}: {ex.Message}");
        }
    }

    private static void Apply(string devicePath, bool enable, DisplayInfo? display = null)
    {
        try
        {
            bool ok = display is not null ? HdrController.SetHdr(display, enable) : HdrController.SetHdrByPath(devicePath, enable);
            Logger.Log($"Set HDR {(enable ? "ON" : "OFF")} for {devicePath}: {(ok ? "ok" : "failed/not connected")}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error setting HDR for {devicePath}: {ex.Message}");
        }
    }
}
