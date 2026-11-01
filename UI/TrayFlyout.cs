using HdrToggle.Monitoring;
using HdrToggle.Rules;

namespace HdrToggle.UI;

/// <summary>
/// The small window that opens from a left click on the tray icon. It shows the main
/// game — the one selected in the library — with Play and a picker to switch to another,
/// and, while none of the library's games is open, offers the programs that are open for
/// a one-click add. It closes as soon as it loses focus, like the system's own flyouts.
/// </summary>
internal sealed class TrayFlyout : Form
{
    private const int MaxQuickAdd = 6;

    private readonly AppConfig _config;
    private readonly RuleEngine _engine;
    private readonly MainForm _main;
    private readonly Action _openMain;
    private readonly ToolTip _tips = new();

    private readonly Label _statusDot;
    private readonly Label _statusText;
    private readonly Label _mainCaption;
    private readonly CardPanel _gameCard;
    private readonly PictureBox _gameIcon;
    private readonly Label _gameName;
    private readonly Label _gameSub;
    private readonly Button _playButton;
    private readonly Label _changeLabel;
    private readonly OptionPicker _picker;
    private readonly Panel _quickPanel;
    private readonly Panel _footerDivider;
    private readonly ToggleSwitch _overlayToggle;
    private readonly Label _overlayLabel;
    private readonly Button _openButton;

    /// <summary>Picker labels back to their profiles (labels are made unique).</summary>
    private readonly Dictionary<string, GameProfile> _pickerMap = new();

    /// <summary>Icons of the open apps listed for quick add; replaced on every refresh.</summary>
    private readonly List<Image> _quickIcons = new();

    private bool _syncing;
    private bool _refreshPending;

    /// <summary>When the flyout last hid itself, so the tray click that caused it doesn't reopen it.</summary>
    public DateTime LastHidden { get; private set; }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    private int ContentWidth => ClientSize.Width - S(32);

