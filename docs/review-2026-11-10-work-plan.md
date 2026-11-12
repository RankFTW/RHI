# RHI review work plan (10 Nov 2026)

Hand-over from a full read-only review of the codebase at commit `5b9fc51`. Seven reviewers read the code; nothing was changed. This file is the to-do list for carrying the work out.

## How to use this file

- **Read `.claude/CLAUDE.md` first.** Its rules apply to every item here: run `publish.bat` after each code change, update `RenoDXCommander/RHI_PatchNotes.md` in the same turn, never bump the version, **ask the user before committing or pushing**, and grep `.kiro/steering/rhi-knowledge.md` for the feature name before changing it.
- **Re-verify before you fix.** Line numbers are as of `5b9fc51` and will drift. Each item has a status:
  - **[V]** the lead reviewer re-read the code or file and confirmed the defect.
  - **[R]** a reviewer read the full code path and reported it; not independently re-checked.
  - **[P]** plausible: a step was not traced, or it needs specific data or timing.
  - Nothing was reproduced by running the app. Read the cited code and trace the scenario before editing. If the defect isn't there, mark the item `[-]` with the reason.
- **One item at a time.** Fix, build, ask the user to test, then move on. Don't batch unrelated fixes into one change.
- **Order.** Work top to bottom: Phase 1 (release regression), Phase 2 (game-folder safety), Phase 3 (launch vs Refresh), Phase 4 (manifest), Phase 5 (other bugs), Phase 6 (tooling). The user's main interest is Phase 4, but Phases 1 and 2 are the ones that hurt users.
- **Tick items off here** (`[x]` done with the commit hash, `[-]` decided against with the reason), the same way as `docs/code-health-plan.md`.
- **Manifest edits are deploys.** Pushing `manifest.json` to `main` changes behaviour for every user, including people on old app versions. Run `python tools/check-manifest.py`, make targeted edits only (no reformatting, keep CRLF), and ask before pushing.

