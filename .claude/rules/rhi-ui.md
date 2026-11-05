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
5. `NvidiaProfileDriver` → `NvidiaProfileDriverContainer` → `NvidiaProfileDriverPanel` ("Driver Settings"; driver version is the first summary item)
6. `Management` → `ManagementContainer` → `ManagementPanel` (Change install folder, Reset / Remove game, Reset Overrides, Copy Report)
7. `Extras` → `ExtrasContainer` → `ExtrasPanel` (ASI Loader, OptiScaler, MFG unlocks, DXVK, dgVoodoo2)

- `NvidiaProfileContainer` is a legacy Border kept collapsed for migration; never show it. Old setting key `NvidiaProfile` migrates to `NvidiaProfileDlss` + `NvidiaProfileDriver`.
- Every section collapses (heading click, `CollapsedDetailSections`) and reorders (≡ drag handle, `DetailSectionOrder`); both persist in settings. `ApplySectionOrder()` must run after all sections are populated and visible. Collapsed headers show a summary (`MakeSectionSummaryInlines`).
- **Layout host** (`DetailLayoutHost.cs`, `DetailPanelBuilder.Layout`): owns the section key → container map (`GetContainer` / `GetKey`), the order (`ApplySectionOrder` / `SaveSectionOrder`) and collapse state (`IsCollapsed` / `SetCollapsed`, which saves). Section builders must ask the host, never read or write `CollapsedDetailSections` / `DetailSectionOrder` directly, so every layout (`docs/layouts-plan-2.9.0.md`) arranges the same containers. Layout = `Settings.MainLayout` (`Classic` | `Tabs`), chosen with the top bar switch (`MainWindow.TabsChrome.cs`, `SetMainLayout`; there is no Settings option); a change calls `MainWindow.ApplyMainLayout()` (one rebuild of the shown game). Every section builder calls `Layout.RegisterHeader(key, headerRow)` after building its header row (Tabs hides headers). Code that shows a section outside the selection path uses `Layout.ShowSection(key)`, never `container.Visibility = Visible` (Tabs would show it under the wrong tab). **Tabs**: tab strip inserted at DetailPanel index 2 (sections start at 3; the Classic ≡ drag code assumes 2, fine because Tabs hides the handles), tabs come in a fixed order (`FixedTabOrder`), not Classic's `DetailSectionOrder`; `IsCollapsed` is always false, `ApplySectionOrder` shows only `Settings.DetailSelectedTab`; switching tabs only changes visibility, never rebuilds. **Overview tab** (Tabs only, `DetailLayoutHost.Overview.cs`): replaces the Components tab (Components container stays built and updated but hidden). One card per Components row (`MainWindow.ComponentRows`, `ComponentRowRef`): cards hold no rules, they copy the hidden row's label, status, button text/colours/enabled state and Info/⚙/✕ visibility after `UpdateDetailComponentRows` (`Layout.RefreshOverviewCards()`) and `UpdateDetailProgressRows` (`RefreshOverviewProgress()`), and their buttons call the row's own handlers with the **row's** button as sender (the Info dialog picks the component by the button's `Name`). New component rows: add them to `ComponentRows`. At a glance tiles copy each section's header summary (TextBlocks after index 3 in the registered header row); summaries filled in later, or rebuilt on their own, must call `Layout.SummaryChanged()` (DLSS, Driver and Neural Rendering after their background builds; `BuildExtrasSection` at its end, which also covers Game Overrides and Management because Extras is built last in `BuildOverridesPanel`). Spotlight's Tune tiles use the same call.
- New sections: add a key to `DefaultSectionOrder`, a container in `DetailLayoutHost.GetContainer`, and a summary.

## Tabs layout (mockup A, v2.9.0)
- Must look and behave like `docs/mockups/layouts-2.9.0/Main.dc.html`. Colours, sizes and control factories: `TabsTheme.cs` (tokens also in `docs/layouts-plan-2.9.0.md`). Grids use `Controls/AutoFitGrid` (CSS `repeat(auto-fit, minmax(N, 1fr))`, items stretch when fewer than columns).
- **Nothing is copied.** Section builders create every control as in Classic; when `Layout.IsTabs` they hand the finished, not-yet-shown content to `DetailLayoutHost.Sections.cs` (`ArrangeOverrides`, `ArrangeDlss`, `ArrangeDriver`, `ArrangeNeuralRendering`, `ArrangeExtras`; Management builds its tiles itself), which moves those same controls into the mockup arrangement and calls `TabsTheme.Restyle`. Arranging before the content is shown adds no layout passes (freeze history). A setting added to a builder appears in both layouts; if a builder's shape changes, the arrangers fall back to untitled cards rather than dropping controls.
- Overview cards (`DetailLayoutHost.Overview.cs`), the game header (`DetailLayoutHost.Header.cs`) and the top bar / sidebar / footer (`MainWindow.TabsChrome.cs`; the top bar and the sidebar (segmented main filter, Filters button) are used in every layout, Classic too, while the footer restyles only in Tabs / Spotlight) copy state from the hidden Classic controls and call their handlers with the Classic control as sender (`MainWindow.ComponentRows.cs`, `MainWindow.HeaderActions.cs`). The Classic header rows and Components section stay in the window (hidden), so their buttons keep a XamlRoot.
- Layout switches happen on the Settings page, where the DLSS / Driver / Neural Rendering background builds skip themselves (freeze guard), so `ApplyMainLayout` only flags `_layoutRebuildPending` there and `UpdatePageVisibility` rebuilds the game page when it's shown again.
- Tabs page: full width (32 px padding, cap 1600) via `MainWindow.UpdateDetailPanelWidth()`; Classic keeps 850. Section containers lose their card frame in Tabs (`ApplyContainerChrome`).
- **WinUI pitfalls hit here:** (1) moving controls: empty the old parent (`Children.Clear()`) before adding them elsewhere; `FrameworkElement.Parent` isn't reliable for code-built trees ("No installed components were detected"). (2) Setting a `ResourceDictionary` key that already exists throws a COMException with no message: use `TabsTheme.SetResource` (removes first). (3) Button hover colours are read when the template loads, so a button whose look changes is replaced, not restyled.
- **Dev screenshot hook** (`MainWindow.DevScreenshot.cs`, needs `unlock.txt`): write `%LocalAppData%\RHI\rhi_screenshot_requested` with an output .png path, optionally `tab=<key>`, `game=<name>`, `layout=Classic|Tabs`, `target=detail`, `wait=<ms>`; RHI renders itself with `RenderTargetBitmap`. Needed because a normal process can't capture RHI running in Admin Mode.

## Spotlight layout (mockup D, v2.9.0)
- `MainLayout.Spotlight` builds on Tabs: `Layout.IsTabs` is true for both (Tabs look, `Arrange*` section layouts, Tabs top bar / sidebar restyle / footer); `Layout.IsSpotlight` adds the rest. Plan and decisions: `docs/layouts-plan-2.9.0.md` step 4.
- Page (`DetailLayoutHost.Spotlight.cs`): `ApplyHeroMode` moves the Tabs header into a hero Border over the Steam artwork (title 44) and puts the page body ("What's installed" `StatusTile`s copied from `ComponentRows`, "Tune this game" = `MakeGlanceTile`s) in `_tabChrome` instead of the tab strip. No section shows on the page.
- **Slide-in panel**: `SelectTab(key)` in Spotlight calls `OpenPanel(key)`, which **moves the section's container** out of `DetailPanel` into an overlay added to `GameViewPanel` (scrim + right-hand sheet, `PaneThemeTransition`); `ReturnPanelContent` puts it back (appended; `ApplySectionOrder` reorders). Overview (`OverviewKey`) shows the component cards with At a glance hidden. `ShowSection` / `ApplySpotlightSelection` show only the panel's container. Leaving Spotlight closes the panel (`ApplySectionOrder`).
- Window (`MainWindow.SpotlightChrome.cs`, called from `ApplyTabsChrome` after the Tabs chrome): `GameViewPanel` gets two rows; the sidebar spans the top row (search + switch left, Filters + counts right in row 0 of its grid), the content column the bottom row. `GameList` is collapsed and a horizontal `ListView` shelf with the same `ItemsSource` (`DisplayedGames`) takes its grid row; shelf selection sets `GameList.SelectedItem`, so the normal selection / debounce / rebuild path runs. Tiles are built in `ContainerContentChanging` (no bindings); right-click uses `GameRow_RightTapped` (card in `Tag`); the wheel scrolls sideways.
- Artwork: `SteamArtworkService` (AppID via `DetailPanelBuilder.ResolveCommunityAppId`), cache `%LocalAppData%\RHI\artwork\{appid}\hero.jpg|cover.jpg`, misses remembered 7 days (`*.miss`), 4 parallel downloads, only requested while Spotlight is on. Covers are decoded at 240 px and cached per path. **Custom art** (game menu › Artwork, Spotlight only; the hero's right-click opens the same menu): `SetCustom` copies the image to `artwork\custom\{GameName|Store, sanitised + hash}\{cover|hero}-{ticks}.ext` (new name per change so cached BitmapImages aren't reused) and wins over Steam art; `ClearCustom` = Use Steam cover/banner. Not a per-game override in settings, so it isn't migrated on rename.
- Dev screenshot hook: `tab=none` closes the panel; any other `tab=` opens it in Spotlight. Its one-shot timer is held in a field (a local `DispatcherQueueTimer` can be garbage-collected before it fires: that silently broke screenshots). `pad=Down,Select,Back` runs controller actions (one per 600 ms, focus logged) before the screenshot; it presses buttons for real, so never script A onto install/remove buttons.
- **Controller support** (`MainWindow.Controller.cs`, `Services/ControllerInput.cs`, `Services/PlayStationPad.cs`, `DetailLayoutHost.Controller.cs`; PlayStation pads (DS4 054C:05C4/09CC/0BA0, DualSense 0CE6, Edge 0DF2) are read natively from HID input reports on a background thread (shared read-only handle; `PlayStationReport.Parse` handles USB 0x01, DS4 BT 0x01/0x11, DualSense BT 0x01 simple/0x31; USB = 64-byte reports) into XInput-style `PadState`; XInput wins when both are present (DS4Windows / Steam virtual pads), and the hints show PlayStation names; Spotlight only, setting `ControllerSupport`, row added under Drop Helper by `SettingsHandler.Controller.cs`): XInput polled on a UI timer (16 ms with a pad, 250 ms otherwise, empty slots rescanned every 2 s) only while RHI is the foreground window; `ControllerPoller.Reset` ignores buttons held when RHI becomes active. Focus moves with `FocusManager.FindNextElement` + `TryFocusAsync(FocusState.Keyboard)` (focus outline, bring-into-view); special cases: shelf left/right = previous/next game, shelf Down = Launch, Launch Up = shelf, shelf Up = the selected All / Installed / … segment (`TabsTheme.SegmentSelectedTag`; segments are Buttons), Down from the search / filter row = the selected cover, panel keeps focus inside `PanelSheet`, footer is skipped, sliders change value. A uses automation peers (Invoke / Toggle / ExpandCollapse; ComboBox opens). Moving on the page and in the panel goes by **rows** (`MainWindow.Controller.Rows.cs`: focusable tab-stop controls grouped by vertical position, zone 0 = fixed chrome, zone 1 = inside the scroll viewer measured in its content so scrolling doesn't reorder; Left/Right stay in the row; entering a row picks Launch / the selected filter segment if the row has one, else the control whose width covers the current control's middle X, else nearest X; there is deliberately no memory of earlier visits: it made Up / Down jump sideways in grids like the DLSS panel); the shelf is one target. Popups are worked **through their controls, never key presses** (`MainWindow.Controller.Popups.cs`): ComboBox items focused by index, A sets `SelectedIndex` + closes, B closes unchanged; MenuFlyout items move with wrap, submenus via ExpandCollapse peer (`_padSubmenus` stack), B closes menu popups / `ContentDialog.Hide()`. In dialogs a control tagged `"PadStart"` is where the controller starts (`FirstInPopup`; What's New tags the selected version, and its version rows are Buttons that show the version on keyboard focus); `WatchForNewPopup` (each poll while the hint bar shows) moves focus into a dialog that appears later, and A with focus behind an open popup moves into it instead of pressing. The right stick in a popup scrolls the biggest ScrollViewer that doesn't hold focus (`PopupScroller`). Dev `pad=` also takes `ScrollDown` / `ScrollUp`. LB/RB: panel = `Layout.StepPanel` (Components + Tune tile order, wraps), Settings = `SettingsHandler.PadJumpSection`. Y (or A on Launch) = `PadLaunch`: toast, 8 s same-game guard, `IsRunning` check, focus returns to the cover when RHI is active again. When a game launched from RHI exits (`MonitorProcessForHdr`, both normal-exit paths), `RefocusAfterGameExit` (`MainWindow.ReturnAfterGame.cs`; Spotlight + `ControllerSupport` only, skipped when hidden to tray) brings RHI to the front: `SetForegroundWindow`, and if Windows refuses (last input went to the game) one Alt tap via `keybd_event` lifts the foreground lock and it retries. No `AttachThreadInput` (removed earlier: hang risk). **Pointer while the pad is in use** (`MainWindow.Controller.Pointer.cs`): `ShowPadHints(true)` shows a transparent, hit-testable shield Border over the root Grid (no hover / tooltips from a resting pointer; popups/dialogs aren't covered) and closes open ToolTips; `ShowPadHints(false)` hides it. The cursor itself stays visible: `ShowCursor(false)` (returned -1) and repeated `SetCursor(null)` from the UI thread, which owns the `DesktopChildSiteBridge` under the pointer, both failed, because WinUI re-sets the arrow. Untried options: WM_SETCURSOR subclass on the bridge, or a blank `ProtectedCursor` (needs a .cur Win32 resource). `PadFocusNext` sets the shield's `IsHitTestVisible = false` for the synchronous `FindNextElement` call in case the search skips covered candidates (unconfirmed: it didn't fix Down from the ⚙ on Settings, which is WinUI's projection search finding nothing below a control far right of the settings column). Opening Settings with the pad (hints showing) focuses the shown section's nav button (`SettingsHandler.PadStartButton`, from `OnSettingsPagePopulated` → `PadFocusSettingsStart`; `FocusHome` uses it too). The shield's arrival raises a PointerMoved without movement, so the hint bar's PointerMoved handler ignores events while the shield is up. **Never set `IsHitTestVisible = false` on the page for this**: `Controller.Rows.cs` skips non-hit-testable nodes, which breaks D-pad navigation. The root Grid is `_uiScaledRoot ?? Content` (`Content` is a scaling Canvas at interface sizes other than 100%). Because the shield swallows PointerMoved, `PadTick` polls `GetCursorPos` (3 px slack) + mouse buttons (`GetAsyncKeyState`) to give the mouse back; losing the foreground restores the pointer but keeps the hints. Touch input is ignored until the mouse moves. Hints + toast (`MainWindow.Controller.Hints.cs`) sit in GameViewPanel's parent grid, list only what works where focus is, and add 64 px bottom padding to `DetailScrollViewer` while shown. `PadFocus` sets a blue (`TabsTheme.Accent`) focus visual. LT / RT = 10 games (triggers folded in as unused button bits by `ControllerPoller.WithTriggers`), left stick click = cycle All / Installed / Favourites (`CycleMainFilter`), right stick click = Home (cover). Controller moves keep the selected cover centred (`CenterShelf`: clamped ChangeView; `_padStepping` skips the shelf's ScrollIntoView and the items panel swallows BringIntoViewRequested for 400 ms). Shelf items have `UseSystemFocusVisuals=False` (the tile's blue ring marks the selection; no white focus box). Spotlight tiles (`TabsTheme.WrapTile`), segmented switches and top bar links (`MakeNavItem`) are Buttons so they can take focus; new clickable Spotlight elements must be focusable controls, not Borders with Tapped.

## Status bar (bottom left)
- `MainWindow.StatusBar.cs` owns `StatusBarText`; never set its Text directly. Priority: `ShowStatusBarMessage(msg, duration)` (one-off notices) > loading/Refresh progress (`StatusText` + `SubStatusText`) > `ViewModel.CurrentActivityText` > offline/error `StatusText` > idle "Last checked: … · Next check in …" (refreshed every 15 s, Minimal mode wording matches the Settings countdown).
- Background work reports itself with `using var activity = BeginActivity("Checking for updates...");` (`MainViewModel.Activity.cs`, thread-safe, overlapping activities show the newest). Used by the background scan, `CheckForUpdatesAsync`, the 4 h periodic refresh and DLSS/Streamline auto-update. New long-running background work should do the same.
- Game and ReShade counts live above the game list (`GameCountText`/`InstalledCountText`), not in the status bar.

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
- **All cog dialogs use `CogDialog` (`CogDialog.cs`)**: `new CogDialog("Name", xamlRoot)` → title "Name Settings", 720 max width, scrolling body, `AddSection` / `AddRow` / `AddCombo` (170 px label column, control stretched, visible help text under every setting), `AddIntro` / `AddWarning` / `AddNote` / `AddEmpty`, `MakeButton` (Primary = deploy, Secondary = open/copy, Danger = delete) + `AddButtons`, and an in-dialog status line (`SetSuccess` / `SetError`, plain text, no emoji). Settings apply on change with a single Close button (no default button, so Enter in a text box doesn't close it); only slider dialogs (RTX HDR, Auto HDR) use `UseApplyButton()`. Cog buttons: `CogDialog.MakeCogButton(tooltip)`. Don't hand-build new cog dialogs. **Look and hosts (v2.9.0)**: `AddSection` starts a card (TabsTheme), settings before the first section share an untitled card, and the body is `TabsTheme.Restyle`d before showing; `cog.Body` is the open card, `cog.Items` walks everything with cards opened up (use it, not `Body.Children`, to dim rows). `ShowAsync` asks `CogDialog.EmbeddedShow` (`DetailLayoutHost.CogHosts.cs`) first: Spotlight shows the cog in the side panel the ⚙ was clicked in (back button returns to the section), Tabs inserts it under the clicked ⚙'s row (found from the focused button; same ⚙ toggles it; re-attached after a rebuild by tooltip + Tag, else closed), Classic+ and anything else get the dialog. `Hide()` closes whichever host; Apply/Cancel cogs get an Apply bar and `ContentDialogResult.Primary`. **Exception, user preference**: dialogs/sections with many small settings (OptiScaler, RenoDX Compatibility) use a compact two-per-row grid (label | dropdown | label | dropdown) with help as tooltips, added via `cog.AddContent(grid)` (see `AddOsGrid`, `AddRdxCompactGrid`). Ask before converting a dense section to one-per-row.
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
  8. `Tools`: Custom game folders card (C#, `SettingsHandler.CustomFolders.cs`, inserted before the Tools card) + Tools card (mass INI, folders and logs) + Troubleshooting card (Full Refresh, Purge Staging Files, Removed games)
  Then the hidden vestigial StackPanel (ReShadeChannelCombo/DxvkVariantCombo), and the dev-only Freeze Diagnostics card appended in C# (`SettingsHandler.cs`).
- Each section = heading TextBlock (18 Bold) + one-line description (12, TextSecondaryBrush) + cards. **Use the `Settings*` styles in `Themes/DarkTheme.xaml`** (matches the approved mock-up): card `SettingsCardStyle` (title `SettingsCardTitleStyle`, optional `SettingsCardDescStyle`), then a Grid `[* | Auto (1px BorderDefaultBrush divider) | *]` with column StackPanels `Spacing=9`, Margin `0,0,20,0` / `20,0,0,0`. Sub-headings `SettingsHeadStyle`.
- **Setting row** (every setting): `Grid ColumnSpacing=10` with columns `*` and `* MaxWidth=210`; label `SettingsLabelStyle` in column 0, the control in column 1 (`SettingsComboStyle` / `SettingsTextBoxStyle`; extra buttons go in an inner Grid `[* | Auto…]` with `SettingsIconButtonStyle`). Short help under a row: `SettingsHelpStyle`; notes: `SettingsNoteStyle`. Long explanations go in the control's tooltip, not on the page. Buttons: `SettingsButtonStyle` (default), `SettingsPrimaryButtonStyle` (the main action of a block), `SettingsDangerButtonStyle` (destructive, must confirm with Cancel default); sized to their text, several in a `controls:WrapPanel` (8/8). **Hover text colour sticks (WinUI quirk)**: after hovering, the text could stay in `ButtonForegroundPointerOver` colour. Settings buttons keep their own text colour on hover/press instead: `SettingsCardsPanel.Resources` maps `ButtonForegroundPointerOver`/`Pressed` to SettingsLabelBrush (grey buttons), and every Primary/Danger button has `<Button.Resources>` mapping both to SettingsButtonBlueBrush/AccentRedBrush. Button text colours: grey `SettingsLabelBrush`, blue `SettingsButtonBlueBrush`, red `AccentRedBrush`. New Settings buttons must do the same; also set Foreground directly. Admin-only settings get the amber "Admin" pill next to the label. Sentence-case labels ("Shader cache size").
- **New settings go into the matching section.** Find sections from C# by walking `SettingsCardsPanel.Children` for `Tag` starting with `Section:`; never add `x:Name` for them (XAML constraint).
- Admin-only settings show a small amber "Admin" pill (`AccentAmberDimBrush` border, `AccentAmberBrush` text) next to the label (ReBAR — only when not elevated, its pill Border binds to `ReBarAdminWarning.Visibility` — DLSS indicator, Drop Helper).
- **Left column, search, countdown (all C#)**. `SettingsHandler.Navigation.cs`: on the first Settings open `EnsureSettingsNavigation()` (once) moves the ScrollViewer into a new Grid `[nav | *]`; the nav ("Find a setting" TextBox + one jump button per section, titles read from each section's heading TextBlock, 244 px, right-aligned in its column) sits outside the ScrollViewer. `SettingsCardsPanel` is left-aligned, MaxWidth 996; on wide windows the nav column is widened by half the spare width so nav + sections are centred as one block (`UpdateSettingsNarrowLayout`). Below 244 + 560 px the nav column is hidden and the search panel moves above the sections. The ScrollViewer is found by searching down from `SettingsPanel` (`FrameworkElement.Parent` of `SettingsCardsPanel` was null on first open), and the countdown finds `CheckForUpdatesBtn` by searching down from `SettingsCardsPanel`. Jump = `ChangeView` to the section's `TransformToVisual(SettingsCardsPanel)` Y. Highlight = last section whose top is above viewport top + 1/3 (last section at the very bottom), throttled 100 ms on `ViewChanged`; after a jump the clicked section stays highlighted until the user scrolls.
- **Last section**: `SettingsViewModel.SettingsLastSection` (default `General`), saved on a jump-button click and when Settings is hidden (visibility callback on `SettingsPanel`, covers Back/About/FAQ), set (not saved) on window close before the shutdown flush. Restored on open at Low dispatcher priority once the ScrollViewer has a viewport, then re-applied after `SettingsButton_Click` finishes populating (`OnSettingsPagePopulated`, in its `finally`) if the user hasn't scrolled. Re-clicking Settings while it's open keeps the position.
- **Search**: 200 ms debounce, Esc clears, Enter searches now. The index is rebuilt per query by walking each section's cards (TextBlocks incl. Runs, button content, Header, `ToolTipService` tooltips; skips Collapsed / Opacity 0 elements and never walks ComboBox items). One row per (context, label); tooltip-only matches show "label — tooltip excerpt". While searching every `SettingsCardsPanel` child (sections, vestigial panel, Freeze Diagnostics) is collapsed and its original Visibility remembered and restored; the results StackPanel is child 0 of `SettingsCardsPanel`. Freeze Diagnostics is indexed as part of the last section (Tools) and calls `HideNewCardWhileSearching` when appended. Clicking a result clears the search, scrolls the setting's row (or the element) to the middle of the viewport and flashes its card border and the matched setting's row (`AccentBlueBrush`, 1.5 s; `FindSettingRow` walks up to the child of the nearest vertical StackPanel, or outlines a text button by itself; the row border is offset with a negative margin so nothing moves). Leaving Settings clears the search.
- **Update countdown** (`SettingsHandler.UpdateCountdown.cs`): panel inserted above `CheckForUpdatesBtn` (found by `Name` next to `BackgroundUpdateChecksCombo`; the generated field is private). Reads `MainViewModel.NextUpdateCheckUtc` (set when the 4 h timer starts and each time it fires; null before the timer starts → "After startup") and `Settings.LastUpdateCheckUtc` (empty/unparsable → "Not yet"). Minimal mode labels: "Next data refresh in" / "Last full check" (the 4 h run skips component checks, so LastUpdateCheckUtc doesn't move). The manual Check For Updates calls `RestartPeriodicUpdateCheckTimer()` (re-arms the same timer with `Change`, no-op before start/after shutdown). 1 s `DispatcherQueueTimer` runs only while Settings is visible; stopped on leave and via `LifetimeToken` on close.

## Admin Mode
- Task Scheduler task "RHI Admin Mode" for UAC-free elevation
- `schtasks /Create /RL HIGHEST /SC ONCE /ST 00:00 /SD 01/01/2000`
- Auto-relaunch: checks on startup, runs task, exits immediately
- `schtasks /Run` can't pass arguments, so the elevated copy never sees the non-elevated one's command line. Hand-off files in `%LocalAppData%\RHI\`: `rhi_start_minimized` (`--minimized`) and `rhi_pending_launch` (UTC time + game name for `--launch` from desktop shortcuts / jump list; ignored after 2 min). Any new startup argument needs the same, or it's silently lost when Admin Mode is on and RHI wasn't running.
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
- **Every dialog gets the navy look automatically**: `DialogService.ShowSafeAsync` / `ShowProgressAsync` call `DialogLook.Apply` (`DialogLook.cs`): `CogDialog.ApplyDialogLook` frame + button styles + blue ticks, a string `Title` becomes `CogDialog.MakeDialogTitle`, content gets colour-only restyling (inputs keep their sizes). Dialogs that call `ApplyDialogLook` themselves are skipped (`RhiDialogLook` resource marker). Don't hand-style new dialogs' colours; don't add icons to titles.
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
