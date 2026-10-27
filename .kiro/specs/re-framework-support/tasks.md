# Tasks

## Task 1: Add REEngine to EngineType and Detection Logic
- [x] 1.1 Add `REEngine` variant to `EngineType` enum in `RenoDXCommander/Models/EngineType.cs`
- [x] 1.2 Add RE Engine detection in `GameDetectionService.DetectEngineAndPathCore` — check for `re_chunk_000.pak` via `FindFileShallow` before the generic fallback, return `EngineType.REEngine`
- [x] 1.3 Add `REEngine` to the engine hint mapping in `MainViewModel` so `EngineType.REEngine` maps to `"RE Engine"` display text and `engine.ico` icon
- [x] 1.4 Add `REEngine` to the `ResolveEngineOverride` mapping so manifest overrides can set `"RE Engine"` label
- [x] 1.5 Add `REEngine` to the graphics API fallback in `DetectGraphicsApi` — RE Engine games typically use DirectX 11 or DirectX 12
- [x] 1.6 Update existing test generators (e.g. `GenEngineType` in `DragDropPropertyTests.cs`) to include `EngineType.REEngine`

## Task 2: Create REFrameworkInstalledRecord Model
- [x] 2.1 Create `RenoDXCommander/Models/REFrameworkInstalledRecord.cs` with `GameName`, `InstallPath`, `InstalledVersion`, `InstalledAt` properties

## Task 3: Create IREFrameworkService and REFrameworkService
- [x] 3.1 Create `RenoDXCommander/Services/IREFrameworkService.cs` interface with `InstallAsync`, `Uninstall`, `CheckForUpdateAsync`, `GetLatestVersionAsync` methods
- [x] 3.2 Create `RenoDXCommander/Services/REFrameworkService.cs` implementing the interface — download MHWILDS.zip from GitHub nightly releases, extract dinput8.dll via `System.IO.Compression.ZipFile`, cache in `%LocalAppData%\RHI\downloads\reframework\`, copy to game directory
- [x] 3.3 Implement version tracking — fetch latest release tag from GitHub API, persist install records to `reframework_installed.json`
- [x] 3.4 Implement update checking — compare installed version tag against latest release tag
- [x] 3.5 Implement uninstall — delete dinput8.dll from game directory, remove install record
- [x] 3.6 Implement ZIP cleanup — delete MHWILDS.zip after successful extraction, retain only dinput8.dll in cache

## Task 4: Create GameCardViewModel.REFramework.cs Partial Class
- [x] 4.1 Create `RenoDXCommander/ViewModels/GameCardViewModel.REFramework.cs` with observable properties: `RefStatus`, `RefIsInstalling`, `RefProgress`, `RefActionMessage`, `RefInstalledVersion`
- [x] 4.2 Add plain properties: `RefRecord`, `IsREEngineGame`, `ExcludeFromUpdateAllRef`
- [x] 4.3 Add computed properties: `RefStatusText`, `RefStatusColor`, `RefShortAction`, `RefActionLabel`, `RefBtnBackground/Foreground/BorderBrush`, `RefDeleteVisibility`, `RefProgressVisibility`, `RefMessageVisibility`, `RefRowVisibility`, `IsRefInstalled`, `IsRefNotInstalling`, `CardRefStatusDot`, `CardRefInstallEnabled`
- [x] 4.4 Add targeted notification methods: `NotifyRefStatusDependents`, `NotifyRefIsInstallingDependents`, and partial `OnRefStatusChanged`/`OnRefIsInstallingChanged` hooks
- [x] 4.5 Add RE Framework properties to `NotifyAll()` in `GameCardViewModel.cs`

## Task 5: Register REFrameworkService in DI and Wire into MainViewModel
- [x] 5.1 Register `IREFrameworkService` / `REFrameworkService` as singleton in `App.xaml.cs` DI container
- [x] 5.2 Inject `IREFrameworkService` into `MainViewModel` and add `InstallREFrameworkCommand` / `UninstallREFrameworkCommand` relay commands
- [x] 5.3 Set `IsREEngineGame = true` on cards where `engine == EngineType.REEngine` during card initialization in `MainViewModel`
- [x] 5.4 Load RE Framework install records on startup and populate `RefRecord`/`RefStatus`/`RefInstalledVersion` on matching cards

## Task 6: Add RE Framework Update Checking
- [x] 6.1 Add RE Framework update check in `MainViewModel` or `UpdateOrchestrationService.CheckForUpdatesAsync` — call `IREFrameworkService.CheckForUpdateAsync` for all cards with `IsREEngineGame && IsRefInstalled`
- [x] 6.2 Set `RefStatus = GameStatus.UpdateAvailable` on all matching cards when a newer version is found
- [x] 6.3 Add `UpdateAllREFrameworkAsync` to `UpdateOrchestrationService` following the `UpdateAllReShadeAsync` pattern
- [x] 6.4 Include RE Framework in the "Update All" button handler in `MainViewModel`

## Task 7: Add RE Framework UI to DetailPanelBuilder
- [x] 7.1 Add `DetailRefRow` Grid to `MainWindow.xaml` with the same column structure as `DetailRsRow` (name, status, install button, delete button, progress bar, message)
- [x] 7.2 Place `DetailRefRow` above `DetailRsRow` in the XAML layout
- [x] 7.3 Add RE Framework row population in `DetailPanelBuilder.UpdateDetailComponentRows` — set status text, button content, visibility, colors based on card's Ref properties
- [x] 7.4 Add RE Framework property change handling in `DetailPanelBuilder.DetailCard_PropertyChanged`

## Task 8: Add RE Framework UI to CardBuilder
- [x] 8.1 Add RE Framework status dot to the card grid status row in `CardBuilder.BuildGameCard`
- [x] 8.2 Add RE Framework component row in `CardBuilder.BuildInstallFlyoutContent` above the ReShade row
- [x] 8.3 Add RE Framework PropertyChanged handlers for live flyout updates

## Task 9: Add RE Framework Install/Uninstall Event Handlers
- [x] 9.1 Add `InstallRefButton_Click` and `UninstallRefButton_Click` in `InstallEventHandler` following the `InstallUlButton_Click`/`UninstallUlButton_Click` pattern
- [x] 9.2 Add RE Framework to `CardComponentInstall_Click` and `CardComponentUninstall_Click` switch cases in `MainWindow.xaml.cs`
- [x] 9.3 Add RE Framework to the combined install flow in `CardInstallButton_Click` — install RE Framework before ReShade for RE Engine games

## Task 10: Write Property-Based Tests
- [x] 10.1 Write property test for RE Engine detection (Property 1): generate random directory structures, verify detection returns REEngine iff re_chunk_000.pak is present
- [x] 10.2 Write property test for engine hint mapping (Property 2): for all EngineType values, verify the hint string mapping
- [x] 10.3 Write property test for RE Framework row visibility (Property 4): generate random (IsREEngineGame, IsLumaMode) pairs, verify RefRowVisibility
- [x] 10.4 Write property test for status text mapping (Property 5): generate random (RefStatus, RefIsInstalling, RefInstalledVersion) tuples, verify RefStatusText
- [x] 10.5 Write property test for action label mapping (Property 6): generate random (RefStatus, RefIsInstalling) pairs, verify RefActionLabel
- [x] 10.6 Write property test for status dot and UI state mapping (Property 7): generate random (RefStatus, RefIsInstalling) pairs, verify CardRefStatusDot, RefDeleteVisibility, RefProgressVisibility
- [x] 10.7 Write property test for install record serialization round-trip (Property 8): generate random REFrameworkInstalledRecord instances, serialize/deserialize, verify equality
