# Custom DLLs (plan, 27 Nov 2026)

Let users put their own DLLs (mods RHI doesn't manage and never will, e.g. Special K) into games through RHI. RHI copies them under a name the user picks, keeps the game's own file, updates every copy when a newer file is dropped into the folder, and treats the name as its own so no other component overwrites it.

Decisions (user, 27 Nov 2026):
- Single files only for now (no packages or companion files).
- Several custom DLLs per game, managed from one Install / Remove button.
- A file in the folder is identified by its **file name** only. Dropping a new file with the same name = a new version.
- Names that an RHI component already holds are shown greyed out with the holder's name. ReShade proxy chaining (`[PROXY] ProxyLibrary=`) may come later.
- Lives in an **Extras row** with a ⚙, like every other component.

Model to copy: Custom ReShade (`CustomReShadeHashService`, folder `Custom\ReShade\`, knowledge doc "Custom ReShade Auto-Redeploy", audit S5 fix).

---

## 1. The folder

- `%LocalAppData%\RHI\Custom\DLLs\` (next to `DlssStreamlineService.RsCustomDir`). Created on first use, opened from the ⚙ and from Settings.
- Every `*.dll` at the top level is one custom DLL. Its file name is its id (`SpecialK64.dll`).
- Bitness comes from the DLL's PE header (`IPeHeaderService`). Only files matching the game's bitness are offered. A file that isn't a valid PE DLL is listed as unusable, with the reason.
- The display label is the file's ProductName + FileVersion when present (`Special K 25.11.3`), else the file name. That's for display only; identity stays the file name.
- Already covered by backups: `Custom\` is in `BackupArchive.DataFolders`. Check this when building.

## 2. Per-game state

Two places, both needed:

1. **`rhi_install.txt`** (`Models/RhiInstallManifest.cs`): one component per installed DLL, `CustomDll:<source file name>`, with the target file as its only file (`SetComponent` / `GetComponentFiles` / `RemoveComponent`). This is what makes RHI treat the file as its own, and what folder moves and other components' shared-file checks read.
2. **A record store** `custom_dll_installs.json` in `%LocalAppData%\RHI` (`SafeJsonFile`). Key = `GameKey.FromCard(name, source).ToKey()`, value = a list of `{ Source, InstalledAs, InstallPath, Sha256, InstalledUtc }`. `Sha256` is the hash of the copy RHI last put in the game. The update pass uses it to tell "RHI's old copy" from "the user replaced it by hand".

- **Rename**: add `RenameRecords(oldName, newName, installPath)` using `Services/RecordRename.cs` (S2 rule in `rhi-install.md`).
- **Copy Report**: list `customDlls` (source → installed as) in `GameReportEncoder.cs`.
- **Reset game settings**: does NOT touch it (it's an install, like the other Extras).

## 3. Install / change / remove

The button opens a **picker** (CogDialog in the usual hosts), not a straight install:

- One line per usable DLL in the folder: tick box, label, "Install as" dropdown.
- Ticked = should be in the game. Apply works out the difference:
  - newly ticked → install;
  - unticked → remove;
  - target name changed → remove under the old name, install under the new one.
- **Dropdown choices**:
  - Standard proxy names: `dxgi`, `d3d11`, `d3d12`, `d3d9`, `dinput8`, `version`, `winmm`, `dbghelp`, plus whatever list `UltimateAsiLoaderService` uses.
  - "Keep its own name".
  - Disabled with a reason: names in `MainViewModel.GetRhiDllOwners(card)` ("ReShade has dxgi.dll"), and names already picked by another ticked line.
  - A name held by the game's own file is allowed; that file is backed up.
- One source DLL at most once per game.
- Default name for a new line: the first free name in the list above.
- Anti-cheat: `DetailPanelBuilder.ConfirmAntiCheatAsync` before the first install into a `card.IsDxvkBlacklisted` game (key `CustomDll`).
- Refused (message, nothing changed): WindowsApps folders (`XboxPaths.IsWindowsApps`), folder missing / not writable, game running (`IsRunning`).

**Install one** (`CustomDllService.Install(card, source, targetName)`, background thread):
1. `AuxInstallService.SentinelBackup(target)` inside the try that also does the copy. It throws if the game's own file can't be moved aside (D3), and then nothing is copied.
2. Copy through `FileHelper` (atomic write).
3. `RhiInstallManifest.SetComponent(gameDir, "CustomDll:<source>", [target])`.
4. Record with the copy's SHA-256.
5. Log `[CustomDllService.Install] ...`.

**Remove one**: delete the file only if its hash is still the recorded one (otherwise leave it and say so), `SentinelRestore`, `RemoveComponent`, drop the record. Settings files that a mod names after its DLL (Special K: `dxgi.ini`, `d3d11.ini`) are **never** deleted (same rule as audit S6).

**✕ on the row** removes every custom DLL from that game after a confirmation (Cancel is the default).

## 4. Updates

`CustomDllService.CheckAndRedeploy()`, run:
- at launch, after the background scan, in both init paths;
- from a `FileSystemWatcher` on the folder (debounced about 2 s, `Created` / `Changed` / `Renamed`);
- in the periodic check.

For each record:
- Source file gone from the folder → leave the game alone, show "source missing" in the ⚙.
- The game's copy equals the folder's file → nothing to do.
- The game's copy is the recorded hash (RHI's old copy) → replace it, update the hash.
- The game's copy is something else → the user changed it by hand. Leave it, show "changed outside RHI" in the ⚙.
- The game's file is missing → the user removed it. Never re-create it; drop the record and the component.
- Skip hidden games and running games. A running game keeps its record so it's retried next time.

Log one summary line per pass (`[CustomDllService] Redeploy: N games checked, M updated`), not one line per game (audit P8).

## 5. Other components must leave it alone

- `MainViewModel.GetRhiDllOwners(card)`: add each installed custom DLL's target name as "Custom DLL (<source>)". That covers the Extras name pickers, ASI Loader and RE Framework's `dinput8.dll` check.
- `UltimateAsiLoaderService.FixedRhiDllOwners`: add them too, so ASI Loader never chains or overwrites them.
- **ReShade**: `AuxInstallService.OtherDllOwnerResolver` / `MainViewModel.OtherReShadeDllOwner` must report a custom DLL on ReShade's name. Then ReShade's install falls back to another name instead of the foreign-DLL prompt or overwriting. Test both: a custom DLL as `dxgi.dll` before ReShade is installed, and ReShade installed afterwards.
- **OptiScaler**: install must not overwrite a custom DLL's name. Its DLL-name picker uses `GetRhiDllOwners`, but check `InstallAsync`'s own target selection too.
- Detection: `OptiScalerService.OtherProxyProducts` already lists Special K. Check that a custom DLL is never detected as ReShade, OptiScaler, DXVK or Display Commander. ReShade detection by file name is the likely one.
- The foreign-DLL warning (`DialogService.Game.cs`) must not fire for a file listed under a `CustomDll:` component.
- Folder moves: add the component to `InstallFolderMigrationService.Inventory.cs` (target file + its `.original`).
- Tune presets: not in the first version.

## 6. UI

- Extras row "Custom DLLs" (new partial `DetailPanelBuilder.Extras.CustomDlls.cs`, under its own separator or "Other", shown for every game).
- Status: "Not installed" / "2 installed: SpecialK64.dll as dxgi.dll, foo.dll as winmm.dll". The Tabs / Spotlight list cards show the same text.
- Button: "Install" when none are installed, "Manage" when some are (both open the picker). ✕ visible when any is installed.
- ⚙ (CogDialog):
  - Open folder.
  - One line per installed DLL: label, installed as, status (up to date / update applied <time> / source missing / changed outside RHI), and a Remove button.
  - A note that RHI only copies the file and that the mod's own settings files are kept.
- Settings › Tools: "Open custom DLL folder" button next to the existing custom folders.
- Plain text, no new emojis; `x:Name` rule: everything built in C#.

## 7. Files (expected)

- `Services/CustomDllService.cs` (+ `ICustomDllService`, DI in `App.xaml.cs`), `.Update.cs` for the watcher / redeploy.
- `Models/CustomDllRecord.cs`.
- `DetailPanelBuilder.Extras.CustomDlls.cs` (row, picker, ⚙).
- Changes:
  - `MainViewModel.UalHooked.cs` (owners);
  - `UltimateAsiLoaderService.cs`;
  - the ReShade owner resolver;
  - `InstallFolderMigrationService.Inventory.cs`;
  - `GameReportEncoder.cs`;
  - both init paths and the periodic check;
  - `MainViewModel.RenameGame`.
- Tests (`CustomDllTests`):
  - install / remove with and without a game file (sentinel / real backup);
  - remove leaves a hand-replaced file;
  - redeploy replaces only RHI's copy;
  - a user-deleted copy isn't re-created;
  - two DLLs can't share a target name;
  - rename moves records;
  - a custom DLL's name appears in the owners list.

## 8. Docs and release

- Patch notes: `### New` line in the current version (and the beta-N file during betas).
- Rules: a "Custom DLLs" section in `.claude/rules/rhi-install.md` (and the `.kiro/steering` copy).
- `BackupArchive`: confirm `Custom\DLLs\` is included and `custom_dll_installs.json` is **not** (install records are never backed up).

## Later (not in the first version)

- Companion files / packages (a subfolder as one package).
- ReShade proxy chaining so a custom DLL can sit behind ReShade's `dxgi.dll`.
- Tune presets field.
