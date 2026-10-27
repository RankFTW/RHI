# Requirements Document

## Introduction

RenoDXCommander uses two services — `CrashReporter` and `AuxInstallService` — that expose significant functionality through static methods and fields. These static call sites prevent constructor injection, make unit testing difficult (no way to substitute fakes), and couple consumers directly to concrete implementations. This feature wraps the static surface area of both services behind injectable interfaces and registers them in the existing DI container, enabling cleaner dependency injection and better testability across the codebase.

## Glossary

- **CrashReporter**: A static class that provides breadcrumb logging (`Log`), crash report writing (`WriteCrashReport`), global error handler registration (`Register`), and a `VerboseLogging` toggle. Currently consumed via `CrashReporter.Log(...)` throughout the codebase.
- **AuxInstallService**: A service that manages Display Commander and ReShade installation. It already implements `IAuxInstallService` for its instance methods, but also exposes ~15 public static methods (e.g. `IsReShadeFileStrict`, `MergeRsIni`, `BackupForeignDll`) and ~20 public static/const fields (e.g. `RsStagingDir`, `TypeReShade`, `InisDir`) that are called directly.
- **DI_Container**: The `Microsoft.Extensions.DependencyInjection.ServiceCollection` configured in `App.xaml.cs` and exposed via `App.Services`.
- **ICrashReporter**: The new interface to be created for the `CrashReporter` logging surface.
- **IAuxFileService**: The new interface to be created for the static file-identification and INI-management methods on `AuxInstallService`.
- **Consumer**: Any class that calls static methods on `CrashReporter` or `AuxInstallService`.

## Requirements

### Requirement 1: Define ICrashReporter Interface

**User Story:** As a developer, I want a `CrashReporter` interface so that I can inject a fake logger in tests and decouple consumers from the static class.

#### Acceptance Criteria

1. THE ICrashReporter interface SHALL declare a `Log(string message)` method matching the signature of `CrashReporter.Log`.
2. THE ICrashReporter interface SHALL declare a `WriteCrashReport(string source, Exception? ex, string? extraContext)` method matching the signature of `CrashReporter.WriteCrashReport`.
3. THE ICrashReporter interface SHALL declare a `VerboseLogging` property with get and set accessors matching the `CrashReporter.VerboseLogging` static property.
4. THE ICrashReporter interface SHALL NOT include the `Register(Application app)` method, because global error handler registration is a one-time app-startup concern that must remain static.

### Requirement 2: Implement CrashReporter Wrapper

**User Story:** As a developer, I want a concrete implementation of `ICrashReporter` that delegates to the existing static `CrashReporter` methods, so that existing behaviour is preserved while enabling DI.

#### Acceptance Criteria

1. THE CrashReporterService class SHALL implement ICrashReporter.
2. WHEN `Log` is called on CrashReporterService, THE CrashReporterService SHALL delegate to `CrashReporter.Log`.
3. WHEN `WriteCrashReport` is called on CrashReporterService, THE CrashReporterService SHALL delegate to `CrashReporter.WriteCrashReport`.
4. WHEN the `VerboseLogging` property is read or written on CrashReporterService, THE CrashReporterService SHALL delegate to `CrashReporter.VerboseLogging`.

### Requirement 3: Define IAuxFileService Interface for Static Methods

**User Story:** As a developer, I want an interface for the static file-identification and INI-management methods on `AuxInstallService`, so that consumers can receive these capabilities through DI instead of static calls.

#### Acceptance Criteria

1. THE IAuxFileService interface SHALL declare methods for: `SyncReShadeToDisplayCommander`, `EnsureReShadeStaging`, `IdentifyDxgiFile`, `IdentifyWinmmFile`, `BackupForeignDll`, `RestoreForeignDll`, `IsReShadeFileStrict`, `IsDcFileStrict`, `IsReShadeFile`, `EnsureInisDir`, `MergeRsIni`, `MergeRsVulkanIni`, `CopyRsIni`, `CopyRsPresetIniIfPresent`, `CopyDcIni`, `ReadInstalledVersion`, and `CheckReShadeUpdateLocal`.
2. THE IAuxFileService interface SHALL preserve the exact parameter signatures and return types of the corresponding static methods on AuxInstallService.
3. THE IAuxFileService interface SHALL NOT include the instance methods already covered by the existing IAuxInstallService interface (e.g. `InstallDcAsync`, `InstallReShadeAsync`, `Uninstall`).

