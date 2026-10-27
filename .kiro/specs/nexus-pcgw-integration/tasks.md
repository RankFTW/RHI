# Implementation Plan: Nexus Mods & PCGamingWiki Integration

## Overview

Incrementally add Nexus Mods and PCGamingWiki link resolution to RHI. Models are extended first, then services are built and tested, followed by integration into the card-building flow and UI changes to the detail panel action row.

## Tasks

- [x] 1. Model changes
  - [x] 1.1 Add `SteamAppId` to `DetectedGame`
    - Add `public int? SteamAppId { get; set; }` to `RenoDXCommander/Models/DetectedGame.cs`
    - _Requirements: 4.4_

  - [x] 1.2 Add manifest override fields to `RemoteManifest`
    - Add `nexusUrlOverrides` (`Dictionary<string, string>?`), `steamAppIdOverrides` (`Dictionary<string, int>?`), and `pcgwUrlOverrides` (`Dictionary<string, string>?`) with `[JsonPropertyName]` attributes to `RenoDXCommander/Models/RemoteManifest.cs`
    - _Requirements: 3.1, 5a.1, 7a.1_

  - [x] 1.3 Add `NexusModsUrl` and `PcgwUrl` to `GameCardViewModel`
    - Add `[ObservableProperty] private string? _nexusModsUrl;` and `[ObservableProperty] private string? _pcgwUrl;` to `GameCardViewModel.cs`
    - Add computed `HasNexusModsUrl` and `HasPcgwUrl` properties to `GameCardViewModel.UI.cs`
    - Register both in `NotifyAll()`
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [x] 1.4 Write property test for `HasNexusModsUrl` / `HasPcgwUrl` computed properties
    - **Property 8: HasNexusModsUrl and HasPcgwUrl computed properties**
    - Create `RenoDXCommander.Tests/LinkVisibilityPropertyTests.cs`
    - For any nullable string assigned to `NexusModsUrl` (or `PcgwUrl`), assert `HasNexusModsUrl` (or `HasPcgwUrl`) equals `!string.IsNullOrEmpty(value)`
    - **Validates: Requirements 8.3, 8.4**

  - [x] 1.5 Write property test for manifest override round-trip serialization
    - **Property 9: Manifest new fields serialization round-trip**
    - Create `RenoDXCommander.Tests/ManifestOverrideRoundTripPropertyTests.cs`
    - For any `RemoteManifest` with arbitrary `nexusUrlOverrides`, `steamAppIdOverrides`, `pcgwUrlOverrides`, assert JSON serialize → deserialize produces equivalent dictionaries
    - **Validates: Requirements 3.1, 5a.1, 7a.1**

  - [x] 1.6 Add `NexusModsGame` deserialization model
    - Create `RenoDXCommander/Models/NexusModsGame.cs` with `Name` and `NexusmodsUrl` properties using `[JsonPropertyName]`
    - _Requirements: 1.6_

  - [x] 1.7 Add Steam Store search response deserialization models
    - Create `RenoDXCommander/Models/SteamStoreSearchResponse.cs` with `SteamStoreSearchResponse` and `SteamStoreSearchItem` classes
    - _Requirements: 5.4, 5.5_

- [x] 2. Checkpoint — Verify model changes compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement `NexusModsService`
  - [x] 3.1 Create `INexusModsService` interface and `NexusModsService` implementation
    - Create `RenoDXCommander/Services/INexusModsService.cs` with `InitAsync()` and `ResolveUrl()` methods
    - Create `RenoDXCommander/Services/NexusModsService.cs` implementing fetch, 24h cache TTL, disk cache fallback, normalized-name dictionary build, and URL resolution with manifest override priority
    - Use `HttpClient` injection, cache at `%LocalAppData%/RHI/nexus_games.json`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 2.1, 2.2, 2.3, 2.4, 2.5, 3.2, 3.3_

  - [x] 3.2 Write property test for Nexus dictionary round-trip
    - **Property 1: Nexus dictionary round-trip**
    - Create `RenoDXCommander.Tests/NexusDictionaryRoundTripPropertyTests.cs`
    - For any list of `NexusModsGame` entries with distinct normalized names, building the dictionary and querying with `NormalizeName(entry.Name)` returns the corresponding URL
    - **Validates: Requirements 1.6, 1.7, 2.1**

  - [x] 3.3 Write property test for Nexus URL resolution priority
    - **Property 2: Nexus URL resolution priority**
    - Create `RenoDXCommander.Tests/NexusUrlResolutionPriorityPropertyTests.cs`
    - For any game name, manifest overrides, and lookup dictionary: override wins, then dictionary, then null
    - **Validates: Requirements 2.3, 2.4, 2.5, 3.3**

  - [x] 3.4 Write property test for cache TTL decision
    - **Property 10: Cache TTL decision**
    - Create `RenoDXCommander.Tests/CacheTtlDecisionPropertyTests.cs`
    - For any `DateTime` last-write and current time, skip fetch iff `(current - lastWrite) < 24 hours`
    - **Validates: Requirements 1.5**

