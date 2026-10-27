# Requirements Document

## Introduction

This feature adds Nexus Mods and PCGamingWiki (PCGW) links to each detected game in RHI. Nexus Mods links are resolved by fetching the public `games.json` and normalize-matching game names. PCGW links are resolved via Steam AppID (parsed from ACF filenames, `steam_appid.txt`, or the Steam Store search API) with an OpenSearch fallback for non-Steam games. Both URLs are displayed as clickable icons in a new dedicated action row on the detail panel, relocated from the current title row.

## Glossary

- **RHI**: RenoDX HDR Injector — the application (RenoDXCommander)
- **Nexus_Service**: The service responsible for fetching, caching, and querying the Nexus Mods games list
- **PCGW_Service**: The service responsible for resolving PCGamingWiki URLs via Steam AppID or OpenSearch
- **AppID_Resolver**: The component within PCGW_Service that resolves Steam AppIDs through the priority chain
- **Detail_Panel**: The right-hand game detail panel built by DetailPanelBuilder
- **Action_Row**: The new horizontal row of action icons below the engine/API badges row
- **GameCardViewModel**: The view-model backing each game card and detail panel
- **DetectedGame**: The model representing a detected game from any platform
- **Manifest**: The `manifest.json` configuration file containing overrides and game metadata
- **NormalizeName**: The existing method that strips diacritics, trademark symbols, and non-alphanumeric characters for fuzzy name matching

## Requirements

### Requirement 1: Fetch and Cache Nexus Mods Game List

**User Story:** As a user, I want RHI to automatically fetch the Nexus Mods game catalogue, so that game links can be resolved without manual configuration.

#### Acceptance Criteria

1. WHEN RHI starts, THE Nexus_Service SHALL fetch the game list from `https://data.nexusmods.com/file/nexus-data/games.json`
2. WHEN the fetch succeeds, THE Nexus_Service SHALL write the response to a local cache file on disk
3. WHEN the fetch fails and a cached file exists on disk, THE Nexus_Service SHALL load the game list from the cached file
4. WHEN the fetch fails and no cached file exists, THE Nexus_Service SHALL log the failure and continue without Nexus link resolution
5. WHILE a cached file exists on disk, THE Nexus_Service SHALL skip the network fetch if the cached file is less than 24 hours old
6. THE Nexus_Service SHALL parse each entry in the game list extracting the `name` and `nexusmods_url` fields
7. THE Nexus_Service SHALL build a lookup dictionary keyed by the normalized game name (using NormalizeName)

### Requirement 2: Resolve Nexus Mods URL per Game

**User Story:** As a user, I want each detected game to show a Nexus Mods link when a match exists, so that I can quickly browse mods for that game.

#### Acceptance Criteria

1. WHEN a game is detected, THE Nexus_Service SHALL normalize the game name using NormalizeName and look it up in the Nexus dictionary
2. WHEN a match is found in the Nexus dictionary, THE Nexus_Service SHALL store the `nexusmods_url` value on the GameCardViewModel `NexusModsUrl` property
3. WHEN no match is found in the Nexus dictionary, THE Nexus_Service SHALL check the Manifest `nexusUrlOverrides` field for a manual override keyed by the detected game name
4. WHEN a manual override exists in `nexusUrlOverrides`, THE Nexus_Service SHALL store the override URL on the GameCardViewModel `NexusModsUrl` property
5. WHEN no match is found and no override exists, THE GameCardViewModel `NexusModsUrl` property SHALL remain null

### Requirement 3: Manifest Nexus URL Override Support

**User Story:** As a maintainer, I want to manually map games to Nexus Mods URLs in the manifest, so that games with mismatched names still get correct links.

#### Acceptance Criteria

1. THE Manifest SHALL support a `nexusUrlOverrides` field containing a dictionary of game name to Nexus Mods URL string pairs
2. WHEN the `nexusUrlOverrides` field is absent from the Manifest, THE Nexus_Service SHALL treat the overrides as an empty dictionary
3. WHEN a `nexusUrlOverrides` entry exists for a detected game name, THE Nexus_Service SHALL use the override URL as the highest priority source, bypassing the dictionary match

### Requirement 4: Parse Steam AppID from ACF Filename

**User Story:** As a user, I want RHI to extract Steam AppIDs during detection, so that PCGW links can be resolved accurately for Steam games.

#### Acceptance Criteria

1. WHEN a Steam ACF file is parsed during detection, THE GameDetectionService SHALL extract the numeric AppID from the filename pattern `appmanifest_<APPID>.acf`
2. WHEN the AppID is successfully extracted, THE GameDetectionService SHALL store the AppID in the `SteamAppId` field on the DetectedGame model
3. WHEN the filename does not match the expected pattern, THE DetectedGame `SteamAppId` field SHALL remain null
4. THE DetectedGame model SHALL include a nullable integer `SteamAppId` property

### Requirement 5: Resolve Steam AppID via Priority Chain

**User Story:** As a user, I want RHI to resolve Steam AppIDs for all games (including non-Steam copies), so that PCGW links work for as many games as possible.

#### Acceptance Criteria

1. WHEN a game name exists in the Manifest `steamAppIdOverrides` dictionary, THE AppID_Resolver SHALL use the override AppID and skip all other lookups
2. WHEN a game has a `SteamAppId` already set on DetectedGame (from ACF parsing), THE AppID_Resolver SHALL use that AppID without further lookups
3. WHEN a game has no `SteamAppId`, THE AppID_Resolver SHALL check for a `steam_appid.txt` file in the game install directory and parse the integer AppID from the first line
4. WHEN `steam_appid.txt` does not exist or is unparseable, THE AppID_Resolver SHALL query the Steam Store search API at `https://store.steampowered.com/api/storesearch/?term=<game_name>&l=english&cc=US`
5. WHEN the Steam Store search returns results, THE AppID_Resolver SHALL normalize-compare each result name against the game name and use the AppID of the first matching result
6. WHEN no Steam Store search result matches the game name, THE AppID_Resolver SHALL leave the Steam AppID unresolved for that game
7. THE AppID_Resolver SHALL rate-limit Steam Store search API requests to a maximum of one request per second

