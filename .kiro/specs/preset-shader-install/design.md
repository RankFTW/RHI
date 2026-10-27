# Design: Preset Shader Install

## Overview

This feature adds the ability for RHI to accept a ReShade preset `.ini` file (via drag-and-drop or the existing preset selection dialog), validate it as a genuine ReShade preset, store it for reuse, and optionally resolve and install the shader packs required by that preset. The implementation introduces three pure-logic service classes (`PresetValidator`, `TechniquesParser`, `ShaderResolver`) and integrates them into the existing `DragDropHandler` and `PresetPopupHelper` flows.

The design follows the existing partial-class pattern used by `DragDropHandler` (e.g., `DragDropHandler.Addon.cs`, `DragDropHandler.Exe.cs`) and reuses the established `DialogService`, `ShaderPackService`, and `PerGameShaderSelection` mechanisms.

## Architecture

```mermaid
flowchart TD
    subgraph Entry Points
        DD["DragDropHandler.Grid_Drop\n(.ini file dropped)"]
        PP["PresetPopupHelper\n(Deploy button clicked)"]
    end

    subgraph Pure Logic Layer
        PV["PresetValidator\n.IsReShadePreset(content)"]
        TP["TechniquesParser\n.ExtractFxFiles(techniquesValue)"]
        SR["ShaderResolver\n.Resolve(fxFiles, packFileLists)"]
    end

    subgraph Integration Layer
        DDP["DragDropHandler.Preset.cs\n.ProcessDroppedPreset(path)"]
        APS["MainViewModel\n.ApplyPresetShaders(gameName, presetPaths)"]
    end

    subgraph Existing Services
        SPS["ShaderPackService\n.SyncGameFolder()"]
        GNS["GameNameService\n.PerGameShaderSelection"]
        DS["DialogService\n(game selection, confirmation)"]
    end

    DD -->|".ini" extension| DDP
    DDP -->|validate| PV
    DDP -->|store to PresetsFolder| DDP
    DDP -->|game selection| DS
    DDP -->|"Yes" to shaders| APS

    PP -->|after DeployPresets| APS

    APS -->|parse techniques| TP
    APS -->|resolve packs| SR
    APS -->|set mode "Select"| GNS
    APS -->|deploy| SPS
```

## Components and Interfaces

### PresetValidator (static class)

Location: `RenoDXCommander/Services/PresetValidator.cs`

```csharp
public static class PresetValidator
{
    /// <summary>
    /// Returns true if the file content contains a Techniques= line
    /// with at least one TechniqueName@ShaderFile.fx entry.
    /// Case-insensitive on the "Techniques" key name.
    /// </summary>
    public static bool IsReShadePreset(string fileContent);
}
```

Rationale: A static pure function keeps validation logic testable without any I/O dependencies. The method accepts string content (not a file path) so callers handle file reading and the validator remains unit-testable.

### TechniquesParser (static class)

Location: `RenoDXCommander/Services/TechniquesParser.cs`

```csharp
public static class TechniquesParser
{
    /// <summary>
    /// Extracts the deduplicated set of .fx filenames from a Techniques= value string.
    /// Splits on commas, extracts the portion after '@', trims whitespace,
    /// skips entries without '@', and returns a case-preserving deduplicated set.
    /// </summary>
    public static HashSet<string> ExtractFxFiles(string techniquesValue);
}
```

Rationale: Separated from `PresetValidator` because shader resolution needs to call this independently (e.g., when processing multiple presets from `PresetPopupHelper`). Returns a `HashSet<string>` for natural deduplication and efficient lookup.

### ShaderResolver (static class)

Location: `RenoDXCommander/Services/ShaderResolver.cs`

```csharp
public static class ShaderResolver
{
    /// <summary>
    /// Given a set of required .fx filenames and a dictionary mapping pack IDs
    /// to their file lists, returns the pack IDs that contain at least one
    /// required .fx file, plus the set of unresolved .fx files not found in any pack.
    /// Case-insensitive matching on .fx filenames.
    /// </summary>
    public static (HashSet<string> MatchedPackIds, HashSet<string> UnresolvedFiles) Resolve(
        IEnumerable<string> requiredFxFiles,
        IReadOnlyDictionary<string, IReadOnlyList<string>> packFileLists);
}
```

