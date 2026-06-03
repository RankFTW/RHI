# v1.9.9-beta3 Changes (from beta2)

## New Features

- **Auto Engine.ini HDR deployment** — When installing UE-Extended, Engine.ini HDR settings are now automatically deployed to the game's AppData config folder. The project name is auto-detected from the install path (folder above `Binaries\`). File is set read-only to prevent the engine from overwriting. No manual Engine.ini editing required anymore. Note: if the game also has an in-game HDR setting, make sure that's enabled too.
- **AppData button** — New "Config" button in the detail panel header (alongside Hide, Favourite, Folder) for Unreal Engine games. Opens the game's config folder in Explorer. Only visible when the folder exists.
- **Luma + RenoDX coexistence** — Games in the manifest `lumaRenodxCompat` list can now have both Luma and RenoDX installed simultaneously. The RenoDX row stays visible in Luma mode, and toggling Luma ON no longer uninstalls RenoDX. First game: Persona 5 Royal.
- **Render scale presets trimmed** — Simplified from 16 options to 10 with shorter names that fit the dropdown width. Added 45% Performance- to fill the gap between 50% and 33%.

## Bug Fixes

- Fixed RenoDX Info button not showing wiki status badge (✅ Working, 🚧 In Progress) for games that have a wiki entry but no notes text. Affected many games with Nexus links.
- Fixed RenoDX delete/uninstall button missing for `lumaRenodxCompat` games in Luma mode.
- Fixed Engine.ini deployment destroying duplicate keys (e.g. 40+ `Paths=` entries in `[Core.System]`). Switched from ParseIni/WriteIni rewrite to append-based approach that preserves all existing content.
- Fixed manually added games (drag-drop or Add Game) not detecting existing Display Commander, OptiScaler, DXVK, or DLSS/Streamline installations until Refresh. These are now scanned in the background immediately after adding.

## Manifest Updates

- Added `lumaRenodxCompat` field — Persona 5 Royal added as first entry.
- Added `engineIniPathOverrides` field (empty dict, fallback for auto-detection).
- Removed `engineIniHdrGames` list (replaced by automatic deployment for all UE-Extended games).
- Removed "set Upgrade Path to Off" from all game notes — now auto-configured.
- Added "HDR must be enabled in the game's display settings" to Crisol, Directive 8020, Echoes of the End.
- Added "Native HDR can be enabled using Ultra+" notes for 13 games with Ultra+ HDR toggles.
- Updated RE Framework game notes — removed external download links (now bundled).
- Added Black Myth: Wukong and LEGO Batman to `nativeHdrGames`.
- Added Of Ash and Steel to `nativeHdrGames`.
- Added REANIMAL to `nativeHdrGames`, removed from `wikiUnlinks`.
- Removed Persona 5 Royal from `wikiUnlinks`.
- Added LEGO Batman `engineIniPathOverride` (project name: "Dinner").
