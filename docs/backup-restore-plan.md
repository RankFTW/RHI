# Backup and restore (plan, Nov 2026)

User request (Discord, temporo): "some kind of export feature so that if we want to reformat our PC our games can be up and kicking. Which ReShades and Reno and custom RHI settings we used, DLSS settings, all that stuff."

RankFTW's direction: keep a record of RHI-managed installs and settings for every game, write it to a file, and offer to import it on the first-run screen. Decisions (20 Nov 2026): restore pre-ticks everything; build all phases.

## What a reformat loses

| What | Where | Backed up as |
|---|---|---|
| RHI settings, per-game overrides (`"Game|Store"` keys), pins, collections, display modes, hidden / favourite / removed games, custom folders | `%LocalAppData%\RHI\settings.json` | copied as is |
| Manual games, favourites, hidden games, DXVK toggles | `game_library.json` | only those parts (the rest is a scan cache) |
| Presets and user data | `os_presets.json`, `rtx_encore_presets.json`, `shader_profiles.json`, `rtx_hdr_defaults.json`, `dldsr-captures.json`, `game_journal.json`, `ReLimiter_Presets\`, `inis\` (user ini templates), `Custom\` (custom ReShade builds, shaders), `optiscaler\saved-inis\`, `optiscaler-custom\` (custom OptiScaler builds), `artwork\custom\` + `artwork\sgdb\chosen.json` | copied as is |
| What RHI installed in each game | install records (`installed.json`, `aux_installed.json`, …) | **not copied** (they'd claim installs that aren't on disk); turned into a per-game install list instead |
| Game config files | game folders: `reshade.ini` + its preset, `relimiter.ini`, `DisplayCommander.ini`, `OptiScaler.ini`, `HSF.ini`, `dxvk.conf` | copied per game (1 MB cap per file) |
| NVIDIA driver profile settings | the driver | the existing NVIDIA profile export (`DlssPresetService.ExportProfiles`) |

Never in a backup: download caches and staged builds (RHI downloads them again), logs, scan caches, `github_api.txt`, `steamgriddb_api.txt`, other keys, `unlock*.txt`, window position.

## The file

`RHI-backup-yyyyMMdd-HHmm.rhibackup`, a zip:

- `backup.json`: format version, RHI version, created (UTC), game count, and per game: name, store, install path, 32-bit, the components RHI installed (ReShade, RenoDX, Luma, RE Framework, ReLimiter, Display Commander, OptiScaler, DXVK, DOF Fix) with their versions, and the config files it holds.
- `rhi\...`: RHI's data files from the table above.
- `games\<n>\...`: each game's config files (paths relative to the game's install folder).
- `nvidia.json`: the NVIDIA profile export (NVIDIA cards only; skipped if it fails).

Pure parts (manifest model, whitelist, zip writing / reading, game matching) live in `Services/Backup*.cs` with unit tests.

## Making a backup

Settings › Tools & Maintenance › **Backup and restore** card (C#, `SettingsHandler.Backup.cs`):

- Backup folder (Choose folder...). Pick one that survives a reformat: another drive, OneDrive, a USB stick.
- Automatic backups Off / On (default Off until a folder is chosen; choosing a folder turns it on). Runs after the first scan and with the periodic update check, at most once a day, and only when something changed since the last backup (hash of the data files + install list). Keeps the newest 5.
- Back up now, Restore from a backup..., Open folder; last backup time.

Settings: `BackupFolder`, `AutoBackup`, `LastBackupUtc`, `LastBackupHash` (SettingsViewModel).

## Restoring

1. **RHI's data** is restored before anything reads it:
   - First-run screen (`SetupWindow`): new "Restore from a backup" button → file picker → the data files are written, the setup choice is taken from the backup, and RHI starts.
   - Settings › Restore from a backup...: confirm, copy the backup to `%LocalAppData%\RHI\restore\pending.rhibackup`, restart RHI; `App.OnLaunched` applies it before settings load.
   - Both write `restore\plan.json` (the per-game list) and unpack the config files into `restore\games\`.
2. **Installs**, after the first scan finishes: a review dialog lists each game in the backup with a checkbox per component, **all pre-ticked** when the game is found on this PC. Games are matched by `"Game|Store"`, then by name. Games not found are listed, unticked and disabled ("Not found on this PC"). Plus a tick for NVIDIA driver settings. Restore / Later / Don't restore.
3. Installs run one game at a time through the normal install code (so backups, foreign-DLL warnings, v4 rules and per-game settings all apply), in the order ReShade, DXVK, RE Framework, RenoDX / Luma, ReLimiter / Display Commander, DOF Fix, OptiScaler. Progress in the status bar. A component that's already installed is skipped.
4. Config files: after a game's installs, each backed-up file goes back **unless the file was already there before the restore started** (a game folder that survived the reformat keeps the user's current file). An overwritten file RHI just created is replaced (that's RHI's default, not the user's).
5. NVIDIA settings: `DlssPresetService.ImportProfiles` with the backed-up export.
6. Summary dialog: installed / skipped / failed per game. `plan.json` is deleted when the restore finishes or the user picks Don't restore; Later keeps it for the next launch.

## Not covered (said in the review dialog)

Extras (HSF, Universal MFG Unlocker, MFG Unlock, RTX Encore, ASI Loader, DLSS Enabler, BFI Presenter, dgVoodoo2, Uplift) and DLSS / Streamline DLL swaps are not reinstalled. Their per-game settings come back with settings.json, so installing them again picks those up.

## Safety

- Every path from a backup goes through `FileHelper.SafeCombine` (zip entries into `%LocalAppData%\RHI` and into game folders).
- Only whitelisted data paths are extracted; anything else in the zip is ignored.
- Install records are never restored.
- Restoring over existing RHI data (Settings path) first copies the current files to `%LocalAppData%\RHI\Backups\before-restore-<time>\`.

## Phases

1. Backup file + data restore + automatic backups + Settings card + first-run button. Done 20 Nov 2026.
2. Review dialog and installs. Done 20 Nov 2026.
3. Game config files and NVIDIA settings. Done 20 Nov 2026.

Tested: unit tests (BackupTests), a real backup of a 101-game library (61 MB, 47 MB of it custom artwork), the Settings card and the review dialog (dev hook eviewbackup=). Not yet tested end to end: the Settings restore restart and the first-run restore on a clean PC.
