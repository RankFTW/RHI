# Linux upstream tracking

Windows and Linux reference the same `RHI.Core` assembly. Its source registry is [SharedSources.props](../RHI.Core/SharedSources.props); the hosts compile their own UI, paths, installation transactions and platform adapters. Sharing portable primitives does not make the remaining Windows service wrappers or every Windows feature available on Linux.

The port's Windows fork base is `b4dcc9c497bd93ebbc1156bde4b2c96079902efc`. The latest applicability audit covers upstream `c1076d38520dce8d69e006194aad86fece7c26d0` (RankFTW/RHI `main`). This is a routing and change-applicability audit, not a claim of full feature parity. Upstream through that commit is now merged into this branch, preserving the shared-core refactors. The OptiScaler update contract retains `IOptiScalerGame` and includes upstream's optional `variantHint` for legacy records. `GitHubETagCache` keeps injected logging, token, authorization reporting and persistence so it still compiles into `RHI.Core`. Decisions for the changed files are recorded individually in [LINUX-PORT-SYNC.json](LINUX-PORT-SYNC.json).

The machine-readable file is authoritative. Each entry records the Linux counterpart, a review rationale, the upstream SHA-256 reviewed, and the separate local SHA-256 after port-specific changes. A null upstream hash means a new shared extraction exists only on this branch. Maintaining separate hashes avoids pretending that the locally adapted Windows file is identical to upstream.

## Changes cannot pass the audit silently

```bash
# Offline: check every current production source against its recorded review.
python3 scripts/check-upstream-sync.py

# Online: explicitly fetch upstream, then detect changes even before merging it.
git fetch --no-tags https://github.com/RankFTW/RHI.git main:refs/remotes/rhi-review/main
python3 scripts/check-upstream-sync.py --upstream refs/remotes/rhi-review/main

# Regression checks use disposable Git repositories and never modify real games.
python3 -m unittest discover -s scripts/tests -v
```

The guard covers all production C#, XAML, project/build files, INI/JSON/config/application manifests under `RenoDXCommander`, `RHI.Core`, and the Windows drag/drop helper; root manifests/Windows solution/build settings; and the game databases and engine recipes. Generated `bin`/`obj` and upstream's explicitly excluded `original` backups are outside the scope. New files, edited files, removed files, missing counterparts and invalid/empty decisions fail the check. Deferred and Windows-only entries also fail when upstream changes: a previously unsupported feature can become relevant.

The **Linux upstream review** workflow checks pull requests and pushes to `linux_port`, supports manual runs, and checks upstream weekly when the workflow is installed on the repository's default branch. Its nonzero result makes the outstanding files visible in CI; branch protection must require this check if merges must be mechanically blocked. No script can determine semantic parity by itself, so review decisions and tests remain required.

To resolve a failure:

1. Inspect `git log <reviewed-commit>..<new-upstream-ref> -- <file>` and the actual diff. Check both the named counterpart and its callers/tests.
2. Either inherit through `RHI.Core`, port the relevant behavior with regression coverage, or record a concrete Windows-only/deferred reason. Do not label a partial adapter as a fully shared service.
3. Update only the reviewed entry's hashes and rationale. Obtain hashes with `git show <new-upstream-ref>:<file> | sha256sum` and `sha256sum <local-file>`. Preserve the distinction between upstream and locally adapted content. Explicitly add/remove mappings when sources are added/removed.
4. Update `reviewed_commit` after auditing all changes through that commit, refresh the table below to match the JSON, and run the guard, Linux tests/package checks, and Windows CI for shared or Windows changes.

There is deliberately no automatic “accept upstream” hash-update command.

## Recent upstream audit

Audit `cde8f9d..c1076d3` (411 upstream commits):

- Ported: manifest `dlssPathOverrides`/`streamlinePathOverrides` drive Linux DLSS detection for DLLs deeper than the recursive scan reaches (The Sinking City 2). Lookups are case-insensitive and traversal-safe, and a missing folder falls back to the scan.
- Ported: removing an OptiScaler install made by Windows RHI no longer deletes the game's own `plugins/` contents (Cyberpunk 2077 keeps CET and RED4ext there). Only OptiPatcher is removed. Windows F5 records also remove their NR runtime, as DLSS NR records do.
- Ported: Windows' "ZZZ Load Order" ShortFuse filename (`zzz_renodx-dlss.addon64`) is detected and removed, and is not duplicated when Linux installs ShortFuse. The toggle itself is deferred.
- Ported: OptiScaler's Streamline deployment adds the newest `nvngx_dlssg.dll`, fetched before the game folder is touched. Linux DLSS detection now skips `OptiScaler/`, so OptiScaler's private copies are not shown as game DLSS.
- Shared: `GitHubETagCache` adopts upstream's 30 s API timeout, shared in-flight requests, 60 s freshness and API-only rate limiting. The Linux-injected token goes to `api.github.com` only, matching upstream's token-scoping fix. Shader pack versions use the release tag. `OptiScalerPolicy` knows the F5 template.
- Already covered on Linux: shader archive traversal guard, NR version strings with `/` in cache folders, dgVoodoo2 `d3d9.dll` casing (case-insensitive resolution), atomic DLSS swaps that keep the original on failure, and batch DLSS deployment continuing past failed games.
- Deferred: OptiScaler F5 and nightly build pinning, RTX Encore, standalone dgVoodoo2, Control UE post-install, update history log, Epic Games Launcher discovery, GitHub sign-in, addon-pack dual-bitness staging (no addon-pack manager on Linux), ASI Loader, RTX 40 MFG and the other Extras unlockers.
- Windows-only: Auto HDR, DLDSR, NVAPI locking and freeze diagnostics, WinUI layout/freeze fixes, tray/foreground/single-instance, desktop/Steam shortcuts (Linux has its own desktop entries), installer and WindowsAppSDK 2.5.1.

## Disposition of `code_review.txt`

