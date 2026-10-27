---
inclusion: fileMatch
fileMatchPattern: "*DetailPanel*,*CompactView*,*MainWindow*,*Builder*,*Flyout*,*Dialog*"
---

# UI Patterns & Knowledge

## Panel Architecture
- Overrides panel: `DetailPanelBuilder.Overrides` split across partials (`.RsChannel.cs`, `.ShadersAddons.cs`, `.Dxvk.cs`, `.NvidiaProfile.cs`, `.DriverSettings.cs`, `.Dlss.cs`)
- Code-behind builders (NOT XAML templates). `DetailPanelBuilder` receives services via constructor injection (11 services).
- `BuildOverridesPanel()` populates BOTH `OverridesPanel` AND `NvidiaProfilePanel`

## Detail Panel Section Layout
4 containers inside `DetailPanel` StackPanel:
1. **Components** — game info, install buttons, progress bars
2. **OverridesContainer** → `OverridesPanel` — per-game overrides
3. **NvidiaProfileContainer** → `NvidiaProfilePanel` — DLSS/Streamline + driver settings
4. **ManagementContainer** → `ManagementPanel` — Change folder, Reset, Copy Report

## ComboBox Patterns
- Simple strings ("Global", "Stable", "Nightly") — not verbose labels
- Auto-apply on selection change — no "Apply" button
- `comboInitializing` guard flags prevent handler firing during panel construction
- "Select" opens picker dialog; if cancelled, reverts to previous value
- For Vulkan games: warn all Vulkan games affected, apply override to all simultaneously

### ComboBox SelectionChanged fires during construction
Handler can't distinguish init from user click. Use `_suppressSelectionChanged` guard flag set during panel build.

### Panel rebuild clobbers ComboBox state
Override changes call `NotifyAll()` → panel rebuild → ComboBox recreated. Suppress rebuilds during active interaction.

### `IsLegacyVersion()` blocks non-standard items
Returns true for ANY string not null/"Stable"/"Nightly"/"Custom". New RS Channel items must be excluded from this guard.

### ComboBox Re-Selection Pattern (DropDownClosed trick)
WinUI `SelectionChanged` doesn't fire on re-selecting current item. Use `DropDownClosed` handler comparing against LIVE persisted state (not stale local variables). Cancel revert must also query live state.

### comboInit guard must be set false immediately after Add
Set `comboInit = false` right after `Children.Add(combo)` — NOT at end of block. Background merge can rebuild mid-block.

## Detail Panel Selection Debounce
- 150ms `DispatcherTimer` in `GameList_SelectionChanged` (Detail mode)
- Guard check ensures pending card still matches `SelectedGame` before rebuilding
- `PopulateDetailPanel` + `BuildOverridesPanel` are synchronous and heavy

## Compact View
- 3 pages: Page 0=Components, Page 1=Game Overrides, Page 2=Nvidia Profile + Management
- `NavigateCompactPage` uses modulo 3. Nav arrows cycle.
- StackPanel vertical centering: disable scrolling + `VerticalAlignment.Center` on all compact pages
- Restore `Auto` scrolling and `Top` alignment when leaving compact mode
- Window size: 1166×820
- Shared `_selectionDebounceTimer` checks `CurrentViewLayout` at fire time

## Component Cog Buttons (⚙️)
- Each component row has ⚙️ in Column 4 → per-component ContentDialog
- Always visible (no conditional hiding)
- Handlers: `RsCogButton_Click`, `RdxCogButton_Click`, `UlCogButton_Click`, `DcCogButton_Click`, `OsCogButton_Click`, `DxvkCogButton_Click` in `MainWindow.Events.cs`

## Admin Mode
- Task Scheduler task "RHI Admin Mode" for UAC-free elevation
- `schtasks /Create /RL HIGHEST /SC ONCE /ST 00:00 /SD 01/01/2000`
- Auto-relaunch: checks on startup, runs task, exits immediately
- Create/Delete requires one-time UAC (`Verb = "runas"`)

