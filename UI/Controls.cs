using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HdrToggle.Monitoring;
using HdrToggle.Rules;

namespace HdrToggle.UI;

/// <summary>Panel painted as a rounded card.</summary>
internal class CardPanel : Panel
{
    public Color CardColor { get; set; } = Theme.Card;
    public Color? BorderColor { get; set; } = Theme.Border;
    public int CornerRadius { get; set; } = 10;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, S(CornerRadius));
        using var fill = new SolidBrush(CardColor);
        e.Graphics.FillPath(fill, path);
        if (BorderColor is { } bc)
        {
            using var pen = new Pen(bc);
            e.Graphics.DrawPath(pen, path);
        }
        base.OnPaint(e);
    }
}

/// <summary>Simple on/off pill switch.</summary>
internal class ToggleSwitch : Control
{
    private bool _checked;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(44, 22);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(track, (Height - 1) / 2f))
        using (var fill = new SolidBrush(_checked ? Theme.Accent : Theme.TrackOff))
        {
            g.FillPath(fill, path);
        }
        float pad = Height * 0.14f;
        float knob = Height - pad * 2;
        float x = _checked ? Width - pad - knob : pad;
        using var knobBrush = new SolidBrush(Color.White);
        g.FillEllipse(knobBrush, x, pad, knob, knob);
    }
}

