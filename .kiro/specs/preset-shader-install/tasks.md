# Tasks: Preset Shader Install

## Task 1: Core Pure-Logic Classes

- [x] 1.1 Create `PresetValidator` static class in `RenoDXCommander/Services/PresetValidator.cs` with `IsReShadePreset(string fileContent)` method — case-insensitive Techniques= line detection with @.fx entry validation (Req 1.1–1.4)
- [x] 1.2 Create `TechniquesParser` static class in `RenoDXCommander/Services/TechniquesParser.cs` with `ExtractFxFiles(string techniquesValue)` method — comma splitting, @ extraction, whitespace trimming, deduplication (Req 7.1–7.6)
- [x] 1.3 Create `ShaderResolver` static class in `RenoDXCommander/Services/ShaderResolver.cs` with `Resolve(IEnumerable<string> requiredFxFiles, IReadOnlyDictionary<string, IReadOnlyList<string>> packFileLists)` method — case-insensitive .fx lookup across pack file lists, returns matching pack IDs and unresolved files (Req 5.1–5.5)

## Task 2: Property-Based Tests

- [x] 2.1 Create `PresetValidatorPropertyTests.cs` — Property 1: preset validation correctness. Generate random INI content with/without valid Techniques= lines, verify IsReShadePreset returns true iff valid @.fx entries exist. Min 100 iterations. (Req 1.1–1.4)
  - [x] ~PBT~ 2.1.1 Property test passes
- [x] 2.2 Create `TechniquesParserExtractionPropertyTests.cs` — Property 2: techniques extraction completeness. Generate random Techniques= values with known .fx filenames, verify ExtractFxFiles returns exactly the expected deduplicated set. Min 100 iterations. (Req 7.1–7.5)
  - [x] ~PBT~ 2.2.1 Property test passes ✅ passed
- [x] 2.3 Create `TechniquesParserOrderPropertyTests.cs` — Property 3: techniques extraction order independence. Generate Techniques= values, shuffle entries, verify both orderings produce the same .fx set. Min 100 iterations. (Req 7.6)
  - [x] ~PBT~ 2.3.1 Property test passes ✅ passed
- [x] 2.4 Create `ShaderResolverPropertyTests.cs` — Property 4: shader resolution correctness. Generate random pack file lists and required .fx files, verify Resolve returns exactly the packs containing at least one match. Min 100 iterations. (Req 5.2, 5.3, 5.5)
  - [x] ~PBT~ 2.4.1 Property test passes ✅ passed
- [x] 2.5 Create `SelectionMergePropertyTests.cs` — Property 5: selection merge is union. Generate random existing and resolved pack ID sets, verify merge produces set union. Min 100 iterations. (Req 6.2)
  - [x] ~PBT~ 2.5.1 Property test passes ✅ passed

## Task 3: DragDropHandler .ini Integration

- [x] 3.1 Add `.ini` to `DragDropHandler.AllowedExtensions` set (Req 8.1)
- [x] 3.2 Add `.ini` routing in `Grid_Drop` method to call `ProcessDroppedPreset` (Req 8.2)
- [x] 3.3 Update `Grid_DragOver` caption to include "install preset" (Req 8.2)
- [x] 3.4 Create `DragDropHandler.Preset.cs` partial class with `ProcessDroppedPreset(string iniPath)` method — validate, copy to presets folder, show game selection dialog, copy to game folder, show shader confirmation dialog (Req 2.1–2.4, 3.1–3.8)

## Task 4: PresetPopupHelper Shader Confirmation

- [x] 4.1 Add shader confirmation dialog after `DeployPresets` in the caller — "Also install the required shaders and textures?" with Yes/No (Req 4.2)
- [x] 4.2 On Yes, collect techniques from all deployed presets, call shared shader resolution and deployment logic (Req 4.3)

## Task 5: Shader Resolution and Per-Game Mode Integration

- [x] 5.1 Add helper method to build pack file lists dictionary from ShaderPackService settings for ShaderResolver input (Req 5.2)
- [x] 5.2 Add `ApplyPresetShaders` method to MainViewModel — calls ShaderResolver, sets per-game mode to "Select", merges resolved packs with existing selection (union), persists, calls SyncGameFolder (Req 6.1–6.4)

## Task 6: Unit Tests

- [x] 6.1 Add unit test verifying `DragDropHandler.AllowedExtensions` contains `.ini` (Req 8.1)
- [x] 6.2 Add unit tests for `PresetValidator` edge cases — empty string, no Techniques line, Techniques with no @.fx (Req 1.3)
- [x] 6.3 Add unit tests for `TechniquesParser` edge cases — empty input, single entry without @, whitespace-only (Req 7.3, 7.4)
- [x] 6.4 Add unit tests for `ShaderResolver` — empty pack lists, no matches, unresolved file logging (Req 5.4)