Rationale: Pure function with no side effects. The caller (MainViewModel) is responsible for building the `packFileLists` dictionary from `ShaderPackService` settings and for logging unresolved files. This keeps the resolver testable and decoupled from settings storage.

### DragDropHandler.Preset.cs (partial class)

Location: `RenoDXCommander/DragDropHandler.Preset.cs`

```csharp
public partial class DragDropHandler
{
    /// <summary>
    /// Processes a dropped .ini file: validate → store → game selection → deploy → shader confirmation.
    /// </summary>
    public async Task ProcessDroppedPreset(string iniPath);
}
```

Follows the same partial-class pattern as `DragDropHandler.Addon.cs` and `DragDropHandler.Exe.cs`. The method orchestrates:
1. Read file content, call `PresetValidator.IsReShadePreset`
2. On failure: show error dialog via `ContentDialog`, return
3. Copy to `PresetPopupHelper.PresetsDir` (create directory if needed, overwrite if exists)
4. Show game selection dialog (reuse existing pattern from `ProcessDroppedAddon`)
5. Copy preset to selected game's install folder
6. Show shader confirmation dialog ("Also install the required shaders and textures?")
7. On Yes: call `ViewModel.ApplyPresetShaders(gameName, new[] { iniPath })`
8. On No/Cancel: return

### MainViewModel.ApplyPresetShaders

Location: Added to `RenoDXCommander/ViewModels/MainViewModel.Settings.cs`

```csharp
/// <summary>
/// Reads techniques from the given preset file paths, resolves required shader packs,
/// switches the game to Per_Game_Shader_Mode "Select", merges resolved packs with
/// existing selection (union), persists, and calls SyncGameFolder.
/// </summary>
public void ApplyPresetShaders(string gameName, IEnumerable<string> presetFilePaths);
```

This method:
1. For each preset file, reads the content, finds the `Techniques=` line, calls `TechniquesParser.ExtractFxFiles`
2. Unions all extracted .fx files across all presets
3. Builds the pack file lists dictionary from ShaderPackService settings
4. Calls `ShaderResolver.Resolve` to get matched pack IDs
5. If matched packs is non-empty:
   - Sets per-game shader mode to "Select" via `SetPerGameShaderMode`
   - Merges resolved pack IDs with existing `PerGameShaderSelection` (union)
   - Persists via `SaveNameMappings`
   - Calls `DeployShadersForCard` to trigger `SyncGameFolder`
6. Logs any unresolved .fx files as warnings

### Integration into PresetPopupHelper

After `DeployPresets` returns in the caller (currently in `DetailPanelBuilder` or wherever the preset popup is invoked), a new shader confirmation dialog is shown. On "Yes", `ApplyPresetShaders` is called with the paths of all deployed presets.

### Changes to DragDropHandler.cs

1. Add `.ini` to `AllowedExtensions`
2. Add `.ini` routing in `Grid_Drop` before the `.exe` handler
3. Update `Grid_DragOver` caption to include "install preset"

## Data Models

### Existing Models (no changes)

- **GameCardViewModel**: Contains `GameName`, `InstallPath`, `ShaderModeOverride` — used to identify the target game and its current shader mode.
- **ShaderPack record**: Contains `Id`, `DisplayName`, `Kind`, `Url`, etc. — pack metadata in `ShaderPackService`.
- **PerGameShaderSelection** (`Dictionary<string, List<string>>`): Stored in `GameNameService`, persisted as JSON in settings. Key = game name, Value = list of selected pack IDs.
- **ShaderPack file lists**: Stored as `ShaderPack_{id}_Files` in settings JSON. Value is a JSON array of relative file paths.

### New Data Flow

```
Preset .ini content
  → PresetValidator.IsReShadePreset(content) → bool
  → Extract "Techniques=" line value
  → TechniquesParser.ExtractFxFiles(value) → HashSet<string> of .fx filenames
  → ShaderResolver.Resolve(fxFiles, packFileLists) → (matchedPackIds, unresolvedFiles)
  → Union matchedPackIds with existing PerGameShaderSelection[gameName]
  → Persist and SyncGameFolder
```