| Finding | Result / boundary |
| --- | --- |
| Large mixed-responsibility classes (line 13) | OptiScaler and Neural Rendering now have service-aligned partials; the DLSS window code is split by panel/default/driver responsibilities. Runtime class identity remains unchanged. |
| Long/packed source lines | New shared primitives and revised paths use readable blocks; moved legacy methods are not globally reformatted. The broader stylistic cleanup is still incremental. |
| Scattered URLs/versions and user agent | Both hosts use `RHI.Core/Sources.cs`; Linux toolchain/extractor pins use `scripts/linux-dependencies.env`. |
| Duplicate wiki download path | Linux refresh uses the shared `WikiService` fetch/cache behavior. |
| Stale bundled manifest | Linux fetches the live manifest with validation, atomic cache and bundled fallback; preferences and warnings refresh with the catalogue. |
| Duplicated portable behavior | Shared assembly includes ETag cache, PE/API/wiki logic, REFramework archive selection, shader catalogue, DLSS discovery/version/archive/sentinel primitives, and OptiScaler contracts/policy. Full Windows orchestration remains adapted, not falsely claimed as shared. |
| Windows ViewModel dependency in OptiScaler | `IOptiScalerGame` supplies a UI-independent contract; Windows view models implement it. Linux keeps its transactional installer. |
| Upstream changes could be missed | Explicit source-to-counterpart registry and CI guard cover production code/data, including unsupported features. |
| External `7z` runtime dependency | Portable packages bundle a SHA-256-pinned static 7-Zip executable and redistribution notices/source-code link. |
| Flatpak restart / missing Snap discovery | Client selection follows the owning Steam installation; native, Flatpak and Snap discovery/command construction have isolated tests. |
| Python needed by menu installer | Installer uses Bash and desktop-entry escaping; tests run it with a PATH containing no Python. Python remains a development/package-test prerequisite. |
| Architecture and distro documentation | x86_64 only is explicit; native prerequisites and immutable-desktop guidance are in [LINUX.md](LINUX.md). |
| Ubuntu-only validation | Fedora container build/package checks and Windows build/tests are added to CI. These CI jobs have not been executed locally by adding the workflow. |

A native WinUI build cannot be completed on Bazzite: the attempted Windows build reaches `XamlCompiler.exe` and fails with exit 126 because that tool requires Windows. The Windows CI job is required for actual WinUI build/test verification; Linux/shared tests do not substitute for it.

## Source map

`shared` means compiled into the common assembly; `adapted` means the listed Linux code owns the counterpart; `data` means a consumed catalogue/template; `deferred` means a known feature gap; `windows-only` means the current Windows mechanism is inapplicable. Rationale and full SHA-256 values are in the JSON. The hash column abbreviates the reviewed upstream content hash, not the local adaptation.