    public TrayFlyout(AppConfig config, RuleEngine engine, MainForm main, Action openMain)
    {
        _config = config;
        _engine = engine;
        _main = main;
        _openMain = openMain;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        Text = "HDR Toggle";
        KeyPreview = true; // Esc closes it
        ClientSize = new Size(S(336), S(300));

        // ===== header =====
        var title = new Label
        {
            Text = "HDR Toggle",
            Font = Theme.Header,
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
            AutoSize = true,
            Location = new Point(S(16), S(14)),
        };
        _statusDot = new Label
        {
            Text = "●",
            Font = Theme.Small,
            BackColor = Theme.Bg,
            AutoSize = true,
            Location = new Point(S(15), S(38)),
        };
        _statusText = new Label
        {
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(S(30), S(38)),
            Size = new Size(ContentWidth - S(14), S(18)),
        };

        // ===== main game =====
        _mainCaption = Caption("MAIN GAME");

        _gameCard = new CardPanel { Size = new Size(ContentWidth, S(64)), CardColor = Theme.Card };
        _gameIcon = new PictureBox
        {
            Location = new Point(S(12), S(12)),
            Size = new Size(S(40), S(40)),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Card,
        };
        _gameName = new Label
        {
            Font = Theme.Header,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(S(62), S(12)),
        };
        _gameSub = new Label
        {
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(S(62), S(34)),
        };
        _playButton = new Button
        {
            Font = Theme.Base,
            Size = new Size(S(82), S(32)),
        };
        ControlStyling.StyleButton(_playButton, Theme.Accent, Color.FromArgb(30, 22, 8), Theme.AccentHover);
        _playButton.Click += (_, _) =>
        {
            if (_main.SelectedProfile is not { } p || _engine.IsRunning(p))
                return;
            Hide();
            _main.Play(p);
        };
        _playButton.Location = new Point(_gameCard.Width - _playButton.Width - S(12), (_gameCard.Height - _playButton.Height) / 2);
        _gameName.Size = new Size(_playButton.Left - _gameName.Left - S(8), S(22));
        _gameSub.Size = new Size(_playButton.Left - _gameSub.Left - S(8), S(18));
        _gameCard.Controls.Add(_gameIcon);
        _gameCard.Controls.Add(_gameName);
        _gameCard.Controls.Add(_gameSub);
        _gameCard.Controls.Add(_playButton);

        _changeLabel = new Label
        {
            Text = "Change",
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            AutoSize = true,
        };
        _picker = new OptionPicker { Size = new Size(ContentWidth - S(62), S(28)), Font = Theme.Small };
        _tips.SetToolTip(_picker, "Switch the main game — the one selected in the library");
        _picker.ValueChanged += (_, _) =>
        {
            if (_syncing || !_pickerMap.TryGetValue(_picker.Value, out var p))
                return;
            // Deferred: the picker's menu is still unwinding its click.
            BeginInvoke(() =>
            {
                _main.Select(p);
                RequestRefresh();
            });
        };

        // ===== quick add (filled by Refresh) =====
        _quickPanel = new Panel { BackColor = Theme.Bg, Width = ContentWidth };

        // ===== footer =====
        _footerDivider = new Panel { BackColor = Theme.Border, Size = new Size(ClientSize.Width, 1) };
        _overlayToggle = new ToggleSwitch { Size = new Size(S(40), S(20)) };
        _overlayToggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _engine.OverlaysEnabled = _overlayToggle.Checked;
        };
        _overlayLabel = new Label
        {
            Text = "Overlays",
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            AutoSize = true,
        };
        _tips.SetToolTip(_overlayToggle, "Lift or restore the \"while playing\" overlays on every display");
        _openButton = new Button
        {
            Text = "Open HDR Toggle",
            Font = Theme.Small,
            AutoSize = true,
            Padding = new Padding(S(8), S(3), S(8), S(3)),
        };
        ControlStyling.StyleButton(_openButton, Theme.Field, Theme.Text);
        _openButton.Click += (_, _) =>
        {
            Hide();
            _openMain();
        };

        Controls.AddRange(new Control[]
        {
            title, _statusDot, _statusText, _mainCaption, _gameCard, _changeLabel, _picker,
            _quickPanel, _footerDivider, _overlayToggle, _overlayLabel, _openButton,
        });

