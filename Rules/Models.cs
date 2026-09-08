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

    /// <summary>
    /// Refresh rate (Hz) to hold <em>every</em> display at while the game runs; 0 means no
    /// cap. This is the frame rate cap: a display can't present faster than it refreshes,
    /// so a game running with V-Sync is pinned to this number. It is one setting for the
    /// whole profile rather than one per display — a game runs on one monitor, and which
    /// one is not worth making the user work out. Displays that can't do exactly this rate
    /// take the closest they support at or below it. Always restored on exit.
    /// </summary>
    public int FrameCapHz { get; set; }

    /// <summary>
    /// The per-display cap this setting replaced. Deserialized only, so an existing
    /// config keeps working; <see cref="ConfigStore.Load"/> folds it into
    /// <see cref="FrameCapHz"/> and clears it.
    /// </summary>
    [JsonPropertyName("RefreshRateRules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] // so it leaves no dead key behind
    public Dictionary<string, int>? LegacyRefreshRateRules { get; set; }

    /// <summary>
    /// What the library's Play button starts, when that isn't the profiled .exe itself —
    /// a launcher, a mod loader, a shortcut. Null means launch <see cref="ExePath"/>. The
    /// profile still watches <see cref="ProcessName"/>, so the rules fire on the game
    /// however it was started.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LaunchPath { get; set; }

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

    /// <summary>Refresh rate (Hz) per display path captured before the first game launched; null when no game is active.</summary>
    public Dictionary<string, int>? RefreshRateSnapshot { get; set; }

    /// <summary>Process names of profiled games currently running (persisted for crash recovery).</summary>
    public List<string> ActiveGames { get; set; } = new();
}