## Drag-Drop (Admin Mode)
- XAML drag-drop (`AllowDrop`) doesn't work elevated (Microsoft bug)
- Fix: `DragAcceptFiles` + `WM_DROPFILES` for Explorer file drops
- `RHI.DropHelper.exe`: non-elevated overlay for Discord URL drops
- `Opacity = 0.01` (NOT TransparencyKey or WS_EX_TRANSPARENT — those block OLE)
- Toggle: `SettingsViewModel.DropHelperEnabled` (default true)

### Two Separate Drop Code Paths
- `Grid_Drop` (non-admin): XAML routing
- `HandleWin32Drop` (admin/WM_DROPFILES): separate routing logic — MUST match
- New file extensions need BOTH paths updated

## HDR Auto-Toggle
- `HdrToggleService.cs` — Win32 CCD API (`DisplayConfigGetDeviceInfo`/`SetDeviceInfo`)
- Per-game UI: button in header, purple when active. Reset Overrides clears to global.
- Process monitoring: polls for game process from `InstallPath`, awaits exit, then disables HDR
- Multi-monitor: `GetAllDisplays()`, `EnableHdr(List<uint>? targetIds)`
- `IsHdrEnabled` false positive: HDR-capable displays always report enabled. Always call `EnableHdr()`.
- Launcher/wrapper fix: after wrapper exits, polls 5s for surviving process in same folder.

## System Tray + Jump List
- `TrayIconService.cs` — Win32 `Shell_NotifyIcon` + `SHAddToRecentDocs(SHARD_LINK)`
- Close-to-tray: `AppWindow.Hide()` on close, tray stays. `_forceClose` bypasses.
- Jump list: `--launch "Game Name"` args. Updated via `TrackRecentLaunch`.
- Cross-elevation pipe: `NamedPipeServerStreamAcl.Create` with WorldSid access.
- Jump list suppressed by "Show recommended" policy being Off.

## Running Game Indicator
- `GameCardViewModel.IsRunning` — set on launch, cleared on exit
- Sidebar shows green background (#1A3A20) when running

## ContentDialog Pitfalls
- Default max width ~548px clips wide content. Fix: `dialog.Resources["ContentDialogMaxWidth"] = 900.0;`
- Disabled controls don't show tooltips — place tooltip on visible sibling instead.
- `XamlRoot` can be null during cache phase. Resolve at click time via `(sender as FrameworkElement)?.XamlRoot`.
- **Cannot open nested ContentDialogs**: WinUI 3 only allows one ContentDialog open at a time per XamlRoot. Attempting to show a second dialog while one is already open silently fails. For "Custom..." input inside an existing dialog, use an inline TextBox + Button that shows/hides instead of opening a nested dialog.

## Window State
- `TryRestoreWindowBounds`: clamp to monitor work area (prevents taskbar auto-hide suppression)
- Use `MonitorFromPoint(savedX, savedY)` not `MonitorFromWindow` (window hasn't moved yet at startup)
- Maximized: use `GetWindowPlacement.rcNormalPosition` (preserves correct monitor)

## Installer Shutdown Signal
- Signal file: `%LocalAppData%\RHI\rhi_shutdown_requested`
- 2-second `DispatcherTimer` checks existence. When found: delete, set `_forceClose`, close.

## TextBox Persistence Pitfall
- Settings TextBoxes only save on Enter key. Button handlers must read and persist explicitly before using the value.

## WinUI 3 Focus Pitfalls
- No `Window.Focus()` method. Defocus via disable/re-enable: `combo.IsEnabled = false; DispatcherQueue.TryEnqueue(() => combo.IsEnabled = true);`
- Editable ComboBox `SelectionChanged` doesn't fire for typed text until focus loss — use `KeyDown` for Enter.
- Editable ComboBox custom text not visible on init — use non-editable + "Custom..." sentinel + dialog instead.