        _engine.StateChanged += RequestRefresh;
        _main.SelectionChanged += RequestRefresh;
    }

    private Label Caption(string text) => new()
    {
        Text = text,
        Font = Theme.Small,
        ForeColor = Theme.TextDim,
        BackColor = Theme.Bg,
        AutoSize = true,
    };

    /// <summary>Opens the flyout next to the tray, already up to date.</summary>
    public void ShowNearTray()
    {
        RefreshNow();
        Location = TrayLocation();
        Show();
        Activate();
    }

    /// <summary>
    /// Bottom-right of the screen the cursor is on, kept inside its working area — so it
    /// sits above a bottom taskbar, below a top one, and beside a side one.
    /// </summary>
    private Point TrayLocation()
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        int margin = S(12);
        int x = Math.Clamp(cursor.X - Width / 2, area.Left + margin, Math.Max(area.Left + margin, area.Right - Width - margin));
        int y = Math.Clamp(cursor.Y - Height - margin, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - Height - margin));
        return new Point(x, y);
    }

    /// <summary>Coalesces refreshes and runs them after the current event has finished.</summary>
    private void RequestRefresh()
    {
        if (!Visible || _refreshPending || !IsHandleCreated)
            return;
        _refreshPending = true;
        BeginInvoke(() =>
        {
            _refreshPending = false;
            if (Visible)
                RefreshNow();
        });
    }

    private void RefreshNow()
    {
        SuspendLayout();
        _syncing = true;

        // ----- status -----
        _statusDot.ForeColor = _engine.State switch
        {
            AppState.Paused => Theme.TextDim,
            AppState.Peeking => Theme.Accent,
            AppState.Active => Theme.Good,
            _ => Theme.Idle,
        };
        _statusText.Text = _engine.StatusText;
        _tips.SetToolTip(_statusText, _engine.StatusText);

        int y = S(70);
        _mainCaption.Location = new Point(S(16), y);
        y += S(22);

        // ----- main game -----
        var main = _main.SelectedProfile;
        _gameCard.Location = new Point(S(16), y);
        if (main is null)
        {
            _gameIcon.Image = null;
            _gameName.Text = "No games yet";
            _gameSub.Text = "Add one below, or from the window";
            _gameSub.ForeColor = Theme.TextDim;
            _playButton.Visible = false;
        }
        else
        {
            bool running = _engine.IsRunning(main);
            _gameIcon.Image = _main.GetGameIcon(main);
            _gameName.Text = main.Name;
            _tips.SetToolTip(_gameName, main.ExePath);
            _gameSub.Text = running ? "Running — rules applied"
                : !main.Enabled ? $"{main.ProcessName}.exe · profile off"
                : $"{main.ProcessName}.exe · not running";
            _gameSub.ForeColor = running ? Theme.Good : Theme.TextDim;
            _playButton.Visible = true;
            _playButton.Text = running ? "Running" : "▶  Play";
            _playButton.BackColor = running ? Theme.Field : Theme.Accent;
            _playButton.ForeColor = running ? Theme.Good : Color.FromArgb(30, 22, 8);
            _playButton.FlatAppearance.MouseOverBackColor = running ? Theme.Field : Theme.AccentHover;
            _playButton.Cursor = running ? Cursors.Default : Cursors.Hand;
        }
        y = _gameCard.Bottom + S(8);

        // ----- change -----
        _pickerMap.Clear();
        _picker.Options.Clear();
        foreach (var p in _config.Profiles)
        {
            string label = p.Name.Length > 0 ? p.Name : p.ProcessName;
            if (_pickerMap.ContainsKey(label))
                label = $"{label} ({Path.GetFileName(p.ExePath)})";
            for (int n = 2; _pickerMap.ContainsKey(label); n++)
                label = $"{p.Name} ({n})";
            _pickerMap[label] = p;
            _picker.Options.Add(label);
        }
        _picker.Value = main is null ? "" : _pickerMap.FirstOrDefault(kv => kv.Value == main).Key ?? "";

        bool canChange = _config.Profiles.Count > 1;
        _changeLabel.Visible = _picker.Visible = canChange;
        if (canChange)
        {
            _changeLabel.Location = new Point(S(16), y + S(6));
            _picker.Location = new Point(S(16) + ContentWidth - _picker.Width, y);
            y = _picker.Bottom + S(8);
        }

        // ----- quick add -----
        y += S(6);
        BuildQuickAdd();
        _quickPanel.Location = new Point(S(16), y);
        if (_quickPanel.Visible)
            y = _quickPanel.Bottom + S(8);

        // ----- footer -----
        _overlayToggle.Checked = _engine.OverlaysEnabled;
        _overlayLabel.ForeColor = _engine.OverlaysEnabled ? Theme.TextDim : Theme.Accent;
        _overlayLabel.Text = _engine.OverlaysEnabled ? "Overlays" : "Overlays lifted";

        _footerDivider.Location = new Point(0, y + S(4));
        y = _footerDivider.Bottom + S(12);
        _overlayToggle.Location = new Point(S(16), y + S(4));
        _overlayLabel.Location = new Point(_overlayToggle.Right + S(8), y + S(5));
        _openButton.Location = new Point(ClientSize.Width - S(16) - _openButton.Width, y);
        y = Math.Max(_openButton.Bottom, _overlayToggle.Bottom) + S(14);

        _syncing = false;

        // Grow or shrink from the bottom edge, which is the one next to the tray.
        int bottom = Bottom;
        bool wasVisible = Visible;
        ClientSize = new Size(ClientSize.Width, y);
        if (wasVisible)
            Top = bottom - Height;

        ResumeLayout();
        Invalidate();
    }

    /// <summary>
    /// The open programs that aren't in the library, each with an Add button. Only shown
    /// while none of the library's games is open — that's when there's a game to add.
    /// </summary>
    private void BuildQuickAdd()
    {
        foreach (var old in _quickPanel.Controls.OfType<Control>().ToList())
            old.Dispose();
        _quickPanel.Controls.Clear();
        foreach (var img in _quickIcons)
            img.Dispose();
        _quickIcons.Clear();

        if (_engine.GameActive)
        {
            _quickPanel.Visible = false;
            return;
        }
        _quickPanel.Visible = true;

        var caption = Caption("QUICK ADD — OPEN APPS");
        caption.Location = new Point(0, 0);
        _quickPanel.Controls.Add(caption);
        int y = S(22);

        List<OpenApp> apps;
        try
        {
            var known = _config.Profiles.Select(p => p.ProcessName).Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            apps = OpenApps.List(known);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not list open apps: {ex.Message}");
            apps = new();
        }

        if (apps.Count == 0)
        {
            var none = new Label
            {
                Text = "No open apps to add. Start the game, then open this again.",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Location = new Point(0, y),
            };
            _quickPanel.Controls.Add(none);
            _quickPanel.Height = none.Bottom + S(2);
            return;
        }

        foreach (var app in apps.Take(MaxQuickAdd))
        {
            var icon = GameIcons.Load(app.ExePath, 32);
            _quickIcons.Add(icon);

            var row = new CardPanel
            {
                CardColor = Theme.Card,
                BorderColor = null,
                CornerRadius = 8,
                Size = new Size(ContentWidth, S(38)),
                Location = new Point(0, y),
            };
            var pic = new PictureBox
            {
                Image = icon,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(S(20), S(20)),
                Location = new Point(S(10), S(9)),
                BackColor = Theme.Card,
            };
            var add = new Button
            {
                Text = "+ Add",
                Font = Theme.Small,
                Size = new Size(S(58), S(26)),
            };
            ControlStyling.StyleButton(add, Theme.AccentSoft, Theme.Accent, Theme.CardSelected);
            add.Location = new Point(row.Width - add.Width - S(6), (row.Height - add.Height) / 2);
            var name = new Label
            {
                Text = app.Name,
                Font = Theme.Small,
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(S(38), S(10)),
                Size = new Size(add.Left - S(38) - S(6), S(18)),
            };
            _tips.SetToolTip(name, $"{app.WindowTitle}\n{app.ExePath}");
            _tips.SetToolTip(add, $"Add {app.ProcessName}.exe to the library and make it the main game");

            var target = app;
            add.Click += (_, _) => BeginInvoke(() =>
            {
                // Deferred: the refresh this triggers disposes the button being clicked.
                _main.AddGame(target.ExePath, target.Name);
                RequestRefresh();
            });

            row.Controls.Add(pic);
            row.Controls.Add(name);
            row.Controls.Add(add);
            _quickPanel.Controls.Add(row);
            y = row.Bottom + S(4);
        }

        if (apps.Count > MaxQuickAdd)
        {
            var more = new Label
            {
                Text = $"+{apps.Count - MaxQuickAdd} more open — add them from the window with \"+ Add game\".",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Location = new Point(0, y + S(2)),
            };
            _quickPanel.Controls.Add(more);
            y = more.Bottom;
        }
        _quickPanel.Height = y;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HideFlyout();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
            HideFlyout();
    }

    private void HideFlyout()
    {
        if (!Visible)
            return;
        LastHidden = DateTime.UtcNow;
        Hide();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - keep out of Alt+Tab
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.StateChanged -= RequestRefresh;
            _main.SelectionChanged -= RequestRefresh;
            foreach (var img in _quickIcons)
                img.Dispose();
            _quickIcons.Clear();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
