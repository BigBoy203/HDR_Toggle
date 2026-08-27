using System.Runtime.InteropServices;
using HdrToggle.Rules;

namespace HdrToggle.Monitoring;

/// <summary>
/// Registers one system-wide hotkey on a message-only window, so the peek toggle
/// works from anywhere — including while a fullscreen game or video has focus and
/// the tray icon is nowhere near the mouse.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xB07;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000; // one message per press, not per key-repeat

    private static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private Keys _current = Keys.None;
    private bool _registered;

    /// <summary>Raised on the UI thread when the registered combo is pressed.</summary>
    public event Action? Pressed;

    /// <summary>Raised whenever the registered combo changes, so menus can relabel.</summary>
    public event Action? Changed;

    public HotkeyManager()
    {
        try
        {
            CreateHandle(new CreateParams { Parent = HwndMessage });
        }
        catch
        {
            // Message-only parenting is the tidy option, not a requirement.
            CreateHandle(new CreateParams());
        }
    }

    /// <summary>The combo the user picked, whether or not Windows accepted it.</summary>
    public Keys Current => _current;

    /// <summary>False when a combo is configured but another app already owns it.</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// Swaps the registered combo. <see cref="Keys.None"/> just clears it.
    /// Returns false only when Windows refuses a real combo, i.e. it is already taken.
    /// </summary>
    public bool Register(Keys hotkey)
    {
        Suspend();
        _current = hotkey;

        var key = hotkey & Keys.KeyCode;
        if (key == Keys.None)
        {
            Logger.Log("Peek hotkey cleared.");
            Changed?.Invoke();
            return true;
        }

        uint mods = ModNoRepeat;
        if (hotkey.HasFlag(Keys.Control)) mods |= ModControl;
        if (hotkey.HasFlag(Keys.Alt)) mods |= ModAlt;
        if (hotkey.HasFlag(Keys.Shift)) mods |= ModShift;

        _registered = RegisterHotKey(Handle, HotkeyId, mods, (uint)key);
        Logger.Log(_registered
            ? $"Peek hotkey registered: {Describe(hotkey)}"
            : $"Peek hotkey {Describe(hotkey)} rejected — another app already owns it.");

        Changed?.Invoke();
        return _registered;
    }

    /// <summary>
    /// Releases the combo without forgetting it — used while the settings field is
    /// capturing keys, otherwise the hotkey would swallow its own replacement.
    /// </summary>
    public void Suspend()
    {
        if (!_registered)
            return;
        UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    /// <summary>Human-readable combo, e.g. "Ctrl+Alt+B".</summary>
    public static string Describe(Keys hotkey)
    {
        var key = hotkey & Keys.KeyCode;
        if (key == Keys.None)
            return "None";

        var parts = new List<string>(4);
        if (hotkey.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (hotkey.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (hotkey.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (key - Keys.NumPad0),
        Keys.Oemtilde => "`",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemPipe => "\\",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.Space => "Space",
        _ => key.ToString(),
    };

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Suspend();
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }
}
