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
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;

    public TrayApplicationContext(bool startHidden)
    {
        _config = ConfigStore.Load();
        _engine = new RuleEngine(_config, Save);
        _engine.RecoverOnStartup(IsProcessRunning);

        _watcher = new ProcessWatcher(() => _config.Profiles, () => _config.Paused);
        _watcher.GameStarted += _engine.OnGameStarted;
        _watcher.GameStopped += _engine.OnGameStopped;

        _form = new MainForm(_config, Save, _engine);
        _ = _form.Handle; // force handle creation so BeginInvoke works before first Show

        var pauseItem = new ToolStripMenuItem("Pause automation") { CheckOnClick = true, Checked = _config.Paused };
        pauseItem.CheckedChanged += (_, _) =>
        {
            _config.Paused = pauseItem.Checked;
            Save();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open HDR Toggle", null, (_, _) => ShowMainForm());
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "HDR Toggle",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainForm();

        Autostart.Sync(_config.StartWithWindows);

        // Second instances signal this event to bring the existing window up.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _form.BeginInvoke(ShowMainForm), null, Timeout.Infinite, executeOnlyOnce: false);

        if (!startHidden)
            ShowMainForm();
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
        _watcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _form.HideOnClose = false;
        _form.Close();
        ExitThread();
    }
}
