---
inclusion: fileMatch
fileMatchPattern: "*manifest*,*Manifest*,*RemoteManifest*"
---

# Manifest Knowledge (manifest.json)

## Fetching & Caching
- Fetched from GitHub on every launch (with ETag caching)
- Game names must match EXACTLY as detected (case-sensitive for matching)
- Special characters (®, ™) in game names cause matching issues — add both variants
- **No duplicate JSON keys** — parser only sees the last one, silently ignoring earlier entries

## Core Fields

| Field | Purpose |
|-------|---------|
| `blacklist` | Removes non-game apps from detection |
| `wikiNameOverrides` | Maps detected name → RenoDX wiki name |
| `lumaNameOverrides` | Maps detected name → Luma wiki name (checked before wikiNameOverrides) |
| `forceExternalOnly` | Redirects install to external URL (Nexus, Discord) |
| `authorOverrides` | Forces mod author display name |
| `donationUrls` | Maps author names → Ko-fi links |
| `pcgwUrlOverrides` | Game name → direct PCGW wiki URL |
| `componentUrls` | Base URL overrides (`ueExtended`, `ueDofFix`) |
| `dlssPresets` | SR/RR/FG preset arrays for manifest-driven presets |
| `shaderPacks` | Dict keyed by pack ID — add/override/disable packs |
| `addonPacks` | Dict keyed by SectionId — add/override/disable addons |

## Per-Game Fields

| Field | Purpose |
|-------|---------|
| `legacyReShadeVersions` | Game → forced RS version string |
| `legacyReShadeAvailable` | Array of picker versions |
| `lumaDefaultGames` | Array — auto-enable Luma mode on first detection |
| `lumaRenodxCompat` | Array — games where RenoDX coexists with Luma |
| `nativeHdrGames` | Array — UE-Extended with Set_Path=0 (supersedes `ueExtendedGames`) |
| `ueExtendedGames` | Array — UE-Extended for SDR→HDR upgrade |
| `thirtyTwoBitGames` | Array — force 32-bit detection |
| `sixtyFourBitGames` | Array — force 64-bit detection |
| `dofFixSkipGames` | Array — exclude from DOF Fix eligibility |
| `dofFixForceGames` | Array — force-enable DOF Fix (bypasses version check) |
| `dlssSkipGames` | Array — skip expensive DLSS recursive scan |
| `gacSymlinkGames` | Dict — game → GAC directory path |
| `installWarnings` | Dict — game → { component: warning message } |
| `launchExeOverrides` | Game → relative exe path (also used for profile matching) |
| `engineIniPathOverrides` | Game → project name or absolute path |
| `engineHintOverrides` | Game → full engine hint string (e.g. "Unreal Engine 4.27.2") |
| `profileExeExclusions` | Array — generic exe names to skip in profile matching |
| `profileNameOverrides` | Game → exact NVIDIA profile name |
| `renodxIniOverrides` | Game → { key: value } for `[renodx]` section |
| `renodxExtraSettings` | Array of dynamic cog dialog toggles |

## Info Button Content Fields

| Field | Component Button |
|-------|-----------------|
| `gameNotes` | RenoDX (this IS the RenoDX info field — there is no separate `renodxGameInfo`) |
| `reshadeGameInfo` | ReShade |
| `reframeworkGameInfo` | RE Framework |
| `relimiterGameInfo` | ReLimiter |
| `displayCommanderGameInfo` | Display Commander |
| `optiScalerGameInfo` | OptiScaler |
| `lumaGameInfo` | Luma (Tier 1 primary) |
| `lumaGameNotes` | Luma (Tier 2 supplementary) |
| `dxvkGameNotes` | DXVK |

All resolved via `AddonInfoResolver.cs` — routes by `AddonType` enum to the correct manifest dict. All use `GameNoteEntry` structure: `{ notes, notesUrl, notesUrlLabel }`.

## installPathOverrides
- Structure: `{ "Game Name": "relative\\subpath" }` — appends to the detected game root
- Pipe-separated for multiple candidates: `"Game": "path1|path2"` — tries each in order, uses first that exists
- Supports `..` for navigating UP from a wrongly-detected subfolder (e.g. Game Pass games where `ResolveXboxGameRoot` picks the wrong inner folder)
- Example: `"Hades II": "Ship|.."` — Steam uses `Ship` subfolder; Game Pass detection lands in `Content\Audio`, `..` navigates back up to `Content\`
- `ResolveXboxGameRoot` logic: checks `Content\` subfolder, picks `innerDirs[0]` (alphabetical). If a game puts its exe directly in `Content\` alongside subfolders (no inner project folder), detection picks the wrong subfolder (e.g. `Audio`). Fix via `..` in the override rather than changing detection code.

## splitGames
- Structure: `{ "Detected Name": [ { "name": "Sub-Game", "subPath": "relative\\path" } ] }`
- Applied at TOP of `BuildCards`, BEFORE engine detection. Original entry suppressed.
- Each split inherits `Source`, `SteamAppId`, `EpicAppName` from parent.
- Wiki matching: each split needs `wikiNameOverrides` to the shared wiki name.
- `subPath: ""` valid — creates card pointing to same root (used for ACF hijack workaround).
- First-run may produce temporary duplicates (self-resolves after library save).

## forceExternalOnly Pitfalls
- Uses `card.GameName` (detected name), NOT wiki-mapped name
- When game has `wikiNameOverrides`, need BOTH detected AND mapped name as entries
- `ExternalUrl` can be lost after install — use `gameNotes` for reliable info content

## wikiUnlinks
- Blocks BOTH specific wiki match AND generic fallback (code sets `fallback = null`)
- For deprecated mods: combine `wikiUnlinks` + `snapshotOverrides` (point to generic URL)

## emulatorGames
- Structure: `{ "Ryubing": { "addons": ["wiki game name 1", ...] } }`
- Addon names are wiki game names, URLs resolved from scraped wiki data at runtime.

## Nexus Update Check
- `POST https://api.nexusmods.com/v2/graphql` — no API key required
- Baselines in `nexus_baselines.json`. First launch seeds baseline.
- `updatedAt` changes for ANY page edit (description, metadata) — not just version updates
- External-only Nexus updates do NOT contribute to Update All button
- Rate-limited path must still run Nexus check (different API from GitHub)

## Manifest-Driven Addon Packs
- Merge after `EnsureLatestAsync` (Addons.ini parse). Re-apply after every `EnsureLatestAsync()` call.
- Fields: `packageName`, `description`, `downloadUrl`, `downloadUrl32`, `downloadUrl64`, `repositoryUrl`, `effectInstallPath`, `deployFileName`, `disabled`

## Cache Phase Manifest Timing
- `LoadCacheAndBuildCardsAsync` runs BEFORE manifest fetch. `_manifest` (instance field) is null.
- The cache phase loads `cachedManifest` as a LOCAL variable via `_manifestService.LoadCached()`. Use THIS variable for all manifest lookups during the cache phase — NOT `_manifest`.
- After loading cached manifest: populate bitness overrides, blacklist, engineHintOverrides BEFORE building cards.
- Any manifest field affecting card properties for install decisions must apply in BOTH phases.
