# HDR Toggle

A small Windows tray app that automatically switches HDR on or off per display when a game you've profiled launches, and restores things when it exits.

## How it works

1. **Add a game** — point the app at the game's `.exe`. It shows up as a tile with the game's icon.
2. **Set rules per display** — each connected monitor gets a card; choose what happens when the game launches (*Turn HDR on / Turn HDR off / No change*) and when it exits (*Restore previous* — the default, *Turn HDR on / off*, or *No change*).
3. **While playing** (every display, HDR or not) — optionally *Black out screen* or *Dim screen* for the duration of the game. This is a pure overlay window (click-through, never steals focus, hidden from Alt+Tab) — no display settings are touched, and it disappears the moment the game closes.
4. The app watches the process list from the system tray (polling every 2 seconds). When the game appears it snapshots the current HDR state of every display, applies your launch rules, and when the game closes it applies the exit rules (restoring the snapshot by default).

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
HdrToggle --list              list displays and their HDR state
HdrToggle --set <index> on    turn HDR on/off for a display (for testing)
```

## Notes

- HDR is switched via the Windows CCD API (`DisplayConfigSetDeviceInfo`): the Windows 11 24H2+ `SET_HDR_STATE` call, falling back to `SET_ADVANCED_COLOR_STATE` on older builds. No admin rights required.
- Displays are identified by their stable monitor device path, so rules survive reboots and display re-ordering. Rules for a disconnected monitor show as "(Disconnected display)" and are skipped safely.
- If the app was killed while a game was running, on next start it restores the leftover HDR snapshot (unless the game is still running).
- If two profiled games overlap, restore happens only after the last one exits, using the snapshot taken before the first launched.

## Building

```
dotnet build -c Release
dotnet publish -c Release /p:RuntimeIdentifier=win-x64 /p:SelfContained=false /p:PublishSingleFile=true
```
