# HDR Toggle

A small Windows tray app that automatically switches HDR on or off per display — and caps the frame rate — when a game you've profiled launches, and restores things when it exits.

## How it works

1. **Add a game** — point the app at the game's `.exe`. It shows up as a tile with the game's icon.
2. **Set rules per display** — each connected monitor gets a card; choose what happens when the game launches (*Turn HDR on / Turn HDR off / No change*) and when it exits (*Restore previous* — the default, *Turn HDR on / off*, or *No change*).
3. **Cap the frame rate** — one setting per game, in the profile header: on NVIDIA it sets the driver's per-game frame rate limit, and every display is held at that refresh rate for as long as the game runs (see below).
4. **While playing** (every display, HDR or not) — optionally *Black out screen* or *Dim screen* for the duration of the game. This is a pure overlay window (click-through, never steals focus, hidden from Alt+Tab) — no display settings are touched, and it disappears the moment the game closes.
5. The app watches the process list from the system tray (polling every 2 seconds). When the game appears it snapshots the current HDR state and refresh rate of every display, applies your launch rules, and when the game closes it applies the exit rules (restoring the snapshot by default).

## Capping the frame rate

Some engines tie physics, animation or input to the frame rate and come apart above the
speed they were written for — GTA IV's bikes above 60 fps are the classic case. The
**FPS cap** picker in the profile header holds *every* display at the rate you choose for
as long as the game runs, and restores what they were on the moment it exits.

- It's one setting for the whole profile, not one per monitor: the game runs on one screen
  and the point is to be under the ceiling, so it applies to all of them.
- **On NVIDIA it also sets a real frame rate limit.** The same value is written to the
  game's driver profile as *Max Frame Rate* — the setting the NVIDIA Control Panel exposes
  under Manage 3D settings → Program Settings. The driver paces the game's own frames, so
  unlike the refresh-rate ceiling it caps the game **whether or not V-Sync is on, and in
  exclusive fullscreen**. That is the part that fixes a game that ignores the display.
  - The driver reads a game's profile when the game *starts*, so this is written the moment
    you pick the cap, not when the game launches — set it, then start the game.
  - It stays on the game's driver profile until you set the cap back to "No cap" or remove
    the profile. You can see and clear it by hand in the NVIDIA Control Panel.
  - It keys off the profile's .exe name, so point the profile at the game itself
    (`GTAIV.exe`, `EFLC.exe`) rather than at a launcher.
  - On AMD or Intel this part is skipped and the cap is the refresh rate alone; the picker's
    tooltip says which you're getting.
- The refresh-rate half of the cap only bites on a game that presents in sync with the
  display: V-Sync on, or usually a borderless window paced by the desktop compositor. **In
  exclusive fullscreen with V-Sync off it does nothing on its own** — that is what the
  driver limit above is for.
- A display that can't do exactly the chosen rate takes the closest it supports at or
  below it, so a mixed 144/60 Hz desk still ends up under the cap everywhere.
- The cap never changes resolution or colour depth, and a rate the panel rejects is refused
  before it is applied rather than dropping you to a black screen.
- **The status bar says whether it's working.** While a game runs, the status line (and the
  tray tooltip) reads `Running: GTAIV — displays at 60 Hz` or `cap not holding, 144 Hz`.
  That distinguishes the two failures: a display that isn't capped, versus a display that
  *is* capped while the game ignores it — the second means the game isn't syncing to the
  display, and no refresh rate will hold it.
- **It holds the cap.** A game that sets its own display mode on the way into exclusive
  fullscreen would otherwise undo it, so any display that comes back above the cap is put
  back — up to five times per display per session, after which it stops rather than
  fighting the game over the display mode forever. `log.txt` records each correction.
- Unlike HDR there's no "leave it as the game set it" choice: the rate is always restored.
  If the app is killed mid-game, the leftover rate is restored on next start, same as HDR.
- Variable-refresh (G-Sync/FreeSync) displays follow the same rule: the chosen rate is the
  ceiling.

If a game ignores the desktop refresh rate entirely (it picks its own mode *and* runs with
V-Sync off), the refresh-rate ceiling can't touch it — that is exactly the case the NVIDIA
driver limit above is for. Without an NVIDIA GPU the remaining options are the game's own
frame limiter, AMD's Radeon Chill / Frame Rate Target Control, or an overlay tool like RTSS.

