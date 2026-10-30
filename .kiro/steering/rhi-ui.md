---
inclusion: fileMatch
fileMatchPattern: "*DetailPanel*,*MainWindow*,*Builder*,*Flyout*,*Dialog*"
---

# UI Patterns & Knowledge

## Panel Architecture
- Overrides panel: `DetailPanelBuilder.Overrides` split across partials (`.RsChannel.cs`, `.ShadersAddons.cs`, `.Dxvk.cs`, `.NvidiaProfile.cs`, `.DriverSettings.cs`, `.Dlss.cs`)
- Code-behind builders (NOT XAML templates). `DetailPanelBuilder` receives services via constructor injection (11 services).
- `BuildOverridesPanel()` fills `OverridesPanel`, the DLSS / Driver Settings panels (`.Overrides.NvidiaProfile.cs`, `.Overrides.DriverSettings.cs`) and `ManagementPanel`. Neural Rendering is built by `DetailPanelBuilder.NeuralRendering*.cs`, Extras by `DetailPanelBuilder.Extras*.cs`.

## Detail Panel Section Layout
There is only one layout (Detail). `DetailPanel` StackPanel = 2 header Grids (game info) at index 0–1, then 7 section Borders. Default order (`SettingsViewModel.DefaultSectionOrder`, key → container → panel):
1. `Components` → `DetailComponentSection` — component install rows (in XAML)
2. `GameOverrides` → `OverridesContainer` → `OverridesPanel`
3. `NeuralRendering` → `NeuralRenderingContainer` → `NeuralRenderingPanel`
4. `NvidiaProfileDlss` → `NvidiaProfileDlssContainer` → `NvidiaProfileDlssPanel` ("DLSS / Streamline")
5. `NvidiaProfileDriver` → `NvidiaProfileDriverContainer` → `NvidiaProfileDriverPanel` ("Driver Settings — Driver x")
6. `Management` → `ManagementContainer` → `ManagementPanel` (Change install folder, Reset / Remove game, Reset Overrides, Copy Report)
7. `Extras` → `ExtrasContainer` → `ExtrasPanel` (ASI Loader, OptiScaler, MFG unlocks, DXVK, dgVoodoo2)

- `NvidiaProfileContainer` is a legacy Border kept collapsed for migration; never show it. Old setting key `NvidiaProfile` migrates to `NvidiaProfileDlss` + `NvidiaProfileDriver`.
- Every section collapses (heading click, `CollapsedDetailSections`) and reorders (≡ drag handle, `DetailSectionOrder`); both persist in settings. `ApplySectionOrder()` must run after all sections are populated and visible. Collapsed headers show a summary (`MakeSectionSummaryInlines`).
- New sections: add a key to `DefaultSectionOrder`, a container in `GetSectionContainer`, and a summary.

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
- 150ms `DispatcherTimer` (`_selectionDebounceTimer`) in `GameList_SelectionChanged`
- Guard check ensures pending card still matches `SelectedGame` before rebuilding
- `PopulateDetailPanel` + `BuildOverridesPanel` are synchronous and heavy

## Compact View (removed)
- Compact/Simple view was removed in v2.7.6. `GameNameService` forces `ViewLayout.Detail` on every settings load; there is no Views toggle. `CompactViewBuilder.cs` no longer exists.
- Leftover `CurrentViewLayout == ViewLayout.Detail` checks are always true; don't build new UI for a compact layout.

## Component Cog Buttons (⚙️)
- Each component row (Components and Extras sections) has ⚙️ → per-component ContentDialog
- Always visible (no conditional hiding)
- **All cog dialogs use `CogDialog` (`CogDialog.cs`)**: `new CogDialog("Name", xamlRoot)` → title "Name Settings", 720 max width, scrolling body, `AddSection` / `AddRow` / `AddCombo` (170 px label column, control stretched, visible help text under every setting), `AddIntro` / `AddWarning` / `AddNote` / `AddEmpty`, `MakeButton` (Primary = deploy, Secondary = open/copy, Danger = delete) + `AddButtons`, and an in-dialog status line (`SetSuccess` / `SetError`, plain text, no emoji). Settings apply on change with a single Close button (no default button, so Enter in a text box doesn't close it); only slider dialogs (RTX HDR, Auto HDR) use `UseApplyButton()`. Cog buttons: `CogDialog.MakeCogButton(tooltip)`. Don't hand-build new cog dialogs. **Exception, user preference**: dialogs/sections with many small settings (OptiScaler, RenoDX Compatibility) use a compact two-per-row grid (label | dropdown | label | dropdown) with help as tooltips, added via `cog.AddContent(grid)` (see `AddOsGrid`, `AddRdxCompactGrid`). Ask before converting a dense section to one-per-row.
- Handlers live in `MainWindow.Events.Components*.cs` partials: ReShade/RE Framework in `.Components.cs`, RenoDX (incl. RTX HDR / Auto HDR) in `.Rdx.cs`, ReLimiter/DC in `.FrameLimiters.cs`, OptiScaler in `.OptiScaler.cs`, DXVK in `.Dxvk.cs`. Exceptions: Luma in `MainWindow.Events.cs`, DOF Fix in `MainWindow.Events.Install.cs`. Extras cogs (ASI Loader, RTX Encore, MFG unlocks, dgVoodoo2) are in `DetailPanelBuilder.Extras*.cs`.
- Addon-based components (ReLimiter, DC, addons) deploy to `ModInstallService.GetAddonDeployPath(installPath)` (honours `AddonPath` in reshade.ini). Cogs that read/write their ini must use the same path, not `card.InstallPath`.

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