No new data models or database schemas are introduced. All state flows through existing settings storage.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Preset validation correctness

*For any* string content, `IsReShadePreset` SHALL return `true` if and only if the content contains a line starting with `Techniques=` (case-insensitive key match) whose value includes at least one entry containing `@` followed by a `.fx` filename.

**Validates: Requirements 1.1, 1.2, 1.4**

### Property 2: Techniques extraction completeness

*For any* `Techniques=` value string containing known `.fx` filenames in `TechniqueName@File.fx` format (with arbitrary whitespace around commas and `@`), `ExtractFxFiles` SHALL return a set containing exactly those `.fx` filenames, deduplicated, with no extra entries and no missing entries.

**Validates: Requirements 7.1, 7.2, 7.5**

### Property 3: Techniques extraction order independence

*For any* valid `Techniques=` value string, shuffling the comma-separated entries SHALL produce the same `.fx` filename set as the original ordering.

**Validates: Requirements 7.6**

### Property 4: Shader resolution correctness

*For any* set of required `.fx` filenames and *for any* dictionary mapping pack IDs to file lists, `Resolve` SHALL return exactly the set of pack IDs whose file lists contain at least one of the required `.fx` filenames (case-insensitive match), and the unresolved set SHALL contain exactly those `.fx` filenames not found in any pack's file list.

**Validates: Requirements 5.2, 5.3, 5.5**

### Property 5: Selection merge is union

*For any* existing set of per-game shader pack IDs and *for any* resolved set of shader pack IDs, the merged result SHALL equal the set union of both input sets.

**Validates: Requirements 6.2**

## Error Handling

| Scenario | Handling |
|---|---|
| `.ini` file fails `IsReShadePreset` validation | Show `ContentDialog` error: "This file is not a recognised ReShade preset." Abort flow. |
| File read error (I/O exception reading .ini) | Log via `_crashReporter`, show error dialog, abort flow. |
| `PresetsDir` does not exist | `Directory.CreateDirectory` before copy (idempotent). |
| File copy to PresetsDir fails | Log error, show dialog, abort flow. |
| User cancels game selection dialog | Abort — preset remains in PresetsDir but is not deployed to any game. |
| `.fx` file not found in any shader pack | Log warning per unresolved file. Resolved packs are still deployed. |
| Shader resolution returns empty set | No shader mode change. Log info message. Flow completes normally. |
| `SyncGameFolder` throws | Caught by existing `DeployShadersForCard` error handling (logged via `_crashReporter`). |

## Testing Strategy

### Property-Based Tests (FsCheck.Xunit)

Each property test uses FsCheck with a minimum of 100 iterations. The project already uses `FsCheck.Xunit` (v2.16.6) with xUnit.

| Test Class | Property | Min Iterations |
|---|---|---|
| `PresetValidatorPropertyTests` | Property 1: Preset validation correctness | 100 |
| `TechniquesParserExtractionPropertyTests` | Property 2: Techniques extraction completeness | 100 |
| `TechniquesParserOrderPropertyTests` | Property 3: Techniques extraction order independence | 100 |
| `ShaderResolverPropertyTests` | Property 4: Shader resolution correctness | 100 |
| `SelectionMergePropertyTests` | Property 5: Selection merge is union | 100 |

Tag format: `Feature: preset-shader-install, Property {N}: {title}`

### Unit Tests (xUnit)

| Test | Coverage |
|---|---|
| `DragDropHandler.AllowedExtensions` contains `.ini` | Req 8.1 |
| `PresetValidator` edge cases: empty string, no Techniques line, Techniques with no @.fx | Req 1.3 |
| `TechniquesParser` edge cases: empty input, single entry without @, whitespace-only | Req 7.3, 7.4 |
| `ShaderResolver` edge cases: empty pack lists, no matches, unresolved file reporting | Req 5.4 |

### Integration Testing

The drag-and-drop flow and dialog interactions (game selection, shader confirmation) are validated through manual testing, as they involve WinUI 3 `ContentDialog` and file system operations that require a running application context.
