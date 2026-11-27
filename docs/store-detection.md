# Game Store Detection

How to detect installed games for each supported storefront on Windows.

---

## Steam

**Primary:** Registry  
**Key:** `HKLM\SOFTWARE\Valve\Steam` → `SteamPath`  
**Fallback:** `HKLM\SOFTWARE\WOW6432Node\Valve\Steam` → `SteamPath`

Once you have the Steam root:

1. Read `{SteamPath}\steamapps\libraryfolders.vdf` — parse `"path"` values to find additional library folders the user has configured.
2. For each library, scan `{library}\steamapps\appmanifest_*.acf` files.
3. Parse each `.acf` for the `name` and `installdir` fields (Valve KeyValues text format).
4. The game folder is `{library}\steamapps\common\{installdir}`.

**Name caveat:** Steam writes `appid_<id>` as the name when it installs before fetching metadata. Read the real name from `{SteamPath}\appcache\appinfo.vdf` (binary KeyValues, v39/40/41) for those cases.

**Additional library locations:** `libraryfolders.vdf` lists all drives the user has set up as Steam libraries — always parse it, not just the default path.

---

## Epic Games

Two launchers exist. Try both; deduplicate by install path.

### Classic launcher
**Manifests:** `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item`  
Each `.item` is JSON. Read:
- `DisplayName` → game name
- `InstallLocation` → install path
- `CatalogNamespace`, `CatalogItemId`, `AppName` → needed to build a launch URI

### New launcher (eglv2)
**Installs:** `%LocalAppData%\com.epicgames.eglv2\installations.jsonl`  
Append-log format: skip the header line, then one JSON entry per event. Last entry per `installationId` wins; only `complete: true` entries count. This file has **no game names**.

**Names from uninstall registry:**  
`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EpicGames-{artifactId}` → `DisplayName`, `InstallLocation`

**Names fallback — catalog cache:**  
`%LocalAppData%\com.epicgames.eglv2\cache\prod\catalog-cache.json`  
Structure: `{ "items": { "{itemId}": { "item": { "title": "...", "mainGameItem": { "id": "..." } | null } } } }`  
Look up the jsonl entry's `itemId`. If `mainGameItem` is set (DLC), use the main game's title instead.

**Launch URI (classic):** `com.epicgames.launcher://apps/{Namespace}%3A{CatalogItemId}%3A{AppName}?action=launch&silent=true`

---

## GOG

**Key:** `HKLM\SOFTWARE\GOG.com\Games` (or `WOW6432Node` variant)  
Enumerate subkeys. Each subkey is a game:
- `GAMENAME` or `GameName` → game name
- `PATH` or `path` → install path

---

## EA App / Origin

Multiple fallback methods; deduplicate by install path.

1. **ProgramData manifests:** `%ProgramData%\EA\content\{id}\__Installer\installerdata.xml`  
   Parse `<gameName>` and `<defaultInstallPath>`.

2. **Registry:** `HKLM\SOFTWARE\Wow6432Node\Origin Games\{contentId}` → `InstallDir`, `DisplayName`

3. **Broader registry scan:** `HKLM\SOFTWARE\(Wow6432Node\)EA Games`, `Electronic Arts`, `BioWare`, `DICE`, `Respawn`, `Criterion Games`, `Ghost Games`, `PopCap`  
   Look for `Install Dir`, `InstallDir`, or `Install Directory` values.

4. **Default folders:** `%ProgramFiles%\EA Games` and `%ProgramFiles(x86)%\EA Games`  
   Scan subdirectories; include only those with an `.exe` at top level.

5. **EA Desktop AppData:** `%LocalAppData%\Electronic Arts\EA Desktop\` — scan ini/json files for paths, then look for `__Installer\installerdata.xml` in subdirectories.

---

## Ubisoft Connect

Three fallback methods; deduplicate by install path.

1. **Registry:** `HKLM\SOFTWARE\(WOW6432Node\)Ubisoft\Launcher\Installs\{id}` → `InstallDir`  
   **Important:** Ubisoft registry keys use numeric IDs with no display name. Use `Path.GetFileName(installDir)` as the game name — the folder name is the game title (e.g. `Assassin's Creed Valhalla`). Manifest entries must match this exact folder name.

