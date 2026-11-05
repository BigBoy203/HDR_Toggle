using HdrToggle.Rules;

namespace HdrToggle.Display;

/// <summary>
/// Shows black (or dimming) overlay windows over whole monitors while a game runs.
/// Pure window trickery — no display settings are touched. The overlay never takes
/// focus, ignores clicks, and stays out of Alt+Tab.
///
/// What each display *should* be showing is tracked separately from the windows that
/// actually exist, so the <see cref="Enabled"/> master switch can drop every overlay
/// for a quick look at the desktop and put them straight back afterwards.
/// </summary>
public sealed class OverlayManager
{
    private sealed class OverlayForm : Form
    {
        public OverlayForm(Rectangle bounds, double opacity)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = opacity;
            Bounds = bounds;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE - never steal focus from the game
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW  - keep out of Alt+Tab
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT - clicks pass through
                return cp;
            }
        }
    }

    /// <summary>What each display should be covering with, per the running games' rules.</summary>
    private readonly Dictionary<string, OverlayAction> _wanted = new();

    /// <summary>The overlay windows that currently exist (empty while disabled).</summary>
    private readonly Dictionary<string, OverlayForm> _forms = new();

    private bool _enabled = true;

    /// <summary>
    /// Master switch. Turning it off tears the overlay windows down without forgetting
    /// what they were covering, so turning it back on restores them exactly.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            Sync();
        }
    }

    public void Apply(string devicePath, OverlayAction action)
    {
        if (action == OverlayAction.None)
            return;
        _wanted[devicePath] = Stronger(_wanted.GetValueOrDefault(devicePath), action);
        Sync();
    }

    public void CloseAll()
    {
        _wanted.Clear();
        Sync();
    }

    /// <summary>
    /// Starts over: every overlay window is torn down, <paramref name="wanted"/> replaces
    /// what each display should show, the master switch is re-armed, and fresh windows are
    /// put up at each monitor's current position. This is the Refresh button — it fixes an
    /// overlay that went missing, landed on the wrong spot after a display mode change, or
    /// slipped behind another always-on-top window.
    /// </summary>
    public void Reset(IEnumerable<KeyValuePair<string, OverlayAction>> wanted)
    {
        foreach (var path in _forms.Keys.ToList())
            Destroy(path);

        _wanted.Clear();
        foreach (var (path, action) in wanted)
        {
            if (action != OverlayAction.None)
                _wanted[path] = Stronger(_wanted.GetValueOrDefault(path), action);
        }

        _enabled = true;
        Sync();
        Logger.Log($"Overlays rebuilt: {_forms.Count} shown, {_wanted.Count} wanted.");
    }

    /// <summary>How many overlay windows are up right now.</summary>
    public int ShownCount => _forms.Count;

    /// <summary>Blackout wins over dim when two profiled games overlap on one display.</summary>
    private static OverlayAction Stronger(OverlayAction a, OverlayAction b) =>
        a == OverlayAction.Blackout || b == OverlayAction.Blackout ? OverlayAction.Blackout :
        a == OverlayAction.Dim || b == OverlayAction.Dim ? OverlayAction.Dim :
        OverlayAction.None;

    /// <summary>Brings the live overlay windows in line with <see cref="_wanted"/> and <see cref="_enabled"/>.</summary>
    private void Sync()
    {
        int closed = 0;
        foreach (var path in _forms.Keys.ToList())
        {
            if (_enabled && _wanted.ContainsKey(path))
                continue;
            Destroy(path);
            closed++;
        }
        if (closed > 0)
            Logger.Log(_enabled ? $"Closed {closed} overlay(s)." : $"Overlays lifted ({closed} hidden).");

        if (!_enabled)
            return;

        foreach (var (path, action) in _wanted)
        {
            double opacity = action == OverlayAction.Blackout ? 1.0 : 0.55;

            if (_forms.TryGetValue(path, out var existing))
            {
                if (Math.Abs(existing.Opacity - opacity) > 0.001)
                    existing.Opacity = opacity;
                continue;
            }

            // Bounds are resolved at show time, so a monitor that moved while the
            // overlays were lifted still gets covered edge to edge.
            var bounds = FindScreenBounds(path);
            if (bounds is null)
            {
                Logger.Log($"Overlay skipped, display not found: {path}");
                continue;
            }

            var form = new OverlayForm(bounds.Value, opacity);
            form.Show();
            _forms[path] = form;
            Logger.Log($"Overlay ({action}) shown on {path}");
        }
    }

    private void Destroy(string devicePath)
    {
        if (!_forms.Remove(devicePath, out var form))
            return;
        try
        {
            form.Close();
            form.Dispose();
        }
        catch
        {
        }
    }

    private static Rectangle? FindScreenBounds(string devicePath)
    {
        try
        {
            var display = HdrController.GetDisplays().FirstOrDefault(d => d.DevicePath == devicePath);
            if (display is null || display.GdiDeviceName.Length == 0)
                return null;
            return RefreshRateController.GetBounds(display.GdiDeviceName)
                ?? Screen.AllScreens.FirstOrDefault(s => s.DeviceName == display.GdiDeviceName)?.Bounds;
        }
        catch
        {
            return null;
        }
    }
}
