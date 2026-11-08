# RHI Overlay — plan

Status (8 Nov 2026): all six steps built in 2.9.3 Beta 1 (hotkey + controller, Home, Achievements, Screenshots, This game, Info and links with game notes, RHI's own playtime count). Code map in `.claude/rules/rhi-ui.md` (RHI overlay). Backdrop is thin acrylic + a light tint (`OverlayBackdrop.cs`). HDR status (Windows' advanced-colour info for the overlay window) and DLSS presets (NVIDIA profile, read in the background) added the same day. Not yet: an Overlay size setting. A faint diagonal line shows through the acrylic on one game; unconfirmed whether it's the backdrop.

## Goal

Press a key in a game and a full-screen RHI overlay appears on top of it, like the Steam overlay. It shows the game's achievements, the ReShade screenshots you've taken, this game's mod setup, session info, and the game's notes and links. Press the key again (or Esc / B) to go back to the game.

The overlay works only while RHI is running (in the window or the tray).

## Decisions

- **It's an RHI window, not something loaded into the game.** A borderless, always-on-top WinUI 3 window owned by RHI, shown over the game's monitor. Nothing is written into game folders, no ReShade needed, any graphics API, and no anti-cheat risk (it never touches the game), so it can be used in online games too.
- **All panels in the first version**: Home, Achievements, Screenshots, This game's setup, Info and links.
- **Only while RHI runs.** No data files for the overlay, no second process: it reads RHI's live data directly.
- **Live DLSS presets come later**, through a separate DLL loaded with the game (like OptiScaler) that talks to RHI. The overlay is built so a "DLSS" panel can be added when that helper is present. Not part of this plan.

## Known limits (tell users)

- **True exclusive fullscreen**: the overlay can't show over it. Most modern games are borderless or use Windows' fullscreen optimisations, which work. When RHI detects exclusive fullscreen (`SHQueryUserNotificationState` = `QUNS_RUNNING_D3D_FULL_SCREEN`) it doesn't open, and shows a tray notification: "Switch the game to borderless or windowed to use the RHI overlay."
- **The game loses focus while the overlay is open.** Many games pause or mute; a few minimise on focus loss (setting in the game). Nothing changes while the overlay is closed.
- While open, Windows composes the game (no independent flip), so latency is slightly higher until it closes.

## How it works

### Opening

1. **Hotkey**: `RegisterHotKey` on a hidden message window in RHI's process (works when RHI is in the tray). Default **Alt + `** (the key left of 1; configurable). Avoided: Shift+Tab (Steam), Alt+Z / Alt+R / Alt+F1 (NVIDIA), Ctrl+Shift+O and Alt+R (AMD), Win+G (Game Bar), Shift+` (Discord), Home (ReShade), Shift+F-keys (some games), Ctrl+Alt combos (AltGr on European keyboards). If registering fails, Settings says the key is taken.
   `WM_HOTKEY` gives RHI the right to take the foreground, so `SetForegroundWindow` on the overlay is allowed.
2. **Controller** (optional): hold **View + Menu** (Back + Start) for half a second. Needs a small background poll while RHI isn't the active window, which watches only for that combo (today the controller is ignored when RHI isn't in front, on purpose, and stays that way for every other button).
3. **Which game**: the foreground window's process → its exe path → matched against the library's install folders, reusing the folder logic in `MainWindow.GameProcess.cs` (skips crash reporters and helpers). Games started outside RHI (from Steam, a shortcut) are found the same way. No match → the overlay opens with only Home (clock, session) and says "RHI doesn't know this game".
4. The overlay covers the game window's monitor (`MonitorFromWindow`), full size, not the work area.

### Closing

Hotkey again, Esc, B on the controller, or the **Back to game** button. On close RHI hides the window and calls `SetForegroundWindow(gameHwnd)` (allowed, RHI is in front). Closing also happens automatically if the game exits.

### The window

