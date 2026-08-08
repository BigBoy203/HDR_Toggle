using HdrToggle.Rules;

namespace HdrToggle.Display;

/// <summary>
/// Shows black (or dimming) overlay windows over whole monitors while a game runs.
/// Pure window trickery — no display settings are touched. The overlay never takes
/// focus, ignores clicks, and stays out of Alt+Tab.
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

    private readonly Dictionary<string, OverlayForm> _active = new();

    public void Apply(string devicePath, OverlayAction action)
    {
        if (action == OverlayAction.None)
            return;
        double opacity = action == OverlayAction.Blackout ? 1.0 : 0.55;

        if (_active.TryGetValue(devicePath, out var existing))
        {
            if (opacity > existing.Opacity)
                existing.Opacity = opacity; // blackout wins over dim when games overlap
            return;
        }

        var bounds = FindScreenBounds(devicePath);
        if (bounds is null)
        {
            Logger.Log($"Overlay skipped, display not found: {devicePath}");
            return;
        }

        var form = new OverlayForm(bounds.Value, opacity);
        form.Show();
        _active[devicePath] = form;
        Logger.Log($"Overlay ({action}) shown on {devicePath}");
    }

    public void CloseAll()
    {
        foreach (var form in _active.Values)
        {
            try
            {
                form.Close();
                form.Dispose();
            }
            catch
            {
            }
        }
        if (_active.Count > 0)
            Logger.Log("All overlays closed.");
        _active.Clear();
    }

    private static Rectangle? FindScreenBounds(string devicePath)
    {
        try
        {
            var display = HdrController.GetDisplays().FirstOrDefault(d => d.DevicePath == devicePath);
            if (display is null || display.GdiDeviceName.Length == 0)
                return null;
            return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == display.GdiDeviceName)?.Bounds;
        }
        catch
        {
            return null;
        }
    }
}