### Requirement 4: Implement IAuxFileService on AuxInstallService

**User Story:** As a developer, I want `AuxInstallService` to implement `IAuxFileService` with instance methods that delegate to the existing static methods, so that the static methods can be consumed through DI.

#### Acceptance Criteria

1. THE AuxInstallService class SHALL implement IAuxFileService in addition to the existing IAuxInstallService interface.
2. WHEN any IAuxFileService method is called on AuxInstallService, THE AuxInstallService SHALL delegate to the corresponding existing static method, preserving identical behaviour.
3. THE existing static methods on AuxInstallService SHALL remain available during the transition period so that call sites can be migrated incrementally.

### Requirement 5: Expose Static Constants via IAuxFileService or a Shared Constants Class

**User Story:** As a developer, I want the static constants and path fields on `AuxInstallService` (e.g. `TypeReShade`, `TypeDc`, `InisDir`, `RsStagingDir`, `DownloadCacheDir`) to remain accessible without requiring an injected instance, so that test setup code and non-DI contexts can still reference them.

#### Acceptance Criteria

1. THE static constants (`TypeDc`, `TypeReShade`, `DcUrl`, `DcCacheFile`, `RsNormalName`, `RsDcModeName`, and all other `public const` fields) SHALL remain as public constants on AuxInstallService.
2. THE static readonly path fields (`RsStagingDir`, `InisDir`, `DownloadCacheDir`, `DcReShadeFolderPath`) and computed path properties (`RsStagedPath64`, `RsStagedPath32`, `RsIniPath`, `RsVulkanIniPath`, `RsPresetIniPath`, `DcIniPath`) SHALL remain as public static members on AuxInstallService.
3. THE IAuxFileService interface SHALL NOT require these constants or path fields as instance members, because they are environment-derived values that do not vary between implementations.

### Requirement 6: Register New Interfaces in DI Container

**User Story:** As a developer, I want the new interfaces registered in the DI container so that any class resolved through DI can receive them via constructor injection.

#### Acceptance Criteria

1. WHEN the application starts, THE DI_Container SHALL register ICrashReporter as a singleton with CrashReporterService as the implementation.
2. WHEN the application starts, THE DI_Container SHALL register IAuxFileService as a singleton with the existing AuxInstallService instance as the implementation.
3. THE DI_Container SHALL use the same AuxInstallService singleton instance for both IAuxInstallService and IAuxFileService registrations.

### Requirement 7: Migrate Consumer Call Sites

**User Story:** As a developer, I want all static `CrashReporter.Log` and `CrashReporter.WriteCrashReport` call sites in DI-resolved classes to use the injected `ICrashReporter` instead, so that those classes become testable.

#### Acceptance Criteria

1. WHEN a class is resolved through the DI_Container, THE Consumer SHALL receive ICrashReporter via constructor injection instead of calling CrashReporter static methods directly.
2. WHEN a class is resolved through the DI_Container and calls static methods on AuxInstallService that are covered by IAuxFileService, THE Consumer SHALL receive IAuxFileService via constructor injection instead of calling the static methods directly.
3. THE `App.xaml.cs` class SHALL continue to call `CrashReporter.Register(this)` and `CrashReporter.Log(...)` statically, because `App` is the composition root and is not itself resolved through DI.
4. IF a Consumer is not resolved through the DI_Container (e.g. static helper classes, test setup code), THEN THE Consumer SHALL continue to use the static methods directly until that Consumer is refactored into the DI graph.

### Requirement 8: Preserve Existing Behaviour

**User Story:** As a developer, I want the refactoring to be purely structural with zero changes to runtime behaviour, so that no regressions are introduced.

#### Acceptance Criteria

1. THE application SHALL produce identical breadcrumb logs, session logs, and crash reports before and after the refactoring.
2. THE application SHALL perform identical file operations (ReShade staging, INI merging, DLL identification, backup/restore) before and after the refactoring.
3. IF any existing unit test references `CrashReporter` or `AuxInstallService` static members directly, THEN THE test SHALL continue to compile and pass without modification, because the static members remain available.
