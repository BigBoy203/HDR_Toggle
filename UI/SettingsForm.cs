using HdrToggle.Display;
using HdrToggle.Monitoring;
using HdrToggle.Rules;

namespace HdrToggle.UI;

/// <summary>
/// App-wide settings, kept out of the main window so that window is only ever about the
/// game library: what starts with Windows, the peek hotkey, and where this build's files
/// live. Per-game settings stay on the game's own page.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly Action _save;
    private readonly HotkeyManager _hotkeys;
    private readonly HotkeyBox _hotkeyBox;
    private readonly Label _hotkeyNote;
    private readonly ToolTip _tips = new();
    private readonly Func<bool> _gameActive;
    private readonly Button _updateButton;
    private readonly Label _updateNote;
    private readonly CancellationTokenSource _closing = new();

    /// <summary>The latest release, once a check has found one newer than this build.</summary>
    private UpdateInfo? _available;
    private bool _updateBusy;

    /// <summary>Set when a new version was installed over this exe; the app should restart.</summary>
    public bool UpdateInstalled { get; private set; }

    private bool _loading = true;

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    public SettingsForm(AppConfig config, Action save, HotkeyManager hotkeys, string version, string buildStamp,
        Func<bool> gameActive)
    {
        _gameActive = gameActive;
        _config = config;
        _save = save;
        _hotkeys = hotkeys;

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(480), S(624));
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Base;

        int y = S(22);

        Label Section(string text, int top)
        {
            var label = new Label
            {
                Text = text,
                Font = Theme.Section,
                ForeColor = Theme.Text,
                BackColor = Theme.Bg,
                AutoSize = true,
                Location = new Point(S(22), top),
            };
            Controls.Add(label);
            return label;
        }

        Label Caption(string text, int top)
        {
            var label = new Label
            {
                Text = text,
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                MaximumSize = new Size(S(436), 0),
                Location = new Point(S(22), top),
            };
            Controls.Add(label);
            return label;
        }

        // ===== startup =====
        Section("Startup", y);
        y += S(32);

        var autostart = new ToggleSwitch
        {
            Checked = _config.StartWithWindows,
            Size = new Size(S(44), S(22)),
            Location = new Point(S(22), y),
        };
        Controls.Add(autostart);

        var autostartLabel = new Label
        {
            Text = "Start with Windows",
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
            Location = new Point(autostart.Right + S(10), y + S(2)),
        };
        Controls.Add(autostartLabel);
        y += S(28);
        Caption("Adds HDR Toggle to your Windows startup apps, so profiles are watched from the moment you log in.", y);
        y += S(40);

        var startHidden = new ToggleSwitch
        {
            Checked = _config.StartHidden,
            Enabled = _config.StartWithWindows,
            Size = new Size(S(44), S(22)),
            Location = new Point(S(22), y),
        };
        Controls.Add(startHidden);
        var startHiddenLabel = new Label
        {
            Text = "Start hidden in the tray",
            AutoSize = true,
            ForeColor = _config.StartWithWindows ? Theme.Text : Theme.TextDim,
            BackColor = Theme.Bg,
            Location = new Point(startHidden.Right + S(10), y + S(2)),
        };
        Controls.Add(startHiddenLabel);
        y += S(28);
        Caption("Off opens the HDR Toggle window when Windows starts it. Starting it yourself always opens the window.", y);
        y += S(44);

        void WriteAutostart()
        {
            try
            {
                Autostart.SetEnabled(_config.StartWithWindows, _config.StartHidden);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not update startup setting:\n{ex.Message}", "HDR Toggle",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _save();
        }

        autostart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _config.StartWithWindows = autostart.Checked;
            // "Start hidden" only means something for the login launch.
            startHidden.Enabled = autostart.Checked;
            startHiddenLabel.ForeColor = autostart.Checked ? Theme.Text : Theme.TextDim;
            WriteAutostart();
        };
        startHidden.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _config.StartHidden = startHidden.Checked;
            WriteAutostart();
        };

        // ===== peek hotkey =====
        Section("Peek hotkey", y);
        y += S(32);

        _hotkeyBox = new HotkeyBox
        {
            Value = _config.PeekHotkey,
            Size = new Size(S(124), S(28)),
            Location = new Point(S(22), y),
        };
        _tips.SetToolTip(_hotkeyBox, "Click, then press the combo that lifts and restores the overlays.\n"
            + "Esc cancels, Backspace clears. A modifier (Ctrl/Alt/Shift) is required.");
        // The live hotkey has to stand down while the field is capturing, or it
        // intercepts the very keys the user is trying to assign.
        _hotkeyBox.CaptureStarted += (_, _) => _hotkeys.Suspend();
        _hotkeyBox.ValueChanged += (_, _) =>
        {
            _config.PeekHotkey = _hotkeyBox.Value;
            _save();
        };
        _hotkeyBox.CaptureEnded += (_, _) => ApplyHotkey();
        Controls.Add(_hotkeyBox);

        _hotkeyNote = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(S(310), 0),
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            Location = new Point(_hotkeyBox.Right + S(14), y + S(4)),
        };
        Controls.Add(_hotkeyNote);
        y += S(40);
        Caption("Lifts the \"while playing\" overlays on every display for a look at your other monitors.", y);
        y += S(38);

        // ===== blackout =====
        Section("Blackout", y);
        y += S(32);

        var confirmBlackout = new ToggleSwitch
        {
            Checked = _config.ConfirmBlackout,
            Size = new Size(S(44), S(22)),
            Location = new Point(S(22), y),
        };
        confirmBlackout.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _config.ConfirmBlackout = confirmBlackout.Checked;
            _save();
        };
        Controls.Add(confirmBlackout);
        Controls.Add(new Label
        {
            Text = "Ask before blacking out screens",
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
            Location = new Point(confirmBlackout.Right + S(10), y + S(2)),
        });
        y += S(28);
        Caption("When a game that blacks out a screen starts, a small Yes / No window asks first.", y);
        y += S(38);

        // ===== updates =====
        Section("Updates", y);
        y += S(32);

        _updateButton = new Button
        {
            Text = "Check for updates",
            Font = Theme.Small,
            Size = new Size(S(170), S(30)), // fixed: the label changes to "Update to x.y.z"
            Location = new Point(S(22), y),
        };
        ControlStyling.StyleButton(_updateButton, Theme.Field, Theme.Text);
        _tips.SetToolTip(_updateButton, $"Looks for a newer release on {Updater.ReleasesPage}");
        _updateButton.Click += (_, _) =>
        {
            if (_available is null) CheckForUpdates();
            else InstallUpdate();
        };
        Controls.Add(_updateButton);

        _updateNote = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(S(250), 0),
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            Location = new Point(S(206), y + S(7)),
            Text = $"You're on {version}.",
        };
        Controls.Add(_updateNote);
        y += S(44);
        Caption("Downloads the newest release from GitHub, replaces this copy of HDR Toggle and restarts it. "
            + "Your games and settings are kept.", y);
        y += S(46);

        // ===== this build =====
        Section("This build", y);
        y += S(30);

        Caption($"HDR Toggle {version}, built {buildStamp}", y);
        y += S(20);
        Caption(NvidiaFrameLimiter.IsAvailable
            ? "Hard frame rate limiting: available (NVIDIA driver)"
            : $"Hard frame rate limiting: unavailable — {NvidiaFrameLimiter.UnavailableReason}", y);
        y += S(24);

        var openFolder = new Button
        {
            Text = "Open settings folder",
            AutoSize = true,
            Font = Theme.Small,
            Padding = new Padding(S(10), S(5), S(10), S(5)),
            Location = new Point(S(22), y),
        };
        ControlStyling.StyleButton(openFolder, Theme.Field, Theme.Text);
        _tips.SetToolTip(openFolder, ConfigStore.ConfigDir);
        openFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.ConfigDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigStore.ConfigDir)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the folder:\n{ex.Message}", "HDR Toggle",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        Controls.Add(openFolder);

        var close = new Button
        {
            Text = "Close",
            AutoSize = true,
            Padding = new Padding(S(16), S(6), S(16), S(6)),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        ControlStyling.StyleButton(close, Theme.Accent, Color.FromArgb(30, 22, 8), Theme.AccentHover);
        close.Click += (_, _) => Close();
        Controls.Add(close);
        close.Location = new Point(ClientSize.Width - close.Width - S(22), ClientSize.Height - close.Height - S(18));
        AcceptButton = close;
        CancelButton = close;

        UpdateHotkeyNote();
        _loading = false;
    }

    /// <summary>Asks GitHub for the latest release and says whether it is newer than this build.</summary>
    public async void CheckForUpdates()
    {
        if (_updateBusy || IsDisposed) return;
        _updateBusy = true;
        _updateButton.Enabled = false;
        SetUpdateNote("Checking…", Theme.TextDim);
        try
        {
            _available = await Updater.CheckAsync(_closing.Token);
            if (IsDisposed) return;
            if (_available is null)
            {
                SetUpdateNote($"You're up to date ({Updater.CurrentVersion.ToString(3)}).", Theme.TextDim);
                _updateButton.Text = "Check again";
            }
            else
            {
                SetUpdateNote($"Version {_available.Version.ToString(3)} is available.", Theme.Accent);
                _updateButton.Text = $"Update to {_available.Version.ToString(3)}";
                _tips.SetToolTip(_updateButton, _available.PageUrl);
            }
        }
        catch (Exception ex) when (!IsDisposed)
        {
            Rules.Logger.Log($"Update check failed: {ex.Message}");
            SetUpdateNote(Describe(ex, "Couldn't check for updates"), Theme.Accent);
        }
        catch
        {
            // Closed mid-check.
        }
        finally
        {
            _updateBusy = false;
            if (!IsDisposed) _updateButton.Enabled = true;
        }
    }

    /// <summary>Downloads the release found by the last check, swaps it in, and closes so the app can restart.</summary>
    private async void InstallUpdate()
    {
        if (_updateBusy || _available is not { } update) return;

        string notes = update.Notes.Trim();
        if (notes.Length > 600) notes = notes[..600] + "…";
        string message = $"Update HDR Toggle from {Updater.CurrentVersion.ToString(3)} to {update.Version.ToString(3)}?\n\n"
            + (notes.Length > 0 ? notes + "\n\n" : "")
            + "HDR Toggle will close, replace its files with the new version and start again. "
            + "Your games and settings are kept.";
        if (_gameActive())
            message += "\n\nA game is running. Its rules pick up again after the restart, but it's smoother to update between games.";
        if (MessageBox.Show(this, message, "Update HDR Toggle", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
            != DialogResult.OK)
            return;

        _updateBusy = true;
        _updateButton.Enabled = false;
        SetUpdateNote($"Downloading {update.Version.ToString(3)}…", Theme.TextDim);
        try
        {
            string file = await Updater.DownloadAsync(update, _closing.Token);
            if (IsDisposed) return;
            SetUpdateNote("Installing…", Theme.TextDim);
            Updater.Install(file);
            UpdateInstalled = true;
            Close();
        }
        catch (Exception ex) when (!IsDisposed)
        {
            Rules.Logger.Log($"Update failed: {ex}");
            SetUpdateNote(Describe(ex, "Update failed"), Theme.Accent);
            _updateBusy = false;
            _updateButton.Enabled = true;
        }
        catch
        {
            // Closed mid-download.
        }
    }

    private static string Describe(Exception ex, string prefix) => ex switch
    {
        UpdateException => ex.Message,
        HttpRequestException or TaskCanceledException => $"{prefix}: couldn't reach GitHub.",
        _ => $"{prefix}: {ex.Message}",
    };

    private void SetUpdateNote(string text, Color color)
    {
        _updateNote.Text = text;
        _updateNote.ForeColor = color;
        _updateNote.Left = _updateButton.Right + S(14);
        _updateNote.MaximumSize = new Size(ClientSize.Width - _updateNote.Left - S(18), 0);
    }

    /// <summary>(Re-)registers the configured peek hotkey and says so if another app owns it.</summary>
    private void ApplyHotkey()
    {
        _hotkeys.Register(_config.PeekHotkey);
        UpdateHotkeyNote();
    }

    private void UpdateHotkeyNote()
    {
        if (_config.PeekHotkey == Keys.None)
        {
            _hotkeyNote.Text = "No hotkey — the switch in the window and tray still works.";
            _hotkeyNote.ForeColor = Theme.TextDim;
        }
        else if (!_hotkeys.IsRegistered)
        {
            _hotkeyNote.Text = $"{HotkeyManager.Describe(_config.PeekHotkey)} is already taken by another app — pick another.";
            _hotkeyNote.ForeColor = Theme.Accent;
        }
        else
        {
            _hotkeyNote.Text = "Works from inside a fullscreen game.";
            _hotkeyNote.ForeColor = Theme.TextDim;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.EnableDarkTitleBar(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // A capture still running would leave the global hotkey suspended.
        _hotkeyBox.CancelCapture();
        _closing.Cancel();
        base.OnFormClosing(e);
    }
}