/// <summary>Clickable tile for a game profile: exe icon, name, process, state.</summary>
internal class GameCard : Control
{
    private bool _hover;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public required GameProfile Profile { get; init; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? GameIcon { get; set; }

    private bool _selected;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public GameCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Sidebar;
        Cursor = Cursors.Hand;
        Height = 64;
    }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, S(10)))
        {
            using var fill = new SolidBrush(_selected ? Theme.CardSelected : _hover ? Theme.CardHover : Theme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(_selected ? Theme.Accent : Theme.Border, _selected ? 1.6f : 1f);
            g.DrawPath(pen, path);
        }

        int pad = S(14);
        int iconSize = S(38);
        int iconY = (Height - iconSize) / 2;
        if (GameIcon is not null)
            g.DrawImage(GameIcon, new Rectangle(pad, iconY, iconSize, iconSize));

        var nameColor = Profile.Enabled ? Theme.Text : Theme.TextDim;
        int textX = pad + iconSize + S(14);
        int textW = Width - textX - S(14);

        // Reserve room for the "Off" pill when disabled
        if (!Profile.Enabled)
        {
            string off = "Off";
            var offSize = TextRenderer.MeasureText(g, off, Theme.Small);
            int pillW = offSize.Width + S(14);
            int pillH = offSize.Height + S(6);
            var pill = new RectangleF(Width - pillW - S(12), (Height - pillH) / 2f, pillW, pillH);
            using (var pillPath = Theme.RoundedRect(pill, pillH / 2f))
            using (var pillFill = new SolidBrush(Theme.TrackOff))
            {
                g.FillPath(pillFill, pillPath);
            }
            TextRenderer.DrawText(g, off, Theme.Small, Rectangle.Round(pill), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            textW -= pillW + S(10);
        }

        // Both lines as one block, centred against the icon, so the tile's height is free
        // to change without the text drifting away from the middle of it.
        int nameH = TextRenderer.MeasureText("X", Theme.Header).Height;
        int subH = TextRenderer.MeasureText("X", Theme.Small).Height;
        int textY = (Height - (nameH + S(3) + subH)) / 2;

        var nameRect = new Rectangle(textX, textY, textW, nameH);
        TextRenderer.DrawText(g, Profile.Name, Theme.Header, nameRect, nameColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var subRect = new Rectangle(textX, nameRect.Bottom + S(3), textW, subH);
        TextRenderer.DrawText(g, Profile.ProcessName + ".exe", Theme.Small, subRect, Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Dark themed dropdown: a flat button that opens a dark context menu.
/// (Native ComboBox can't be reliably dark-themed in WinForms.)
/// </summary>
internal class OptionPicker : Button
{
    private string _value = "";

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<string> Options { get; } = new();

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    public OptionPicker()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = Theme.Border;
        FlatAppearance.BorderSize = 1;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        Cursor = Cursors.Hand;
        FlatAppearance.MouseOverBackColor = Theme.CardHover;
        Text = "";
        TextAlign = ContentAlignment.MiddleLeft;
    }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var textRect = new Rectangle(S(10), 0, Width - S(30), Height);
        TextRenderer.DrawText(e.Graphics, _value, Font, textRect, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var arrowRect = new Rectangle(Width - S(24), 0, S(20), Height);
        TextRenderer.DrawText(e.Graphics, "▾", Font, arrowRect, Theme.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnClick(EventArgs e)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            ShowImageMargin = false,
            Font = Font,
        };
        foreach (var option in Options)
        {
            var item = new ToolStripMenuItem(option)
            {
                Checked = option == _value,
                ForeColor = Theme.Text,
            };
            item.Click += (_, _) => Value = option;
            menu.Items.Add(item);
        }
        // Dispose after the click pipeline fully unwinds — disposing inside Closed
        // crashes ToolStrip's own post-click handling (ObjectDisposedException).
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.MinimumSize = new Size(Width, 0);
        menu.Show(this, new Point(0, Height));
        base.OnClick(e);
    }
}

/// <summary>
/// Click-to-capture field for a system-wide hotkey. Esc cancels, Backspace clears.
/// A modifier is required: registering a bare key globally would swallow it in every
/// other app on the machine.
/// </summary>
internal class HotkeyBox : Button
{
    private Keys _value = Keys.None;
    private bool _capturing;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    /// <summary>The owner must release the live hotkey while this is open, or it eats its own replacement.</summary>
    public event EventHandler? CaptureStarted;

    public event EventHandler? CaptureEnded;

    public HotkeyBox()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.BorderColor = Theme.Border;
        FlatAppearance.MouseOverBackColor = Theme.CardHover;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        Font = Theme.Small;
        Cursor = Cursors.Hand;
        Text = "";
        TextAlign = ContentAlignment.MiddleCenter;
    }

    protected override void OnClick(EventArgs e)
    {
        if (!_capturing)
        {
            _capturing = true;
            FlatAppearance.BorderColor = Theme.Accent;
            Invalidate();
            CaptureStarted?.Invoke(this, EventArgs.Empty);
        }
        base.OnClick(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        StopCapture();
        base.OnLostFocus(e);
    }

    /// <summary>Ends capture from outside, e.g. when the window is hidden to the tray. No-op when idle.</summary>
    public void CancelCapture() => StopCapture();

    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        FlatAppearance.BorderColor = Theme.Border;
        Invalidate();
        CaptureEnded?.Invoke(this, EventArgs.Empty);
    }

    // Key handling goes through ProcessCmdKey so Tab, Enter, Alt and the arrows are
    // captured as part of a combo instead of being consumed by form navigation.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing)
            return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        switch (key)
        {
            case Keys.Escape:
                StopCapture();
                return true;
            case Keys.Back:
            case Keys.Delete:
                Value = Keys.None;
                StopCapture();
                return true;
            case Keys.None:
            case Keys.ControlKey:
            case Keys.ShiftKey:
            case Keys.Menu:
            case Keys.LWin:
            case Keys.RWin:
                return true; // modifier held; still waiting for the real key
        }

        var modifiers = keyData & (Keys.Control | Keys.Alt | Keys.Shift);
        if (modifiers == Keys.None)
            return true;

        Value = modifiers | key;
        StopCapture();
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        string text = _capturing ? "Press keys\u2026" : HotkeyManager.Describe(_value);
        var color = _capturing ? Theme.Accent : _value == Keys.None ? Theme.TextDim : Theme.Text;
        TextRenderer.DrawText(e.Graphics, text, Font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => Theme.CardSelected;
    public override Color MenuItemBorder => Theme.CardSelected;
    public override Color MenuBorder => Theme.Border;
    public override Color ToolStripDropDownBackground => Theme.Field;
    public override Color ImageMarginGradientBegin => Theme.Field;
    public override Color ImageMarginGradientMiddle => Theme.Field;
    public override Color ImageMarginGradientEnd => Theme.Field;
    public override Color CheckBackground => Theme.AccentSoft;
    public override Color CheckSelectedBackground => Theme.AccentSoft;
    public override Color CheckPressedBackground => Theme.AccentSoft;
}

/// <summary>
/// Vertically scrolling card list: horizontal scrollbar is always suppressed and
/// the vertical scrollbar uses the system dark style.
/// </summary>
internal class VerticalCardList : FlowLayoutPanel
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    private const int SB_HORZ = 0;

    public VerticalCardList()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = SetWindowTheme(Handle, "DarkMode_Explorer", null);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (IsHandleCreated)
            ShowScrollBar(Handle, SB_HORZ, false);
    }
}

internal static class ControlStyling
{
    /// <summary>Flat, rounded, hoverable button.</summary>
    public static void StyleButton(Button b, Color back, Color fore, Color? hover = null)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = back;
        b.ForeColor = fore;
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.MouseOverBackColor = hover ?? ControlPaint.Light(back, 0.12f);
        b.Resize += (_, _) => ApplyRoundRegion(b, 8);
        ApplyRoundRegion(b, 8);
    }

    public static void ApplyRoundRegion(Control c, int radius)
    {
        int r = (int)Math.Round(radius * c.DeviceDpi / 96.0);
        using var path = Theme.RoundedRect(new RectangleF(0, 0, c.Width, c.Height), r);
        c.Region = new Region(path);
    }
}
