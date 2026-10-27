# Tasks

## Task 1: Add Screenshot Settings to SettingsViewModel
- [x] 1.1 Add `[ObservableProperty] private string _screenshotPath = "";` and `[ObservableProperty] private bool _perGameScreenshotFolders;` to `RenoDXCommander/ViewModels/SettingsViewModel.cs`
- [x] 1.2 Add `ScreenshotPath` and `PerGameScreenshotFolders` reading to `LoadSettingsFromDict` — read `"ScreenshotPath"` as string and `"PerGameScreenshotFolders"` as `"true"`/`"false"` bool, following the `AddonWatchFolder`/`UseCustomShaders` pattern
- [x] 1.3 Add `ScreenshotPath` and `PerGameScreenshotFolders` writing to `SaveSettingsToDict` — write `s["ScreenshotPath"]` and `s["PerGameScreenshotFolders"]` following the existing pattern

## Task 2: Add SanitizeDirectoryName and ApplyScreenshotPath to AuxInstallService
- [x] 2.1 Add `public static string SanitizeDirectoryName(string name)` to `RenoDXCommander/Services/AuxInstallService.cs` — remove characters `< > : " / | ? *` from the input string
- [x] 2.2 Make `ParseIni` and `WriteIni` methods `internal static` (instead of `private static`) so they can be called from `ApplyScreenshotPath` and tested — update the `OrderedDict` class visibility to `internal` as well
- [x] 2.3 Add `public static void ApplyScreenshotPath(string iniFilePath, string savePath)` to `AuxInstallService` — call `ParseIni`, ensure `[SCREENSHOT]` section exists, set `SavePath` key, call `WriteIni`

## Task 3: Integrate Screenshot Path into MergeRsIni and MergeRsVulkanIni
- [x] 3.1 Add optional `string? screenshotSavePath = null` parameter to `MergeRsIni` — after the existing `WriteIni` call, if `screenshotSavePath` is non-null, call `ApplyScreenshotPath` on the merged file
- [x] 3.2 Add optional `string? screenshotSavePath = null` parameter to `MergeRsVulkanIni` — same pattern as 3.1
- [x] 3.3 Update all existing callers of `MergeRsIni`/`MergeRsVulkanIni` (in `MainWindow.xaml.cs`, `InstallEventHandler`, `AuxInstallService.InstallReShadeAsync`) to pass the configured screenshot path from `SettingsViewModel` when available

## Task 4: Add Screenshots Section to Settings Page XAML
- [x] 4.1 Add the Screenshots `<Border>` section to `RenoDXCommander/MainWindow.xaml` inside the Settings panel `<StackPanel>`, after the "Custom Shaders" section — include `ScreenshotPathBox` TextBox, `PerGameScreenshotToggle` ToggleSwitch, and `ApplyScreenshotPathBtn` Button
- [x] 4.2 Add `x:FieldModifier="internal"` to `ScreenshotPathBox` and `PerGameScreenshotToggle` so they are accessible from `SettingsHandler`
- [x] 4.3 Add `Click="ApplyScreenshotPath_Click"` handler reference on the Apply button

## Task 5: Add Apply Handler to SettingsHandler and MainWindow
- [x] 5.1 Add `ApplyScreenshotPath_Click` method to `RenoDXCommander/SettingsHandler.cs` — read path from `ScreenshotPathBox.Text` and toggle from `PerGameScreenshotToggle.IsOn`, persist via `SettingsViewModel`, iterate game cards, call `ApplyScreenshotPath` for each eligible game, show confirmation count
- [x] 5.2 Add `ApplyScreenshotPath_Click` delegate method in `MainWindow.xaml.cs` that forwards to `_settingsHandler.ApplyScreenshotPath_Click(sender, e)` — following the existing pattern for settings handlers
- [x] 5.3 Populate `ScreenshotPathBox.Text` and `PerGameScreenshotToggle.IsOn` from `SettingsViewModel` in `SettingsHandler.SettingsButton_Click` — following the pattern used for `AddonWatchFolderBox` and other toggles
- [x] 5.4 Expose the full card list from `MainViewModel` so `SettingsHandler` can iterate all games (add a public accessor if not already available, e.g. `AllCards` property)

## Task 6: Write Property-Based Tests
- [x] 6.1 Create `RenoDXCommander.Tests/ScreenshotSettingsRoundTripPropertyTests.cs` — Property 1: for any string path and bool perGame, save/load round-trip preserves both values
- [x] 6.2 Create `RenoDXCommander.Tests/ApplyScreenshotPathPropertyTests.cs` — Property 2: for any INI content and non-empty path, ApplyScreenshotPath produces [SCREENSHOT] SavePath with the correct value
- [x] 6.3 Create `RenoDXCommander.Tests/PerGamePathConstructionPropertyTests.cs` — Property 3: for any base path and game name, per-game path equals basePath + "\" + SanitizeDirectoryName(gameName)
- [x] 6.4 Create `RenoDXCommander.Tests/SanitizeDirectoryNamePropertyTests.cs` — Property 4: for any string, output contains no invalid chars and preserves all valid chars in order
- [x] 6.5 Create `RenoDXCommander.Tests/ScreenshotIniPreservationPropertyTests.cs` — Property 5: for any INI content, ApplyScreenshotPath preserves all non-SCREENSHOT sections and keys
- [x] 6.6 Create `RenoDXCommander.Tests/ScreenshotSectionIdempotencePropertyTests.cs` — Property 6: applying screenshot path twice results in exactly one [SCREENSHOT] section with the latest path
- [x] 6.7 Create `RenoDXCommander.Tests/MergeRsIniScreenshotPropertyTests.cs` — Property 7: MergeRsIni with screenshotSavePath produces [SCREENSHOT] SavePath in the merged result

## Task 7: Write Unit Tests for Edge Cases
- [x] 7.1 Add unit test: fresh `SettingsViewModel` has `PerGameScreenshotFolders == false` (Req 3.1 default)
- [x] 7.2 Add unit test: saving with empty path then loading results in empty `ScreenshotPath` and `false` toggle (Req 5.2)
- [x] 7.3 Add unit test: `SanitizeDirectoryName` with known input `Game: The "Sequel"` returns `Game The Sequel`
- [x] 7.4 Add unit test: `SanitizeDirectoryName` with all-invalid-chars input returns empty string
