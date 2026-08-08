using HdrToggle.Display;

namespace HdrToggle.Rules;

/// <summary>
/// Applies launch rules when a profiled game starts and exit rules (default:
/// restore the pre-launch HDR state) when the last profiled game stops.
/// </summary>
public sealed class RuleEngine
{
    private readonly AppConfig _config;
    private readonly Action _save;
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly OverlayManager _overlays = new();

    public event Action<string>? StatusChanged;

    public RuleEngine(AppConfig config, Action save)
    {
        _config = config;
        _save = save;
    }

    public bool GameActive => _active.Count > 0;

    /// <summary>
    /// If the app previously crashed/exited while a game was active, and that game
    /// is no longer running, restore the snapshot it left behind.
    /// </summary>
    public void RecoverOnStartup(Func<string, bool> isProcessRunning)
    {
        if (_config.HdrSnapshot is null)
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

        Logger.Log("Restoring HDR snapshot left over from a previous session.");
        RestoreSnapshot();
        _config.HdrSnapshot = null;
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

        foreach (var (path, action) in profile.OverlayRules)
            _overlays.Apply(path, action);

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

        _overlays.CloseAll();

        _config.HdrSnapshot = null;
        _save();
        StatusChanged?.Invoke($"Applied exit rules for {profile.Name}");
    }

    private void RestoreSnapshot()
    {
        if (_config.HdrSnapshot is null)
            return;
        foreach (var (path, enabled) in _config.HdrSnapshot)
            Apply(path, enabled);
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