## Peeking at your other monitors

Blacking out the other screens is great for a movie right up to the moment you want
to glance at one. The **Overlays** switch — in the window header, in the tray menu, and
on a system-wide hotkey (**Ctrl+Alt+B** by default) — drops every overlay instantly and
puts them all back on the next press. HDR is deliberately left alone, so there's no
display mode change and no black-screen flicker mid-film.

- The switch is app-wide, not per-profile: it lifts every overlay on every display.
- The hotkey is global, so it works from inside a fullscreen game or video player.
  Rebind it from the **Peek hotkey** field in the bottom bar (click, press the combo;
  Esc cancels, Backspace clears). A modifier is required, and the field tells you if
  another app already owns the combo.
- A peek is a moment, not a setting: it isn't saved to disk, and it re-arms itself when
  the last profiled game exits, so a press you forgot to undo can't silently disable the
  blackout for the next movie.

## The tray icon

Closing the window hides the app to the tray. Double-click opens the window; right-click
gives you the current status, **Open**, **Overlays**, **Pause automation**, and **Exit**.
The icon carries a status dot so you can tell what the app is doing at a glance:

| Dot | Meaning |
| --- | --- |
| Blue | Watching — idle, polling for a profiled game |
| Green | A profiled game is running and its rules are applied |
| Amber | Overlays lifted for a peek |
| Faded icon | Automation paused |

## Running it

- Published exe: `bin\Release\net10.0-windows\win-x64\publish\HdrToggle.exe` (framework-dependent; needs the .NET 10 Desktop Runtime).
- **Start with Windows**: checkbox in the app — writes a `HKCU\...\Run` entry that launches the app minimized to the tray (`--tray`).
- Config lives in `%APPDATA%\HdrToggle\config.json`; a small activity log is written to `%APPDATA%\HdrToggle\log.txt`. If the config ever fails to parse it is copied to `config.bad.json` before the app starts fresh, so profiles are recoverable.

### Command-line helpers

```
HdrToggle --list              list displays, their HDR state and refresh rates
HdrToggle --set <index> on    turn HDR on/off for a display (for testing)
HdrToggle --rate <index> 60   switch a display to a refresh rate (for testing)
HdrToggle --fps GTAIV.exe 60  set the NVIDIA per-game frame rate limit ("off" clears it)
HdrToggle --fps GTAIV.exe     read it back
```

## Notes

- HDR is switched via the Windows CCD API (`DisplayConfigSetDeviceInfo`): the Windows 11 24H2+ `SET_HDR_STATE` call, falling back to `SET_ADVANCED_COLOR_STATE` on older builds. No admin rights required.
- The frame rate cap is a refresh-rate switch through the GDI mode API
  (`ChangeDisplaySettingsEx` with `dmDisplayFrequency`, resolution and colour depth left
  alone), applied to every connected display and re-applied from the same 2-second poll
  that watches for the game. No overlay, no injection into the game, no admin rights.
- The driver-level limit goes through NVAPI's driver settings (DRS) API: the game's own
  profile is found by .exe name and its `Max Frame Rate` setting written, exactly as the
  control panel does it. Every NVAPI struct carries its own size in a version field, so a
  driver that doesn't recognise a call rejects it rather than misreading it; anything
  missing (no NVIDIA driver, an entry point that moved) turns the feature off with a reason
  shown in the picker's tooltip. Saving driver settings can require running as
  administrator on some machines — the status line says so if the save is refused.
- Driver limits are re-checked against the config once at startup, so a driver reinstall or
  a control-panel "restore defaults" can't leave a cap silently unenforced.
- A per-display cap saved by an earlier version is folded into the profile-wide one on
  first load — the lowest rate wins — so existing profiles keep capping.
- Displays are identified by their stable monitor device path, so rules survive reboots and display re-ordering. Rules for a disconnected monitor show as "(Disconnected display)" and are skipped safely.
- If the app was killed while a game was running, on next start it restores the leftover HDR and refresh-rate snapshot (unless the game is still running).
- If two profiled games overlap, restore happens only after the last one exits, using the snapshot taken before the first launched.

## Building

```
dotnet build -c Release
dotnet publish -c Release /p:RuntimeIdentifier=win-x64 /p:SelfContained=false /p:PublishSingleFile=true
```