- New `OverlayWindow` (a separate WinUI `Window`, built in C#, no XAML), created on first use and then kept hidden, so it opens instantly the next time.
- `OverlappedPresenter`: no border or title bar, not resizable, `IsAlwaysOnTop`; `WS_EX_TOOLWINDOW` so it's not in Alt+Tab or the taskbar.
- Backdrop: `DesktopAcrylicController` (blurred game behind, dark tint), falling back to a solid 85% dark if acrylic isn't available.
- Same process as RHI, so it shares the view models, services and Admin Mode.
- Look: Tabs/Spotlight theme (`TabsTheme`), large type for TV distance. Size follows the monitor (1080p = 100%, 1440p = 133%, 4K = 200%) times an **Overlay size** setting.
- Costs nothing while hidden: no timers run, the hotkey is message-driven, and only the controller combo poll runs (100 ms, only with a pad connected and the combo turned on).

## Panels

Layout: a left rail (Home, Achievements, Screenshots, This game, Info and links), the game's name and cover at the top, a clock and **Back to game** top right. LB / RB switch panels on the controller.

### Home

- Game cover, name, store.
- **Session**: time playing now (from the game process's start time), total playtime (Steam games: Steam's own playtime from `localconfig.vdf`; other stores: RHI's own count, started with this feature, saved per game), clock.
- Battery level and charging, when the PC has a battery (handhelds).
- Windows HDR on/off for this monitor (`HdrToggleService`).
- Tiles with the latest unlock, latest screenshot and any update available, each opening its panel.

### Achievements

Reuses `SteamAchievementService` / `XboxAchievementService` and the Spotlight achievements panel. The panel builder in `DetailLayoutHost.Achievements.cs` is split so both Spotlight and the overlay can host it (summary card, search, unlocked newest first, locked list). Extra in the overlay: sort the locked list by rarity ("easiest next"). Uses the same Settings › Achievements switches; when they're off the panel says where to turn them on.

### Screenshots

- Folder: `BuildScreenshotSavePath(gameName)` (`MainWindow.UISync.cs`). With **Folder per game** off, the shared folder is filtered by ReShade's default file name, which starts with the game's exe name. No screenshot folder set: the game's ReShade folder (ReShade's default). If the folder holds screenshots of other games, a note offers to turn on Folder per game.
- Grid of thumbnails, newest first; click (A) for a large view with left/right to step through.
- Actions: **Delete** (to the Recycle Bin, confirm), **Open folder**, **Copy image**.
- New `ScreenshotLibraryService`: lists files (png, jpg, bmp, jxr), makes thumbnails with WIC off the UI thread and caches them in `%LocalAppData%\RHI\overlay\thumbs\` (keyed by path + write time + size). HDR `.jxr` screenshots are tone-mapped to SDR for display. A `FileSystemWatcher` adds new screenshots while the overlay is open.

### This game's setup

Read from the game card and its records (no new detection):
- ReShade version and channel, RenoDX / Luma mod and version, frame limiter, RE Framework, DOF Fix.
- DLSS SR / RR / FG DLL versions and presets, Streamline, Neural Rendering method, OptiScaler version.
- An **Update available** mark next to anything Update All would update.
- **Copy report**: the same report as Management › Copy Report.

NVIDIA profile reads (presets) run off the UI thread and are cached from the DLSS section's last read; the overlay never waits on NVAPI.

### Info and links

- Mod notes (RenoDX / Luma "View Notes"), the manifest's install warnings for the game, PCGamingWiki data RHI already has.
- **Game notes**: your own notes for the game, typed in RHI (Management or the game menu) or in the overlay; saved per game (`GameName|Store`).
- Links: Nexus, PCGamingWiki, UW Fix, Ultra+, RenoDX / Luma wiki, Discord. Opening one in the browser leaves the game (the overlay closes first).

## Settings

New **Overlay** section in Settings (built in C# like `SettingsHandler.Achievements.cs`, before Tools & Maintenance):
- **RHI overlay**: Off / On (On by default).
- **Hotkey**: key capture box like the ReShade overlay key; says when the key is taken.
- **Controller (hold View + Menu)**: Off / On.
- **Overlay size**: 75–200%.

Global settings in `SettingsViewModel` (`OverlayEnabled`, `OverlayHotkey`, `OverlayControllerCombo`, `OverlayScale`), saved through `SaveNameMappings()`. Game notes and RHI's own playtime are per-game data (`GameName|Store`) and need `RenameGame` migration and Reset Overrides handling (notes: not reset).

## Code layout

- `Overlay/OverlayWindow.cs` (window, show/hide, monitor, backdrop), `Overlay/OverlayWindow.Rail.cs`, `Overlay/OverlayPanel.*.cs` (Home, Achievements, Screenshots, Setup, Info).
- `Services/OverlayHotkeyService.cs` (RegisterHotKey + message window), `Services/ForegroundGameResolver.cs` (foreground process → card; shares the helper list with `MainWindow.GameProcess.cs`), `Services/ScreenshotLibraryService.cs`, `Services/PlaytimeService.cs`.
- `SettingsHandler.Overlay.cs`.
- Controller: a small combo watcher in `ControllerInput`, plus overlay navigation (rail + grids with `FocusManager.FindNextElement`, A / B / LB / RB), not the full MainWindow row system.

## Build order

1. **Shell**: hotkey, foreground game lookup, the window over the game's monitor, open/close with focus back to the game, exclusive-fullscreen check, Settings section. Test this across the matrix below before building panels.
2. **Home, This game's setup, Info and links** (data RHI already has).
3. **Achievements** (split the Spotlight panel builder).
4. **Screenshots** (service, thumbnails, JXR, viewer, delete).
5. **Controller**: combo to open, navigation inside.
6. **Game notes, playtime**, polish, patch notes, guide.

## Test matrix

- APIs: DX9, DX11, DX12, Vulkan, OpenGL; 32-bit and 64-bit.
- Modes: borderless, windowed, "fullscreen" with Windows' optimisations, true exclusive fullscreen (must refuse cleanly).
- HDR on and off; RenoDX game in HDR.
- RHI normal and Admin Mode; elevated game; RHI in the tray.
- Games that pause or minimise on focus loss; games that hide or lock the cursor.
- Multi-monitor (game on the second screen), 1080p / 1440p / 4K, VRR (G-Sync) on.
- Game started from RHI and from Steam / a shortcut.

## Answered (8 Nov 2026)

1. Default hotkey: Alt + ` (Shift+F1 rejected: likely used by other things).
2. On by default in the beta.
3. Total playtime: Steam's number for Steam games, RHI's own count for the rest.
4. No QR codes.
5. Nothing more on Home for now.

## Later: live DLSS

A small DLL loaded with the game (proxy DLL like OptiScaler, or an ASI plugin) that hooks DLSS (NGX) parameter calls, re-creates the DLSS feature when a preset changes, and talks to RHI over a local pipe. When it's present the overlay gets a **DLSS** panel: SR / RR / FG presets and render scale live. Research first how OptiScaler and the ShortFuse DLSS Tool change presets in game. This part carries the usual single-player / anti-cheat warning.
