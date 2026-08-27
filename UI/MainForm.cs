using System.ComponentModel;
using HdrToggle.Display;
using HdrToggle.Monitoring;
using HdrToggle.Rules;

namespace HdrToggle.UI;

public sealed class MainForm : Form
{
    private const string LaunchNoChange = "No change";
    private const string LaunchOn = "Turn HDR on";
    private const string LaunchOff = "Turn HDR off";
    private static readonly string[] LaunchOptions = { LaunchNoChange, LaunchOn, LaunchOff };

    private const string ExitRestore = "Restore previous";
    private const string ExitNoChange = "No change";
    private const string ExitOn = "Turn HDR on";
    private const string ExitOff = "Turn HDR off";
    private static readonly string[] ExitOptions = { ExitRestore, ExitNoChange, ExitOn, ExitOff };

    private const string OverlayNone = "Do nothing";
    private const string OverlayBlackout = "Black out screen";
    private const string OverlayDim = "Dim screen";
    private static readonly string[] OverlayOptions = { OverlayNone, OverlayBlackout, OverlayDim };

    private readonly AppConfig _config;
    private readonly Action _save;
    private readonly RuleEngine _engine;
    private readonly HotkeyManager _hotkeys;

    private readonly FlowLayoutPanel _gameList;
    private readonly Panel _content;
    private readonly Panel _emptyPanel;
    private readonly PictureBox _iconBox;
    private readonly TextBox _nameBox;
    private readonly Label _pathLabel;
    private readonly ToggleSwitch _enabledToggle;
    private readonly FlowLayoutPanel _displayList;
    private readonly Label _statusLabel;
    private readonly ToggleSwitch _autostartToggle;
    private readonly ToggleSwitch _overlayToggle;
    private readonly Label _overlayLabel;
    private readonly HotkeyBox _hotkeyBox;
    private readonly ToolTip _tips = new();
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private GameProfile? _selected;
    private bool _loading;

    /// <summary>Set while pushing engine state into the controls, so echoes don't loop back.</summary>
    private bool _syncing;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HideOnClose { get; set; } = true;

