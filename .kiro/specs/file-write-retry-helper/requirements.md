# Requirements Document

## Introduction

The RenoDXCommander codebase contains a duplicated file-write retry pattern copy-pasted across at least six files (ModInstallService, GameLibraryService, GameNameService, AuxInstallService, LumaService, SettingsViewModel). Each instance retries `File.WriteAllText` up to 3 times with progressive delays on `IOException`, then logs via `CrashReporter` on final failure. This spec extracts that pattern into a shared `FileHelper` utility class and replaces all call sites, eliminating duplication without changing runtime behavior.

## Glossary

- **FileHelper**: A new static utility class in the `RenoDXCommander.Services` namespace that provides reusable file-write operations with built-in retry logic.
- **Retry_Loop**: The pattern of attempting a file write up to a fixed number of times, sleeping between attempts on transient `IOException`, and logging on non-recoverable failure.
- **CrashReporter**: The existing static logging class (`CrashReporter.Log`) used throughout the application for diagnostic output.
- **Call_Site**: A location in the codebase where the duplicated retry pattern currently exists and will be replaced by a call to `FileHelper`.

## Requirements

### Requirement 1: Shared Retry Write Method

**User Story:** As a developer, I want a single reusable method for writing text files with retry logic, so that I do not have to maintain the same retry pattern in six or more places.

#### Acceptance Criteria

1. THE FileHelper SHALL expose a static method `WriteAllTextWithRetry` that accepts a file path (string), content (string), and a caller-context tag (string) for log messages.
2. WHEN `WriteAllTextWithRetry` is called, THE FileHelper SHALL attempt to write the content to the specified file path using `File.WriteAllText`.
3. WHEN an `IOException` occurs on an attempt that is not the final attempt, THE FileHelper SHALL wait using `Thread.Sleep` with a progressive delay of `50 * (attempt + 1)` milliseconds before retrying.
4. THE FileHelper SHALL attempt the write operation a maximum of 3 times before giving up.
5. IF a non-`IOException` exception occurs on any attempt, THEN THE FileHelper SHALL log the failure via `CrashReporter.Log` using the caller-context tag and the exception message, and stop retrying.
6. IF all 3 attempts fail with `IOException`, THEN THE FileHelper SHALL stop without throwing an unhandled exception.

### Requirement 2: Replace Existing Duplicated Patterns

**User Story:** As a developer, I want all existing copy-pasted retry loops replaced with calls to the shared helper, so that future changes to retry behavior only need to happen in one place.

#### Acceptance Criteria

1. THE ModInstallService `SaveDb` method SHALL use `FileHelper.WriteAllTextWithRetry` instead of an inline retry loop.
2. THE GameLibraryService `SaveLibrary` method SHALL use `FileHelper.WriteAllTextWithRetry` instead of an inline retry loop.
3. THE GameNameService `SaveNameMappings` method SHALL use `FileHelper.WriteAllTextWithRetry` instead of an inline retry loop.
4. THE AuxInstallService `SaveDb` method SHALL use `FileHelper.WriteAllTextWithRetry` instead of an inline retry loop.
5. THE LumaService `SaveAllRecords` method SHALL use `FileHelper.WriteAllTextWithRetry` instead of an inline retry loop.
6. THE SettingsViewModel `SaveSettingsFile` method SHALL use `FileHelper.WriteAllTextWithRetry` instead of an inline retry loop.

### Requirement 3: Behavioral Equivalence

**User Story:** As a user, I want the refactored code to behave identically to the current implementation, so that no existing functionality is broken.

#### Acceptance Criteria

1. THE FileHelper SHALL use the same maximum attempt count (3) as the existing inline retry loops.
2. THE FileHelper SHALL use the same progressive delay formula (`50 * (attempt + 1)` milliseconds) as the existing inline retry loops.
3. THE FileHelper SHALL catch `IOException` on non-final attempts and retry, matching the existing `catch (IOException) when (attempt < 2)` guard.
4. THE FileHelper SHALL catch general `Exception` on any attempt and log via `CrashReporter.Log`, matching the existing error-handling behavior in SettingsViewModel and GameNameService.
5. WHEN a Call_Site previously performed `Directory.CreateDirectory` before the retry loop, THE refactored Call_Site SHALL continue to perform `Directory.CreateDirectory` before calling `FileHelper.WriteAllTextWithRetry`.

### Requirement 4: Code Placement and Conventions

**User Story:** As a developer, I want the helper class to follow the project's existing code organization conventions, so that it is easy to discover and maintain.

#### Acceptance Criteria

1. THE FileHelper SHALL be a static class in the `RenoDXCommander.Services` namespace.
2. THE FileHelper SHALL reside in a file named `FileHelper.cs` inside the `RenoDXCommander/Services/` directory.
3. THE FileHelper SHALL contain only file-related utility methods and no unrelated logic.
