using HdrToggle.Display;
using HdrToggle.Rules;

namespace HdrToggle.UI;

/// <summary>
/// The small "Ready to black out the screens?" window shown when a game whose profile
/// blacks out a display starts. Yes puts the blackout up, No leaves the screens on for
/// this run of the game.
///
/// It sits on top without taking focus, so a game starting up isn't knocked out of its
/// window, and it goes on a screen that isn't about to be blacked out. If nobody answers
/// — a fullscreen game can cover it — it says yes on its own after a short countdown,
/// so the blackout still happens the way it always did.
/// </summary>
internal sealed class BlackoutPrompt : Form
{
    private const int AutoYesSeconds = 15;

    private readonly Button _yes;
    private readonly System.Windows.Forms.Timer _countdown;
    private int _remaining = AutoYesSeconds;
    private bool _answered;

    /// <summary>Raised once, with true for yes (also on the countdown) and false for no.</summary>
    public event Action<bool>? Answered;

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    public BlackoutPrompt(GameProfile profile)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        Text = "HDR Toggle";

        int width = S(340);
        int screens = profile.OverlayRules.Values.Count(a => a == OverlayAction.Blackout);

        var title = new Label
        {
            Text = "Ready to black out the screens?",
            Font = Theme.Header,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            AutoSize = true,
            Location = new Point(S(18), S(16)),
        };
        var body = new Label
        {
            Text = $"{profile.Name} is starting. This blacks out {screens} screen{(screens == 1 ? "" : "s")}.",
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card,
            AutoSize = true,
            MaximumSize = new Size(width - S(36), 0),
            Location = new Point(S(18), S(42)),
        };
        Controls.Add(title);
        Controls.Add(body);

        int buttonTop = Math.Max(body.Bottom, S(60)) + S(14);

        var no = new Button
        {
            Text = "No",
            Size = new Size(S(84), S(32)),
            Font = Theme.Base,
            Location = new Point(width - S(18) - S(84), buttonTop),
        };
        ControlStyling.StyleButton(no, Theme.Field, Theme.Text);
        no.Click += (_, _) => Answer(false);

        _yes = new Button
        {
            Size = new Size(S(96), S(32)),
            Font = Theme.Base,
            Location = new Point(no.Left - S(8) - S(96), buttonTop),
        };
        ControlStyling.StyleButton(_yes, Theme.Accent, Color.FromArgb(30, 22, 8), Theme.AccentHover);
        _yes.Click += (_, _) => Answer(true);

        Controls.Add(_yes);
        Controls.Add(no);
        AcceptButton = _yes;
        CancelButton = no;

        ClientSize = new Size(width, no.Bottom + S(16));

        _countdown = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdown.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
                Answer(true);
            else
                UpdateYesText();
        };
        UpdateYesText();

        Location = PlaceOn(PickScreen(profile));
    }

    private void UpdateYesText() => _yes.Text = $"Yes ({_remaining})";

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _countdown.Start();
    }

    /// <summary>Closes the prompt without answering — the game has already gone.</summary>
    public void Cancel()
    {
        _answered = true;
        Close();
    }

    private void Answer(bool blackOut)
    {
        if (_answered)
            return;
        _answered = true;
        _countdown.Stop();
        Answered?.Invoke(blackOut);
        Close();
    }

    /// <summary>
    /// The primary screen, unless that's one of the ones being blacked out — then any
    /// screen that isn't, so the question isn't drawn where the blackout is about to go.
    /// </summary>
    private static Screen PickScreen(GameProfile profile)
    {
        var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        try
        {
            var covered = profile.OverlayRules.Where(r => r.Value != OverlayAction.None).Select(r => r.Key).ToHashSet();
            var coveredNames = HdrController.GetDisplays()
                .Where(d => covered.Contains(d.DevicePath) && d.GdiDeviceName.Length > 0)
                .Select(d => d.GdiDeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!coveredNames.Contains(primary.DeviceName))
                return primary;
            return Screen.AllScreens.FirstOrDefault(s => !coveredNames.Contains(s.DeviceName)) ?? primary;
        }
        catch
        {
            return primary;
        }
    }

    /// <summary>Centred across the screen, a little above the middle.</summary>
    private Point PlaceOn(Screen screen)
    {
        var area = screen.WorkingArea;
        return new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) * 2 / 5);
    }

    /// <summary>Don't take focus from the game starting up; a click still works.</summary>
    protected override bool ShowWithoutActivation => true;

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
        using var accent = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(accent, 0, 0, Width, S(3));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _countdown.Dispose();
        base.OnFormClosed(e);
    }
}
