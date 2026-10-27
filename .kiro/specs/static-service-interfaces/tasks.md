# Tasks: Static Service Interfaces

## Task 1: Create ICrashReporter interface and CrashReporterService wrapper

- [x] 1.1 Create `RenoDXCommander/Services/ICrashReporter.cs` with `Log`, `WriteCrashReport`, and `VerboseLogging` members matching the static `CrashReporter` signatures
- [x] 1.2 Create `RenoDXCommander/Services/CrashReporterService.cs` implementing `ICrashReporter`, delegating each member to the corresponding `CrashReporter` static method

## Task 2: Create IAuxFileService interface and implement on AuxInstallService

- [x] 2.1 Create `RenoDXCommander/Services/IAuxFileService.cs` declaring instance methods for all 17 static methods listed in Requirement 3
- [x] 2.2 Add `: IAuxFileService` to `AuxInstallService` class declaration and add explicit interface implementations that delegate to the existing static methods

## Task 3: Register new interfaces in DI container

- [x] 3.1 In `App.xaml.cs`, register `ICrashReporter` as singleton with `CrashReporterService`
- [x] 3.2 In `App.xaml.cs`, register `IAuxFileService` reusing the same `AuxInstallService` singleton registered for `IAuxInstallService`

## Task 4: Migrate consumer call sites — ICrashReporter

- [x] 4.1 Add `ICrashReporter` constructor parameter to `MainViewModel` and replace `CrashReporter.Log` / `CrashReporter.WriteCrashReport` static calls with injected instance calls
- [x] 4.2 Add `ICrashReporter` constructor parameter to `UpdateOrchestrationService` and replace static `CrashReporter` calls
- [x] 4.3 Add `ICrashReporter` constructor parameter to `MainWindow` and replace static `CrashReporter` calls
- [x] 4.4 Add `ICrashReporter` constructor parameter to `DragDropHandler` and replace static `CrashReporter` calls
- [x] 4.5 Add `ICrashReporter` constructor parameter to `WindowStateManager` and replace static `CrashReporter` calls
- [x] 4.6 Add `ICrashReporter` constructor parameter to `OverridesFlyoutBuilder` and replace static `CrashReporter` calls

## Task 5: Migrate consumer call sites — IAuxFileService

- [x] 5.1 Add `IAuxFileService` constructor parameter to `UpdateOrchestrationService` and replace `AuxInstallService.IdentifyDxgiFile`, `ReadInstalledVersion`, `CheckReShadeUpdateLocal` static calls
- [x] 5.2 Add `IAuxFileService` constructor parameter to `LumaService` and replace `AuxInstallService.EnsureInisDir`, `MergeRsIni` static calls

## Task 6: Write property-based tests

- [x] 6.1 Create `RenoDXCommander.Tests/CrashReporterServiceDelegationPropertyTests.cs` — Property 1: Log delegation preserves breadcrumbs
- [x] 6.2 Create `RenoDXCommander.Tests/CrashReporterServiceVerboseRoundTripPropertyTests.cs` — Property 2: VerboseLogging round-trip
- [x] 6.3 Create `RenoDXCommander.Tests/AuxFileServiceDelegationPropertyTests.cs` — Property 3: IAuxFileService delegation equivalence

## Task 7: Write unit tests for structural and DI checks

- [x] 7.1 Create `RenoDXCommander.Tests/StaticServiceInterfaceStructureTests.cs` — reflection tests for interface exclusions (Req 1.4, 3.3, 5.3) and DI registration checks (Req 6.1, 6.2, 6.3)
