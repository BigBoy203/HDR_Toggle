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

    private bool _loading = true;

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    public SettingsForm(AppConfig config, Action save, HotkeyManager hotkeys, string version, string buildStamp)
    {
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
        ClientSize = new Size(S(480), S(348));
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
        autostart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _config.StartWithWindows = autostart.Checked;
            try
            {
                Autostart.SetEnabled(autostart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not update startup setting:\n{ex.Message}", "HDR Toggle",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _save();
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
        Caption("Launches minimised to the tray, so profiles are watched from the moment you log in.", y);
        y += S(38);

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
        base.OnFormClosing(e);
    }
}
