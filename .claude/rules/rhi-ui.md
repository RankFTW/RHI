---
paths:
  - "**/*DetailPanel*"
  - "**/*MainWindow*"
  - "**/*Builder*"
  - "**/*Flyout*"
  - "**/*Dialog*"
---

<!-- Copied from .kiro/steering/rhi-ui.md. Keep both in sync while Kiro is still used. -->

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

## Settings Page Layout
- `SettingsPanel` (MainWindow.xaml) → `ScrollViewer` → `SettingsCardsPanel` (StackPanel; MaxWidth 996 and left alignment set from C# once the nav is added). One long scrolling page, no collapsing. Its children are 8 unnamed section StackPanels, each with `Tag="Section:<Key>"`, in this order:
  1. `General`: Updates card (check for updates, background checks, Components to check / Update Inclusion, automatic updates for components, DLSS, Streamline) + Windows and Admin Mode card (tray, start with Windows, Admin Mode, Drop Helper)
  2. `Accounts`: one card, `RenoDxDbSourceCard` (GitHub token | mod data source + `NexusModsCard`, an inner Border shown only when `FeatureFlags.NexusMods`)
  3. `ReShade`: Keys, screenshots and brightness card + Shaders and addons card
  4. `Display`: Display card (`ResolutionControlCard`: HDR/resolution auto-toggle, target resolution, output colour, Digital vibrance) + `DldsrControlCard`
  5. `Dlss`: Defaults card (`DlssDefaultsSummaryPanel`) + DLSS options card (DLSS indicator, DMFG, Batch Deploy)
  6. `Driver`: Driver defaults card (NVAPI combos, G-Sync indicator) + Profiles card
  7. `Components`: ReLimiter and OptiScaler card
  8. `Tools`: Tools card (mass INI, folders and logs) + Troubleshooting card (Full Refresh, Purge Staging Files)
  Then the hidden vestigial StackPanel (ReShadeChannelCombo/DxvkVariantCombo), and the dev-only Freeze Diagnostics card appended in C# (`SettingsHandler.cs`).
- Each section = heading TextBlock (18 Bold) + one-line description (12, TextSecondaryBrush) + cards. **Use the `Settings*` styles in `Themes/DarkTheme.xaml`** (matches the approved mock-up): card `SettingsCardStyle` (title `SettingsCardTitleStyle`, optional `SettingsCardDescStyle`), then a Grid `[* | Auto (1px BorderDefaultBrush divider) | *]` with column StackPanels `Spacing=9`, Margin `0,0,20,0` / `20,0,0,0`. Sub-headings `SettingsHeadStyle`.
- **Setting row** (every setting): `Grid ColumnSpacing=10` with columns `*` and `* MaxWidth=210`; label `SettingsLabelStyle` in column 0, the control in column 1 (`SettingsComboStyle` / `SettingsTextBoxStyle`; extra buttons go in an inner Grid `[* | Auto…]` with `SettingsIconButtonStyle`). Short help under a row: `SettingsHelpStyle`; notes: `SettingsNoteStyle`. Long explanations go in the control's tooltip, not on the page. Buttons: `SettingsButtonStyle` (default), `SettingsPrimaryButtonStyle` (the main action of a block), `SettingsDangerButtonStyle` (destructive, must confirm with Cancel default); sized to their text, several in a `controls:WrapPanel` (8/8). **Hover text colour sticks (WinUI quirk)**: after hovering, the text could stay in `ButtonForegroundPointerOver` colour. Settings buttons keep their own text colour on hover/press instead: `SettingsCardsPanel.Resources` maps `ButtonForegroundPointerOver`/`Pressed` to SettingsLabelBrush (grey buttons), and every Primary/Danger button has `<Button.Resources>` mapping both to SettingsButtonBlueBrush/AccentRedBrush. Button text colours: grey `SettingsLabelBrush`, blue `SettingsButtonBlueBrush`, red `AccentRedBrush`. New Settings buttons must do the same; also set Foreground directly. Admin-only settings get the amber "Admin" pill next to the label. Sentence-case labels ("Shader cache size").
- **New settings go into the matching section.** Find sections from C# by walking `SettingsCardsPanel.Children` for `Tag` starting with `Section:`; never add `x:Name` for them (XAML constraint).
- Admin-only settings show a small amber "Admin" pill (`AccentAmberDimBrush` border, `AccentAmberBrush` text) next to the label (ReBAR — only when not elevated, its pill Border binds to `ReBarAdminWarning.Visibility` — DLSS indicator, Drop Helper).
- **Left column, search, countdown (all C#)**. `SettingsHandler.Navigation.cs`: on the first Settings open `EnsureSettingsNavigation()` (once) moves the ScrollViewer into a new Grid `[nav | *]`; the nav ("Find a setting" TextBox + one jump button per section, titles read from each section's heading TextBlock, 244 px, right-aligned in its column) sits outside the ScrollViewer. `SettingsCardsPanel` is left-aligned, MaxWidth 996; on wide windows the nav column is widened by half the spare width so nav + sections are centred as one block (`UpdateSettingsNarrowLayout`). Below 244 + 560 px the nav column is hidden and the search panel moves above the sections. The ScrollViewer is found by searching down from `SettingsPanel` (`FrameworkElement.Parent` of `SettingsCardsPanel` was null on first open), and the countdown finds `CheckForUpdatesBtn` by searching down from `SettingsCardsPanel`. Jump = `ChangeView` to the section's `TransformToVisual(SettingsCardsPanel)` Y. Highlight = last section whose top is above viewport top + 1/3 (last section at the very bottom), throttled 100 ms on `ViewChanged`; after a jump the clicked section stays highlighted until the user scrolls.
- **Last section**: `SettingsViewModel.SettingsLastSection` (default `General`), saved on a jump-button click and when Settings is hidden (visibility callback on `SettingsPanel`, covers Back/About/FAQ), set (not saved) on window close before the shutdown flush. Restored on open at Low dispatcher priority once the ScrollViewer has a viewport, then re-applied after `SettingsButton_Click` finishes populating (`OnSettingsPagePopulated`, in its `finally`) if the user hasn't scrolled. Re-clicking Settings while it's open keeps the position.
- **Search**: 200 ms debounce, Esc clears, Enter searches now. The index is rebuilt per query by walking each section's cards (TextBlocks incl. Runs, button content, Header, `ToolTipService` tooltips; skips Collapsed / Opacity 0 elements and never walks ComboBox items). One row per (context, label); tooltip-only matches show "label — tooltip excerpt". While searching every `SettingsCardsPanel` child (sections, vestigial panel, Freeze Diagnostics) is collapsed and its original Visibility remembered and restored; the results StackPanel is child 0 of `SettingsCardsPanel`. Freeze Diagnostics is indexed as part of the last section (Tools) and calls `HideNewCardWhileSearching` when appended. Clicking a result clears the search, scrolls the element ~80 px below the top and flashes its card border (`AccentBlueBrush`, 1.5 s). Leaving Settings clears the search.
- **Update countdown** (`SettingsHandler.UpdateCountdown.cs`): panel inserted above `CheckForUpdatesBtn` (found by `Name` next to `BackgroundUpdateChecksCombo`; the generated field is private). Reads `MainViewModel.NextUpdateCheckUtc` (set when the 4 h timer starts and each time it fires; null before the timer starts → "After startup") and `Settings.LastUpdateCheckUtc` (empty/unparsable → "Not yet"). Minimal mode labels: "Next data refresh in" / "Last full check" (the 4 h run skips component checks, so LastUpdateCheckUtc doesn't move). The manual Check For Updates calls `RestartPeriodicUpdateCheckTimer()` (re-arms the same timer with `Change`, no-op before start/after shutdown). 1 s `DispatcherQueueTimer` runs only while Settings is visible; stopped on leave and via `LifetimeToken` on close.

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
- Settings page text boxes save on Enter **and** on LostFocus (ScreenshotPathBox, AddonWatchFolderBox, PeakNitsBox; handlers in `MainWindow.Events.SettingsInputs.cs`). Hotkey boxes (HotkeyBox, ScreenshotHotkeyBox, UlHotkeyBox) persist the captured key immediately. The "Write ... to all games" buttons still read the box and persist before writing to games; keep that, since a click can run before LostFocus has saved.
- New Settings text boxes must do the same (save on Enter + LostFocus, compare with the saved value first so repeated focus changes don't re-save). Don't rely on an Apply button to persist a typed value.

## WinUI 3 Focus Pitfalls
- No `Window.Focus()` method. Defocus via disable/re-enable: `combo.IsEnabled = false; DispatcherQueue.TryEnqueue(() => combo.IsEnabled = true);`
- Editable ComboBox `SelectionChanged` doesn't fire for typed text until focus loss — use `KeyDown` for Enter.
- Editable ComboBox custom text not visible on init — use non-editable + "Custom..." sentinel + dialog instead.