<!-- BEGIN SOURCE MAP -->
| Upstream / shared source | Disposition | Linux counterpart | Reviewed hash |
| --- | --- | --- | --- |
| `Directory.Build.props` | adapted | `RHI.Core/RHI.Core.csproj`, `RHI.Linux.sln`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `516fb3e9a3c4` |
| `RHI.Core/CoreLog.cs` | shared | `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RHI.Core/RHI.Core.csproj` | shared | `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RHI.Core/SharedSources.props` | shared | `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RHI.Core/Sources.cs` | shared | `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RHI.DropHelper/Program.cs` | windows-only | See explicit exclusion in JSON | `31401b9bfd4a` |
| `RHI.DropHelper/RHI.DropHelper.csproj` | windows-only | See explicit exclusion in JSON | `f41feccb77e6` |
| `RHI.DropHelper/app.manifest` | windows-only | See explicit exclusion in JSON | `daf74c2f20d4` |
| `RenoDXCommander.sln` | adapted | `RHI.Core/RHI.Core.csproj`, `RHI.Linux.sln`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `400866c4d6e9` |
| `RenoDXCommander/AddonManagerDialog.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `186cefbb3f06` |
| `RenoDXCommander/AddonPopupHelper.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `b78c8322e866` |
| `RenoDXCommander/App.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `9e6d9c46bfac` |
| `RenoDXCommander/App.xaml.cs` | adapted | `RHI.Linux/Program.cs`, `RHI.Linux/MainWindow.cs` | `fd6798349765` |
| `RenoDXCommander/Collections/BatchObservableCollection.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `a899deed052a` |
| `RenoDXCommander/Controls/WrapPanel.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `949bee74e582` |
| `RenoDXCommander/Converters/ValueConverters.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `f632547d4e78` |
| `RenoDXCommander/DetailPanelBuilder.Components.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `818454310daa` |
| `RenoDXCommander/DetailPanelBuilder.Extras.ApiWrappers.cs` | deferred | See explicit exclusion in JSON | `1d86308fbb50` |
| `RenoDXCommander/DetailPanelBuilder.Extras.AsiLoaders.cs` | deferred | See explicit exclusion in JSON | `4b2e21e80cd6` |
| `RenoDXCommander/DetailPanelBuilder.Extras.FrameGen.cs` | deferred | See explicit exclusion in JSON | `df34b4c66445` |
| `RenoDXCommander/DetailPanelBuilder.Extras.Unlocks.cs` | deferred | See explicit exclusion in JSON | `f3f5c6ff1be4` |
| `RenoDXCommander/DetailPanelBuilder.Extras.cs` | adapted | `RHI.Linux/MainWindow.OptiScaler.cs` | `ae39eabf4ac8` |
| `RenoDXCommander/DetailPanelBuilder.NeuralRendering.Feeder.cs` | adapted | `RHI.Linux.Core/NeuralRendering.Install.cs`, `RHI.Linux.Core/NeuralRendering.DgVoodoo.cs` | `f7fbf5e60621` |
| `RenoDXCommander/DetailPanelBuilder.NeuralRendering.Install.cs` | adapted | `RHI.Linux.Core/NeuralRendering.Install.cs` | `d0c70abdeae5` |
| `RenoDXCommander/DetailPanelBuilder.NeuralRendering.SectionUi.cs` | adapted | `RHI.Linux/MainWindow.NeuralRendering.cs` | `6a2deb73009f` |
| `RenoDXCommander/DetailPanelBuilder.NeuralRendering.cs` | adapted | `RHI.Linux/MainWindow.NeuralRendering.cs` | `2932cb0adc9c` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.Dlss.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `464a53625315` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.DriverSettings.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `8e3d38c5a435` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.Dxvk.cs` | deferred | See explicit exclusion in JSON | `c8e1f0c46440` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.NvidiaProfile.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `dcf1b9ae8515` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.RsChannel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `48a850d6c85e` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.ShadersAddons.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `edeb0d0a485d` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `2e3df0c03066` |
| `RenoDXCommander/DetailPanelBuilder.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `bcac374b59ec` |
| `RenoDXCommander/DialogService.Game.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `4b0e3207c6c3` |
| `RenoDXCommander/DialogService.Update.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `f542a8888197` |
| `RenoDXCommander/DialogService.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `f223208ec1a3` |
| `RenoDXCommander/DisplayCommander.ini` | windows-only | See explicit exclusion in JSON | `4a4f0f84c81f` |
| `RenoDXCommander/DlssDefaultsDialog.cs` | adapted | `RHI.Linux/MainWindow.Dlss.Defaults.cs` | `f83b9aaa7b7f` |
| `RenoDXCommander/DragDropHandler.Addon.cs` | deferred | See explicit exclusion in JSON | `bbbee597099a` |
| `RenoDXCommander/DragDropHandler.Exe.cs` | deferred | See explicit exclusion in JSON | `d0e0ed263b05` |
| `RenoDXCommander/DragDropHandler.Luma.cs` | deferred | See explicit exclusion in JSON | `cd6ce571f3eb` |
| `RenoDXCommander/DragDropHandler.Preset.cs` | deferred | See explicit exclusion in JSON | `4e0c144cd332` |
| `RenoDXCommander/DragDropHandler.cs` | deferred | See explicit exclusion in JSON | `969bf31cc9d0` |
| `RenoDXCommander/GameReportEncoder.cs` | deferred | See explicit exclusion in JSON | `f43cae19e585` |
| `RenoDXCommander/HotkeyManager.cs` | windows-only | See explicit exclusion in JSON | `d517271f317b` |
| `RenoDXCommander/InstallEventHandler.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `0fad4f596683` |
| `RenoDXCommander/MainWindow.Events.Components.Dxvk.cs` | windows-only | See explicit exclusion in JSON | `9494877c86cd` |
| `RenoDXCommander/MainWindow.Events.Components.FrameLimiters.cs` | deferred | See explicit exclusion in JSON | `6e9bd9832af7` |
| `RenoDXCommander/MainWindow.Events.Components.Hdr.cs` | windows-only | See explicit exclusion in JSON | `a2aa3374d3d0` |
| `RenoDXCommander/MainWindow.Events.Components.OptiScaler.cs` | adapted | `RHI.Linux/MainWindow.OptiScaler.cs` | `ec8e5283554a` |
| `RenoDXCommander/MainWindow.Events.Components.Rdx.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `6bedaa45a989` |
| `RenoDXCommander/MainWindow.Events.Components.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `d4c91f184e1e` |
| `RenoDXCommander/MainWindow.Events.Install.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `95f8f4092488` |
| `RenoDXCommander/MainWindow.Events.Settings.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `70f92cfefc07` |
| `RenoDXCommander/MainWindow.Events.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `cfed991ab26b` |
| `RenoDXCommander/MainWindow.FaqBuilder.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `0c89fcb78013` |
| `RenoDXCommander/MainWindow.Skeleton.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `d8f2e4576374` |
| `RenoDXCommander/MainWindow.UISync.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `10be3975e8fe` |
| `RenoDXCommander/MainWindow.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `dd397785c90b` |
| `RenoDXCommander/MainWindow.xaml.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `ae6c955208cd` |
| `RenoDXCommander/MassDeployHandler.cs` | deferred | See explicit exclusion in JSON | `bc1b792e3d25` |
| `RenoDXCommander/MassDlssDeployDialog.cs` | adapted | `RHI.Linux/MainWindow.Dlss.Defaults.cs` | `319bc352d8df` |
| `RenoDXCommander/MfgDialog.cs` | adapted | `RHI.Linux/MainWindow.Dlss.Defaults.cs` | `5cd403f9d744` |
| `RenoDXCommander/Models/AddonEntry.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `e3292fb4e6aa` |
| `RenoDXCommander/Models/AddonInfoResult.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `19ef906e5ec3` |
| `RenoDXCommander/Models/AddonType.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `dc5012c066a1` |
| `RenoDXCommander/Models/AppPage.cs` | adapted | `RHI.Linux/MainWindow.cs` | `3ecb9e8bd91c` |
| `RenoDXCommander/Models/AuxInstalledRecord.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `a6d09fbfa661` |
| `RenoDXCommander/Models/CustomFilter.cs` | adapted | `RHI.Linux/MainWindow.cs` | `23acf564080f` |
| `RenoDXCommander/Models/DetectedGame.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `4f3218e7878a` |
| `RenoDXCommander/Models/DllOverrideConfig.cs` | adapted | `RHI.Linux.Core/Proton.cs` | `93f985d372a4` |
| `RenoDXCommander/Models/DllOverrideConstants.cs` | adapted | `RHI.Linux.Core/Proton.cs` | `4f214e83f20e` |
| `RenoDXCommander/Models/DxvkInstalledRecord.cs` | windows-only | See explicit exclusion in JSON | `f319ccc40cd2` |
| `RenoDXCommander/Models/DxvkVariant.cs` | windows-only | See explicit exclusion in JSON | `0fcde3ad699a` |
| `RenoDXCommander/Models/EngineType.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `043a05a698fc` |
| `RenoDXCommander/Models/ForceExternalEntry.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `56970e2e8870` |
| `RenoDXCommander/Models/GameKey.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `30f546dfb7fc` |
| `RenoDXCommander/Models/GameMod.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `c4e11e4b208c` |
| `RenoDXCommander/Models/GameNoteEntry.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `2f5079d28075` |
| `RenoDXCommander/Models/GameStatus.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `fcf7f4b003ad` |
| `RenoDXCommander/Models/GraphicsApiType.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `9f7c7730574b` |
| `RenoDXCommander/Models/HdrModEntry.cs` | deferred | See explicit exclusion in JSON | `800094f80c05` |
| `RenoDXCommander/Models/InstalledModRecord.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/REFramework.cs`, `RHI.Linux.Core/OptiScaler.cs` | `213956626ad1` |
| `RenoDXCommander/Models/LumaInstalledRecord.cs` | deferred | See explicit exclusion in JSON | `3f32b839d2a6` |
| `RenoDXCommander/Models/LumaMod.cs` | deferred | See explicit exclusion in JSON | `2e7b172a0da1` |
| `RenoDXCommander/Models/ManifestDllNames.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `c928180e834c` |
| `RenoDXCommander/Models/NexusModsGame.cs` | deferred | See explicit exclusion in JSON | `6f1da63f395d` |
| `RenoDXCommander/Models/NvApiSettingsSnapshot.cs` | windows-only | See explicit exclusion in JSON | `949b44b73ec2` |
| `RenoDXCommander/Models/OptiScalerWikiData.cs` | deferred | See explicit exclusion in JSON | `39246b655b0c` |
| `RenoDXCommander/Models/OsPreset.cs` | deferred | See explicit exclusion in JSON | `c7dc34921a22` |
| `RenoDXCommander/Models/PcgwApiInfo.cs` | deferred | See explicit exclusion in JSON | `1a36aaf4b3ef` |
| `RenoDXCommander/Models/REFrameworkInstalledRecord.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/REFramework.cs`, `RHI.Linux.Core/OptiScaler.cs` | `59473b38c5a8` |
| `RenoDXCommander/Models/RemoteManifest.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `65ed6d5c36fa` |
| `RenoDXCommander/Models/RenoDXDbUnityEntry.cs` | deferred | See explicit exclusion in JSON | `135d0060b836` |
| `RenoDXCommander/Models/RenoDXDbUnrealEntry.cs` | deferred | See explicit exclusion in JSON | `cccf7dbd0bf1` |
| `RenoDXCommander/Models/RhiInstallManifest.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/REFramework.cs`, `RHI.Linux.Core/OptiScaler.cs` | `aac0fcf54b52` |
| `RenoDXCommander/Models/SavedGame.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `750eb4ad429e` |
| `RenoDXCommander/Models/SavedGameLibrary.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `c6edb8426188` |
| `RenoDXCommander/Models/ShaderProfile.cs` | deferred | See explicit exclusion in JSON | `38447e56eb30` |
| `RenoDXCommander/Models/SteamStoreSearchResponse.cs` | deferred | See explicit exclusion in JSON | `15f1626a5223` |
| `RenoDXCommander/Models/UpdateLogEntry.cs` | deferred | See explicit exclusion in JSON | `bf1c9f68995e` |
| `RenoDXCommander/Models/ViewLayout.cs` | adapted | `RHI.Linux/MainWindow.cs` | `f722974b669f` |
| `RenoDXCommander/NativeInterop.cs` | windows-only | See explicit exclusion in JSON | `c51f263e8179` |
| `RenoDXCommander/OptiScaler.amd-dlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `fa98579f18f3` |
| `RenoDXCommander/OptiScaler.amd-nodlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `a9ef7643ebac` |
| `RenoDXCommander/OptiScaler.nvidia.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `fa98579f18f3` |
| `RenoDXCommander/OptiScaler_dlssnr.amd-dlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `884f5185ad63` |
| `RenoDXCommander/OptiScaler_dlssnr.amd-nodlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `874d064f99a7` |
| `RenoDXCommander/OptiScaler_dlssnr.nvidia.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `884f5185ad63` |
| `RenoDXCommander/OptiScaler_f5.amd-dlss.ini` | deferred | See explicit exclusion in JSON | `5fae06dc4846` |
| `RenoDXCommander/OptiScaler_f5.amd-nodlss.ini` | deferred | See explicit exclusion in JSON | `77aea9cba1ec` |
| `RenoDXCommander/OptiScaler_f5.nvidia.ini` | deferred | See explicit exclusion in JSON | `5fae06dc4846` |
| `RenoDXCommander/OptiScaler_nightly.amd-dlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `cf3ca875b28d` |
| `RenoDXCommander/OptiScaler_nightly.amd-nodlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `cf3ca875b28d` |
| `RenoDXCommander/OptiScaler_nightly.nvidia.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `cf3ca875b28d` |
| `RenoDXCommander/PresetPopupHelper.cs` | deferred | See explicit exclusion in JSON | `0394a2bdf052` |
| `RenoDXCommander/ReShade.Vulkan.ini` | windows-only | See explicit exclusion in JSON | `dd084a0c82d1` |
| `RenoDXCommander/ReShade.ini` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/IniSettings.cs` | `1d933fd9d580` |
| `RenoDXCommander/ReShade64.json` | windows-only | See explicit exclusion in JSON | `dfdc50064270` |
| `RenoDXCommander/RenoDXCommander.csproj` | adapted | `RHI.Core/RHI.Core.csproj`, `RHI.Linux.sln`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `3c7109c92395` |
| `RenoDXCommander/ResourceKeys.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `c4c570214edd` |
| `RenoDXCommander/Services/AddonFileWatcher.cs` | deferred | See explicit exclusion in JSON | `829bdd240b4e` |
| `RenoDXCommander/Services/AddonInfoResolver.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs` | `7840bc232473` |
| `RenoDXCommander/Services/AddonPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `73e97a30c569` |
| `RenoDXCommander/Services/AddonsIniParser.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `4369e2188d4a` |
| `RenoDXCommander/Services/AppPaths.cs` | adapted | `RHI.Linux.Core/LinuxPaths.cs` | `c68521c71d7b` |
| `RenoDXCommander/Services/AsyncExtensions.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `347ed142de30` |
| `RenoDXCommander/Services/AutoHdrService.cs` | windows-only | See explicit exclusion in JSON | `ac9ecac41005` |
| `RenoDXCommander/Services/AutoUpdateService.cs` | deferred | See explicit exclusion in JSON | `cee0b00d8970` |
| `RenoDXCommander/Services/AuxInstallService.DllIdentification.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `80759b71c2b4` |
| `RenoDXCommander/Services/AuxInstallService.GacSymlink.cs` | windows-only | See explicit exclusion in JSON | `6a2ca9cc8b1b` |
| `RenoDXCommander/Services/AuxInstallService.Ini.cs` | adapted | `RHI.Linux.Core/IniSettings.cs`, `RHI.Linux.Core/Installation.cs` | `da1251c46b18` |
| `RenoDXCommander/Services/AuxInstallService.Install.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `3998df400e2e` |
| `RenoDXCommander/Services/AuxInstallService.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `5bf89af30ed6` |
| `RenoDXCommander/Services/ControlUePostInstallService.cs` | deferred | See explicit exclusion in JSON | `6d53183aa648` |
| `RenoDXCommander/Services/CrashReporter.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `d4ba84f88105` |
| `RenoDXCommander/Services/CrashReporterService.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `1cb2163c916a` |
| `RenoDXCommander/Services/CustomReShadeHashService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/InstallationStatus.cs` | `9be0c66ca581` |
| `RenoDXCommander/Services/DevUnlockService.cs` | deferred | See explicit exclusion in JSON | `ba51a955a899` |
| `RenoDXCommander/Services/DgVoodooService.cs` | adapted | `RHI.Linux.Core/NeuralRendering.DgVoodoo.cs` | `9e23cb9aa8ca` |
| `RenoDXCommander/Services/DialogCoordinator.cs` | windows-only | See explicit exclusion in JSON | `c466362d6578` |
| `RenoDXCommander/Services/DigitalVibranceService.cs` | windows-only | See explicit exclusion in JSON | `df2381aeaccc` |
| `RenoDXCommander/Services/DldsrService.cs` | windows-only | See explicit exclusion in JSON | `03dcea369b21` |
| `RenoDXCommander/Services/DllArchive.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/DllOverrideService.cs` | adapted | `RHI.Linux.Core/Proton.cs` | `a150bfe9108a` |
| `RenoDXCommander/Services/DlssEnablerService.cs` | deferred | See explicit exclusion in JSON | `785c3f06758b` |
| `RenoDXCommander/Services/DlssFileDiscovery.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/DlssNrCostScalerService.cs` | adapted | `RHI.Linux.Core/NeuralRendering.CostScaler.cs` | `e2d255f686a6` |
| `RenoDXCommander/Services/DlssPresetService.DriverSettings.cs` | adapted | `RHI.Linux.Core/DlssProfile.cs`, `RHI.Linux.Core/Proton.cs` | `6ecd5dc6b59f` |
| `RenoDXCommander/Services/DlssPresetService.Export.cs` | windows-only | See explicit exclusion in JSON | `4244658a21f8` |
| `RenoDXCommander/Services/DlssPresetService.ProfileMatching.cs` | windows-only | See explicit exclusion in JSON | `b27704c00493` |
| `RenoDXCommander/Services/DlssPresetService.ReBar.cs` | windows-only | See explicit exclusion in JSON | `34fad59d26e7` |
| `RenoDXCommander/Services/DlssPresetService.Reset.cs` | adapted | `RHI.Linux.Core/DlssProfile.cs`, `RHI.Linux.Core/Proton.cs` | `73418e5c35a6` |
| `RenoDXCommander/Services/DlssPresetService.cs` | adapted | `RHI.Linux.Core/DlssProfile.cs`, `RHI.Linux.Core/Proton.cs` | `67ad5d09d38d` |
| `RenoDXCommander/Services/DlssStreamlineService.Detection.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `c940ea977657` |
| `RenoDXCommander/Services/DlssStreamlineService.Swap.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `51f492b2995c` |
| `RenoDXCommander/Services/DlssStreamlineService.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `839d74501306` |
| `RenoDXCommander/Services/DlssSwapException.cs` | adapted | `RHI.Linux.Core/Dlss.cs` | `f7be9bca8e2e` |
| `RenoDXCommander/Services/DlssVersion.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/Dlssg20_30Service.cs` | deferred | See explicit exclusion in JSON | `d6d2375f05e4` |
| `RenoDXCommander/Services/DofFixService.cs` | deferred | See explicit exclusion in JSON | `e9b2c95c1359` |
| `RenoDXCommander/Services/DownloadPaths.cs` | adapted | `RHI.Linux.Core/LinuxPaths.cs` | `2b779a2e6657` |
| `RenoDXCommander/Services/DownloadsMigrationService.cs` | windows-only | See explicit exclusion in JSON | `9e4c8ca20f6b` |
| `RenoDXCommander/Services/DxvkService.Install.cs` | windows-only | See explicit exclusion in JSON | `146ab4c8da2d` |
| `RenoDXCommander/Services/DxvkService.Staging.cs` | windows-only | See explicit exclusion in JSON | `80991c970537` |
| `RenoDXCommander/Services/DxvkService.Tracking.cs` | windows-only | See explicit exclusion in JSON | `9ebfcd3ef483` |
| `RenoDXCommander/Services/DxvkService.cs` | windows-only | See explicit exclusion in JSON | `6e504ec14729` |
| `RenoDXCommander/Services/EngineFolderStamp.cs` | windows-only | See explicit exclusion in JSON | `1036fe01c4ac` |
| `RenoDXCommander/Services/FeatureFlags.cs` | deferred | See explicit exclusion in JSON | `27c6f1cb0a9d` |
| `RenoDXCommander/Services/FileAssociationService.cs` | windows-only | See explicit exclusion in JSON | `24756b8fc126` |
| `RenoDXCommander/Services/FileHelper.cs` | adapted | `RHI.Linux.Core/LinuxPaths.cs`, `RHI.Linux.Core/Installation.cs` | `23e4343aafc8` |
| `RenoDXCommander/Services/ForegroundActivation.cs` | windows-only | See explicit exclusion in JSON | `7f40ab4d611a` |
| `RenoDXCommander/Services/ForegroundActivationTarget.cs` | windows-only | See explicit exclusion in JSON | `f1d63fe10e1a` |
| `RenoDXCommander/Services/GameDetectionService.Epic.cs` | deferred | See explicit exclusion in JSON | `766ccf87f822` |
| `RenoDXCommander/Services/GameDetectionService.Platform.cs` | windows-only | See explicit exclusion in JSON | `db6f80fa9e9a` |
| `RenoDXCommander/Services/GameDetectionService.Steam.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `cd5358909870` |
| `RenoDXCommander/Services/GameDetectionService.Xbox.cs` | windows-only | See explicit exclusion in JSON | `e6b273d3114e` |
| `RenoDXCommander/Services/GameDetectionService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `8aadd169dae0` |
| `RenoDXCommander/Services/GameInitializationService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `2d6c91be1a3c` |
| `RenoDXCommander/Services/GameLibraryService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `1ea3f1d87a82` |
| `RenoDXCommander/Services/GameNameService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `9ee3df02cd0a` |
| `RenoDXCommander/Services/GitHubAuthService.cs` | deferred | See explicit exclusion in JSON | `76918934ab26` |
| `RenoDXCommander/Services/GitHubETagCache.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `bebc745b59f0` |
| `RenoDXCommander/Services/GitHubTokenHandler.cs` | adapted | `RHI.Core/RHI.Core.csproj` | `99a593592eb6` |
| `RenoDXCommander/Services/GraphicsApiDetector.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `dae13175da2c` |
| `RenoDXCommander/Services/HdrDatabaseService.cs` | deferred | See explicit exclusion in JSON | `549e13e3ef0b` |
| `RenoDXCommander/Services/HdrToggleService.cs` | windows-only | See explicit exclusion in JSON | `4248a409aca4` |
| `RenoDXCommander/Services/IAddonPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `076aff79040b` |
| `RenoDXCommander/Services/IAutoHdrService.cs` | windows-only | See explicit exclusion in JSON | `489201246a0b` |
| `RenoDXCommander/Services/IAuxFileService.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `66a7d2c4a8e4` |
| `RenoDXCommander/Services/IAuxInstallService.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `20a42f6d7eef` |
| `RenoDXCommander/Services/ICrashReporter.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `ca77a1407527` |
| `RenoDXCommander/Services/IDldsrService.cs` | windows-only | See explicit exclusion in JSON | `843283a80b6f` |
| `RenoDXCommander/Services/IDllOverrideService.cs` | adapted | `RHI.Linux.Core/Proton.cs` | `9191d7b866a3` |
| `RenoDXCommander/Services/IDlssStreamlineService.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `a76ae3f2f45e` |
| `RenoDXCommander/Services/IDofFixService.cs` | deferred | See explicit exclusion in JSON | `e0b52b9a6aa0` |
| `RenoDXCommander/Services/IDxvkService.cs` | windows-only | See explicit exclusion in JSON | `baeaac91963d` |
| `RenoDXCommander/Services/IGameDetectionService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `27f8f2ad0c20` |
| `RenoDXCommander/Services/IGameInitializationService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `70a4b89e1542` |
| `RenoDXCommander/Services/IGameLibraryService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `71c9efdc9838` |
| `RenoDXCommander/Services/IGameNameService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `20ce2eab6c0d` |
| `RenoDXCommander/Services/ILumaService.cs` | deferred | See explicit exclusion in JSON | `886602926e6d` |
| `RenoDXCommander/Services/IManifestService.cs` | adapted | `RHI.Linux.Core/Catalog.cs`, `RHI.Core/Sources.cs` | `2a997c275184` |
| `RenoDXCommander/Services/IModInstallService.cs` | adapted | `RHI.Linux.Core/GameSetup.cs`, `RHI.Linux.Core/Installation.cs` | `c001e78bc8c1` |
| `RenoDXCommander/Services/INexusModsService.cs` | deferred | See explicit exclusion in JSON | `04191a94f27b` |
| `RenoDXCommander/Services/INormalReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `ef5c26d17323` |
| `RenoDXCommander/Services/IOptiScalerGame.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/IOptiScalerService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `23faa5684e8d` |
| `RenoDXCommander/Services/IPcgwService.cs` | deferred | See explicit exclusion in JSON | `fe3fab8f6c2c` |
| `RenoDXCommander/Services/IPeHeaderService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `6f3d56d69865` |
| `RenoDXCommander/Services/IREFrameworkService.cs` | adapted | `RHI.Linux.Core/REFramework.cs` | `582cdd597eb5` |
| `RenoDXCommander/Services/IReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `ffa2d61bc431` |
| `RenoDXCommander/Services/IRenoDXDbService.cs` | deferred | See explicit exclusion in JSON | `286b979e1a9b` |
| `RenoDXCommander/Services/ISevenZipExtractor.cs` | adapted | `RHI.Linux.Core/ArchiveTools.cs`, `RHI.Linux.Core/Downloads.cs`, `scripts/bundle-linux-dependencies.sh` | `508ad1b9340d` |
| `RenoDXCommander/Services/IShaderPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `6cbfd0d61de7` |
| `RenoDXCommander/Services/ISteamAppIdResolver.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs` | `1be7dff0402d` |
| `RenoDXCommander/Services/IUltraPlusService.cs` | deferred | See explicit exclusion in JSON | `5a5a58f3cc2c` |
| `RenoDXCommander/Services/IUltrawideFixService.cs` | deferred | See explicit exclusion in JSON | `77fc808e1b87` |
| `RenoDXCommander/Services/IUpdateLogService.cs` | deferred | See explicit exclusion in JSON | `0b84ff942a12` |
| `RenoDXCommander/Services/IUpdateOrchestrationService.cs` | adapted | `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/GameSetup.cs` | `02cbd891cb25` |
| `RenoDXCommander/Services/IUpdateService.cs` | deferred | See explicit exclusion in JSON | `2ac5920f0d09` |
| `RenoDXCommander/Services/IWikiService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `d3f8255f2bc7` |
| `RenoDXCommander/Services/LumaService.cs` | deferred | See explicit exclusion in JSON | `52f51b6a1e88` |
| `RenoDXCommander/Services/ManifestService.cs` | adapted | `RHI.Linux.Core/Catalog.cs`, `RHI.Core/Sources.cs` | `3ebb1b8af90f` |
| `RenoDXCommander/Services/ModInstallService.cs` | adapted | `RHI.Linux.Core/GameSetup.cs`, `RHI.Linux.Core/Installation.cs` | `df2e9a791fce` |
| `RenoDXCommander/Services/MotdService.cs` | deferred | See explicit exclusion in JSON | `4f6f7aff85a6` |
| `RenoDXCommander/Services/NexusDownloadService.cs` | deferred | See explicit exclusion in JSON | `14a5fb78456d` |
| `RenoDXCommander/Services/NexusModsService.cs` | deferred | See explicit exclusion in JSON | `c91629009bd1` |
| `RenoDXCommander/Services/NexusSsoService.cs` | deferred | See explicit exclusion in JSON | `079e93bda469` |
| `RenoDXCommander/Services/NexusUpdateService.cs` | deferred | See explicit exclusion in JSON | `e58e8563ab83` |
| `RenoDXCommander/Services/NormalReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `9cf1e2dca6db` |
| `RenoDXCommander/Services/NvColorService.cs` | windows-only | See explicit exclusion in JSON | `e3c16d9d64c9` |
| `RenoDXCommander/Services/NxmProtocolHandler.cs` | windows-only | See explicit exclusion in JSON | `0b049af1a00d` |
| `RenoDXCommander/Services/OptiScalerPolicy.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/OptiScalerService.Coexist.cs` | adapted | `RHI.Linux.Core/OptiScaler.Coexist.cs` | `cbaf496903bd` |
| `RenoDXCommander/Services/OptiScalerService.Install.cs` | adapted | `RHI.Linux.Core/OptiScaler.Install.cs` | `18ccd5a471f4` |
| `RenoDXCommander/Services/OptiScalerService.Staging.cs` | adapted | `RHI.Linux.Core/OptiScaler.Staging.cs` | `f351e87fe419` |
| `RenoDXCommander/Services/OptiScalerService.Streamline.cs` | adapted | `RHI.Linux.Core/OptiScaler.Streamline.cs` | `7ea14478e825` |
| `RenoDXCommander/Services/OptiScalerService.cs` | adapted | `RHI.Linux.Core/OptiScaler.cs` | `6f65e62ce3dd` |
| `RenoDXCommander/Services/OptiScalerWikiService.cs` | deferred | See explicit exclusion in JSON | `e806d86538d9` |
| `RenoDXCommander/Services/OsPresetService.cs` | deferred | See explicit exclusion in JSON | `7bed8ee94c05` |
| `RenoDXCommander/Services/PcgwService.cs` | deferred | See explicit exclusion in JSON | `55a35bba5031` |
| `RenoDXCommander/Services/PeHeaderService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `d5d69e22c7f4` |
| `RenoDXCommander/Services/PresetValidator.cs` | deferred | See explicit exclusion in JSON | `1ca6db14eb61` |
| `RenoDXCommander/Services/REFrameworkArchive.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/REFrameworkService.cs` | adapted | `RHI.Linux.Core/REFramework.cs` | `66311b1aeb0e` |
| `RenoDXCommander/Services/ReShadeNightlyService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `22e0fcaee7e0` |
| `RenoDXCommander/Services/ReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `dfa88fa02351` |
| `RenoDXCommander/Services/RenoDXDbService.cs` | deferred | See explicit exclusion in JSON | `bb84b594849e` |
| `RenoDXCommander/Services/Renodx5AddonService.cs` | adapted | `RHI.Linux.Core/NeuralRendering.Staging.cs`, `RHI.Linux.Core/NeuralRendering.Install.cs` | `8939aedada54` |
| `RenoDXCommander/Services/ResolutionToggleService.cs` | windows-only | See explicit exclusion in JSON | `5f994b445f19` |
| `RenoDXCommander/Services/Rtx40MfgService.cs` | deferred | See explicit exclusion in JSON | `6fdb2bb2d7d4` |
| `RenoDXCommander/Services/RtxEncoreService.cs` | deferred | See explicit exclusion in JSON | `576523a0e088` |
| `RenoDXCommander/Services/SafeJsonFile.cs` | adapted | `RHI.Linux.Core/LinuxPaths.cs` | `b558017bdf29` |
| `RenoDXCommander/Services/SeenLumaModsService.cs` | deferred | See explicit exclusion in JSON | `fb48691dc4a6` |
| `RenoDXCommander/Services/SeenUltraPlusModsService.cs` | deferred | See explicit exclusion in JSON | `94878fc32451` |
| `RenoDXCommander/Services/SeenWikiModsService.cs` | adapted | `RHI.Linux.Core/Catalog.cs`, `RHI.Linux.Core/Game.cs` | `f544f209dc7f` |
| `RenoDXCommander/Services/SentinelFiles.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/SettingsFile.cs` | adapted | `RHI.Linux.Core/Game.cs` | `bb595cfabd9e` |
| `RenoDXCommander/Services/SevenZipExtractor.cs` | adapted | `RHI.Linux.Core/ArchiveTools.cs`, `RHI.Linux.Core/Downloads.cs`, `scripts/bundle-linux-dependencies.sh` | `342040ccc40a` |
| `RenoDXCommander/Services/ShaderPackCatalog.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/ShaderPackService.Deploy.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/ShaderArchives.cs` | `5bb6032f5594` |
| `RenoDXCommander/Services/ShaderPackService.Download.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/ShaderArchives.cs` | `5466353ce718` |
| `RenoDXCommander/Services/ShaderPackService.IncludeScan.cs` | deferred | See explicit exclusion in JSON | `52d4828edc3d` |
| `RenoDXCommander/Services/ShaderPackService.Mirror.cs` | adapted | `RHI.Linux.Core/Installation.cs` | `55466b14a905` |
| `RenoDXCommander/Services/ShaderPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/ShaderArchives.cs` | `c6ecbaea762e` |
| `RenoDXCommander/Services/ShaderProfileService.cs` | deferred | See explicit exclusion in JSON | `599550681f8c` |
| `RenoDXCommander/Services/ShaderResolver.cs` | deferred | See explicit exclusion in JSON | `334d47591000` |
| `RenoDXCommander/Services/ShortcutService.cs` | adapted | `scripts/install-linux-desktop.sh` | `349a4e04a5ed` |
| `RenoDXCommander/Services/SingleInstanceService.cs` | windows-only | See explicit exclusion in JSON | `92acc2f88994` |
| `RenoDXCommander/Services/SteamAppIdResolver.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs` | `e29202325037` |
| `RenoDXCommander/Services/TechniquesParser.cs` | deferred | See explicit exclusion in JSON | `325583bcbccd` |
| `RenoDXCommander/Services/TrayIconService.cs` | windows-only | See explicit exclusion in JSON | `40787263098f` |
| `RenoDXCommander/Services/UiDispatch.cs` | windows-only | See explicit exclusion in JSON | `93574905e8b5` |
| `RenoDXCommander/Services/UltimateAsiLoaderService.cs` | deferred | See explicit exclusion in JSON | `be2404934557` |
| `RenoDXCommander/Services/UltraPlusService.cs` | deferred | See explicit exclusion in JSON | `6479f9ccb753` |
| `RenoDXCommander/Services/UltrawideFixService.cs` | deferred | See explicit exclusion in JSON | `c7e7a1b2f10c` |
| `RenoDXCommander/Services/UpdateLogService.cs` | deferred | See explicit exclusion in JSON | `20ad1d6ec471` |
| `RenoDXCommander/Services/UpdateOrchestrationService.cs` | adapted | `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/GameSetup.cs` | `e73ce9620184` |
| `RenoDXCommander/Services/UpdateService.cs` | deferred | See explicit exclusion in JSON | `1c918012b48c` |
| `RenoDXCommander/Services/VulkanFootprintService.cs` | windows-only | See explicit exclusion in JSON | `0e996fa89f38` |
| `RenoDXCommander/Services/VulkanLayerService.cs` | windows-only | See explicit exclusion in JSON | `1259f3fbd770` |
| `RenoDXCommander/Services/WikiService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `7847286383cf` |
| `RenoDXCommander/SettingsHandler.Accounts.cs` | deferred | See explicit exclusion in JSON | `6006cd6bdd0e` |
| `RenoDXCommander/SettingsHandler.Channels.cs` | windows-only | See explicit exclusion in JSON | `2cbc4d04e0e2` |
| `RenoDXCommander/SettingsHandler.DataTools.cs` | deferred | See explicit exclusion in JSON | `95feaa1f4b5c` |
| `RenoDXCommander/SettingsHandler.General.cs` | windows-only | See explicit exclusion in JSON | `fe32f87f9d83` |
| `RenoDXCommander/SettingsHandler.Hotkeys.cs` | adapted | `RHI.Linux/MainWindow.OptiScaler.cs` | `84ff6cdee7c6` |
| `RenoDXCommander/SettingsHandler.ReLimiterOptiScaler.cs` | deferred | See explicit exclusion in JSON | `8b86abf7b7d5` |
| `RenoDXCommander/SettingsHandler.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `e9c35fb03498` |
| `RenoDXCommander/SetupWindow.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `89e1f40041db` |
| `RenoDXCommander/SetupWindow.xaml.cs` | adapted | `RHI.Linux/Program.cs`, `RHI.Linux/MainWindow.cs` | `ac1d55a4c3db` |
| `RenoDXCommander/ShaderPopupHelper.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `46d173b58347` |
| `RenoDXCommander/Themes/DarkTheme.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `25e277e52d85` |
| `RenoDXCommander/UIFactory.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `b329e0484a71` |
| `RenoDXCommander/UpdateInclusionHelper.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `5f0b4dff1ab3` |
| `RenoDXCommander/UpdateLogWindow.cs` | deferred | See explicit exclusion in JSON | `7d4abc1482b2` |
| `RenoDXCommander/ViewModels/FilterViewModel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `30300b8eb29c` |
| `RenoDXCommander/ViewModels/GameCardViewModel.DisplayCommander.cs` | deferred | See explicit exclusion in JSON | `322624119172` |
| `RenoDXCommander/ViewModels/GameCardViewModel.DlssStreamline.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `96e5a0557b4a` |
| `RenoDXCommander/ViewModels/GameCardViewModel.DofFix.cs` | deferred | See explicit exclusion in JSON | `e656de4c0e49` |
| `RenoDXCommander/ViewModels/GameCardViewModel.Dxvk.cs` | deferred | See explicit exclusion in JSON | `a69baed1a79b` |
| `RenoDXCommander/ViewModels/GameCardViewModel.Luma.cs` | deferred | See explicit exclusion in JSON | `d176300e98e6` |
| `RenoDXCommander/ViewModels/GameCardViewModel.NormalReShade.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `7c567838d444` |
| `RenoDXCommander/ViewModels/GameCardViewModel.NvidiaProfile.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `bb32413cbc6d` |
| `RenoDXCommander/ViewModels/GameCardViewModel.OptiScaler.cs` | adapted | `RHI.Linux/MainWindow.OptiScaler.cs` | `2ffe5c4d4482` |
| `RenoDXCommander/ViewModels/GameCardViewModel.REFramework.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `b738d7b30441` |
| `RenoDXCommander/ViewModels/GameCardViewModel.ReShade.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `394b298a9b47` |
| `RenoDXCommander/ViewModels/GameCardViewModel.RenoDX.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `34e9eaa39427` |
| `RenoDXCommander/ViewModels/GameCardViewModel.UI.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `ea35cea51a2f` |
| `RenoDXCommander/ViewModels/GameCardViewModel.UltraLimiter.cs` | deferred | See explicit exclusion in JSON | `5603e5c57ee0` |
| `RenoDXCommander/ViewModels/GameCardViewModel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `deef37037607` |
| `RenoDXCommander/ViewModels/MainViewModel.BackgroundScan.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `d4efbbae743c` |
| `RenoDXCommander/ViewModels/MainViewModel.BuildCards.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `ec6a359ad0d6` |
| `RenoDXCommander/ViewModels/MainViewModel.CacheLoad.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `15f683b370d2` |
| `RenoDXCommander/ViewModels/MainViewModel.Dxvk.cs` | deferred | See explicit exclusion in JSON | `6262f627d0c5` |
| `RenoDXCommander/ViewModels/MainViewModel.GameMatching.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `7204a51123e9` |
| `RenoDXCommander/ViewModels/MainViewModel.Init.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `20d04506bbd5` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.Components.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `cd15aeff8733` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.Luma.cs` | deferred | See explicit exclusion in JSON | `5d525deb3281` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.Nexus.cs` | deferred | See explicit exclusion in JSON | `1799ef7ddffb` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `e9933d4529d7` |
| `RenoDXCommander/ViewModels/MainViewModel.Settings.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `e2dc7fe1717b` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.All.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs` | `eda15768a8b4` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.Check.cs` | adapted | `RHI.Linux/MainWindow.Game.cs` | `630a3af48f1e` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.Heartbeat.cs` | windows-only | See explicit exclusion in JSON | `c407d25ded5d` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.NativeStack.cs` | windows-only | See explicit exclusion in JSON | `0f1810c5e472` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.ReLimiterDc.cs` | deferred | See explicit exclusion in JSON | `68b3fc424d82` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `316b2f555b96` |
| `RenoDXCommander/ViewModels/MainViewModel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `9462d7f9b8d8` |
| `RenoDXCommander/ViewModels/SettingsViewModel.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `bfa05fee22d0` |
| `RenoDXCommander/WindowStateManager.cs` | windows-only | See explicit exclusion in JSON | `74218778eedb` |
| `RenoDXCommander/app.manifest` | windows-only | See explicit exclusion in JSON | `0a022b747e71` |
| `RenoDXCommander/dxvk.conf` | windows-only | See explicit exclusion in JSON | `492043b2a8cb` |
| `RenoDXCommander/relimiter.ini` | windows-only | See explicit exclusion in JSON | `909a602c4110` |
| `RenoDXCommander/reshade.rdr2.ini` | windows-only | See explicit exclusion in JSON | `5858aa5aff29` |
| `database/RenoDXdb-unity.json` | deferred | See explicit exclusion in JSON | `987a0e35c651` |
| `database/pcgw_data.json` | deferred | See explicit exclusion in JSON | `ba8665924cc8` |
| `dlss_manifest.json` | data | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `f881d0b6c04d` |
| `docs/RenoDXdb.json` | deferred | See explicit exclusion in JSON | `d653817ffca6` |
| `engine-files/black-myth-wukong.ini` | deferred | See explicit exclusion in JSON | `8cda6b6ebd2a` |
| `game-db/game_db.json` | deferred | See explicit exclusion in JSON | `fd3e7dc48ad9` |
| `manifest.json` | data | `RHI.Linux.Core/Catalog.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `468cf3a9e60a` |
<!-- END SOURCE MAP -->