### Requirement 5a: Manifest Steam AppID Override Support

**User Story:** As a maintainer, I want to manually map games to Steam AppIDs in the manifest, so that games where automatic resolution fails still get correct PCGW links.

#### Acceptance Criteria

1. THE Manifest SHALL support a `steamAppIdOverrides` field containing a dictionary of game name to integer Steam AppID pairs
2. WHEN the `steamAppIdOverrides` field is absent from the Manifest, THE AppID_Resolver SHALL treat the overrides as an empty dictionary
3. WHEN a `steamAppIdOverrides` entry exists for a detected game name, THE AppID_Resolver SHALL use the override AppID as the highest priority source, bypassing all other resolution steps

### Requirement 6: Cache Resolved Steam AppIDs

**User Story:** As a user, I want resolved Steam AppIDs to be cached permanently, so that repeated lookups are avoided on subsequent launches.

#### Acceptance Criteria

1. WHEN a Steam AppID is resolved for a game (from any source in the priority chain), THE PCGW_Service SHALL write the game name and AppID pair to a persistent cache file on disk
2. WHEN RHI starts, THE PCGW_Service SHALL load the AppID cache from disk before resolving any new AppIDs
3. WHEN a game name exists in the AppID cache, THE AppID_Resolver SHALL use the cached AppID and skip the priority chain
4. THE PCGW_Service SHALL store the AppID cache as a JSON dictionary mapping normalized game names to integer AppIDs

### Requirement 7: Build PCGW URL from Steam AppID

**User Story:** As a user, I want RHI to construct the correct PCGW URL for each game, so that clicking the PCGW icon takes me to the right wiki page.

#### Acceptance Criteria

1. WHEN a game name exists in the Manifest `pcgwUrlOverrides` dictionary, THE PCGW_Service SHALL use the override URL as the highest priority source, bypassing all other resolution steps
2. WHEN a Steam AppID is resolved for a game, THE PCGW_Service SHALL construct the PCGW URL as `https://www.pcgamingwiki.com/api/appid.php?appid=<STEAM_APPID>`
3. WHEN no Steam AppID is resolved, THE PCGW_Service SHALL query the PCGW OpenSearch API at `https://www.pcgamingwiki.com/w/api.php?action=opensearch&search=<name>&limit=5&format=json`
4. WHEN the OpenSearch API returns one or more results, THE PCGW_Service SHALL construct the PCGW URL as `https://www.pcgamingwiki.com/wiki/<page_name>` using the first result page title (with spaces replaced by underscores)
5. WHEN the OpenSearch API returns no results, THE GameCardViewModel `PcgwUrl` property SHALL remain null
6. WHEN a PCGW URL is resolved, THE PCGW_Service SHALL store the URL on the GameCardViewModel `PcgwUrl` property

### Requirement 7a: Manifest PCGW URL Override Support

**User Story:** As a maintainer, I want to manually map games to PCGW URLs in the manifest, so that games where automatic resolution fails or resolves incorrectly still get correct wiki links.

#### Acceptance Criteria

1. THE Manifest SHALL support a `pcgwUrlOverrides` field containing a dictionary of game name to PCGW URL string pairs
2. WHEN the `pcgwUrlOverrides` field is absent from the Manifest, THE PCGW_Service SHALL treat the overrides as an empty dictionary
3. WHEN a `pcgwUrlOverrides` entry exists for a detected game name, THE PCGW_Service SHALL use the override URL and skip AppID resolution and OpenSearch entirely

### Requirement 8: Store Link URLs on GameCardViewModel

**User Story:** As a developer, I want the resolved URLs stored on the view-model, so that the UI can bind to them for visibility and navigation.

#### Acceptance Criteria

1. THE GameCardViewModel SHALL include a nullable string `NexusModsUrl` property
2. THE GameCardViewModel SHALL include a nullable string `PcgwUrl` property
3. THE GameCardViewModel SHALL expose a boolean `HasNexusModsUrl` computed property that returns true when `NexusModsUrl` is not null or empty
4. THE GameCardViewModel SHALL expose a boolean `HasPcgwUrl` computed property that returns true when `PcgwUrl` is not null or empty

### Requirement 9: Relocate Action Icons to New Action Row

**User Story:** As a user, I want the action icons moved to their own row below the badges, so that the title row is cleaner and there is room for new link icons.

#### Acceptance Criteria

1. THE Detail_Panel SHALL display the game name as the sole content of the title row (no action icons)
2. THE Detail_Panel SHALL display a new Action_Row below the engine/API badges row
3. THE Action_Row SHALL contain the following icons in left-to-right order: Favourite star, Browse folder, Hide, PCGW link, Nexus Mods link, Info/notes, Discussion link
4. WHEN a game has no resolved PCGW URL, THE Action_Row SHALL hide the PCGW link icon
5. WHEN a game has no resolved Nexus Mods URL, THE Action_Row SHALL hide the Nexus Mods link icon
6. WHEN the PCGW link icon is clicked, THE Detail_Panel SHALL open the resolved PCGW URL in the default browser
7. WHEN the Nexus Mods link icon is clicked, THE Detail_Panel SHALL open the resolved Nexus Mods URL in the default browser
8. THE Action_Row SHALL preserve the existing behaviour of all relocated action icons (Favourite, Browse, Hide, Info, Discussion)
