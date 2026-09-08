using System.Diagnostics;
using HdrToggle.Monitoring;
using HdrToggle.Rules;
using HdrToggle.UI;

namespace HdrToggle;

/// <summary>Owns the tray icon, config, process watcher and rule engine for the app's lifetime.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    public const string ShowEventName = "HdrToggle_ShowEvent";

    private readonly NotifyIcon _tray;
    private readonly AppConfig _config;
    private readonly ProcessWatcher _watcher;
    private readonly RuleEngine _engine;
    private readonly MainForm _form;
    private readonly HotkeyManager _hotkeys;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;

    private readonly ToolStripLabel _statusItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _pauseItem;

    private readonly Icon _baseIcon;
    private readonly Dictionary<AppState, Icon> _stateIcons = new();

    /// <summary>Set while pushing engine state into the menu, so echoes don't loop back.</summary>
    private bool _syncing;

    public TrayApplicationContext(bool startHidden)
    {
        Logger.Log($"HDR Toggle {Application.ProductVersion.Split('+')[0]} starting.");
        _config = ConfigStore.Load();
        _engine = new RuleEngine(_config, Save);
        _engine.RecoverOnStartup(IsProcessRunning);
        _engine.SyncDriverLimits();

        _watcher = new ProcessWatcher(() => _config.Profiles, () => _config.Paused);
        _watcher.GameStarted += _engine.OnGameStarted;
        _watcher.GameStopped += _engine.OnGameStopped;
        // A game that sets its own display mode can undo the frame cap; this puts it back.
        _watcher.Polled += _engine.HoldFrameCap;

        // Registered on the UI thread, so WM_HOTKEY lands here without marshalling.
        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += _engine.ToggleOverlays;
        _hotkeys.Register(_config.PeekHotkey);

        _form = new MainForm(_config, Save, _engine, _hotkeys);
        _ = _form.Handle; // force handle creation so BeginInvoke works before first Show

        _baseIcon = LoadAppIcon();

        // ===== tray menu =====
        _statusItem = new ToolStripLabel
        {
            ForeColor = Theme.TextDim,
            Font = Theme.Small,
        };

        var openItem = new ToolStripMenuItem("Open HDR Toggle", null, (_, _) => ShowMainForm())
        {
            ForeColor = Theme.Text,
        };
        openItem.Font = new Font(Theme.Base, FontStyle.Bold); // the double-click default

        _overlayItem = new ToolStripMenuItem("Overlays")
        {
            CheckOnClick = true,
            Checked = _engine.OverlaysEnabled,
            ForeColor = Theme.Text,
        };
        _overlayItem.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _engine.OverlaysEnabled = _overlayItem.Checked;
        };

        _pauseItem = new ToolStripMenuItem("Pause automation")
        {
            CheckOnClick = true,
            Checked = _config.Paused,
            ForeColor = Theme.Text,
        };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _engine.Paused = _pauseItem.Checked;
        };

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()) { ForeColor = Theme.Text };

        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Font = Theme.Base,
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = IconFor(_engine.State),
            Text = "HDR Toggle",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainForm();

        _engine.StateChanged += ApplyState;
        _hotkeys.Changed += ApplyState; // relabel the menu when the combo is rebound
        ApplyState();

        Autostart.Sync(_config.StartWithWindows);

        // Second instances signal this event to bring the existing window up.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _form.BeginInvoke(ShowMainForm), null, Timeout.Infinite, executeOnlyOnce: false);

        if (!startHidden)
            ShowMainForm();
    }

    /// <summary>Pushes the engine's current state into the tray icon, tooltip and menu.</summary>
    private void ApplyState()
    {
        var state = _engine.State;
        string status = _engine.StatusText;

        _tray.Icon = IconFor(state);
        _tray.Text = Truncate($"HDR Toggle — {status}", 63);

        _syncing = true;
        _statusItem.Text = status;
        _overlayItem.Checked = _engine.OverlaysEnabled;
        _overlayItem.ShowShortcutKeys = _hotkeys.IsRegistered;
        _overlayItem.ShortcutKeyDisplayString = HotkeyManager.Describe(_hotkeys.Current);
        _pauseItem.Checked = _engine.Paused;
        _syncing = false;
    }

    /// <summary>NotifyIcon tooltips are capped at 63 characters.</summary>
    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    private Icon IconFor(AppState state)
    {
        if (!_stateIcons.TryGetValue(state, out var icon))
        {
            icon = TrayIcons.ForState(_baseIcon, state);
            _stateIcons[state] = icon;
        }
        return icon;
    }

    private static bool IsProcessRunning(string name)
    {
        var procs = Process.GetProcessesByName(name);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    private static Icon LoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    private void Save() => ConfigStore.Save(_config);

    private void ShowMainForm()
    {
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void ExitApp()
    {
        _showWait.Unregister(null);
        _hotkeys.Dispose();
        _watcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        foreach (var icon in _stateIcons.Values)
            icon.Dispose();
        _stateIcons.Clear();
        _form.HideOnClose = false;
        _form.Close();
        ExitThread();
    }
}
