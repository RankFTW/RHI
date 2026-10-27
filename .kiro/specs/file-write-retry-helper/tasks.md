# Implementation Plan: File Write Retry Helper

## Overview

Extract the duplicated file-write retry pattern from 6 call sites into a shared `FileHelper.WriteAllTextWithRetry` static method, then replace each inline retry loop with a single call to the helper. The implementation proceeds incrementally: create the helper, wire up each call site one at a time, and validate with tests.

## Tasks

- [x] 1. Create the FileHelper utility class
  - [x] 1.1 Create `RenoDXCommander/Services/FileHelper.cs` with the `WriteAllTextWithRetry` static method
    - Static class in `RenoDXCommander.Services` namespace
    - Method signature: `public static void WriteAllTextWithRetry(string path, string content, string callerTag)`
    - Retry loop: up to 3 attempts, `catch (IOException) when (attempt < 2)` with `Thread.Sleep(50 * (attempt + 1))`
    - Catch general `Exception` on any attempt: log via `CrashReporter.Log` using `callerTag` and `ex.Message`, then return
    - Never throw an unhandled exception
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 4.1, 4.2, 4.3_

  - [x] 1.2 Write property test: Write round trip (Property 1)
    - **Property 1: Write round trip**
    - For any valid temp file path and any non-null string content, `WriteAllTextWithRetry` followed by `File.ReadAllText` returns identical content
    - Use FsCheck with xUnit, minimum 100 iterations
    - **Validates: Requirements 1.2**

  - [x] 1.3 Write property test: IOException retry with bounded attempts (Property 2)
    - **Property 2: IOException retry with bounded attempts**
    - For any number of transient IOException failures (0–3), verify the method attempts exactly `min(n+1, 3)` writes, never throws, and succeeds when an attempt succeeds
    - Use a testable delegate/wrapper to simulate IOException on configurable attempts
    - **Validates: Requirements 1.3, 1.4, 1.6, 3.1, 3.2, 3.3**

  - [x] 1.4 Write property test: Non-IOException halts and logs (Property 3)
    - **Property 3: Non-IOException halts and logs**
    - For any attempt number and any non-IOException exception type, verify the method stops retrying immediately, logs via CrashReporter with the caller tag and exception message, and does not throw
    - **Validates: Requirements 1.5, 3.4**

- [x] 2. Replace retry pattern in ModInstallService.SaveDb
  - [x] 2.1 Replace the inline retry loop in `ModInstallService.SaveDb` with `FileHelper.WriteAllTextWithRetry(DbPath, json, "ModInstallService.SaveDb")`
    - Keep the existing `Directory.CreateDirectory` call before the helper call
    - Remove the `for` loop, `try/catch (IOException)`, and `Thread.Sleep` block
    - _Requirements: 2.1, 3.5_

- [x] 3. Replace retry pattern in GameLibraryService.Save
  - [x] 3.1 Replace the inline retry loop in `GameLibraryService.Save` with `FileHelper.WriteAllTextWithRetry(LibraryPath, json, "GameLibraryService.Save")`
    - Keep the existing `Directory.CreateDirectory` call before the helper call
    - Remove the `for` loop, `try/catch (IOException)`, and `Thread.Sleep` block
    - _Requirements: 2.2, 3.5_

- [x] 4. Replace retry pattern in AuxInstallService.SaveDb
  - [x] 4.1 Replace the inline retry loop in `AuxInstallService.SaveDb` with `FileHelper.WriteAllTextWithRetry(DbPath, json, "AuxInstallService.SaveDb")`
    - Keep the existing `Directory.CreateDirectory` call before the helper call
    - Remove the `for` loop, `try/catch (IOException)`, and `Thread.Sleep` block
    - _Requirements: 2.4, 3.5_

- [x] 5. Checkpoint - Verify silent-variant call sites
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Replace retry pattern in SettingsViewModel.SaveSettingsFile
  - [x] 6.1 Replace the inline retry loop in `SettingsViewModel.SaveSettingsFile` with `FileHelper.WriteAllTextWithRetry(_settingsFilePath, json, "SettingsViewModel.SaveSettingsFile")`
    - Keep the existing `Directory.CreateDirectory` call before the helper call
    - Remove the `for` loop, `try/catch (IOException)`, `catch (Exception)`, and `Thread.Sleep` block
    - _Requirements: 2.6, 3.4, 3.5_

- [x] 7. Replace retry pattern in LumaService.SaveAllRecords
  - [x] 7.1 Replace the inline retry loop in `LumaService.SaveAllRecords` with `FileHelper.WriteAllTextWithRetry(DbPath, json, "LumaService.SaveAllRecords")`
    - Keep the existing `Directory.CreateDirectory` call before the helper call
    - Remove the inner `for` loop, `try/catch (IOException)`, and `Thread.Sleep` block
    - The outer `try/catch (Exception)` that wraps the entire method can also be removed since `WriteAllTextWithRetry` already handles general exceptions with logging
    - _Requirements: 2.5, 3.4, 3.5_

- [x] 8. Replace retry pattern in GameNameService.SaveNameMappings
  - [x] 8.1 Verify that the outer retry loop in `GameNameService.SaveNameMappings` (which retries the full read-modify-write cycle) remains intact
    - The outer loop retries `IOException` on the entire load-serialize-save sequence — this is a different concern from the write retry
    - Only the inner write call (`SettingsViewModel.SaveSettingsFile`) is already refactored (task 6)
    - No additional changes needed in this method since it delegates to `SaveSettingsFile` which now uses `FileHelper`
    - _Requirements: 2.3, 3.5_

- [x] 9. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each call site preserves its existing `Directory.CreateDirectory` call before invoking the helper
- The "silent" variant sites (ModInstallService, GameLibraryService, AuxInstallService) had no `catch (Exception)` — the helper's general exception logging is additive but harmless
- The "logging" variant sites (SettingsViewModel, LumaService, GameNameService) already logged via CrashReporter — the helper preserves this behavior via the `callerTag` parameter
- GameNameService.SaveNameMappings delegates its write to `SettingsViewModel.SaveSettingsFile`, so it is indirectly refactored by task 6