Path shorthand: `App\` = `RenoDXCommander\`, `S\` = `RenoDXCommander\Services\`, `VM\` = `RenoDXCommander\ViewModels\`.

---

## Phase 1: release regression (do first)

- [x] **1.1 Three files the app reads from the exe folder are not there. [V]** Done in `eacc622`: `AppPaths.InExeDir` falls back to the extraction folder (`AppContext.BaseDirectory`), `publish.bat` copies `DisplayCommander.ini` + `dxvk.conf`. Tested: Display Commander ini seeded and deployed; ReBAR global (helper) and per-game (direct, Admin Mode) writes stuck. Correction: `DeployDcIniIfAbsent` logged a skip, it didn't throw.
  - `NvAPIWrapper.dll`, `DisplayCommander.ini` and `dxvk.conf` are read with `AppPaths.InExeDir(...)` but are missing from `C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI\`. In the single-file build they only exist in the extraction folder (`%TEMP%\.net\RHI\<hash>\`). Reported to date from commit `1b51cb2` (27 Oct), which changed `AppContext.BaseDirectory` to `AppPaths.InExeDir`.
  - **ReBAR:** `S\DlssPresetService.DriverSettings.cs:1055`, `S\DlssPresetService.ReBar.cs:181` and `:406` pass that DLL path to `Add-Type -Path` inside elevated PowerShell scripts, so global ReBAR enable, ReBAR mode and per-game ReBAR probably fail. `DriverSettings.cs:416` assigns the same path to an unused variable.
  - **Display Commander:** `S\AuxInstallService.cs:281` can't seed `DisplayCommander.ini` on a fresh install; `S\AuxInstallService.Ini.cs:349` then throws `FileNotFoundException`. Existing users seeded before 27 Oct are unaffected. `dxvk.conf` (`AuxInstallService.cs:299`) has a built-in fallback, so low impact.
  - **Fix:** for `NvAPIWrapper.dll` use the loaded assembly's location (`typeof(NvAPIWrapper.NVIDIA).Assembly.Location`) or `AppContext.BaseDirectory` for this one file. For the two ini/conf files add two `copy` lines to `publish.bat`.
  - **Test:** in a published build, toggle ReBAR; delete `%LocalAppData%\RHI\inis\DisplayCommander.ini` and install Display Commander.
  - Open question (answered): loose files come from `publish.bat`'s explicit `copy` lines, not from the csproj; these two were missing from that list. The NuGet DLL was only ever in the extraction folder.
  - Follow-up (not done): the elevated ReBAR helper's result isn't checked, so a failed write still logs "completed". Read the value back after the script.

- [x] **1.2 HDR Gaming Database parses 0 entries. [V in a user log from 8 Nov]** Done in `3d99906`: parser reads the HTML table rows (and still the old Markdown list), HTML-decodes names (`Ratchet &amp; Clank`), logs when 0 parse. Tested: 138 entries; links show in RenoDX / Luma Info.
  - `S\HdrDatabaseService.cs:30-32` `EntryPattern` only matches markdown bullets; the upstream README is now an HTML table (138 entries). Log line: `[HdrDatabaseService.FetchAsync] Parsed 0 HDR database entries`.
  - Fix: parse the table rows.

---

## Phase 2: game-folder safety

These can delete or overwrite files in a user's game install. Items 2.2, 2.4, 2.5, 2.6 and 2.9 share a cause: `.original` sentinels and `rhi_install.txt` shared-file owners track the same files without consulting each other. Fix them individually first; the structural fix is 2.14.

- [ ] **2.1 A user's own `reshade-shaders` folder can be wiped. [V]**
  - `S\ShaderPackService.Deploy.cs:278-290` (`SyncGameFolder`): for an unmanaged folder, if `reshade-shaders-original` already exists the code only logs "skipping rename"; if `Directory.Move` throws it only logs. It then falls through to the mirror (`S\ShaderPackService.Mirror.cs:121-134`), which deletes every file not in the plan. `DeployToGameFolder` at `Deploy.cs:189-207` has the same skip and then writes the marker.
  - Scenario: the game has `reshade-shaders-original` from an earlier RHI cycle plus a new unmanaged `reshade-shaders` (user re-ran the ReShade installer or deleted the marker file), or the folder is held open.
  - Fix: if the folder is unmanaged and the rename did not happen, return without mirroring and without writing the marker.

- [ ] **2.2 OptiScaler uninstall deletes the game's own DLSS DLLs. [V]**
  - `S\OptiScalerService.Install.cs:813-846`: deletion is gated only on `RhiInstallManifest.RemoveSharedFileOwner(...)`, which returns true when there is no manifest or no owner entry (`App\Models\RhiInstallManifest.cs:361-363`), and reportedly on an exception too.
  - Scenario: the DLSS cache was empty at install, so OptiScaler never copied `nvngx_dlss.dll` / `nvngx_dlssd.dll` / `nvngx_dlssg.dll`. Uninstall deletes the game's originals; there is no `.original` to restore.
  - Rule (`.claude/rules/rhi-install.md`): "only delete DLSS DLLs if `.original` backup exists".
  - Fix: delete only when `<file>.original` exists or OptiScaler is actually listed as an owner. Make `RemoveSharedFileOwner` return false on error.

- [ ] **2.3 Luma archive install overwrites `dxgi.dll` with no backup or ownership check. [V for the unguarded copy; R for the dead flag]**
  - `S\LumaService.cs:1489-1504` copies stable ReShade to `dxgi.dll` with `overwrite: true` when `archiveHasDxgi` is false. Reported: the flag can never be set, because `dxgi.dll` entries hit `continue` (zip ~1333-1339, 7z ~1460-1467) before the lines that set it (1361, 1469).
  - Scenario: the game has OptiScaler, DXVK, Special K or ENB as `dxgi.dll`, and the user drops a Luma archive or uses the Nexus path. The DLL is replaced, the other component's record still says installed, and the later foreign-DLL prompt is bypassed because the file is now ReShade.
  - Fix: remove step 3 and the flag. `ApplyLumaPostInstallAsync` already installs ReShade through the guarded path (see `rhi-knowledge.md` ~line 226). Update the "If no `dxgi.dll` in archive" line in `rhi-install.md`.

- [ ] **2.4 "Restore DLSS/SL" deletes DLLs other RHI components own. [R]**
  - `S\DlssStreamlineService.Swap.cs:266-273` (`Restore`) and `:383-390` (`RestoreAll`). Callers: `App\DetailPanelBuilder.Overrides.NvidiaProfile.cs:488`, `App\MassDlssDeployDialog.cs:569-571`.
  - `Restore` calls `SentinelRestore` on every detected DLSS DLL and never consults shared owners; a 0-byte `.original` means delete.
  - Scenario: a game with no DLSS of its own has DLSS5 Tool, Feeder or RTX Encore installed. Restore deletes `nvngx_dlssnr.dll` (and `nvngx_dlss.dll` for Feeder) while the feature still shows installed.
  - Fix: skip when the `.original` is 0 bytes or `GetSharedFileOwners` is non-empty. "Default" should only undo a version swap.

- [ ] **2.5 OptiScaler `UpdateAsync` overwrites game files with no backup. [R]**
  - `S\OptiScalerService.Install.cs:1384` (root files), `1469/1478/1487` (DLSS DLLs), `1502` (NR DLL). The comment at 1349 only holds for files that were in the first installed version.
  - Scenario A: a new upstream build adds a root file the game also ships; the silent auto-update overwrites it and uninstall later deletes it. Scenario B: the DLSS cache filled after install; update overwrites the game's DLSS DLLs with no backup or owner entry, then 2.2 deletes them.
  - Fix: call `BackupOriginalIfExists(destPath)` before every root copy in `UpdateAsync` (no-op when `.original` exists) and add the owner entry for DLSS files.

- [ ] **2.6 OptiScaler install/update/uninstall drops other components' data from `rhi_install.txt`. [R]**
  - `S\OptiScalerService.Install.cs:638-650`, `1588-1600`, `1118`. Install and update rebuild the manifest copying only `SharedFiles`, `NrMethod` and `Components`, so `Dlss5Build*` fields are lost; uninstall calls `RhiInstallManifest.Delete` unconditionally.
  - Scenario: a pinned DLSS5 Tool Foveated build loses its marker on any OptiScaler update and the stock auto-update (`S\Renodx5AddonService.cs:531`, `1278`) overwrites it. After an OptiScaler uninstall, RTX Encore / ShortFuse / Feeder owner data is gone.
  - Fix: read-modify-write the existing manifest; on uninstall clear only OptiScaler's fields.

- [ ] **2.7 DXVK DX9 reinstall overwrites the ReShade backup with DXVK's own DLL. [R, re-read by the reviewer]**
  - `S\DxvkService.Install.cs:120-128`: any existing `d3d9.dll` is copied to `.reshade_backup` with `overwrite: true` and no `IsReShadeFile` / `IsDxvkFile` check. A failed backup is only logged and the file is still deleted.
  - Scenario: DX9 game with ReShade as `d3d9.dll`; install DXVK; click install again (reachable from `App\MainWindow.Events.Install.cs:144-145`). Uninstall then "restores" DXVK as `d3d9.dll` and records it as ReShade.
  - Fix: back up only when the file is ReShade, never overwrite an existing `.reshade_backup`, skip when the file is already DXVK, use `.original` for anything else.

- [ ] **2.8 OptiScaler over RHI DXVK: the coexistence move can never run. [R]**
  - `S\OptiScalerService.Install.cs:322-323` against `539-573`. Step 9 moves DXVK's DLL to `OptiScaler\plugins` only if the file at that path is DXVK, but step 5 has already replaced it with OptiScaler. DXVK's `dxgi.dll` ends up as `.original`, and DXVK uninstall (`S\DxvkService.Install.cs:397-404`, no identity check) deletes OptiScaler's DLL.
  - Fix: do the DXVK move before step 5, next to the ReShade rename.

- [ ] **2.9 OptiScaler uninstall deletes a real `.original` backup of a foreign `dxgi.dll`. [R, re-read by the reviewer]**
  - `S\OptiScalerService.Install.cs:782-800`: when ReShade will reclaim the name, any `<dll>.original` is deleted as "stale" with no size check.
  - Scenario: Special K or ENB as `dxgi.dll`, backed up by ReShade install (`S\AuxInstallService.DllIdentification.cs:58-62`); OptiScaler install then uninstall deletes that backup.
  - Fix: delete only when the `.original` is 0 bytes.

- [ ] **2.10 `dinput8.dll` and other proxy names have no shared owner. [R]**
  - `S\REFrameworkService.cs:103-104`, `:287-293`; `VM\MainViewModel.UalHooked.cs:26-37` (`GetRhiDllOwners` has no RE Framework entry); `S\UltimateAsiLoaderService.cs:411-420` (`IsRhiManagedFile` checks only ReShade / OptiScaler / DC) and `:360-399`; `VM\MainViewModel.Install.Luma.cs:523-545` (no check before the RE Framework install).
  - Scenario A: ASI Loader is `dinput8.dll` on an RE Engine game; installing RE Framework moves it to `.original` as "the game's file"; at next launch the ASI Loader auto-update copies itself back over RE Framework. Scenario B: 20/30 FG Unlock or Universal MFG is the `dinput8.dll` proxy; RE Framework overwrites it and its uninstall deletes file and sentinel.
  - Fix: add RE Framework (and DXVK's DLL names, dgVoodoo's `d3d9.dll`) to `GetRhiDllOwners`; check it in `InstallREFrameworkAsync`, `IsRhiManagedFile` and before the ASI Loader auto-update copy.

- [ ] **2.11 ReShade's foreign-DLL check and backup only cover some names. [R]**
  - `VM\MainViewModel.Install.Luma.cs:79-110` checks only `dxgi.dll`, whatever name ReShade will actually use. `S\AuxInstallService.DllIdentification.cs:40-56` backs up only `dxgi.dll` and `d3d8/9/10core/11.dll`. `S\AuxInstallService.Install.cs:125` is `File.Copy(overwrite: true)`; `:393-395` deletes on uninstall.
  - Scenario: ReShade deploys as `opengl32.dll`, `d3d12.dll`, a DLL-override name (`version.dll`, `winmm.dll`) or the `.asi`; an existing file of that name is overwritten with no prompt and deleted on uninstall.
  - Related: `BackupForeignDll` line 61 deletes an existing `.original` before moving, so a second foreign DLL replaces the first true original.
  - Fix: run the check and sentinel backup on the resolved `destName` for every name; never replace a non-empty `.original`.

- [ ] **2.12 Mass "Deploy ReShade.ini" ignores the per-game ini lock and hidden games. [R, re-read by the reviewer]**
  - `App\MassDeployHandler.cs:29-58` has no `GetKeepRsIniUpdated` check and no `card.IsHidden` skip; `SettingsHandler.General.cs` does both.
  - Fix: add both skips and count the dialog total from the filtered list.

- [ ] **2.13 Smaller game-folder items. Verify each before fixing.**
  - [ ] **DXVK uninstall while the game is running** loses the record and on DX9 deletes the ReShade backup. `S\DxvkService.Install.cs:397-412`, `489-510`, `527-541`, `585`. Fix: preflight for locked files, keep the record, tell the user to close the game. [R]
  - [ ] **Uninstalling ReShade deletes `ReShadePreset.ini` and `reshade.ini` unconditionally.** `S\AuxInstallService.Install.cs:412-423`. May be intended: ask the user. Suggested: leave the preset unless RHI deployed it; skip `reshade.ini` when the game is ini-locked. [R]
  - [ ] **`rhi_install.txt` has no lock, is not atomic, and fails open.** `App\Models\RhiInstallManifest.cs:113-144`, `175`, `318`, `361-363`. `Read` returns null on any error and writers do `Read(...) ?? new`. Fix: route through `SafeJsonFile` with `LockFor(path)`; abort on "unreadable" instead of treating it as absent. [R / race P]
  - [ ] **NR Cost Scaler is clobbered by other writers of `nvngx_dlssnr.dll`, and its update resets the user's ini.** `S\DlssNrCostScalerService.cs:178-186`; other writers: `App\DetailPanelBuilder.Overrides.NvidiaProfile.cs:396-404`, `:636`, `S\RtxEncoreService.cs:216-217`. Fix: one helper that resolves the real NR DLL path; copy the ini only when absent. [R]
  - [ ] **A failed Streamline backup does not stop the overwrite, and the backup is never refreshed.** `S\DlssStreamlineService.Swap.cs:167`, `234`, `336-371`, `299-324`. Fix: return a bool and abort on failure; write the zip via temp-and-move; delete it after a successful restore. [R / P]
  - [ ] **Turning Luma mode off can delete the whole `reshade-shaders` folder and `reshade.ini` while ReShade is installed.** `VM\MainViewModel.Install.Luma.cs:805-846`. Fix: skip the fallback when a ReShade record exists or the folder has no managed marker. [R]
  - [ ] **DXVK install failure is reported as success** and leaves the card in a Vulkan state. `VM\MainViewModel.Dxvk.cs:97-115`; `InstallAsync` never throws. [R]
  - [ ] **DXVK standard-mode reinstall drops `BackedUpFiles`**, so the game's own DLL stays as `.original` after uninstall. `S\DxvkService.Install.cs:857-870`. [R]
  - [ ] **`SentinelBackup` saves RHI's own DLL as "original" when the sentinel is missing** (old installs). `S\Rtx40MfgService.cs:724-725`, `S\RtxEncoreService.cs:401-402`, `S\Dlssg20_30Service.cs:286-287`. [R]
  - [ ] **20/30 FG auto-update can overwrite another component's `version.dll`.** `S\Dlssg20_30Service.cs:313-319`. [R]
  - [ ] **RTX Encore's in-place XeSS replacement has no rollback.** `S\Rtx40MfgService.EncoreIntel.cs:118-121`. [R]
  - [ ] **Standalone DLSS Enabler overwrites and deletes with no ownership check.** `S\DlssEnablerService.cs:285`, `303`. [R]
  - [ ] **Newest cached DLSS DLL is chosen by string sort** ("310.9" beats "310.10"). `S\OptiScalerService.Staging.cs:748-751`. Fix: `Version.TryParse`. [R]
  - [ ] **`DeployAddonsForGame` leaks `.addon64` to later addons for a 32-bit game.** `S\AddonPackService.cs:906`, `917`. [R]
  - [ ] **Emulator bundle uninstall resolves names differently from install.** `VM\MainViewModel.Install.Components.cs:167-183` vs `261-265`. [R]
  - [ ] **A timed-out 7z extraction is still deployed** (Nexus path). `VM\MainViewModel.Install.Nexus.cs:224-256`. [R]
  - [ ] **Single-game install passes `forceOverwrite: true` for `[renodx]` keys.** `VM\MainViewModel.Install.cs:1022`. The rule says false on install. [R]
  - [ ] **dgVoodoo `IsDeployed` is true when any `d3d9.dll.original` exists**, and `RemoveFromGame` then deletes `d3d9.dll` unchecked. `S\DgVoodooService.cs:180-195`, `218-220`. [P]
  - [ ] **Missing `RecordDlssFound`** after Luma's `nvngx_dlss.dll` deploy (`VM\MainViewModel.Install.Luma.cs:1098-1105`) and RTX Encore's NR deploy. [R]
  - [ ] **Plain `Path.Combine` on record or remote values** (rule: `FileHelper.SafeCombine`): `S\DxvkService.Install.cs:399/420/439/654/671`, `S\LumaService.cs:1476`, `S\DlssStreamlineService.Swap.cs:49/74/99/149`. [R]
  - [ ] **Staged files written in place and accepted by `File.Exists`.** OptiScaler clears staging before the replacement download works (`S\OptiScalerService.Install.cs:131-139`, `1232-1244`; see `Logs\session_2026-11-06_12-35-03.txt` lines 2528-2566). Also `S\AddonPackService.cs:1160-1201`, `1256-1259`, `775-780`; `S\Renodx5AddonService.cs:639-641`, `1154-1156`; `S\RtxEncoreService.cs:311-313`; `S\DlssNrCostScalerService.cs:99-102`; `S\DlssStreamlineService.Swap.cs:480`, `535`; `S\UltimateAsiLoaderService.cs:164-169`. Fix: stage to temp, validate, then move, as `UpliftService.StageAsync` and `Rtx40MfgService` already do. [R]

- [ ] **2.14 Structural follow-ups (after the individual fixes; ask the user before starting).**
  - One "owned file" API (deploy, replace, release) used by every component, replacing the two parallel ownership systems.
  - One map of who holds each proxy DLL name (extend `GetRhiDllOwners`).
  - One OptiScaler deploy routine shared by install and update that returns the exact files written.
  - A per-game operation lock (`IsInstalling` flags are set but never checked, so a silent auto-update and a user install can run on the same game).
  - Installs that swallow failures and return void (DXVK, Renodx5, Cost Scaler, `SentinelBackup`) should return a result.

---

## Phase 3: normal launch behaves differently from Refresh

Background: on a normal launch the cards the user keeps are the cache cards from `LoadCacheAndBuildCardsAsync`. `MergeCardsAsync` (`VM\MainViewModel.BackgroundScan.cs:755-845`) copies a hand-written list of properties from the fresh `BuildCards` card onto them. Anything not in that list, and that the cache phase can't compute, is wrong until Refresh. The cache phase runs before the manifest is fetched: it must use the local `cachedManifest`, never `_manifest` (null there).

- [ ] **3.1 DXVK global variant is never loaded from settings on a normal launch. [V]**
  - `S\DxvkService.cs:71` defaults `_selectedVariant` to `LiliumHdr`. The only startup sync from `Settings.DxvkVariant` is `VM\MainViewModel.Init.cs:491`, in the full-scan branch; the dxvk task at `VM\MainViewModel.BackgroundScan.cs:153` has none (`.claude/rules/rhi-dxvk.md` says it must). `ResolveDxvkVariant` (`VM\MainViewModel.Settings.cs:190`) falls back to the service value.
  - Scenario: saved global variant is Development or Stable and a game has a DXVK install with no per-game override. On a normal launch it resolves to Lilium HDR; Update All or the silent updater deploys Lilium DLLs over it.
  - Fix: set `SelectedVariant` once at startup before either branch, or make `ResolveDxvkVariant` read the setting.

- [ ] **3.2 The merge drops manifest-driven flags. [V for the missing properties and the null `_manifest` call]**
  - Not in the merge list: `IsDxvkBlacklisted`, `HasDxvkApiOverride`, `IsDofFixEligible`, `DofFixStatus`, `DofFixInstalledVersion`, `EmulatorAddonNames`, `IsREEngineGame`. `VM\MainViewModel.CacheLoad.cs:1120` calls `ApplyManifestCardOverrides(_manifest, _allCards)` with a null manifest; `CacheLoad.cs:650` reads `_manifest` for emulator games; `CacheLoad.cs:820` computes DOF Fix eligibility before the skip/force lists are set.
  - Consequences on a normal launch: the DXVK toggle is offered for anti-cheat games; `dofFixForceGames` get no DOF Fix row; Ryubing's update check finds 0 cards; a game that becomes RE Engine has no RE Framework row until next launch.
  - Fix: add the missing properties to the merge block; pass `cachedManifest` at `CacheLoad.cs:1120` and use it at `:650`; call `SetSkipGames`/`SetForceGames` from the cached manifest before the cache loop.
  - Also: `DlssDetection` is merged only when the fresh value is non-null (`BackgroundScan.cs:838`), so DLSS removed by a game update stays on the card. Clear it when the game was actually scanned and has none.

- [ ] **3.3 Game Pass ReShade auto-reinstall never fires on a normal launch. [V]**
  - `_pendingRsReinstall` is filled in `VM\MainViewModel.BuildCards.cs:639` and consumed only in `VM\MainViewModel.Init.cs:782-808`.
  - Fix: move that block into a helper and call it after `MergeCardsAsync` too.

- [ ] **3.4 Cache phase ignores manifest API overrides and can rename ReShade on disk. [R; needs specific data]**
  - With `_manifest` null, the UE5 strip, legacy cap and PCGW corrections apply to manifest-overridden games (`VM\MainViewModel.CacheLoad.cs:890-962`). Scenario: manifest says DX9, PCGW lists no DX9; the cache card becomes DX12 and `ReconcileDefaultNaming` (`CacheLoad.cs:1123`) renames `d3d9.dll` to `dxgi.dll`. A missing `game_api_cache.json` has the same effect for any DX9/OpenGL game.
  - Fix: use `cachedManifest` there; skip reconcile in the cache phase when `DetectedApis` is empty.

- [ ] **3.5 DLL rename and reconcile run on stale cards, then the merge undoes their card state. [R]**
  - `ApplyManifestDllRenames` and `ReconcileDefaultNaming` iterate `_allCards` (`VM\MainViewModel.GameMatching.cs:71`, `129`) and run before `MergeCardsAsync` (`BackgroundScan.cs:451-457`). After a new `dllNameOverrides` entry, the merge assigns the fresh card's old `RsRecord` / `RsInstalledFile` / `DllOverrideEnabled = false`. Cards added by the merge are never reconciled.
  - Fix: run both after the merge.

- [ ] **3.6 Steps that exist in only one init path. [R]**
  - Only in the background-scan path (missing on first launch or with a deleted library file): heartbeat timer, DLSS auto-update, silent auto-update trigger, Uplift, MFG/Encore reconciles, Cost Scaler deploy, Streamline redeploy (`BackgroundScan.cs:471-507`, `597-608`); NR Cost Scaler, Universal MFG, DLSSG 20/30 and ASI Loader staging tasks (`BackgroundScan.cs:170-218`).
  - Only in the full-scan path (don't run for an upgrading user until Refresh): `MigrateNightlyStagingFolder`, `MigrateDxvkStagingFolder`, `CleanOrphanedOptiScalerSentinels`, DLSS5Feeder cleanup (`Init.cs:386-426`, `499`); RenoDX5 versions fetch and `UpdateFoveatedGamesAsync` (`Init.cs:515`).
  - Fix: put each in both paths (CLAUDE.md "Dual init paths").

- [ ] **3.7 Smaller parity gaps. [R]**
  - `PcgwUrl` resolved after the loop is set on the discarded fresh card (`BuildCards.cs:1689-1711`).
  - `lumaRenodxCompat` is read only in the cache phase (case-sensitive `List.Contains`, `CacheLoad.cs:629`) and `AddManualGame` (`Install.cs:764`); the merge overwrites it with `f.LumaMod != null`. The field is effectively dead: decide whether to wire it in or remove it.
  - Display Commander: BuildCards checks the default addon file, then legacy, then the aux record; CacheLoad checks the aux record only.

---

## Phase 4: manifest (the user's main interest)

Goal: less manual manifest work. Three parts: delete what is already dead, fix the code so classes of entries stop being needed, and automate the hand-maintained version lists.

### 4.1 Entries that can be deleted now (no code change)

Before each deletion, confirm it against the current data (the lists came from scripts run on 10 Nov), and check that old app versions don't depend on the entry. Then follow the "Push manifest" workflow in CLAUDE.md.

- [ ] **`_engineOverrides` and `_engineOverrides_disabled`** (120 entries, ~5 KB per fetch). Unread: the model property is `engineOverrides` (`App\Models\RemoteManifest.cs:131`); user logs show "+0 engine overrides". Also remove what it left behind: `ResolveEngineOverride` (`VM\MainViewModel.GameMatching.cs:745-772`), `_manifestEngineOverrides` (`VM\MainViewModel.cs:686`), `tools/RHI-ManifestEditor/RHI.ManifestEditor/Services/Checks.cs:20`, `tools/game-db/seed_from_manifest.py:116`. Check each is truly unused first.
- [ ] **`blacklist`** (20):
  - Covered by the `Battle.net.` prefix (11): `Battle.net.15492`, `Battle.net.15561`, `Battle.net.15688`, `Battle.net.17477`, `Battle.net.16014`, `Battle.net.16089`, `Battle.net.16111`, `Battle.net.16125`, `Battle.net.17296`, `Battle.net.17359`, `Battle.net.exe`.
  - Duplicates of the hardcoded `permanentExclusions` in `S\GameInitializationService.cs:96-106` (8): Apple Music, DSX, PlayStation®VR2 App, QuickPasta, Steamworks Common Redistributables, SteamVR, Telegram Desktop, Windows.
  - "Skyrim Creation Kit" is listed twice (remove one).
- [ ] **`launchExeOverrides`**: "Skyrim Creation Kit" (the game is blacklisted, so it never has a card).
- [ ] **`nativeHdrGames`** (12): already hardcoded in `VM\MainViewModel.GameMatching.cs:257-269` (10): Alone in the Dark, Avowed, Hell is Us, Lies of P, Lost Soul Aside, Mafia: The Old Country, Marvel's Midnight Suns, Mortal Kombat 1, Returnal, Still Wakes the Deep. Implied by `ueExtendedCompatibility` (2): Beast of Reincarnation, Black Myth: Wukong.
- [ ] **`wikiNameOverrides`**, redundant (the matcher gives the same result) (9): `Borderlands 2`, `Dying Light`, `HELLDIVERS™ 2`, `ELDEN RING`, `ELDEN RING NIGHTREIGN`, `Grand Theft Auto V Enhanced`, `Persona 5 Royal`, `Terraria`, `Trails in the Sky 1st Chapter`.
- [ ] **`wikiNameOverrides`**, target not in the database or Luma (8). Decide per entry; don't blanket-delete:
  - `Assassin's Creed`: **fix, don't delete** (see 4.2).
  - `Far Cry 2`, `Far Cry 3`: the database split them into `Far Cry 2 (dx10)` and `Far Cry 3 (dx11)`; they only work through `snapshotOverrides`.
  - The three GTA Definitive Edition entries: the Trilogy row is on the wiki but not in the database.
  - `EVERSPACE™ 2`, `Minecraft for Windows`.
