using System.ComponentModel;
using System.Reflection;
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

    private const string CapNone = "No cap";

    /// <summary>The frame rates worth offering beyond whatever the displays happen to run at.</summary>
    private static readonly int[] CommonRates = { 30, 45, 50, 60, 72, 75, 90, 100, 120, 144, 165, 240 };

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
    private readonly ToggleSwitch _overlayToggle;
    private readonly Label _overlayLabel;
    private readonly OptionPicker _capPicker;
    private readonly Label _capNote;
    private readonly Button _playButton;
    private readonly ToolTip _tips = new();
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private GameProfile? _selected;
    private bool _loading;

    /// <summary>Set while pushing engine state into the controls, so echoes don't loop back.</summary>
    private bool _syncing;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HideOnClose { get; set; } = true;

    /// <summary>App version for the corner of the status bar, without any build metadata.</summary>
    private static string AppVersion =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? Application.ProductVersion).Split('+')[0];

    /// <summary>
    /// When this .exe was built, from its own timestamp on disk. The version number only
    /// moves when it is bumped, so this is what answers "am I running what I just built?".
    /// </summary>
    private static string BuildStamp
    {
        get
        {
            try
            {
                return File.GetLastWriteTime(Application.ExecutablePath).ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "unknown";
            }
        }
    }

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
        Size = new Size(S(1140), S(760));
        MinimumSize = new Size(S(1000), S(660));
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ===== bottom bar =====
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = S(60), BackColor = Theme.Sidebar };
        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Sidebar,
            Margin = new Padding(S(12), S(21), 0, 0),
            Text = "Ready",
        };
        var versionLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Sidebar,
            Font = Theme.Small,
            Margin = new Padding(0, S(22), 0, 0),
            Text = $"v{AppVersion}",
        };
        _tips.SetToolTip(versionLabel, $"HDR Toggle {AppVersion}, built {BuildStamp}"
            + $"\nDriver frame rate limiting: {(NvidiaFrameLimiter.IsAvailable ? "on (NVIDIA)" : NvidiaFrameLimiter.UnavailableReason)}"
            + $"\nSettings and log: {ConfigStore.ConfigDir}");
        var statusDivider = new Panel
        {
            Size = new Size(1, S(14)),
            BackColor = Theme.Border,
            Margin = new Padding(S(12), S(22), 0, 0),
        };
        var bottomLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Sidebar,
            WrapContents = false,
            Padding = new Padding(S(20), 0, 0, 0),
        };
        bottomLeft.Controls.Add(versionLabel);
        bottomLeft.Controls.Add(statusDivider);
        bottomLeft.Controls.Add(_statusLabel);
        bottomBar.Controls.Add(bottomLeft);

        // ===== sidebar =====
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = S(324),
            BackColor = Theme.Sidebar,
            Padding = new Padding(S(18), S(18), S(18), S(14)),
        };

        _gameList = new VerticalCardList
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Sidebar,
        };
        _gameList.ClientSizeChanged += (_, _) => ResizeCards(_gameList);

        var addBtn = new Button { Dock = DockStyle.Top, Height = S(42), Text = "+  Add game", Font = Theme.Header };
        ControlStyling.StyleButton(addBtn, Theme.Accent, Color.FromArgb(30, 22, 8), Theme.AccentHover);
        addBtn.Click += (_, _) => AddProfile();

        var addBtnSpacer = new Panel { Dock = DockStyle.Top, Height = S(16), BackColor = Theme.Sidebar };
        var gamesLabel = new Label { Dock = DockStyle.Top, Height = S(38), Text = "Games", Font = Theme.Section, ForeColor = Theme.Text, BackColor = Theme.Sidebar };

        var settingsBtn = new Button
        {
            Dock = DockStyle.Bottom,
            Height = S(36),
            Text = "⚙   Settings",
            Font = Theme.Base,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(S(12), 0, 0, 0),
        };
        ControlStyling.StyleButton(settingsBtn, Theme.Field, Theme.Text);
        settingsBtn.Click += (_, _) => ShowSettings();
        var settingsSpacer = new Panel { Dock = DockStyle.Bottom, Height = S(12), BackColor = Theme.Sidebar };

        sidebar.Controls.Add(_gameList);
        sidebar.Controls.Add(settingsSpacer);
        sidebar.Controls.Add(settingsBtn);
        sidebar.Controls.Add(addBtnSpacer);
        sidebar.Controls.Add(addBtn);
        sidebar.Controls.Add(gamesLabel);

        // ===== main content =====
        _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(28), S(22), S(28), S(16)) };

        var header = new GradientPanel { Dock = DockStyle.Top, Height = S(150), BackColor = Theme.Bg };

        _iconBox = new PictureBox
        {
            Location = new Point(0, S(6)),
            Size = new Size(S(64), S(64)),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };

        _nameBox = new TextBox
        {
            Location = new Point(S(82), S(8)),
            Font = Theme.Title,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.HeroTop,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_loading || _selected is not { } p) return;
            p.Name = _nameBox.Text;
            _save();
            foreach (var card in _gameList.Controls.OfType<GameRow>())
                if (card.Profile == p) card.Invalidate();
        };

        _pathLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.Small,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _enabledToggle = new ToggleSwitch { Size = new Size(S(44), S(22)) };
        _enabledToggle.CheckedChanged += (_, _) =>
        {
            if (_loading || _selected is not { } p) return;
            p.Enabled = _enabledToggle.Checked;
            _save();
            foreach (var card in _gameList.Controls.OfType<GameRow>())
                if (card.Profile == p) card.Invalidate();
            _engine.NotifyProfilesChanged();
        };
        var enabledLabel = new Label { Text = "Profile enabled", AutoSize = true, ForeColor = Theme.TextDim, BackColor = Color.Transparent };

        // The library's Play button: the app knows where the game is, so it may as well
        // start it. The chevron beside it covers the cases where the .exe isn't the thing
        // you want to launch — a launcher, a mod loader, a shortcut.
        _playButton = new Button
        {
            Text = "▶   PLAY",
            Font = Theme.Section,
            Size = new Size(S(150), S(42)),
        };
        ControlStyling.StyleButton(_playButton, Theme.Accent, Color.FromArgb(30, 22, 8), Theme.AccentHover);
        _playButton.Click += (_, _) => PlaySelected();

        var playMenuButton = new Button
        {
            Text = "▾",
            Font = Theme.Base,
            Size = new Size(S(30), S(42)),
        };
        ControlStyling.StyleButton(playMenuButton, Theme.Field, Theme.Text);
        _tips.SetToolTip(playMenuButton, "What Play launches, and where the game lives");
        playMenuButton.Click += (_, _) => ShowPlayMenu(playMenuButton);

        // Overlay master switch. App-wide rather than per-profile, so it sits under its
        // own separator and turns amber when lifted to flag the non-default state.
        var overlaySeparator = new Panel { Size = new Size(1, S(22)), BackColor = Theme.Border };
        _overlayToggle = new ToggleSwitch { Checked = engine.OverlaysEnabled, Size = new Size(S(44), S(22)) };
        _overlayLabel = new Label { Text = "Overlays", AutoSize = true, ForeColor = Theme.TextDim, BackColor = Color.Transparent };
        _overlayToggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _engine.OverlaysEnabled = _overlayToggle.Checked;
        };

        // Frame rate cap. One setting for the profile rather than one per display: the game
        // runs on one monitor and the point is to be under the ceiling, so it applies to all
        // of them. Options are filled in from the connected displays by PopulateDisplayCards.
        var capSeparator = new Panel { Size = new Size(1, S(22)), BackColor = Theme.Border, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var capLabel = new Label
        {
            Text = "FPS cap",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _capPicker = new OptionPicker { Size = new Size(S(120), S(26)), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _capPicker.ValueChanged += (_, _) =>
        {
            if (_loading || _selected is not { } p) return;
            p.FrameCapHz = TextToRate(_capPicker.Value);
            _save();
            ApplyDriverLimit(p);
        };
        _tips.SetToolTip(_capPicker, CapTooltip());

        // A hard frame rate limit needs the NVIDIA driver. Say so where the setting is,
        // not only in a tooltip: on any other GPU this picker can only move the refresh
        // rate, which a game is free to ignore.
        _capNote = new Label
        {
            Text = NvidiaFrameLimiter.IsAvailable
                ? "hard limit via NVIDIA driver"
                : "NVIDIA GPU required — refresh rate only",
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = NvidiaFrameLimiter.IsAvailable ? Theme.Good : Theme.Accent,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _tips.SetToolTip(_capNote, NvidiaFrameLimiter.IsAvailable
            ? "The cap is written to this game's NVIDIA driver profile as Max Frame Rate, which\n"
                + "holds the game itself. The displays are moved to the same rate as a second line\n"
                + "of defence."
            : $"No NVIDIA driver here: {NvidiaFrameLimiter.UnavailableReason}.\n"
                + "The cap can only lower the displays' refresh rate, which caps a game just while\n"
                + "it presents in sync with the display — V-Sync on, and not in exclusive fullscreen.");

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
        header.Controls.Add(_playButton);
        header.Controls.Add(playMenuButton);
        header.Controls.Add(_enabledToggle);
        header.Controls.Add(enabledLabel);
        header.Controls.Add(overlaySeparator);
        header.Controls.Add(_overlayToggle);
        header.Controls.Add(_overlayLabel);
        header.Controls.Add(capSeparator);
        header.Controls.Add(capLabel);
        header.Controls.Add(_capPicker);
        header.Controls.Add(_capNote);
        header.Controls.Add(removeBtn);
        header.Resize += (_, _) =>
        {
            // Library page: name and path across the top, the Play button under them, and
            // the switches for this game trailing to its right.
            int rightEdge = header.ClientSize.Width;
            _nameBox.Width = Math.Max(S(120), rightEdge - _nameBox.Left - S(120));
            _pathLabel.Location = new Point(S(84), _nameBox.Bottom + S(4));
            _pathLabel.Size = new Size(rightEdge - S(84), _pathLabel.Font.Height + S(4));

            int rowTop = Math.Max(_iconBox.Bottom, _pathLabel.Bottom) + S(16);
            _playButton.Location = new Point(0, rowTop);
            playMenuButton.Location = new Point(_playButton.Right + S(4), rowTop);

            int switchTop = rowTop + (_playButton.Height - _enabledToggle.Height) / 2;
            _enabledToggle.Location = new Point(playMenuButton.Right + S(26), switchTop);
            enabledLabel.Location = new Point(_enabledToggle.Right + S(8), switchTop + S(1));
            overlaySeparator.Location = new Point(enabledLabel.Right + S(18), switchTop);
            _overlayToggle.Location = new Point(overlaySeparator.Right + S(18), switchTop);
            _overlayLabel.Location = new Point(_overlayToggle.Right + S(8), switchTop + S(1));

            // The cap group is pinned to the right edge. If the switches would run into it,
            // it drops to a line of its own rather than the two overlapping.
            int capWidth = _capPicker.Width + S(8) + capLabel.Width;
            bool sameRow = rightEdge - capWidth - S(18) > _overlayLabel.Right + S(24);
            int capTop = sameRow ? switchTop : _playButton.Bottom + S(14);

            _capPicker.Location = new Point(rightEdge - _capPicker.Width, capTop - S(2));
            capLabel.Location = new Point(_capPicker.Left - capLabel.Width - S(8), capTop + S(1));
            capSeparator.Visible = sameRow;
            capSeparator.Location = new Point(capLabel.Left - S(18), capTop);
            _capNote.Location = new Point(rightEdge - _capNote.Width, _capPicker.Bottom + S(4));
            removeBtn.Location = new Point(rightEdge - removeBtn.Width, S(8));
            header.Height = Math.Max(_playButton.Bottom, _capNote.Bottom) + S(18);
        };

        // Section header with its own action, rather than a lone button in the status bar.
        var displaysHeader = new Panel { Dock = DockStyle.Top, Height = S(42), BackColor = Theme.Bg };
        var displaysLabel = new Label
        {
            Text = "Displays",
            Font = Theme.Section,
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
            AutoSize = true,
            Location = new Point(0, S(6)),
        };
        var refreshBtn = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            Font = Theme.Small,
            Padding = new Padding(S(10), S(4), S(10), S(4)),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        ControlStyling.StyleButton(refreshBtn, Theme.Field, Theme.Text);
        _tips.SetToolTip(refreshBtn, "Re-read the connected displays, their HDR state and refresh rates");
        refreshBtn.Click += (_, _) => LoadSelectedProfile();
        displaysHeader.Controls.Add(displaysLabel);
        displaysHeader.Controls.Add(refreshBtn);
        displaysHeader.Resize += (_, _) =>
            refreshBtn.Location = new Point(displaysHeader.ClientSize.Width - refreshBtn.Width, S(4));

        _displayList = new VerticalCardList
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
        };
        _displayList.ClientSizeChanged += (_, _) => ResizeCards(_displayList);

        _content.Controls.Add(_displayList);
        _content.Controls.Add(displaysHeader);
        _content.Controls.Add(header);

        // ===== empty state =====
        _emptyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var emptyLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Your library is empty.\n\nClick \"+  Add game\" and pick the game's .exe.\n"
                + "Each game gets a Play button, per-display HDR rules and an FPS cap.",
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

    /// <summary>What the Play button starts: the launch override if set, else the game's .exe.</summary>
    private static string LaunchTarget(GameProfile profile) =>
        string.IsNullOrWhiteSpace(profile.LaunchPath) ? profile.ExePath : profile.LaunchPath;

    private void PlaySelected()
    {
        if (_selected is not { } profile)
            return;

        if (_engine.IsRunning(profile))
        {
            _statusLabel.Text = $"{profile.Name} is already running";
            return;
        }

        string target = LaunchTarget(profile);
        if (!File.Exists(target))
        {
            MessageBox.Show(this, $"Can't find:\n{target}\n\nUse the ▾ button to pick what Play should launch.",
                "HDR Toggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            // UseShellExecute so a shortcut or a launcher URL works, and the working
            // directory set to the game's own folder — plenty of games rely on that.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(target) ?? "",
            });
            _statusLabel.Text = $"Launched {Path.GetFileName(target)}";
            Logger.Log($"Launched {target} for {profile.Name}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not launch {target}: {ex.Message}");
            MessageBox.Show(this, $"Could not launch:\n{target}\n\n{ex.Message}", "HDR Toggle",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>The Play button's chevron menu: where the game lives, and what Play runs.</summary>
    private void ShowPlayMenu(Control anchor)
    {
        if (_selected is not { } profile)
            return;

        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            ShowImageMargin = false,
            Font = Theme.Base,
        };

        var openFolder = new ToolStripMenuItem("Open the game's folder", null, (_, _) => OpenGameFolder(profile))
        {
            ForeColor = Theme.Text,
        };
        var choose = new ToolStripMenuItem("Choose what Play launches…", null, (_, _) => ChooseLaunchTarget(profile))
        {
            ForeColor = Theme.Text,
        };
        menu.Items.Add(openFolder);
        menu.Items.Add(choose);

        if (!string.IsNullOrWhiteSpace(profile.LaunchPath))
        {
            var reset = new ToolStripMenuItem($"Reset to {Path.GetFileName(profile.ExePath)}", null, (_, _) =>
            {
                profile.LaunchPath = null;
                _save();
                LoadSelectedProfile();
            })
            {
                ForeColor = Theme.Text,
            };
            menu.Items.Add(reset);
        }

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void OpenGameFolder(GameProfile profile)
    {
        string? folder = Path.GetDirectoryName(LaunchTarget(profile));
        if (folder is null || !Directory.Exists(folder))
        {
            _statusLabel.Text = "That folder isn't there any more";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Could not open the folder: {ex.Message}";
        }
    }

    /// <summary>
    /// Points Play at something other than the profiled .exe — a launcher, a mod loader,
    /// a shortcut. The profile still *watches* its .exe, so the rules fire on the game
    /// itself however it was started.
    /// </summary>
    private void ChooseLaunchTarget(GameProfile profile)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "What should Play launch?",
            Filter = "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd;*.url)|*.exe;*.lnk;*.bat;*.cmd;*.url|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(profile.ExePath) ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        profile.LaunchPath = string.Equals(dialog.FileName, profile.ExePath, StringComparison.OrdinalIgnoreCase)
            ? null
            : dialog.FileName;
        _save();
        LoadSelectedProfile();
    }

    private void ShowSettings()
    {
        using var settings = new SettingsForm(_config, _save, _hotkeys, AppVersion, BuildStamp);
        settings.ShowDialog(this);
        // The peek combo may have changed while the dialog was open.
        ApplyHotkeyTooltip();
        UpdateStatus();
    }

    /// <summary>Mirrors engine state into the controls; the engine is the source of truth.</summary>
    private void SyncEngineState()
    {
        _syncing = true;
        _overlayToggle.Checked = _engine.OverlaysEnabled;
        _overlayLabel.ForeColor = _engine.OverlaysEnabled ? Theme.TextDim : Theme.Accent;
        _syncing = false;

        foreach (var row in _gameList.Controls.OfType<GameRow>())
            row.Running = _engine.IsRunning(row.Profile);
        UpdatePlayButton();
        UpdateStatus();
    }

    /// <summary>
    /// Play doubles as the running indicator, the way a library does it. It stays clickable
    /// while the game is up — a disabled flat button paints its own grey and loses the
    /// colour that carries the state — and the click just says so instead of launching a
    /// second copy.
    /// </summary>
    private void UpdatePlayButton()
    {
        if (_selected is not { } profile)
            return;

        bool running = _engine.IsRunning(profile);
        _playButton.Text = running ? "●   RUNNING" : "▶   PLAY";
        _playButton.BackColor = running ? Theme.Field : Theme.Accent;
        _playButton.ForeColor = running ? Theme.Good : Color.FromArgb(30, 22, 8);
        _playButton.FlatAppearance.MouseOverBackColor = running ? Theme.CardHover : Theme.AccentHover;
        _tips.SetToolTip(_playButton, running
            ? $"{profile.ProcessName}.exe is running — its rules are applied"
            : $"Launch {LaunchTarget(profile)}");
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
            var row = new GameRow
            {
                Profile = p,
                GameIcon = GetGameIcon(p),
                Running = _engine.IsRunning(p),
                Height = S(56),
                Margin = new Padding(0, 0, 0, S(2)),
            };
            row.Click += (_, _) => SelectProfile(row.Profile);
            _tips.SetToolTip(row, p.ExePath);
            _gameList.Controls.Add(row);
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
        foreach (var card in _gameList.Controls.OfType<GameRow>())
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
        if (p.FrameCapHz > 0 && NvidiaFrameLimiter.IsAvailable && !string.IsNullOrEmpty(p.ExePath))
        {
            NvidiaFrameLimiter.TryClearLimit(Path.GetFileName(p.ExePath), out string message);
            Logger.Log($"Driver limit for {p.Name}: {message}");
        }

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
        _pathLabel.Text = string.IsNullOrWhiteSpace(p.LaunchPath)
            ? p.ExePath
            : $"{p.ExePath}      ▸ Play launches {p.LaunchPath}";
        _tips.SetToolTip(_pathLabel, p.ExePath);
        _iconBox.Image = GetGameIcon(p);
        _enabledToggle.Checked = p.Enabled;
        _loading = false;
        UpdatePlayButton();
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

        var rates = new SortedSet<int>();
        foreach (var d in displays)
        {
            rates.UnionWith(SafeGetRates(d));
            _displayList.Controls.Add(BuildDisplayCard(profile, d.DevicePath, d.FriendlyName, d.SupportsHdr,
                connected: true, d.RefreshHz));
        }
        PopulateCapPicker(profile, rates);

        var connected = displays.Select(d => d.DevicePath).ToHashSet();
        var orphaned = profile.LaunchRules.Keys.Concat(profile.ExitRules.Keys).Concat(profile.OverlayRules.Keys)
            .Where(path => !connected.Contains(path)).Distinct().ToList();
        foreach (var path in orphaned)
            _displayList.Controls.Add(BuildDisplayCard(profile, path, "Disconnected display", supportsHdr: true,
                connected: false, refreshHz: 0));

        ResizeCards(_displayList);
        _displayList.ResumeLayout();

        if (error is null)
            UpdateStatus();
        else
            _statusLabel.Text = error;
    }

    /// <summary>
    /// Pushes the profile's cap to the NVIDIA driver as a per-game frame rate limit, and
    /// reads it straight back so the status line reports what the driver actually holds
    /// rather than what we asked for.
    /// </summary>
    private void ApplyDriverLimit(GameProfile profile)
    {
        if (!NvidiaFrameLimiter.IsAvailable || string.IsNullOrEmpty(profile.ExePath))
            return;

        string exe = Path.GetFileName(profile.ExePath);
        bool ok = profile.FrameCapHz > 0
            ? NvidiaFrameLimiter.TrySetLimit(exe, profile.FrameCapHz, out string message)
            : NvidiaFrameLimiter.TryClearLimit(exe, out message);

        bool mismatch = ok && profile.FrameCapHz > 0
            && NvidiaFrameLimiter.GetLimit(exe) is { } actual && actual != profile.FrameCapHz;
        if (mismatch)
            message = $"Asked the NVIDIA driver for {profile.FrameCapHz} FPS on {exe} but it reads back differently";

        Logger.Log($"Driver limit for {profile.Name}: {message}");
        if (!ok || mismatch)
            Logger.Log($"Driver profile after the write:{Environment.NewLine}{NvidiaFrameLimiter.DescribeProfile(exe)}");
        _statusLabel.Text = message;
    }

    /// <summary>What the cap does on this machine — the driver limit only exists on NVIDIA.</summary>
    private static string CapTooltip() => NvidiaFrameLimiter.IsAvailable
        ? "A hard frame rate limit, written to this game's NVIDIA driver profile as \"Max Frame\n"
            + "Rate\" — the same setting the NVIDIA Control Panel writes. It holds the game whether\n"
            + "or not V-Sync is on, and in exclusive fullscreen.\n\n"
            + "The driver reads a game's profile when the game starts, so set this before launching.\n"
            + "It stays on the profile until the cap goes back to \"No cap\".\n\n"
            + "Every display is also held at this refresh rate while the game runs, as a second line\n"
            + "of defence, and put back when it exits."
        : "Hard frame rate limiting needs an NVIDIA GPU: "
            + $"{NvidiaFrameLimiter.UnavailableReason}.\n\n"
            + "What this can still do is hold every display at the chosen refresh rate while the game\n"
            + "runs. That caps a game only while it presents in sync with the display — V-Sync on,\n"
            + "and not in exclusive fullscreen.";

    /// <summary>
    /// Fills the header's cap picker with every rate any connected display offers. A cap
    /// the displays no longer offer is kept in the list, so a saved setting is always
    /// visible and can be changed rather than silently disappearing.
    /// </summary>
    private void PopulateCapPicker(GameProfile profile, IReadOnlyCollection<int> rates)
    {
        // The driver limit takes any frame rate, not just ones a panel can display, so the
        // usual suspects are offered alongside the real refresh rates. A choice the display
        // can't do exactly still caps it: the refresh side picks the closest rate at or
        // below. With no display to ask at all, the standard list is all there is.
        var choices = new SortedSet<int>(rates);
        if (NvidiaFrameLimiter.IsAvailable || choices.Count == 0)
            choices.UnionWith(CommonRates);

        var options = new List<string> { CapNone };
        options.AddRange(choices.Select(RateToText));
        int cap = profile.FrameCapHz;
        string value = cap == 0 ? CapNone : RateToText(cap);
        if (cap != 0 && !options.Contains(value))
            options.Add(value);

        bool wasLoading = _loading;
        _loading = true;
        _capPicker.Options.Clear();
        _capPicker.Options.AddRange(options);
        _capPicker.Value = value;
        _loading = wasLoading;
    }

    /// <summary>Refresh rates a display offers; an enumeration failure just means fewer cap choices.</summary>
    private static IReadOnlyList<int> SafeGetRates(DisplayInfo display)
    {
        try
        {
            return RefreshRateController.GetAvailableRates(display.GdiDeviceName);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not list refresh rates for {display.FriendlyName}: {ex.Message}");
            return Array.Empty<int>();
        }
    }

    private CardPanel BuildDisplayCard(GameProfile profile, string path, string name, bool supportsHdr, bool connected,
        int refreshHz)
    {
        var card = new CardPanel { Height = S(96), Margin = new Padding(0, 0, 0, S(14)) };

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
        if (connected && refreshHz > 0)
            badgeText += $"  ·  {refreshHz} Hz";
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

        card.Height = Math.Max(S(88), S(24 + rows.Count * 38));

        void Position()
        {
            int w = card.ClientSize.Width;
            for (int i = 0; i < rows.Count; i++)
            {
                int y = S(16 + i * 38);
                rows[i].Picker.Location = new Point(w - rows[i].Picker.Width - S(18), y);
                rows[i].Label.Location = new Point(rows[i].Picker.Left - rows[i].Label.Width - S(10), y + S(5));
            }
        }
        card.Resize += (_, _) => Position();
        Position();

        return card;
    }

    private static string RateToText(int hz) => $"{hz} Hz";

    private static int TextToRate(string s) =>
        int.TryParse(s.AsSpan(0, Math.Max(s.IndexOf(' '), 0)), out int hz) ? hz : 0;

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
