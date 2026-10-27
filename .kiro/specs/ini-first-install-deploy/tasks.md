# Implementation Plan: INI First-Install Deploy

## Overview

Add two static methods (`DeployUlIniIfAbsent`, `DeployDcIniIfAbsent`) to `AuxInstallService.Ini.cs` that copy the user's INI template from AppData to the game's deploy path only when the INI is not already present. Wire them into the existing `InstallUlAsync` and `InstallDcAsync` flows in `MainViewModel.Install.cs`. All failures are logged and swallowed so the addon install is never blocked.

## Tasks

- [x] 1. Add DeployUlIniIfAbsent and DeployDcIniIfAbsent methods
  - [x] 1.1 Implement `DeployUlIniIfAbsent(string gameInstallPath)` in `AuxInstallService.Ini.cs`
    - Static, void, never throws
    - Resolve deploy path via `ModInstallService.GetAddonDeployPath(gameInstallPath)`
    - If `relimiter.ini` already exists at deploy path, return (no-op)
    - If `UlIniPath` source does not exist, log via `CrashReporter.Log` and return
    - Otherwise `File.Copy(UlIniPath, deployPath/relimiter.ini)` inside try/catch; log success or failure
    - _Requirements: 1.1, 1.2, 1.3, 4.1, 4.2_

  - [x] 1.2 Implement `DeployDcIniIfAbsent(string gameInstallPath)` in `AuxInstallService.Ini.cs`
    - Same contract as `DeployUlIniIfAbsent` but for `DisplayCommander.ini` / `DcIniPath`
    - _Requirements: 2.1, 2.2, 2.3, 4.1, 4.2_

  - [x] 1.3 Write property test: Deploy copies INI when destination is absent
    - **Property 1: Deploy copies INI when destination is absent**
    - Create `IniFirstInstallDeployCopyPropertyTests.cs` in `RenoDXCommander.Tests`
    - Use FsCheck.Xunit with `MaxTest = 100`
    - Generate random INI content (byte arrays), use temp directories for source and deploy paths
    - Verify file appears at deploy path with content identical to source
    - **Validates: Requirements 1.1, 2.1**

  - [x] 1.4 Write property test: Deploy preserves existing INI
    - **Property 2: Deploy preserves existing INI**
    - Create `IniFirstInstallDeployPreservePropertyTests.cs` in `RenoDXCommander.Tests`
    - Use FsCheck.Xunit with `MaxTest = 100`
    - Generate random existing INI content + random source INI content in temp dirs
    - Verify existing file is byte-for-byte unchanged after deploy call
    - **Validates: Requirements 1.2, 2.2, 3.1, 3.2**

  - [x] 1.5 Write property test: Deploy is a no-op when source INI is missing
    - **Property 3: Deploy is a no-op when source INI is missing**
    - Create `IniFirstInstallDeployNoSourcePropertyTests.cs` in `RenoDXCommander.Tests`
    - Use FsCheck.Xunit with `MaxTest = 100`
    - Use temp deploy paths with no source INI file present
    - Verify no file created at deploy path and no exception thrown
    - **Validates: Requirements 1.3, 2.3**

- [x] 2. Checkpoint - Verify new methods compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Wire deploy methods into install flows
  - [x] 3.1 Call `DeployUlIniIfAbsent` from `InstallUlAsync` in `MainViewModel.Install.cs`
    - Insert call after the addon DLL `File.Copy` succeeds and before the UI success dispatch
    - Pass `card.InstallPath` as the argument
    - _Requirements: 1.1, 1.2, 1.3, 4.1, 4.2_

  - [x] 3.2 Call `DeployDcIniIfAbsent` from `InstallDcAsync` in `MainViewModel.Install.cs`
    - Insert call after the addon DLL `File.Copy` succeeds and before the `AuxInstalledRecord` is saved
    - Pass `card.InstallPath` as the argument
    - _Requirements: 2.1, 2.2, 2.3, 4.1, 4.2_

  - [x] 3.3 Write unit tests for error handling in install flows
    - Verify `InstallUlAsync` / `InstallDcAsync` still succeed when INI deploy encounters an I/O error
    - Use temp directories to simulate read-only destinations or missing paths
    - _Requirements: 4.1, 4.2_

- [x] 4. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests validate the three correctness properties from the design document
- The new methods follow the same static pattern as existing `CopyUlIni` / `CopyDcIni`