- [ ] **`lumaNameOverrides`** redundant (5): `Borderlands GOTY Enhanced` (key equals value), `Batman™: Arkham Knight`, `LEGO® City Undercover`, `LEGO City Undercover`, `Mafia III: Definitive Edition`. Eight more duplicate a `wikiNameOverrides` key with the same target; keep one of each pair.
- [ ] **`wikiStatusOverrides`** (4): all no-ops under the `DbOnly` default (three names aren't in the named database, the fourth is already "Done").
- [ ] **`snapshotOverrides`** identical to the database URL (4): `FINAL FANTASY VII REMAKE INTERGRADE`, `RoboCop: Rogue City`, `Routine`, `The Witcher 3: Wild Hunt - Game of the Year Edition`.
- [ ] **`nexusUrlOverrides`** redundant (4): `Halo: Campaign Evolved`, `CONTROL Resonant`, `The Witcher 3: Wild Hunt`, `Kingdoms of Amalur: Reckoning™`.
- [ ] **`pcgwUrlOverrides`**: 16 of 18 resolve from `pcgw_data.json` without the manifest entry. Keep `Alan Wake 2` (until its central `name_overrides` is fixed to "Alan Wake II" in PCGW-Editor), `Far Cry® 2: Fortune's Edition` (its central key is mis-encoded, see 4.2) and `DOOM (DOS)` (carries a `#The_Ultimate_Doom` anchor). Re-run the check to get the exact 16.
- [ ] **`authorDisplayNames`**: `oopydoopy` duplicates the hardcoded dictionary (`VM\GameCardViewModel.UI.cs:298-303`).
- [ ] **`authorOverrides`**: `GreedFall: The Dying World` (the database already says RankFTW).
- [ ] **`dofFixForceGames`**: `Avowed` (its engine hint 5.3.2 already makes it eligible, `S\DofFixService.cs:219-240`).
- [ ] **`dllNameOverrides`**: `Guild Wars 2®` (`GetManifestDllNames` already normalises).
- [ ] **`sixtyFourBitGames`**: `Control`, `Control Ultimate Edition`, `Ghost Song` (now detected as x64 since the 9 Nov exe-exclusion fix; verified locally for the last two); `AI The Somnium Files` and `Watch_Dogs® 2` are variants the set matcher already catches.
- [ ] **`engineHintOverrides`**: `Hogwarts Legacy`, `SILENT HILL: Townfall`, `Kaku Ancient Seal` equal what detection reads locally (4.27.2, 5.6.1, 4.27.2). They may still be needed for Game Pass copies, where the exe is unreadable: ask the user.
- [ ] **Unity trio, test one game first** (18 entries): `Death's Door`, `Descenders`, `Haste`, `Outer Wilds`, `Spark the Electric Jester 3`, `XF Extreme Formula` each have a `wikiUnlinks`, a `snapshotOverrides` (generic Unity URL) and an `authorOverrides` (Voosh) entry, and all six are in `RenoDXdb-unity.json`. The generic Unity fallback should already give that URL and author.
- [ ] **`wikiUnlinks` with no match today** (10): `ARC Raiders`, `Call of Duty®: Modern Warfare®`, `Dead by Daylight`, `Denshattack!`, `Doom Eternal`, `Elin`, `L.A. Noire`, `MIO: Memories in Orbit`, `Stardew Valley`, `Grand Theft Auto V Legacy`. Keep only if they are meant to block the generic engine fallback: ask the user.

### 4.2 Entries that are wrong now

- [ ] **`wikiNameOverrides."Assassin's Creed"`** points at `Assassin's Creed™: Director's Cut Edition` [V], but the database name reportedly now ends in "(DX10)". The override silently falls through and the prefix rule picks `Assassin's Creed® Rogue`; the `snapshotOverrides` entry then writes the AC1 32-bit addon URL into Rogue's shared row (see 5.6). Fix: check the exact name in `RenoDXdb.json` (rhi-repo) and update the value. Also add a log line when an override target is not found (`S\GameDetectionService.cs` ~505-510).
- [ ] **"Zero Parades"** (`sixtyFourBitGames`, `graphicsApiOverrides`, `renodxIniOverrides`): the user's library detects "ZERO PARADES: Director's Cut", which matches none of the three.
- [ ] **`dxvkBlacklist."Rainbow Six Siege"`**: the DXVK check is an exact set lookup and the detected name is "Tom Clancy's Rainbow Six Siege".
- [ ] **`snapshotOverrides."Days Gone"`**: the database has `oopydoopy.github.io/…/renodx-daysgone.addon64`, the manifest has the `github.com/OopyDoopy/…/releases/download/snapshot/` URL. One is stale.
- [ ] **`snapshotOverrides."CODE VEIN II"`**: the game is also in `nativeHdrGames`; the field reference says a snapshot override blocks UE-Extended. Check intent.
- [ ] **`optiScalerWikiNames."CODE VEIN II"`** → "Code Vein 2" was not found on the standard compatibility list (the FSR4 list wasn't checked).
- [ ] **`nexusUrlOverrides."Tunguska: The Visitation - Final Cut"`**: Nexus now calls it "Enhanced Edition".
- [ ] **`pcgw_data.json` is partly mis-encoded.** 388 game titles and 7 `name_overrides` keys are UTF-8 read as an OEM code page, e.g. `BatmanÔäó: Arkham Knight`, `Stellar BladeÔäó`, `Far Cry┬« 2: Fortune's Edition`, `AssassinÔÇÖs Creed┬« III`. Those keys can never match. `tools/pcgw-fetch/fetch_pcgw_data.py` and `PcgwDataService.cs` both use UTF-8 explicitly, so find where the corruption enters (probably an earlier read being round-tripped). Fix in the tool, never by hand-editing the JSON.
- [ ] **`check-manifest.py` checks dictionary keys only**, so duplicate items in list fields pass. Add a duplicate check for lists.

### 4.3 Code changes that retire classes of entries

Do these one at a time, in this order (cheapest and safest first). After each, list the manifest entries it makes redundant and ask the user before removing them.

- [ ] **A. One normalised lookup for every per-game manifest field.** Small, low risk.
  - Add a helper (exact, then ™®© stripped, then apostrophe/colon folding and case-fold), as `GetManifestDllNames` already does at `VM\MainViewModel.GameMatching.cs:779-793`.
  - Use it at: `VM\MainViewModel.BuildCards.cs:33`, `:179`, `:811`, `:1420`, `:1459`, `:1485`; `VM\MainViewModel.CacheLoad.cs:473`, `:529`, `:795`, `:814`; `VM\MainViewModel.GameMatching.cs:350`, `:558`; `App\InstallEventHandler.cs:271`, `:380` (`pdUpscalerGames`, case-sensitive today); `S\GameInitializationService.cs:348`; the blacklist filter at `VM\MainViewModel.Init.cs:744` and `VM\MainViewModel.BackgroundScan.cs:409`; the `lumaRenodxCompat` `List.Contains` at `CacheLoad.cs:629`.
  - Add `PdUpscalerGames`, `EngineHintOverrides`, `EngineIniPathOverrides`, `LaunchExeOverrides`, `SplitGames`, `ProfileNameOverrides` to `ManifestService.Normalize()`. **Remove case-only key pairs in the same push** (the two Dragon Quest VII keys in `engineHintOverrides`, the Resident Evil 7 spellings in `pdUpscalerGames`), or `Normalize()` throws and every user falls back to the cached manifest. Old app versions don't have the new lookup, so keep both spellings until the new version has shipped.
  - Don't strip all punctuation: that would merge pairs like `3D Mark` / `3DMark`.
  - Retires about 20 variant entries (32 games currently have two or three spellings across the manifest) and ends the "add both spellings" rule. Known variant sets: `installPathOverrides` Anno 117 (2), Baldur's Gate 3 (2), Elden Ring Nightreign (2), Watch_Dogs 2 (3), `Borderlands® 4`/`Borderlands 4`; `splitGames` Mass Effect LE ™ and COD MWII; `thirtyTwoBitGames` "Tomb Raider"/"Tomb Raider (2013)"; `lumaRenodxCompat` `Mass Effect Andromeda`/`Mass Effect: Andromeda`; Baldur's Gate 3 twice in the info fields.

- [ ] **B. Page the Lyall repo list.** Small, no risk. `S\UltrawideFixService.cs:23` requests `?limit=50` with no paging [V]; Codeberg reports 76. Restores 26 missing fix links and retires the `DQ7RFix` and `LEGOBatmanLotDKFix` entries in `uwFixUrlOverrides`.

- [ ] **C. Fix Battle.net detection.** Small, low risk. `S\GameDetectionService.Platform.cs:490-504` ("Method 3") lists the launcher's own version folders as games (user logs show cards for `C:\Program Files (x86)\Battle.net\Battle.net.17438`). Stop enumerating children of the `Battle.net` launcher folder, or skip names starting with `Battle.net`.

- [ ] **D. Derive boilerplate text.** Small to medium, low risk. Keep the manifest able to override or suppress each.
  - 21 `installWarnings` are the identical "unreleased / work-in-progress Luma mod" text. Derive from "the Luma mod comes from the releases feed and is not in the Completed table" (`S\LumaService.cs` releases merge ~1030-1175; `CheckInstallWarningAsync` in `VM\MainViewModel.Install.Luma.cs`).
  - 8 `gameNotes` are identical "Uses UE-Extended" (7 of those games are in `nativeHdrGames`). Derive from the card's own UE-Extended flag in `BuildNotes` / `S\AddonInfoResolver.cs`. The 13 "Native HDR can be enabled using Ultra+" notes are not derivable.
  - `lumaRequiresDgVoodoo`: the Luma wiki's Special Notes cell says "Requires dgVoodoo" for Medal of Honor: Airborne and Vanquish; derive from it and keep the manifest as a supplement.
  - `forceExternalOnly` (2): treat "database row has `nexusUrl` and no `snapshotUrl`" as external-only.

- [ ] **E. Pick the game exe by score, not size.** Small to medium, low risk.
  - `S\PeHeaderService.cs:124-131` (`FindGameExe`) takes the largest exe [V]. Hades II reads as 32-bit from `Ship\F10.exe` (9 MB) instead of `Hades2.exe` (6 MB, x64). Rank with the existing `CustomGameExeScorer`.
  - `S\GameDetectionService.cs:380-385` (`FindUEBinariesFolder`) takes the first candidate with a Shipping exe: SILENT HILL: Townfall resolves to `BonusApp\Townfall_BonusContent\Binaries\Win64`, Of Ash and Steel to the wrong project. Break ties by launch-exe or folder name and skip `BonusApp`-style folders.
  - Add a log line whenever a manifest bitness/API/path override equals the detected value, so stale entries can be pruned from logs.
  - **Do not** swap `FindShallowExeFolder` (`S\GameDetectionService.cs:466-486`) for the scorer wholesale. On the user's library the raw scorer matched 107 of 115 but moved 5 games that have no override (three Source games to `bin\glview.exe`, GTA to `WINO`, Quake to `rerelease`).
  - The bitness cache is only cleared on Full Refresh (`VM\MainViewModel.Init.cs:158`), and `BuildCards.cs:202` reuses any cached value except 32-bit: bump a cache version with this change.

- [ ] **F. PCGW lookup by Steam AppID first.** Medium effort, medium risk (a wrong API means the wrong ReShade DLL name).
  - `S\PcgwService.cs:67-75` tries the name before the AppID [V]. Reported: `SteamAppId` is never passed. Call sites: `VM\MainViewModel.BuildCards.cs:89`, `:907`, `:1433`; `VM\MainViewModel.CacheLoad.cs:807`, `:916`. The overload exists (`PcgwService.cs:883`). Example: "Resident Evil 4" (appid 2050650) resolves to the 2005 page (dx9) instead of "Resident Evil 4 (2023)" (dx12, RE Engine).
  - The AppID index keeps only the last title per AppID (`PcgwService.cs` `LoadFromJson` ~316): `Doom + Doom II` and `Doom (1993)` share 2280. Handle multiple titles per id.
  - At `BuildCards.cs:905-966` add "PCGW lists exactly one API, so use it". Measured: 22 of the 47 `graphicsApiOverrides` are games where PCGW lists exactly the manifest's API.
  - When the engine hint is plain "Unreal Engine", take PCGW's major version; the UE5 checks require "5." with a dot today (`VM\MainViewModel.GameMatching.cs:371`, `:514`, `:616`; `BuildCards.cs:859`). All 24 Unreal `engineHintOverrides` found in PCGW agree on the major. Keep manifest entries only where the minor matters (DOF Fix 5.7+).
  - `engineIniPathOverrides`: PCGW `config_path` already matches the manifest value for 8 entries (`BuildCards.cs:972-999`); test each before removing.
  - The game API cache stores manifest-forced values (`BuildCards.cs:899`) and is returned before scanning (`GameMatching.cs:398`), cleared only on Refresh (`Init.cs:267`). Bump a cache version, or don't cache forced values.
  - Expected: about 20-25 `graphicsApiOverrides`, up to 20 `engineHintOverrides`, `dofFixForceGames` Clair Obscur.

- [ ] **G. Stricter mod matcher.** Medium effort, medium risk (a wrong match installs the wrong mod). **Ask the user before starting; ship with tests.**
  - Today (`S\GameDetectionService.cs` ~489-557): override, exact `NormalizeName`, then "detected name contains mod name", then "mod name starts with detected name" unless the remainder matches `^[ivxlcdm0-9]+$` (line 543 [V]). That regex treats `dx10`/`dx11`/`dx12` as sequel numbers, so `Far Cry 2` can never match `Far Cry 2 (dx10)` and `Teardown` can't match `Teardown (dx12)`; and "Grand Theft Auto" matches `Grand Theft Auto V : Enhanced` (remainder "venhanced").
  - Measured against 49,183 PCGW Steam titles: 329 substring matches today (the mod "Journey" attracts 131 titles; "KIN" → Kingdom Come: Deliverance II; "Hades II" → Hades). A prototype strict matcher gives 49 non-exact matches.
  - Proposed rules: strip parentheticals on both sides, map roman to arabic numerals, drop a leading "The", strip edition words **from the detected name only**, accept only a unique match. Stripping edition words from the database side produced wrong matches in the prototype (Crysis → Crysis Remastered, Oblivion → Oblivion Remastered, Skyrim → Special Edition, Dead Space (2023) → Dead Space).
  - `Resident Evil 4` → Resident Evil 4 Remake would stop matching and need an alias.
  - Add a table-driven test in `RenoDXCommander.Tests\CoreLogicTests.cs` built from the current overrides and unlinks, and diff old against new over the PCGW titles before release.
  - Reuse for Nexus (`S\NexusModsService.cs` `ResolveUrl`), the OptiScaler wiki (`S\AddonInfoResolver.cs:380`: look up via `NormalizeName`, then parenthetical strip when unique; 0 collisions in 720 wiki names) and Luma (`VM\MainViewModel.Install.Luma.cs:593-664`).
  - Expected: 18 of the 20 false-match `wikiUnlinks` (Dead Island and Grand Theft Auto V still need theirs), 7 `wikiNameOverrides`, about 10 `nexusUrlOverrides`, 18-19 `optiScalerWikiNames`.
  - Note: since v2.7.2 the mod source default is `DbOnly` (`VM\SettingsViewModel.cs:98`), so `wikiNameOverrides` map onto names in `RenoDXdb.json`, not the wiki. `rhi-knowledge.md` ~line 1187 still says `WikiOnly`: correct it.

- [ ] **H. Read Steam's `appinfo.vdf`.** Medium effort, medium risk. **Ask the user before starting.**
  - `<Steam>\appcache\appinfo.vdf` (binary, v29 format). Hook into `FindSteamGames` (`S\GameDetectionService.Steam.cs:24-38`) and `DetectEngineAndPathCore` (`S\GameDetectionService.cs:216-264`).
  - `common/type`: skip Tool and Application (Steamworks Common Redistributables, Lossless Scaling, 3DMark, Creation Kit). Retires an estimated 12-15 blacklist entries.
  - `config/launch/*/executable`: gives the right folder for 007 First Light (`Retail`), Gothic II (`system`), Watch_Dogs (`bin`), Borderlands 4, and picks the right Unreal project for Townfall and Of Ash and Steel. It doesn't help when Steam launches a launcher (Witcher 3, MGS4, Peace Walker, Project Motor Racing). Estimated 20-30 `installPathOverrides`, extrapolated from 6 of 12 local cases.
  - A folder change moves where mods install: pair it with the existing install-folder migration (`InstallFolderMigrationService`).

- [ ] **I. DLSS scan depth.** Small, low risk. Bound the scan (folder or time cap) and allow deeper recursion under a `Plugins` segment (`S\DlssStreamlineService.Detection.cs:352`, depth 8 today). Retires `dlssSkipGames` (10; the app already auto-skips after 3 empty scans, `S\DlssStreamlineService.cs:298`) and `dlssPathOverrides` / `streamlinePathOverrides` (2 + 2).

- [ ] **J. Aliases in the database (rhi-repo; ask the user).** Add `aliases` to named rows in `RenoDXdb.json` (model `S\RenoDXDbService.cs:247-258`, plus RenoDXdb-Editor). About 25 duplicate rows exist today as hand-made aliases (`Devil May Cry 5` / `Devil May Cry V`, `Watch Dogs` / `Watch_Dogs`). Moves the 14 bundle overrides and 2 folder abbreviations next to the mod they describe.

- [ ] **K. Suspected, needs a user log first.** The comment at `S\GameDetectionService.Xbox.cs:34` says optional packages are skipped, but the code checks only framework, resource and bundle. If Xbox DLC packs are optional packages, that is why about 28 DLC/skin entries are in the blacklist. Get one user log with those names and paths before changing anything.

### 4.4 Automation and safety net

- [ ] **Manifest audit script** (`tools/audit-manifest.py`): reports override targets that don't exist, entries the matcher already resolves, keys duplicated across fields, database rows sharing an addon, and duplicate list items. The reviewers' prototypes are in the scratchpad if it still exists (`C:\Users\Mark\AppData\Local\Temp\claude\1e0d5c6d5834e2c4\975a0dca-87d7-4e02-8c2a-eb25b91ccf13\scratchpad\`: `common.py`, `a1.py`-`a5.py`, `sim*.py`, `appinfo.py`, `schema.py`); it is a temp folder, so rewrite if gone. Run it from the Manifest Editor's Push step.
- [ ] **Real-manifest test.** Copy the `OsPresetManifestTests` pattern: read repo-root `manifest.json`, deserialise with the real options, call `ManifestService.Normalize`, assert non-null and a few counts; same for `dlss_manifest.json` with `DlssManifestData`. `ManifestService.JsonOptions` is `private static` (line 35): make it `internal` or add an `internal static Parse(string json)`. This is the only guard against a wrong value type failing the manifest for every user (today they silently fall back to the cached copy, `S\ManifestService.cs:77-101`).
- [ ] **Extend `.github/workflows/manifest-check.yml`** to trigger on `optiscaler_presets.json` and check it is valid JSON.
- [ ] **Version lists (ask the user before adding workflows).** A scheduled Action that opens a PR for new `dgVoodooVersions` (releases API for `dege-diosg/dgVoodoo2`; diff the zip's file list against the previous one; never auto-merge) and `legacyReShadeAvailable`. It must splice text, not re-serialise, to keep CRLF and order.
- [ ] **`tools/upload-dlss-releases.ps1`** writes the `dlss_manifest.json` line after each `gh release create`. Three entries need a label kept: `streamline-2.14.16.0-v2`, `dlssnr-310.8.Lecram`, `dlssnr-310.8.SF-v2`.
- [ ] **`addonPacks.reshade-hdr-metadata`** is pinned to `2025.08.18`; switch to `releaseApiUrl` like its neighbours.
- [ ] **PCGW fetch on a daily Action** (ask the user). Today PCGW-Editor's timer does it only while the editor runs on the user's PC. Needs `PCGW_USERNAME` / `PCGW_PASSWORD` repo secrets and `python tools/pcgw-fetch/fetch_pcgw_data.py --update`. Check the Python output still matches the C# port (`PcgwFetchService.cs`) first.
- [ ] **Match diagnostics.** `MatchGame` logs "No match" for every unmatched game on every scan and nothing for substring matches. Log one line per non-exact match instead, so false matches show up in user logs.

---

## Phase 5: other bugs

- [ ] **5.1 GitHub rate-limit flag never clears. [V]** `S\GitHubETagCache.cs:78`, `121`, `182`: `_rateLimited` is set on the first 403 with remaining=0 and nothing resets it. RHI sits in the tray for days; after one hit, every `api.github.com` call returns stale cache or null until restart and `CheckForUpdatesAsync` skips GitHub checks (`VM\MainViewModel.Update.Check.cs:76`). Adding a PAT doesn't help. Fix: store `X-RateLimit-Reset` and clear the flag after it; also clear it in `GitHubTokenHandler.SetToken`.

- [ ] **5.2 `GameJournalStore` can wipe all overlay notes and RHI playtime. [V]** `S\GameJournalStore.cs:26-48`: a read or parse error starts empty and the next `Save` overwrites the file. `Save()` (`:51-72`) also writes outside the lock to a fixed `.tmp`, and can overlap with `Task.Run(GameJournalStore.Save)` from `App\MainWindow.Overlay.cs:263`. Fix: route through `SafeJsonFile` / `SafeJsonFile.LockFor(path)`. `S\LaunchSessionStore.cs:119-136` has the same load-empty pattern (lower impact).

- [ ] **5.3 Other user-data stores that bypass `SafeJsonFile`. [R]** `S\ShaderProfileService.cs:23-63` (user shader profiles), `S\OsPresetService.cs` ~30-80 (the four user preset slots), `S\DlssPresetService.ProfileMatching.cs:209-236`. Plain `File.WriteAllText`; a corrupt file loads as empty and the next save writes that back.

- [ ] **5.4 Rename problems. [R]**
  - `S\GameNameService.cs:1087`: `_folderOverrides.TryGetValue(oldName, ...)` uses the plain name, but keys are reported to be `"Name|Store"` since v2.3.0. Change folder then rename: on the next scan the game is detected at its original path, the rename doesn't apply, and every per-game setting is orphaned under the new name. Verify the key format first.
  - `GameNameService.cs:1190-1212`: `RenameGame` only re-keys `card.InstalledRecord`, `card.RsRecord`, `card.LumaRecord`. RE Framework, DXVK, ReLimiter, Display Commander and OptiScaler records are looked up by `r.GameName == game.Name` (`VM\MainViewModel.BuildCards.cs:1153-1160`, `1296-1301`, `1315`), so they are lost after a rename.
  - `GameNameService.cs:1073`: the card is picked by name with no store, while `MigrateComposite*` moves every store's keys. Same game on two stores: renaming one affects both. [P]
  - `GameNameService.cs:1306-1315`: legacy name-only keys become `"New|"`, which no getter fallback reads. Low.

- [ ] **5.5 Reset Overrides gaps. [R]** Handler: `App\DetailPanelBuilder.Overrides.ShadersAddons.cs:469-658`.
  - Not cleared: `_hdrToggleOverrides`, `_resToggleOverrides` (no `Remove` anywhere; `App\MainWindow.Events.Settings.cs:1474-1483` always writes On/Off, so one click and the game never follows the global setting again; `rhi-ui.md` says Reset clears it), `_updateAllExcludedRef` (RE Framework stays excluded from Update All; also missing from the report, `App\GameReportEncoder.cs:389-393`).
  - Not cleared, possibly on purpose (ask the user): `_engineVersionOverrides`, `_rsIniLockedGames`, `_rsAsiGames`, `_perGameAddonSelection`.
  - Lines 636-644: `SetSrPreset/SetRrPreset/SetFgPreset(…, 0)` run NVAPI on the UI thread (four `DeletePreset` calls, each with a 5 s lock wait). Fix: `await Task.Run`.
  - Lines 571, 598: `HandleDxvkToggleAsync` and `InstallReShadeAsync` are both fire-and-forget on the same game folder. Await the DXVK uninstall before the ReShade reinstall. [P]
  - Line 593: on a Vulkan game the channel reset isn't propagated to the other Vulkan games. [P]

- [ ] **5.6 A snapshot override writes into a shared mod object. [V]** `VM\MainViewModel.BuildCards.cs:501` (inside `Parallel.ForEach`) and `VM\MainViewModel.Install.cs:144` do `effectiveMod.SnapshotUrl = override`; `S\GameInitializationService.cs:303` and `:382` null `card.Mod.SnapshotUrl` for `forceExternalOnly`. `effectiveMod` can be the single `genericUnreal` / `genericUnity` instance (`BuildCards.cs:24-25`) or a shared database row. `Hogwarts Legacy` and `ACE COMBAT™7` (override = UE-Extended URL) can rewrite the shared generic Unreal URL for other cards, depending on thread order. Fix: clone before mutating, as the block at `BuildCards.cs:463-476` already does.

- [ ] **5.7 `GameReportEncoder` gaps. [V for line 286]** `App\GameReportEncoder.cs:286`: `gns.ApiOverrides.ContainsKey(gameName)` on a dictionary reported to be composite-keyed, so the "Corrections" API line never appears. Lines 309, 395: `dxvkVariantOv` and `keepRsIniUpdated` are computed but never printed. HDR/RES toggle, engine version override, launch args and the OptiScaler/NR per-game settings are absent. `tools/decode_game_report.py` mirrors the encoder: keep it in step.

- [ ] **5.8 Source engine install can be left half-done. [R]** `VM\MainViewModel.Install.cs:950-957`, catch ~`:1141`. `_installer.InstallAsync` copies the addon and saves the record (`S\ModInstallService.cs:275`) before `ApplySourceSetupAsync`, which throws when DXVK or the second addon can't be downloaded (`VM\MainViewModel.SourceEngine.cs:148-149`, `192-194`). After a restart the game shows RenoDX installed with no DXVK proxy, no `[PROXY]` lines and no `-insecure`. Fix: stage DXVK and the extra addon before `InstallAsync`, or on failure call `_installer.Uninstall(record)` + `RemoveSourceSetup(card)`.

- [ ] **5.9 DXVK variant is shared mutable state. [R / race P]** Pass the variant as a parameter instead of swapping `_dxvkService.SelectedVariant` across awaits, and keep `HasUpdate` per variant.
  - Swap sites: `VM\MainViewModel.Update.Check.cs:368-375`, `VM\MainViewModel.Update.All.cs:138-164`, `VM\MainViewModel.Dxvk.cs:84-142` and `224-240` (the latter restores outside a `finally`), `VM\MainViewModel.SourceEngine.cs:136-139`, `S\DxvkService.Staging.cs:407`.
  - One `HasUpdate` flag serves all variants (`S\DxvkService.Install.cs:615-619`, `714`; `S\DxvkService.Staging.cs:79`): with Stable and Lilium both in use, cards can flip between Update Available and Installed without changing files.

- [ ] **5.10 Self-update and Nexus download. [R]**
  - `S\UpdateService.cs:218-224`, `269`: the installer is saved to `%TEMP%\RHI-Setup.exe` and shell-executed; the only check is byte count against Content-Length. In Admin Mode RHI is elevated, so a process that swaps the file runs elevated without a UAC prompt. Fix: download to a fresh random folder under `%LocalAppData%\RHI` and verify the release asset's SHA-256 (or Authenticode signer) before launch.
  - `S\NexusDownloadService.cs:217-221`: `Path.Combine(GetTempPath(), fileName)` with `fileName` from Content-Disposition. Fix: `Path.GetFileName` plus a per-download temp folder; check size against Content-Length. Line 166 logs the full `download_link` URL including the nxm `key`: redact it.

- [ ] **5.11 Overlay and UI. [R unless noted]**
  - **Controller scanning while a game runs** (on by default): `App\MainWindow.Overlay.cs:148-250`, `App\MainWindow.Controller.cs:220-235`, `S\PlayStationPad.cs:123-131`, `S\HidNative.cs:64-100`. With no XInput pad it ticks every 500 ms, polls XInput every 3 s, and every 10 s enumerates and opens every HID device, indefinitely. Stutter impact is unproven. Fix: scan on device arrival (WM_DEVICECHANGE via the existing window subclass) plus once at start; cache VID/PID per device path.
  - **One `NotifyAll()` queues about 145 component-row refreshes**: `App\DetailPanelBuilder.Components.cs:604-623`, `VM\GameCardViewModel.cs:231`. Fix: a `_rowsRefreshQueued` flag so one dispatcher callback serves the burst. **Read the "UI Freeze Investigation" section of `rhi-knowledge.md` first**; it says not to change panel code while that investigation is open. Ask the user.
  - **Overlay open does folder IO on the UI thread**: `App\Overlay\OverlayWindow.Screenshots.cs:29-48`, also from the 1.2 s watcher (`:300-320`). Fix: copy the Spotlight pattern (`App\DetailLayoutHost.Screenshots.cs:35`, `Task.Run`).
  - **About 45 `+= async (s, e) =>` lambdas have no try/catch**; an exception after an `await` there kills the process. Examples: `App\MainWindow.Events.Components.OptiScaler.IniLibrary.cs:183`, `App\DetailPanelBuilder.Overrides.NvidiaProfile.cs:544` (also NVAPI on the UI thread before its try), `App\DetailPanelBuilder.Overrides.cs:511`, `:693`, `App\SettingsHandler.OsCustomBuilds.cs:72-245`, `App\MainWindow.Events.cs:724`, `:740`, `App\Overlay\OverlayWindow.Achievements.cs:306`. Fix: a small `UIFactory.Safe(Func<Task>)` wrapper. [P]
  - **Tabs in-place cogs**: `ReopenOsCog` (`App\MainWindow.Events.Components.OptiScaler.cs:334-340`) re-invokes the handler 80 ms later; if the selection changed it can open for the wrong game. Fix: bail if `ViewModel.SelectedGame` is not `ctx.Card`. [P]
  - **Notes typed in the overlay just before exit are lost**: `OverlayWindow.Destroy()` (`App\Overlay\OverlayWindow.cs:217`) doesn't call `SaveNotes()`, and `DisposeRhiOverlay` (`App\MainWindow.Overlay.cs:279-283`) saves before destroying. Fix: `SaveNotes()` in `Destroy`, then save.
  - **Per-game overlay switch exists only in Spotlight** (`App\DetailLayoutHost.GameSettings.cs`; hidden elsewhere at `App\DetailLayoutHost.Header.cs:271-272`). Ask the user whether Classic/Tabs should get it.
  - **`RefreshHeroArt`** (`App\DetailLayoutHost.Spotlight.cs:254`) compares `GameName` only, but custom art is keyed `GameName|Store`.
  - **"Pause the game" leaves the game frozen if RHI is killed** (`App\Overlay\GamePauser.cs:76-82`). Opt-in. At least state it in the tooltip.

- [ ] **5.12 Concurrency in card building. [R / P]**
  - Unsynchronised shared collections inside `BuildCards`' `Parallel.ForEach`: `_ueExtendedGames.Add` (`BuildCards.cs:569`, `584`), `_pendingRsReinstall.Add` (`:639`), `SetDxvkVariantOverride` (`:1328`). `IsUe5OrHigher` (`VM\MainViewModel.GameMatching.cs:618`) enumerates the live `_allCards` from worker threads; an exception there aborts the scan. Fix: collect into a `ConcurrentBag` and apply after the loop; snapshot `_allCards`.
  - The merge can overwrite an install made during the scan (records read at `BackgroundScan.cs:418`). Fix: skip status/record assignments for a card whose `*IsInstalling` flag is set.
  - The auto-update retry queue holds card objects that Refresh orphans (`S\AutoUpdateService.cs:44`, `349`). Fix: queue `GameKey` and resolve at retry time.
  - Name-only keys where per-store keys are expected: `prevUpdateStatus` (`Init.cs:204`, `819`), `DllOverrideService._dllOverrides`, Nexus cached-update restore (`CacheLoad.cs:1143`).

- [ ] **5.13 NVIDIA. [P]** `S\DlssPresetService.ProfileMatching.cs:492-497`: if `CreateApplication` throws, the empty profile stays in the session and the next `Save()` persists it; later attempts fail on the duplicate name. Fix: delete the profile in the catch; expire cached "no profile" entries in `nvidia_profile_cache.json` when the driver version changes.

- [ ] **5.14 Smaller. [R]**
  - `S\GameNameService.cs:873-1006`: the settings save retries on `InvalidOperationException` rather than snapshotting under a lock (the code-health plan says snapshot). Low.
  - `S\SafeJsonFile.cs:64-72`: a missing `settings.json` is restored from `.bak`, so "delete settings.json" no longer resets the app. Decide whether that's wanted and tell users to delete the `.bak` too.
  - `S\CrashReporter.cs:279-300`, `407`: `SanitisePath` only rewrites `C:\Users\<name>\`; profiles on other drives pass through, and the crash report includes the machine name.
  - `VM\MainViewModel.Init.cs:64-67`: `GetGenericNote` reads name mappings from `settings.json` on disk, so database notes are not found through a manifest mapping. [P]
  - MGS4 Master Collection is cached as engine "Unreal" because of its UE5 companion app; the engine hint only changes the badge, so the generic Unreal mod may still be offered. [P]

---

## Phase 6: build, tests, tooling, performance

- [ ] **6.1 `publish.bat` fails silently.** No check after the publish fallback; it builds DropHelper and copies with `>nul` regardless; no `exit /b`. A failed build leaves the old `RHI.exe` in place and looks like success. Add `if errorlevel 1 exit /b 1` after the publish fallback and the DropHelper build, and print `RHI.exe`'s timestamp at the end. (See the memory note "Close RHI before publishing".)
- [ ] **6.2 CI.** Only `manifest-check.yml` runs today. Blocker: the ClrMD DLL is referenced by a hard-coded `%USERPROFILE%\.nuget\packages\microsoft.diagnostics.runtime\3.1.512801\...` path in the csproj and the last line of `publish.bat`. Suggested (untested): `<PackageDownload Include="Microsoft.Diagnostics.Runtime" Version="[3.1.512801]" />` and `$(NuGetPackageRoot)` in the `None Include` path; check it doesn't bring back the XAML-compiler crash the csproj comment describes. Then a `windows-latest` job running `dotnet test RenoDXCommander.Tests/RenoDXCommander.Tests.csproj -p:Platform=x64`. Ask the user before adding workflows.
- [ ] **6.3 SharpCompress 0.37.2 has a moderate advisory** (GHSA-6c8g-7p36-r338), and `<NoWarn>NU1902</NoWarn>` in the csproj hides that class of warning. Update, remove the NoWarn, re-test 7z/rar/zip extraction (it handles untrusted archives).
- [ ] **6.4 Tests to add** (all without UI):
  - Real manifest files through the real model (see 4.4).
  - `RenameGame` completeness: reflect over every per-game `Dictionary`/`HashSet` field in `GameNameService`, seed a key, rename, assert none still holds the old name.
  - Settings save/load round-trip including "clear the last entry, then reload" and the legacy `Name` → `Name|` migration.
  - Corrupt `settings.json` through the real load path.
  - `.original` sentinel: all three states, install then uninstall, plus `OptiScalerService.BackupOriginalIfExists`.
  - `ManifestService.Normalize` throws on a case collision; `FetchAsync` falls back without overwriting the cache.
  - Name matching table (required for 4.3 G).
  - Delete the tautological `NextViewLayout_*` tests (`CoreLogicTests.cs:75-99`), which test a local `switch` for the removed Compact layout, and the `ViewLayout.Compact` enum member if unused.
- [ ] **6.5 Startup performance. [R]**
  - JSON re-read per card: `LumaService.GetRecordByPath` (`S\LumaService.cs:991`), `_dxvkService.FindRecord` (`S\DxvkService.Tracking.cs:27`, called at `BuildCards.cs:1315`), `ReadUlInstalledVersion` / `ReadDcInstalledVersion`. Load each once before the loop.
  - `GetGenericNote` per card (`VM\MainViewModel.Init.cs:58-97`): parses `settings.json` and runs five regexes over every generic-note key. Precompute one normalised dictionary per build (estimated 0.5-2 s per scan).
  - `MatchesGameSet` per card (`VM\MainViewModel.GameMatching.cs:817`): pre-normalise the sets once in `ApplyManifest`.
  - `IsUe5OrHigher`: use the `EngineHint` already computed.
  - Don't move the cache phase off the UI thread: the code-health plan already decided against it.
- [ ] **6.6 Installer (`RHI Setup.iss`; the user runs it).** `PublishDir` default points at the build output, not the single-file publish folder, and hard-codes `net8.0`. `[Files] Source: "{#PublishDir}\*"` with `recursesubdirs` would ship `RHI.pdb` (1.25 MB) and a stale `Microsoft.Diagnostics.Runtime.dll.old`: exclude `*.pdb` and delete the `.old`. No .NET runtime check (the app is published `--self-contained false`). `[UninstallDelete] {localappdata}\RHI` wipes all user settings on uninstall: confirm that's intended. The real release reportedly uses a OneDrive copy of the script: ask the user which is current.
- [ ] **6.7 Doc drift.**
  - `lumaDefaultGames` (removed) is still in `.claude/rules/rhi-manifest.md:49`, `.claude/rules/rhi-reshade.md:72`, both `.kiro` copies, `docs/manifest-field-reference.md:114`, `rhi-knowledge.md:503`. `rhi-knowledge.md:499` still describes the removed `optiScalerDllOverrides`.
  - `docs/manifest-field-reference.md` is missing: `authorRoles`, `blacklistPrefixes`, `dgVoodooVersions`, `dlssPathOverrides`, `streamlinePathOverrides`, `dlssPresetsDev`, `engineIniFiles`, `featureFlags`, `lumaRequiresDgVoodoo`, `pcgwUrlCacheVersion`, `pcgwUseAppId`.
  - CLAUDE.md and the code-health plan say ~190 nullable warnings; it is 70 (83 warnings total: CS8604 ×54, CS8625 ×8, CS8602 ×6, CS8601 ×2, CS0219 ×10, CS0649, CA1416, PRI249). CLAUDE.md is the user's file: propose the change, don't make it unasked.
  - `.claude/rules/rhi-install.md` and `.kiro/steering/rhi-install.md` differ (the `.claude` copy has "Pushing component updates into game folders"; the `.kiro` copy has a "### 20/30 FG Unlock" heading). `rhi-ui.md` "Controller support" line differs.
  - `tools/pcgw-fetch/fetch_pcgw_data.py` header says the output goes to rhi-repo; it writes to this repo.
  - The code-health plan says the installer "installs the Windows App Runtime"; that was removed in `5e0952d`.
- [ ] **6.8 Repo hygiene.** `Properties/PublishProfiles/FolderProfile.pubxml.user` is tracked (and the profile still publishes to `…\Publish\RDXC`); `docs/RenoDXdb.json` and three handover docs are tracked but match `.gitignore`; `tools/*.md` in `.gitignore` hides any README at the tools root.
- [ ] **6.9 Maintainability (lowest priority; ask before starting any).**
  - Shared `ScreenshotGrid` for the overlay and Spotlight (~110 duplicated lines; also fixes the overlay IO item in 5.11).
  - Shared achievement row views (~150 lines).
  - One `RunRowActionAsync(button, busyText, Func<Task>, onDone)` for the 14 copies of the install/remove button handler (~400 lines; closes most of the unguarded async lambdas).
  - One `BuildSectionHeader(...)` for the 7 detail sections (~350 lines).
  - 27 source files are over 50 KB (rule: none). Largest: `MainWindow.Events.Settings.cs` (122 KB), `OptiScalerService.Install.cs` (122 KB), `MainViewModel.Settings.cs` (117 KB), `DetailPanelBuilder.NeuralRendering.SectionUi.cs` (114 KB, one ~1,920-line method), `MainViewModel.BuildCards.cs` (107 KB).

---

## Checked and found clean

Don't spend time re-auditing these:

- Every `ContentDialog` goes through `DialogService.ShowSafeAsync` / `ShowProgressAsync` or `CogDialog.ShowAsync`.
- No `GetRequiredService<MainWindow>()` outside `App.xaml.cs`; no `Button.ToolTip`; no `FileOpenPicker`.
- The overlay uses `RegisterHotKey` and an out-of-context WinEvent hook (no low-level keyboard hook), both removed on dispose.
- The per-game overlay switch follows the full override recipe.
- Detail-panel event subscriptions are unsubscribed or one-time.
- `GitHubTokenHandler` is api.github.com-only; the token isn't logged.
- Luma downloads are prefix-locked to `https://github.com/Filoppi/`; RenoDX installs accept only `.addon64/.addon32`, strip to a file name and check the MZ header.
- Zip extraction uses `SafeCombine` or `entry.Name` at the sites checked.
- `settings.json` and the five record stores named in the code-health plan do use `SafeJsonFile` with the lock.
- `manifest.json`, `dlss_manifest.json` and `optiscaler_presets.json` have no wrong value types, no unknown keys and no overlaps between mutually exclusive lists. `optiscaler_presets.json` matches `OsPresetService.BuiltInPresets`.
- Load, save and comparer handling are correct for every per-game collection in `GameNameService.cs`; the gaps are in rename side-effects, reset and the report (5.4, 5.5, 5.7).

## Not reviewed

- ReLimiter / Display Commander install and uninstall (`VM\MainViewModel.Install.Components.cs`), `S\AuxInstallService.GacSymlink.cs`, `S\InstallFolderMover.cs` and `S\InstallFolderMigrationService*.cs`, `S\SevenZipExtractor.cs`.
- The store scanners beyond the parts cited (`S\GameDetectionService.*`, `XboxPaths`, `SteamAppIdResolver`), `S\GameLibraryService.cs`.
- Most of `VM\MainViewModel.Settings.cs`, all `VM\GameCardViewModel.*`, `VM\MainViewModel.Update.cs` / `.Heartbeat` / `.NativeStack` / `.ReLimiterDc`.
- Vulkan override propagation, the `FindRecord` / null `InstalledRecord` pitfalls, version comparison in update detection.
- `S\PcgwService.cs` beyond lookup order, `OsForkService` / `OsCustomBuildService` / `OsReleaseService`, NVIDIA DRS session lifetime, `NvColorService`, `DigitalVibranceService`.
- The bodies of `App\MainWindow.Events.Settings.cs`, `App\DetailPanelBuilder.NeuralRendering.SectionUi.cs`, `App\ShaderPopupHelper.cs`; the `*Dialog.cs` files; `SetupWindow`; `DragDropHandler*`; the controller and Tabs/Spotlight chrome files.
- Anything that needs the app running: high DPI, theme, exclusive fullscreen, HDR, multi-monitor.
