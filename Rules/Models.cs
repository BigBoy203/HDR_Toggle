using System.Text.Json.Serialization;

namespace HdrToggle.Rules;

public enum LaunchAction
{
    NoChange,
    TurnOn,
    TurnOff,
}

public enum ExitAction
{
    RestorePrevious,
    NoChange,
    TurnOn,
    TurnOff,
}

/// <summary>Overlay shown on a display while the game is running (no display settings touched).</summary>
public enum OverlayAction
{
    None,
    Blackout,
    Dim,
}

public class GameProfile
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Rules keyed by monitor device path.</summary>
    public Dictionary<string, LaunchAction> LaunchRules { get; set; } = new();
    public Dictionary<string, ExitAction> ExitRules { get; set; } = new();
    public Dictionary<string, OverlayAction> OverlayRules { get; set; } = new();

    [JsonIgnore]
    public string ProcessName => Path.GetFileNameWithoutExtension(ExePath);

    public override string ToString() => Enabled ? Name : $"{Name} (disabled)";
}

public class AppConfig
{
    public List<GameProfile> Profiles { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool Paused { get; set; }

    /// <summary>
    /// System-wide combo that lifts/restores the "while playing" overlays for a quick
    /// look at the other monitors. <see cref="Keys.None"/> disables the hotkey.
    /// </summary>
    [JsonIgnore]
    public Keys PeekHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.B;

    /// <summary>
    /// Serialized form of <see cref="PeekHotkey"/>. Stored as a raw int rather than
    /// enum names: Keys is a flags enum with several aliased members, and an unreadable
    /// config costs the user every profile in it.
    /// </summary>
    [JsonPropertyName("PeekHotkey")]
    public int PeekHotkeyValue
    {
        get => (int)PeekHotkey;
        set => PeekHotkey = (Keys)value;
    }

    /// <summary>HDR state per display path captured before the first game launched; null when no game is active.</summary>
    public Dictionary<string, bool>? HdrSnapshot { get; set; }

    /// <summary>Process names of profiled games currently running (persisted for crash recovery).</summary>
    public List<string> ActiveGames { get; set; } = new();
}
