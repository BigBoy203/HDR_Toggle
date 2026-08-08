# HDR Toggle

A small Windows tray app that automatically switches HDR on or off per display when a game you've profiled launches, and restores things when it exits.

## How it works

1. **Add a game** — point the app at the game's `.exe`. It shows up as a tile with the game's icon.
2. **Set rules per display** — each connected monitor gets a card; choose what happens when the game launches (*Turn HDR on / Turn HDR off / No change*) and when it exits (*Restore previous* — the default, *Turn HDR on / off*, or *No change*).
3. **While playing** (every display, HDR or not) — optionally *Black out screen* or *Dim screen* for the duration of the game. This is a pure overlay window (click-through, never steals focus, hidden from Alt+Tab) — no display settings are touched, and it disappears the moment the game closes.
3. The app watches the process list from the system tray (polling every 2 seconds). When the game appears it snapshots the current HDR state of every display, applies your launch rules, and when the game closes it applies the exit rules (restoring the snapshot by default).

Closing the window hides the app to the tray. Right-click the tray icon for **Open**, **Pause automation**, and **Exit**. Double-click opens the window.

## Running it

- Published exe: `bin\Release\net10.0-windows\win-x64\publish\HdrToggle.exe` (framework-dependent; needs the .NET 10 Desktop Runtime).
- **Start with Windows**: checkbox in the app — writes a `HKCU\...\Run` entry that launches the app minimized to the tray (`--tray`).
- Config lives in `%APPDATA%\HdrToggle\config.json`; a small activity log is written to `%APPDATA%\HdrToggle\log.txt`.

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