2. **Launcher config:** `%LocalAppData%\Ubisoft Game Launcher\settings.yml`  
   Parse `game_installation_path` to find the games root, then scan subdirectories.

3. **Default folders:** `%ProgramFiles(x86)%\Ubisoft\Ubisoft Game Launcher\games` and the 64-bit equivalent.

---

## Battle.net

Three fallback methods; deduplicate by install path.

1. **Registry uninstall entries:** `HKLM\SOFTWARE\(WOW6432Node\)Microsoft\Windows\CurrentVersion\Uninstall\{id}`  
   Filter by `Publisher` = `Blizzard Entertainment`, `Activision`, or `Activision Blizzard`.  
   Read `InstallLocation` and `DisplayName`. Skip entries where the name contains "Battle.net" but not "game" (the launcher itself).

2. **Battle.net config:** `%AppData%\Battle.net\Battle.net.config`  
   Parse JSON for `Client.Install.DefaultInstallPath`, then scan subdirectories for `.exe` files.

3. **Default folders:** `%ProgramFiles(x86)%\Battle.net`, `%ProgramFiles%\Battle.net`, and the Blizzard Entertainment equivalents.

---

## Rockstar Games Launcher

Three fallback methods; deduplicate by install path.

1. **Registry uninstall entries:** Filter by `Publisher` containing "Rockstar". Read `InstallLocation` and `DisplayName`. Skip the launcher entry itself (name contains "Launcher" and "Rockstar" but not "Grand").

2. **Launcher titles.dat:** Read `InstallFolder` from the launcher's `InstallFolder` registry key, then parse `titles.dat` for `InstallFolder` JSON values.

3. **Default folders:** `%ProgramFiles%\Rockstar Games` and `%ProgramFiles(x86)%\Rockstar Games`.

---

## Xbox / Game Pass

Uses the Windows `PackageManager` API (`Windows.Management.Deployment`).

1. Read `HKLM\SOFTWARE\Microsoft\GamingServices\PackageRepository\Root` to get the list of registered game packages (same list SKIF uses). Only packages registered here, or that ship a `MicrosoftGame.config` file, are games — regular Store apps are excluded.

2. Call `PackageManager.FindPackagesForUser("")`. For each `Package`:
   - Skip frameworks, resource packs, bundles, and non-Store-signed packages.
   - Get `InstalledLocation.Path`.
   - **Resolve junctions:** For games installed to an Xbox games folder (the Xbox app default since 2021), `InstalledLocation` is a junction inside `WindowsApps` pointing to the real folder (e.g. `D:\xbox\<Game>\Content`). Resolve it with `CreateFileW` + `GetFinalPathNameByHandleW` and use the real path. Only use the junction path if it doesn't resolve outside `WindowsApps`.
   - Filter: package full name must be in the Gaming Services list, or `MicrosoftGame.config` must exist in the install folder.

3. **Display name:** `package.DisplayName`. Falls back to `package.Id.Name`.

4. **AUMID for launching:** `package.GetAppListEntries().FirstOrDefault().AppUserModelId`. Direct exe launch does not work for packaged GDK apps — always use `shell:AppsFolder\{AUMID}`.

**Key caveat:** True `WindowsApps` paths (not junctions) change version segment on every game update — never rely on stored exact paths for these. Games behind junctions are stable across updates.

---

## Notes

- Always deduplicate by install path (`OrdinalIgnoreCase`) when combining results from multiple methods for the same store.
- Path strings from registries and config files often use forward slashes — normalize to backslashes before comparison.
- Never write into `C:\Program Files\WindowsApps\` — it is access-denied for non-admin processes.
- `%ProgramFiles%` and `%ProgramFiles(x86)%` resolve to the same folder on 32-bit Windows — check both regardless.