    /// <summary>Scales a 96-dpi design value to the window's DPI.</summary>
    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    public MainForm(AppConfig config, Action save, RuleEngine engine, HotkeyManager hotkeys)
    {
        _config = config;
        _save = save;
        _engine = engine;
        _hotkeys = hotkeys;

        Text = "HDR Toggle";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None; // all layout goes through S()
        Size = new Size(S(1080), S(700));
        MinimumSize = new Size(S(940), S(620));
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ===== bottom bar =====
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = S(56), BackColor = Theme.Sidebar };
        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Sidebar,
            Location = new Point(S(20), S(19)),
            Text = "Ready",
        };
        bottomBar.Controls.Add(_statusLabel);

        var bottomRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Sidebar,
            WrapContents = false,
            Padding = new Padding(0, 0, S(12), 0),
        };
        var refreshBtn = new Button
        {
            Text = "Refresh displays",
            AutoSize = true,
            Font = Theme.Small,
            Padding = new Padding(S(8), S(4), S(8), S(4)),
            Margin = new Padding(S(6), S(12), S(6), S(12)),
        };
        ControlStyling.StyleButton(refreshBtn, Theme.Field, Theme.Text);
        refreshBtn.Click += (_, _) => LoadSelectedProfile();
        var autoLabel = new Label
        {
            Text = "Start with Windows",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Sidebar,
            Margin = new Padding(S(10), S(19), S(8), 0),
        };
        _autostartToggle = new ToggleSwitch
        {
            Checked = _config.StartWithWindows,
            Size = new Size(S(44), S(22)),
            Margin = new Padding(0, S(17), S(6), 0),
        };
        _autostartToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _config.StartWithWindows = _autostartToggle.Checked;
            try
            {
                Autostart.SetEnabled(_autostartToggle.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not update startup setting:\n{ex.Message}", "HDR Toggle",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _save();
        };
        var hotkeyLabel = new Label
        {
            Text = "Peek hotkey",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Sidebar,
            Margin = new Padding(S(14), S(19), S(8), 0),
        };
        _hotkeyBox = new HotkeyBox
        {
            Value = _config.PeekHotkey,
            Size = new Size(S(112), S(26)),
            Margin = new Padding(0, S(15), S(6), 0),
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

        bottomRight.Controls.Add(refreshBtn);
        bottomRight.Controls.Add(hotkeyLabel);
        bottomRight.Controls.Add(_hotkeyBox);
        bottomRight.Controls.Add(autoLabel);
        bottomRight.Controls.Add(_autostartToggle);
        bottomBar.Controls.Add(bottomRight);

        // ===== sidebar =====
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = S(300),
            BackColor = Theme.Sidebar,
            Padding = new Padding(S(16), S(16), S(16), S(12)),
        };

        _gameList = new VerticalCardList
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Sidebar,
        };
        _gameList.ClientSizeChanged += (_, _) => ResizeCards(_gameList);

        var addBtn = new Button { Dock = DockStyle.Top, Height = S(38), Text = "+  Add game", Font = Theme.Header };
        ControlStyling.StyleButton(addBtn, Theme.Accent, Color.FromArgb(30, 22, 8));
        addBtn.Click += (_, _) => AddProfile();

        var addBtnSpacer = new Panel { Dock = DockStyle.Top, Height = S(14), BackColor = Theme.Sidebar };
        var gamesLabel = new Label { Dock = DockStyle.Top, Height = S(34), Text = "Games", Font = Theme.Section, ForeColor = Theme.Text, BackColor = Theme.Sidebar };

        sidebar.Controls.Add(_gameList);
        sidebar.Controls.Add(addBtnSpacer);
        sidebar.Controls.Add(addBtn);
        sidebar.Controls.Add(gamesLabel);

        // ===== main content =====
        _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(24), S(20), S(24), S(12)) };

        var header = new Panel { Dock = DockStyle.Top, Height = S(118), BackColor = Theme.Bg };

        _iconBox = new PictureBox
        {
            Location = new Point(0, S(6)),
            Size = new Size(S(56), S(56)),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Bg,
        };

        _nameBox = new TextBox
        {
            Location = new Point(S(74), S(6)),
            Font = Theme.Title,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_loading || _selected is not { } p) return;
            p.Name = _nameBox.Text;
            _save();
            foreach (var card in _gameList.Controls.OfType<GameCard>())
                if (card.Profile == p) card.Invalidate();
        };

        _pathLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            Font = Theme.Small,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _enabledToggle = new ToggleSwitch { Size = new Size(S(44), S(22)) };
        _enabledToggle.CheckedChanged += (_, _) =>
        {
            if (_loading || _selected is not { } p) return;
            p.Enabled = _enabledToggle.Checked;
            _save();
            foreach (var card in _gameList.Controls.OfType<GameCard>())
                if (card.Profile == p) card.Invalidate();
            _engine.NotifyProfilesChanged();
        };
        var enabledLabel = new Label { Text = "Profile enabled", AutoSize = true, ForeColor = Theme.TextDim, BackColor = Theme.Bg };

        // Overlay master switch. App-wide rather than per-profile, so it sits under its
        // own separator and turns amber when lifted to flag the non-default state.
        var overlaySeparator = new Panel { Size = new Size(1, S(22)), BackColor = Theme.Border };
        _overlayToggle = new ToggleSwitch { Checked = engine.OverlaysEnabled, Size = new Size(S(44), S(22)) };
        _overlayLabel = new Label { Text = "Overlays", AutoSize = true, ForeColor = Theme.TextDim, BackColor = Theme.Bg };
        _overlayToggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _engine.OverlaysEnabled = _overlayToggle.Checked;
        };

        var removeBtn = new Button
        {
            Text = "Remove",
            AutoSize = true,
            Font = Theme.Small,
            Padding = new Padding(S(8), S(3), S(8), S(3)),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        ControlStyling.StyleButton(removeBtn, Theme.Field, Theme.Danger);
        removeBtn.Click += (_, _) => RemoveProfile();

        header.Controls.Add(_iconBox);
        header.Controls.Add(_nameBox);
        header.Controls.Add(_pathLabel);
        header.Controls.Add(_enabledToggle);
        header.Controls.Add(enabledLabel);
        header.Controls.Add(overlaySeparator);
        header.Controls.Add(_overlayToggle);
        header.Controls.Add(_overlayLabel);
        header.Controls.Add(removeBtn);
        header.Resize += (_, _) =>
        {
            int rightEdge = header.ClientSize.Width;
            _nameBox.Width = rightEdge - _nameBox.Left - S(110);
            _pathLabel.Location = new Point(S(76), _nameBox.Bottom + S(6));
            _pathLabel.Size = new Size(rightEdge - S(76), _pathLabel.Font.Height + S(4));
            _enabledToggle.Location = new Point(S(76), _pathLabel.Bottom + S(10));
            enabledLabel.Location = new Point(_enabledToggle.Right + S(8), _enabledToggle.Top + S(1));
            overlaySeparator.Location = new Point(enabledLabel.Right + S(18), _enabledToggle.Top);
            _overlayToggle.Location = new Point(overlaySeparator.Right + S(18), _enabledToggle.Top);
            _overlayLabel.Location = new Point(_overlayToggle.Right + S(8), _enabledToggle.Top + S(1));
            removeBtn.Location = new Point(rightEdge - removeBtn.Width, S(6));
            header.Height = _enabledToggle.Bottom + S(16);
        };

        var displaysLabel = new Label { Dock = DockStyle.Top, Height = S(36), Text = "Displays", Font = Theme.Section, ForeColor = Theme.Text, BackColor = Theme.Bg };

        _displayList = new VerticalCardList
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
        };
        _displayList.ClientSizeChanged += (_, _) => ResizeCards(_displayList);

        _content.Controls.Add(_displayList);
        _content.Controls.Add(displaysLabel);
        _content.Controls.Add(header);

        // ===== empty state =====
        _emptyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var emptyLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "No games yet.\n\nClick \"+  Add game\" and pick the game's .exe\nto set up per-display HDR rules.",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextDim,
            Font = Theme.Header,
            BackColor = Theme.Bg,
        };
        _emptyPanel.Controls.Add(emptyLabel);

        Controls.Add(_content);
        Controls.Add(_emptyPanel);
        Controls.Add(sidebar);
        Controls.Add(bottomBar);

        engine.StatusChanged += msg => _statusLabel.Text = msg;
        engine.StateChanged += SyncEngineState;

        RefreshProfileList();
        SyncEngineState();
        ApplyHotkeyTooltip();

        // The hotkey is registered before this window exists, so report a clash here.
        if (_config.PeekHotkey != Keys.None && !_hotkeys.IsRegistered)
            _statusLabel.Text = $"Peek hotkey {HotkeyManager.Describe(_config.PeekHotkey)} is already taken by another app";
    }

    /// <summary>Mirrors engine state into the controls; the engine is the source of truth.</summary>
    private void SyncEngineState()
    {
        _syncing = true;
        _overlayToggle.Checked = _engine.OverlaysEnabled;
        _overlayLabel.ForeColor = _engine.OverlaysEnabled ? Theme.TextDim : Theme.Accent;
        _syncing = false;
        UpdateStatus();
    }

    /// <summary>(Re-)registers the configured peek hotkey and reports a clash in the status bar.</summary>
    private void ApplyHotkey()
    {
        bool ok = _hotkeys.Register(_config.PeekHotkey);
        ApplyHotkeyTooltip();
        if (!ok)
            _statusLabel.Text = $"{HotkeyManager.Describe(_config.PeekHotkey)} is already taken by another app — pick another";
        else
            UpdateStatus();
    }

    private void ApplyHotkeyTooltip()
    {
        string combo = HotkeyManager.Describe(_config.PeekHotkey);
        _tips.SetToolTip(_overlayToggle, _config.PeekHotkey == Keys.None
            ? "Lifts the \"while playing\" overlays on every display so you can glance at your other monitors. Applies to the whole app, not just this profile."
            : $"Lifts the \"while playing\" overlays on every display so you can glance at your other monitors ({combo}). Applies to the whole app, not just this profile.");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.EnableDarkTitleBar(Handle);
    }

    // ===== game icons =====

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private Image GetGameIcon(GameProfile p)
    {
        if (_iconCache.TryGetValue(p.ExePath, out var cached))
            return cached;

        Image? img = null;
        try
        {
            // High-res extraction first (64px), fall back to the classic 32px API.
            if (SHDefExtractIcon(p.ExePath, 0, 0, out IntPtr hLarge, out IntPtr hSmall, 64) == 0 && hLarge != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(hLarge);
                img = icon.ToBitmap();
                DestroyIcon(hLarge);
                if (hSmall != IntPtr.Zero) DestroyIcon(hSmall);
            }
        }
        catch
        {
        }
        if (img is null)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(p.ExePath);
                img = icon?.ToBitmap();
            }
            catch
            {
            }
        }
        img ??= SystemIcons.Application.ToBitmap();
        _iconCache[p.ExePath] = img;
        return img;
    }

    // ===== profile list =====

    private static void ResizeCards(FlowLayoutPanel list)
    {
        foreach (Control c in list.Controls)
            c.Width = list.ClientSize.Width - c.Margin.Horizontal;
    }

    private void RefreshProfileList(GameProfile? select = null)
    {
        _gameList.SuspendLayout();
        foreach (var old in _gameList.Controls.OfType<Control>().ToList())
            old.Dispose();
        _gameList.Controls.Clear();

        foreach (var p in _config.Profiles)
        {
            var card = new GameCard
            {
                Profile = p,
                GameIcon = GetGameIcon(p),
                Height = S(64),
                Margin = new Padding(0, 0, 0, S(8)),
            };
            card.Click += (_, _) => SelectProfile(card.Profile);
            _tips.SetToolTip(card, p.ExePath);
            _gameList.Controls.Add(card);
        }
        ResizeCards(_gameList);
        _gameList.ResumeLayout();

        bool any = _config.Profiles.Count > 0;
        _content.Visible = any;
        _emptyPanel.Visible = !any;

        if (any)
            SelectProfile(select ?? (_config.Profiles.Contains(_selected!) ? _selected! : _config.Profiles[0]));
        else
            _selected = null;
    }

    private void SelectProfile(GameProfile p)
    {
        _selected = p;
        foreach (var card in _gameList.Controls.OfType<GameCard>())
            card.Selected = card.Profile == p;
        LoadSelectedProfile();
    }

    private void AddProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the game's .exe",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var profile = new GameProfile
        {
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            ExePath = dialog.FileName,
        };
        _config.Profiles.Add(profile);
        _save();
        RefreshProfileList(profile);
        _engine.NotifyProfilesChanged();
    }

    private void RemoveProfile()
    {
        if (_selected is not { } p)
            return;
        if (MessageBox.Show(this, $"Remove profile \"{p.Name}\"?", "HDR Toggle",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _config.Profiles.Remove(p);
        _selected = null;
        _save();
        RefreshProfileList();
        _engine.NotifyProfilesChanged();
    }

    // ===== rule editor =====

    private void LoadSelectedProfile()
    {
        if (_selected is not { } p)
            return;
        _loading = true;
        _nameBox.Text = p.Name;
        _pathLabel.Text = p.ExePath;
        _tips.SetToolTip(_pathLabel, p.ExePath);
        _iconBox.Image = GetGameIcon(p);
        _enabledToggle.Checked = p.Enabled;
        _loading = false;
        PopulateDisplayCards();
    }

    private void PopulateDisplayCards()
    {
        if (_selected is not { } profile)
            return;

        _displayList.SuspendLayout();
        foreach (var old in _displayList.Controls.OfType<Control>().ToList())
            old.Dispose();
        _displayList.Controls.Clear();

        List<DisplayInfo> displays;
        string? error = null;
        try
        {
            displays = HdrController.GetDisplays();
        }
        catch (Exception ex)
        {
            displays = new();
            error = $"Could not enumerate displays: {ex.Message}";
        }

        foreach (var d in displays)
            _displayList.Controls.Add(BuildDisplayCard(profile, d.DevicePath, d.FriendlyName, d.SupportsHdr, connected: true));

        var connected = displays.Select(d => d.DevicePath).ToHashSet();
        var orphaned = profile.LaunchRules.Keys.Concat(profile.ExitRules.Keys).Concat(profile.OverlayRules.Keys)
            .Where(path => !connected.Contains(path)).Distinct().ToList();
        foreach (var path in orphaned)
            _displayList.Controls.Add(BuildDisplayCard(profile, path, "Disconnected display", supportsHdr: true, connected: false));

        ResizeCards(_displayList);
        _displayList.ResumeLayout();

        if (error is null)
            UpdateStatus();
        else
            _statusLabel.Text = error;
    }

    private CardPanel BuildDisplayCard(GameProfile profile, string path, string name, bool supportsHdr, bool connected)
    {
        var card = new CardPanel { Height = S(92), Margin = new Padding(0, 0, 0, S(10)) };

        var nameLabel = new Label
        {
            Text = name,
            Font = Theme.Header,
            ForeColor = connected && supportsHdr ? Theme.Text : Theme.TextDim,
            BackColor = Theme.Card,
            AutoSize = true,
            Location = new Point(S(18), S(16)),
        };

        string badgeText = !connected ? "Not connected" : supportsHdr ? "HDR capable" : "HDR not supported";
        var badge = new Label
        {
            Text = badgeText,
            Font = Theme.Small,
            ForeColor = !connected ? Theme.TextDim : supportsHdr ? Theme.Good : Theme.TextDim,
            BackColor = Theme.Card,
            AutoSize = true,
            Location = new Point(S(18), S(46)),
        };

        card.Controls.Add(nameLabel);
        card.Controls.Add(badge);
        _tips.SetToolTip(nameLabel, path);

        // Each rule row: dim label on the left of an OptionPicker, anchored to the card's right edge.
        var rows = new List<(Label Label, OptionPicker Picker)>();

        void AddRow(string labelText, string[] options, string value, Action<string> onChanged)
        {
            var picker = new OptionPicker { Size = new Size(S(190), S(28)) };
            picker.Options.AddRange(options);
            picker.Value = value;
            picker.ValueChanged += (_, _) => onChanged(picker.Value);
            var label = new Label
            {
                Text = labelText,
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Card,
                AutoSize = true,
            };
            card.Controls.Add(picker);
            card.Controls.Add(label);
            rows.Add((label, picker));
        }

        if (supportsHdr)
        {
            AddRow("On launch", LaunchOptions, LaunchToText(profile.LaunchRules.GetValueOrDefault(path)), v =>
            {
                var action = TextToLaunch(v);
                if (action == LaunchAction.NoChange)
                    profile.LaunchRules.Remove(path);
                else
                    profile.LaunchRules[path] = action;
                _save();
            });
            AddRow("On exit", ExitOptions, ExitToText(profile.ExitRules.GetValueOrDefault(path)), v =>
            {
                var action = TextToExit(v);
                if (action == ExitAction.RestorePrevious)
                    profile.ExitRules.Remove(path);
                else
                    profile.ExitRules[path] = action;
                _save();
            });
        }

        // Overlays work on every display, HDR or not.
        AddRow("While playing", OverlayOptions, OverlayToText(profile.OverlayRules.GetValueOrDefault(path)), v =>
        {
            var action = TextToOverlay(v);
            if (action == OverlayAction.None)
                profile.OverlayRules.Remove(path);
            else
                profile.OverlayRules[path] = action;
            _save();
        });

        card.Height = Math.Max(S(80), S(20 + rows.Count * 36));

        void Position()
        {
            int w = card.ClientSize.Width;
            for (int i = 0; i < rows.Count; i++)
            {
                int y = S(14 + i * 36);
                rows[i].Picker.Location = new Point(w - rows[i].Picker.Width - S(18), y);
                rows[i].Label.Location = new Point(rows[i].Picker.Left - rows[i].Label.Width - S(10), y + S(5));
            }
        }
        card.Resize += (_, _) => Position();
        Position();

        return card;
    }

    private static string OverlayToText(OverlayAction a) => a switch
    {
        OverlayAction.Blackout => OverlayBlackout,
        OverlayAction.Dim => OverlayDim,
        _ => OverlayNone,
    };

    private static OverlayAction TextToOverlay(string s) => s switch
    {
        OverlayBlackout => OverlayAction.Blackout,
        OverlayDim => OverlayAction.Dim,
        _ => OverlayAction.None,
    };

    private static string LaunchToText(LaunchAction a) => a switch
    {
        LaunchAction.TurnOn => LaunchOn,
        LaunchAction.TurnOff => LaunchOff,
        _ => LaunchNoChange,
    };

    private static LaunchAction TextToLaunch(string s) => s switch
    {
        LaunchOn => LaunchAction.TurnOn,
        LaunchOff => LaunchAction.TurnOff,
        _ => LaunchAction.NoChange,
    };

    private static string ExitToText(ExitAction a) => a switch
    {
        ExitAction.TurnOn => ExitOn,
        ExitAction.TurnOff => ExitOff,
        ExitAction.NoChange => ExitNoChange,
        _ => ExitRestore,
    };

    private static ExitAction TextToExit(string s) => s switch
    {
        ExitOn => ExitAction.TurnOn,
        ExitOff => ExitAction.TurnOff,
        ExitNoChange => ExitAction.NoChange,
        _ => ExitAction.RestorePrevious,
    };

    private void UpdateStatus() => _statusLabel.Text = _engine.StatusText;

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // If the window is hidden or backgrounded mid-capture the field may never see
        // LostFocus, which would leave the suspended hotkey dead. Ending the capture
        // here re-registers it through CaptureEnded; it's a no-op when not capturing.
        _hotkeyBox.CancelCapture();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (HideOnClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