- [x] 4. Implement `SteamAppIdResolver`
  - [x] 4.1 Create `ISteamAppIdResolver` interface and `SteamAppIdResolver` implementation
    - Create `RenoDXCommander/Services/ISteamAppIdResolver.cs` with `ResolveAsync()` method
    - Create `RenoDXCommander/Services/SteamAppIdResolver.cs` implementing the 5-step priority chain: manifest override → cached AppID → `DetectedGame.SteamAppId` → `steam_appid.txt` → Steam Store search API (rate-limited via `SemaphoreSlim` + 1s delay)
    - Normalize-compare Steam Store search results against game name
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5a.2, 5a.3_

  - [x] 4.2 Write property test for Steam AppID resolution priority chain
    - **Property 4: Steam AppID resolution priority chain**
    - Create `RenoDXCommander.Tests/SteamAppIdPriorityPropertyTests.cs`
    - For any game name, manifest overrides, cached AppID, and `DetectedGame.SteamAppId`: override wins, then cache, then detected, each short-circuiting
    - **Validates: Requirements 5.1, 5.2, 5a.3, 6.3**

  - [x] 4.3 Write property test for normalize-compare search result matching
    - **Property 5: Normalize-compare search result matching**
    - Create `RenoDXCommander.Tests/NormalizeCompareSearchPropertyTests.cs`
    - For any game name and list of `SteamStoreSearchItem`, return the `Id` of the first item whose normalized name matches, or null
    - **Validates: Requirements 5.5, 5.6**

- [x] 5. Implement `PcgwService`
  - [x] 5.1 Create `IPcgwService` interface and `PcgwService` implementation
    - Create `RenoDXCommander/Services/IPcgwService.cs` with `LoadCacheAsync()` and `ResolveUrlAsync()` methods
    - Create `RenoDXCommander/Services/PcgwService.cs` implementing AppID cache load/save, URL resolution with manifest override priority, `appid.php` redirect URL construction, and OpenSearch fallback
    - Cache at `%LocalAppData%/RHI/steam_appid_cache.json` as JSON dict of normalized name → int
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7a.2, 7a.3_

  - [x] 5.2 Write property test for PCGW URL construction
    - **Property 6: PCGW URL construction**
    - Create `RenoDXCommander.Tests/PcgwUrlConstructionPropertyTests.cs`
    - For any positive integer AppID, URL equals `https://www.pcgamingwiki.com/api/appid.php?appid={appId}`; for any non-empty page title, URL equals `https://www.pcgamingwiki.com/wiki/{title}` with spaces → underscores
    - **Validates: Requirements 7.2, 7.4**

  - [x] 5.3 Write property test for PCGW URL resolution priority
    - **Property 7: PCGW URL resolution priority**
    - Create `RenoDXCommander.Tests/PcgwUrlResolutionPriorityPropertyTests.cs`
    - For any game name, manifest overrides, and resolved AppID: override wins, then appid.php URL, then OpenSearch fallback
    - **Validates: Requirements 7.1, 7a.3**

- [x] 6. Checkpoint — Verify all services compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. ACF AppID extraction in `GameDetectionService`
  - [x] 7.1 Extract Steam AppID from ACF filename during detection
    - In `RenoDXCommander/Services/GameDetectionService.Steam.cs`, inside the `FindSteamGames()` ACF loop, parse the numeric AppID from `appmanifest_<APPID>.acf` filename and set `game.SteamAppId`
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 7.2 Write property test for ACF filename AppID extraction round-trip
    - **Property 3: ACF filename AppID extraction round-trip**
    - Create `RenoDXCommander.Tests/AcfAppIdExtractionPropertyTests.cs`
    - For any positive integer `n`, `appmanifest_{n}.acf` extraction yields `n`; for non-matching strings, extraction yields null
    - **Validates: Requirements 4.1, 4.2, 4.3**

- [x] 8. Integration into `BuildCards` flow
  - [x] 8.1 Register services and wire into startup
    - Register `INexusModsService`/`NexusModsService`, `ISteamAppIdResolver`/`SteamAppIdResolver`, `IPcgwService`/`PcgwService` in DI (follow existing pattern in `App.xaml.cs`)
    - Call `NexusModsService.InitAsync()` and `PcgwService.LoadCacheAsync()` at startup before `BuildCards`
    - _Requirements: 1.1, 6.2_

  - [x] 8.2 Resolve URLs during card building
    - In the `BuildCards` loop (or equivalent), call `nexusModsService.ResolveUrl(game.Name, manifest)` and `await pcgwService.ResolveUrlAsync(game.Name, game.SteamAppId, game.InstallPath, manifest)` for each detected game
    - Assign results to `card.NexusModsUrl` and `card.PcgwUrl`
    - _Requirements: 2.1, 2.2, 7.6, 8.1, 8.2_

- [x] 9. Checkpoint — Verify integration compiles and link resolution works
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. UI changes — Action row relocation and new icons
  - [x] 10.1 Relocate action icons from title row to new action row
    - In `DetailPanelBuilder.PopulateDetailPanel()`, remove action icons (favourite, browse, hide, info, discussion) from the title row so only the game name remains
    - Build a new `StackPanel` (horizontal) action row below the badges row containing: favourite star, browse folder, hide, PCGW link, Nexus Mods link, info/notes, discussion link — in that order
    - PCGW and Nexus Mods icons are conditionally visible based on `HasPcgwUrl` / `HasNexusModsUrl`
    - Clicking PCGW or Nexus Mods icon opens the resolved URL via `Windows.System.Launcher.LaunchUriAsync`
    - Preserve existing behaviour of all relocated icons
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8_

- [x] 11. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The design uses C# throughout, matching the existing codebase
- Services follow the existing interface + implementation + DI pattern (same as `ManifestService`)
- `NormalizeName` is reused from `GameDetectionService` (extracted to shared helper if needed)
