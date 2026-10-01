# Linux upstream tracking

Windows and Linux reference the same `RHI.Core` assembly. Its source registry is [SharedSources.props](../RHI.Core/SharedSources.props); the hosts compile their own UI, paths, installation transactions and platform adapters. Sharing portable primitives does not make the remaining Windows service wrappers or every Windows feature available on Linux.

The port's Windows fork base is `b4dcc9c497bd93ebbc1156bde4b2c96079902efc`. The latest applicability audit covers upstream `cde8f9df284e50dce7deded71c3870e2c27d3e9d` (RankFTW/RHI `main`). This is a routing and change-applicability audit, not a claim of full feature parity. Upstream through that commit is now merged into this branch, preserving the shared-core refactors. The OptiScaler update contract retains `IOptiScalerGame` and includes upstream's optional `variantHint` for legacy records. Decisions for the changed files are recorded individually in [LINUX-PORT-SYNC.json](LINUX-PORT-SYNC.json).

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

- NR release ordering already uses numeric prerelease comparison on Linux (`rc10` above `rc5`, stable above RC). Explicit Refresh and Update All now bypass release-list caches; startup may reuse fresh caches.
- Upstream OptiScaler's legacy variant hint addresses Windows records without `OsVariant`. Linux Update All requires a Linux ownership record whose variant matches saved preferences. An installation imported from Windows is explicitly reinstalled/adopted before automated updates, so it is not silently updated as Stable.
- Upstream's Windows-manifest NR auto-redeploy change is deferred: Linux detects Windows NR files for removal/reinstallation, but automatic updating requires a Linux `NrRecord`.
- Custom shader ID inheritance and include auto-selection fixes do not apply to the Linux whole-pack selection UI; there is no global/per-game per-file inheritance or include auto-selector.
- Unity database status/comments, ungated upgrade recipes and Tonemap/Scaling controls remain explicitly deferred with RenoDXDb integration. Linux wiki notes are a separate supported path.
- The Windows DXVK eligibility change, WinUI timing diagnostics, native thread-ID import fix, Windows button brush changes and Windows release version bump do not change supported Linux behavior. Their files remain tracked for future review.

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
| `RenoDXCommander/AddonManagerDialog.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `950c8c68a137` |
| `RenoDXCommander/AddonPopupHelper.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `90a64ec8d3e6` |
| `RenoDXCommander/App.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `9e6d9c46bfac` |
| `RenoDXCommander/App.xaml.cs` | adapted | `RHI.Linux/Program.cs`, `RHI.Linux/MainWindow.cs` | `ce88e9633955` |
| `RenoDXCommander/Collections/BatchObservableCollection.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `a899deed052a` |
| `RenoDXCommander/CompactViewBuilder.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `b7a9451a7a8e` |
| `RenoDXCommander/Controls/WrapPanel.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `949bee74e582` |
| `RenoDXCommander/Converters/ValueConverters.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `805c2f0ad8cd` |
| `RenoDXCommander/DetailPanelBuilder.Components.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `21faa3a54a25` |
| `RenoDXCommander/DetailPanelBuilder.Extras.cs` | adapted | `RHI.Linux/MainWindow.OptiScaler.cs` | `857d847adbfa` |
| `RenoDXCommander/DetailPanelBuilder.NeuralRendering.cs` | adapted | `RHI.Linux/MainWindow.NeuralRendering.cs` | `395fc9a45073` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.Dlss.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `c7f1b664646e` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.DriverSettings.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `f0b5e2f519e7` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.Dxvk.cs` | deferred | See explicit exclusion in JSON | `c8e1f0c46440` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.NvidiaProfile.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `eaebb7561a10` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.RsChannel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `48a850d6c85e` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.ShadersAddons.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `996289c45142` |
| `RenoDXCommander/DetailPanelBuilder.Overrides.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `b4dc17b9d0d4` |
| `RenoDXCommander/DetailPanelBuilder.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `04b0de1a4a1e` |
| `RenoDXCommander/DialogService.Game.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `f909db2106f5` |
| `RenoDXCommander/DialogService.Update.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `a77faa058e29` |
| `RenoDXCommander/DialogService.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `db35df2c7283` |
| `RenoDXCommander/DisplayCommander.ini` | windows-only | See explicit exclusion in JSON | `4a4f0f84c81f` |
| `RenoDXCommander/DlssDefaultsDialog.cs` | adapted | `RHI.Linux/MainWindow.Dlss.Defaults.cs` | `f83b9aaa7b7f` |
| `RenoDXCommander/DragDropHandler.Addon.cs` | deferred | See explicit exclusion in JSON | `9448d06e23b1` |
| `RenoDXCommander/DragDropHandler.Exe.cs` | deferred | See explicit exclusion in JSON | `d0e0ed263b05` |
| `RenoDXCommander/DragDropHandler.Luma.cs` | deferred | See explicit exclusion in JSON | `f2f7e0144f2f` |
| `RenoDXCommander/DragDropHandler.Preset.cs` | deferred | See explicit exclusion in JSON | `4e0c144cd332` |
| `RenoDXCommander/DragDropHandler.cs` | deferred | See explicit exclusion in JSON | `5b8330e88ad3` |
| `RenoDXCommander/GameReportEncoder.cs` | deferred | See explicit exclusion in JSON | `f43cae19e585` |
| `RenoDXCommander/HotkeyManager.cs` | windows-only | See explicit exclusion in JSON | `d517271f317b` |
| `RenoDXCommander/InstallEventHandler.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `c572182be9b4` |
| `RenoDXCommander/MainWindow.Events.Components.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `465082239616` |
| `RenoDXCommander/MainWindow.Events.Install.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `2e4eff67c723` |
| `RenoDXCommander/MainWindow.Events.Settings.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `75b6e47f24ad` |
| `RenoDXCommander/MainWindow.Events.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `0437eedb5187` |
| `RenoDXCommander/MainWindow.FaqBuilder.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `0c89fcb78013` |
| `RenoDXCommander/MainWindow.Skeleton.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `dd6f953c8eb6` |
| `RenoDXCommander/MainWindow.UISync.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `103c19d61238` |
| `RenoDXCommander/MainWindow.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `256de4a5c332` |
| `RenoDXCommander/MainWindow.xaml.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `df722fe03356` |
| `RenoDXCommander/MassDeployHandler.cs` | deferred | See explicit exclusion in JSON | `e762f0bb10b8` |
| `RenoDXCommander/MassDlssDeployDialog.cs` | adapted | `RHI.Linux/MainWindow.Dlss.Defaults.cs` | `a99ee17f7639` |
| `RenoDXCommander/MfgDialog.cs` | adapted | `RHI.Linux/MainWindow.Dlss.Defaults.cs` | `2ddd9ae617d3` |
| `RenoDXCommander/Models/AddonEntry.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `e3292fb4e6aa` |
| `RenoDXCommander/Models/AddonInfoResult.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `19ef906e5ec3` |
| `RenoDXCommander/Models/AddonType.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `dc5012c066a1` |
| `RenoDXCommander/Models/AppPage.cs` | adapted | `RHI.Linux/MainWindow.cs` | `3ecb9e8bd91c` |
| `RenoDXCommander/Models/AuxInstalledRecord.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `aa81dd873a84` |
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
| `RenoDXCommander/Models/InstalledModRecord.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/REFramework.cs`, `RHI.Linux.Core/OptiScaler.cs` | `d6bb53c6a2c4` |
| `RenoDXCommander/Models/LumaInstalledRecord.cs` | deferred | See explicit exclusion in JSON | `3f32b839d2a6` |
| `RenoDXCommander/Models/LumaMod.cs` | deferred | See explicit exclusion in JSON | `2e7b172a0da1` |
| `RenoDXCommander/Models/ManifestDllNames.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `c928180e834c` |
| `RenoDXCommander/Models/NexusModsGame.cs` | deferred | See explicit exclusion in JSON | `6f1da63f395d` |
| `RenoDXCommander/Models/NvApiSettingsSnapshot.cs` | windows-only | See explicit exclusion in JSON | `949b44b73ec2` |
| `RenoDXCommander/Models/OptiScalerWikiData.cs` | deferred | See explicit exclusion in JSON | `39246b655b0c` |
| `RenoDXCommander/Models/OsPreset.cs` | deferred | See explicit exclusion in JSON | `16c8535c29dc` |
| `RenoDXCommander/Models/PcgwApiInfo.cs` | deferred | See explicit exclusion in JSON | `1a36aaf4b3ef` |
| `RenoDXCommander/Models/REFrameworkInstalledRecord.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/REFramework.cs`, `RHI.Linux.Core/OptiScaler.cs` | `59473b38c5a8` |
| `RenoDXCommander/Models/RemoteManifest.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `fa450d2716fa` |
| `RenoDXCommander/Models/RenoDXDbUnityEntry.cs` | deferred | See explicit exclusion in JSON | `135d0060b836` |
| `RenoDXCommander/Models/RenoDXDbUnrealEntry.cs` | deferred | See explicit exclusion in JSON | `cccf7dbd0bf1` |
| `RenoDXCommander/Models/RhiInstallManifest.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/REFramework.cs`, `RHI.Linux.Core/OptiScaler.cs` | `c07eb324dabc` |
| `RenoDXCommander/Models/SavedGame.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `750eb4ad429e` |
| `RenoDXCommander/Models/SavedGameLibrary.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `f279e7369eb9` |
| `RenoDXCommander/Models/ShaderProfile.cs` | deferred | See explicit exclusion in JSON | `38447e56eb30` |
| `RenoDXCommander/Models/SteamStoreSearchResponse.cs` | deferred | See explicit exclusion in JSON | `15f1626a5223` |
| `RenoDXCommander/Models/ViewLayout.cs` | adapted | `RHI.Linux/MainWindow.cs` | `f722974b669f` |
| `RenoDXCommander/NativeInterop.cs` | windows-only | See explicit exclusion in JSON | `fa51a25d1859` |
| `RenoDXCommander/OptiScaler.amd-dlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `fa98579f18f3` |
| `RenoDXCommander/OptiScaler.amd-nodlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `a9ef7643ebac` |
| `RenoDXCommander/OptiScaler.nvidia.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `fa98579f18f3` |
| `RenoDXCommander/OptiScaler_dlssnr.amd-dlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `884f5185ad63` |
| `RenoDXCommander/OptiScaler_dlssnr.amd-nodlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `874d064f99a7` |
| `RenoDXCommander/OptiScaler_dlssnr.nvidia.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `884f5185ad63` |
| `RenoDXCommander/OptiScaler_nightly.amd-dlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `cf3ca875b28d` |
| `RenoDXCommander/OptiScaler_nightly.amd-nodlss.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `cf3ca875b28d` |
| `RenoDXCommander/OptiScaler_nightly.nvidia.ini` | data | `RHI.Linux.Core/OptiScaler.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `cf3ca875b28d` |
| `RenoDXCommander/PresetPopupHelper.cs` | deferred | See explicit exclusion in JSON | `18108961d0d8` |
| `RenoDXCommander/ReShade.Vulkan.ini` | windows-only | See explicit exclusion in JSON | `dd084a0c82d1` |
| `RenoDXCommander/ReShade.ini` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/IniSettings.cs` | `1d933fd9d580` |
| `RenoDXCommander/ReShade64.json` | windows-only | See explicit exclusion in JSON | `dfdc50064270` |
| `RenoDXCommander/RenoDXCommander.csproj` | adapted | `RHI.Core/RHI.Core.csproj`, `RHI.Linux.sln`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `9946d5af9519` |
| `RenoDXCommander/ResourceKeys.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `c4c570214edd` |
| `RenoDXCommander/Services/AddonFileWatcher.cs` | deferred | See explicit exclusion in JSON | `829bdd240b4e` |
| `RenoDXCommander/Services/AddonInfoResolver.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs` | `5e5788ad1bc3` |
| `RenoDXCommander/Services/AddonPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `65453db05838` |
| `RenoDXCommander/Services/AddonsIniParser.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `9daa2c1b4dcf` |
| `RenoDXCommander/Services/AsyncExtensions.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `347ed142de30` |
| `RenoDXCommander/Services/AutoUpdateService.cs` | deferred | See explicit exclusion in JSON | `73f5c9663d32` |
| `RenoDXCommander/Services/AuxInstallService.DllIdentification.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `80759b71c2b4` |
| `RenoDXCommander/Services/AuxInstallService.GacSymlink.cs` | windows-only | See explicit exclusion in JSON | `8b733692a677` |
| `RenoDXCommander/Services/AuxInstallService.Ini.cs` | adapted | `RHI.Linux.Core/IniSettings.cs`, `RHI.Linux.Core/Installation.cs` | `8cedf6b4a4e9` |
| `RenoDXCommander/Services/AuxInstallService.Install.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `81bbb7249c97` |
| `RenoDXCommander/Services/AuxInstallService.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `1bd4d071f10f` |
| `RenoDXCommander/Services/CrashReporter.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `efa9b1d4f563` |
| `RenoDXCommander/Services/CrashReporterService.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `1cb2163c916a` |
| `RenoDXCommander/Services/CustomReShadeHashService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/InstallationStatus.cs` | `659edda77fd4` |
| `RenoDXCommander/Services/DevUnlockService.cs` | deferred | See explicit exclusion in JSON | `0c1c1fc81bcd` |
| `RenoDXCommander/Services/DgVoodooService.cs` | adapted | `RHI.Linux.Core/NeuralRendering.DgVoodoo.cs` | `e165aa5da0a4` |
| `RenoDXCommander/Services/DigitalVibranceService.cs` | windows-only | See explicit exclusion in JSON | `df2381aeaccc` |
| `RenoDXCommander/Services/DllArchive.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/DllOverrideService.cs` | adapted | `RHI.Linux.Core/Proton.cs` | `1236f550527e` |
| `RenoDXCommander/Services/DlssEnablerService.cs` | deferred | See explicit exclusion in JSON | `baa1be620905` |
| `RenoDXCommander/Services/DlssFileDiscovery.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/DlssNrCostScalerService.cs` | adapted | `RHI.Linux.Core/NeuralRendering.CostScaler.cs` | `f5dc0603e709` |
| `RenoDXCommander/Services/DlssPresetService.DriverSettings.cs` | adapted | `RHI.Linux.Core/DlssProfile.cs`, `RHI.Linux.Core/Proton.cs` | `d94f06b5c5f1` |
| `RenoDXCommander/Services/DlssPresetService.Export.cs` | windows-only | See explicit exclusion in JSON | `587950b75b76` |
| `RenoDXCommander/Services/DlssPresetService.ProfileMatching.cs` | windows-only | See explicit exclusion in JSON | `c04ca415f208` |
| `RenoDXCommander/Services/DlssPresetService.ReBar.cs` | windows-only | See explicit exclusion in JSON | `b2d18582d40b` |
| `RenoDXCommander/Services/DlssPresetService.Reset.cs` | adapted | `RHI.Linux.Core/DlssProfile.cs`, `RHI.Linux.Core/Proton.cs` | `3fd091f6f628` |
| `RenoDXCommander/Services/DlssPresetService.cs` | adapted | `RHI.Linux.Core/DlssProfile.cs`, `RHI.Linux.Core/Proton.cs` | `d5454caf0f33` |
| `RenoDXCommander/Services/DlssStreamlineService.Detection.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `68b1c15a6dce` |
| `RenoDXCommander/Services/DlssStreamlineService.Swap.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `943736d7e9b2` |
| `RenoDXCommander/Services/DlssStreamlineService.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `087750b68a8a` |
| `RenoDXCommander/Services/DlssVersion.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/Dlssg20_30Service.cs` | deferred | See explicit exclusion in JSON | `cde995ceea36` |
| `RenoDXCommander/Services/DofFixService.cs` | deferred | See explicit exclusion in JSON | `ffc9a0089db4` |
| `RenoDXCommander/Services/DownloadPaths.cs` | adapted | `RHI.Linux.Core/LinuxPaths.cs` | `7956a5894196` |
| `RenoDXCommander/Services/DownloadsMigrationService.cs` | windows-only | See explicit exclusion in JSON | `f4c7011f9d07` |
| `RenoDXCommander/Services/DxvkService.Install.cs` | windows-only | See explicit exclusion in JSON | `1a316f6d74bf` |
| `RenoDXCommander/Services/DxvkService.Staging.cs` | windows-only | See explicit exclusion in JSON | `5039d63bca76` |
| `RenoDXCommander/Services/DxvkService.Tracking.cs` | windows-only | See explicit exclusion in JSON | `48270128963c` |
| `RenoDXCommander/Services/DxvkService.cs` | windows-only | See explicit exclusion in JSON | `3b60834b769d` |
| `RenoDXCommander/Services/FeatureFlags.cs` | deferred | See explicit exclusion in JSON | `27c6f1cb0a9d` |
| `RenoDXCommander/Services/FileAssociationService.cs` | windows-only | See explicit exclusion in JSON | `24756b8fc126` |
| `RenoDXCommander/Services/FileHelper.cs` | adapted | `RHI.Linux.Core/LinuxPaths.cs`, `RHI.Linux.Core/Installation.cs` | `01e25dd52dae` |
| `RenoDXCommander/Services/GameDetectionService.Platform.cs` | windows-only | See explicit exclusion in JSON | `e74a9842527a` |
| `RenoDXCommander/Services/GameDetectionService.Steam.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `cd5358909870` |
| `RenoDXCommander/Services/GameDetectionService.Xbox.cs` | windows-only | See explicit exclusion in JSON | `e6b273d3114e` |
| `RenoDXCommander/Services/GameDetectionService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `d9269803c093` |
| `RenoDXCommander/Services/GameInitializationService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `7781ec7764a6` |
| `RenoDXCommander/Services/GameLibraryService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `eda9c3e80722` |
| `RenoDXCommander/Services/GameNameService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `50fb03996e54` |
| `RenoDXCommander/Services/GitHubETagCache.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `fe497df80826` |
| `RenoDXCommander/Services/GraphicsApiDetector.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `dae13175da2c` |
| `RenoDXCommander/Services/HdrDatabaseService.cs` | deferred | See explicit exclusion in JSON | `549e13e3ef0b` |
| `RenoDXCommander/Services/HdrToggleService.cs` | windows-only | See explicit exclusion in JSON | `4248a409aca4` |
| `RenoDXCommander/Services/IAddonPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `753a7cfe57b4` |
| `RenoDXCommander/Services/IAuxFileService.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `66a7d2c4a8e4` |
| `RenoDXCommander/Services/IAuxInstallService.cs` | adapted | `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/InstallationStatus.cs`, `RHI.Linux.Core/GameSetup.cs` | `5addbb36d5a7` |
| `RenoDXCommander/Services/ICrashReporter.cs` | adapted | `RHI.Linux.Core/CrashReporter.cs`, `RHI.Core/CoreLog.cs` | `ca77a1407527` |
| `RenoDXCommander/Services/IDllOverrideService.cs` | adapted | `RHI.Linux.Core/Proton.cs` | `0992374c5acc` |
| `RenoDXCommander/Services/IDlssStreamlineService.cs` | adapted | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/PeVersion.cs` | `44df507c1ffa` |
| `RenoDXCommander/Services/IDofFixService.cs` | deferred | See explicit exclusion in JSON | `e0b52b9a6aa0` |
| `RenoDXCommander/Services/IDxvkService.cs` | windows-only | See explicit exclusion in JSON | `f0e8faffb7b9` |
| `RenoDXCommander/Services/IGameDetectionService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `6d8af520637a` |
| `RenoDXCommander/Services/IGameInitializationService.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs`, `RHI.Linux.Core/Catalog.cs` | `70a4b89e1542` |
| `RenoDXCommander/Services/IGameLibraryService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `0a7beef72e59` |
| `RenoDXCommander/Services/IGameNameService.cs` | adapted | `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/GameDiscovery.cs` | `bc0f8e603fa3` |
| `RenoDXCommander/Services/ILiliumShaderService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `c1ab4b78863f` |
| `RenoDXCommander/Services/ILumaService.cs` | deferred | See explicit exclusion in JSON | `886602926e6d` |
| `RenoDXCommander/Services/IManifestService.cs` | adapted | `RHI.Linux.Core/Catalog.cs`, `RHI.Core/Sources.cs` | `2a997c275184` |
| `RenoDXCommander/Services/IModInstallService.cs` | adapted | `RHI.Linux.Core/GameSetup.cs`, `RHI.Linux.Core/Installation.cs` | `c001e78bc8c1` |
| `RenoDXCommander/Services/INexusModsService.cs` | deferred | See explicit exclusion in JSON | `04191a94f27b` |
| `RenoDXCommander/Services/INormalReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `ef5c26d17323` |
| `RenoDXCommander/Services/IOptiScalerGame.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/IOptiScalerService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `701a592ace31` |
| `RenoDXCommander/Services/IPcgwService.cs` | deferred | See explicit exclusion in JSON | `06f4fa193594` |
| `RenoDXCommander/Services/IPeHeaderService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `6f3d56d69865` |
| `RenoDXCommander/Services/IREFrameworkService.cs` | adapted | `RHI.Linux.Core/REFramework.cs` | `582cdd597eb5` |
| `RenoDXCommander/Services/IReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `ffa2d61bc431` |
| `RenoDXCommander/Services/IRenoDXDbService.cs` | deferred | See explicit exclusion in JSON | `fe10e8cb6d36` |
| `RenoDXCommander/Services/ISevenZipExtractor.cs` | adapted | `RHI.Linux.Core/ArchiveTools.cs`, `RHI.Linux.Core/Downloads.cs`, `scripts/bundle-linux-dependencies.sh` | `508ad1b9340d` |
| `RenoDXCommander/Services/IShaderPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `f288a0d37024` |
| `RenoDXCommander/Services/ISteamAppIdResolver.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs` | `1be7dff0402d` |
| `RenoDXCommander/Services/IUltraPlusService.cs` | deferred | See explicit exclusion in JSON | `5a5a58f3cc2c` |
| `RenoDXCommander/Services/IUltrawideFixService.cs` | deferred | See explicit exclusion in JSON | `77fc808e1b87` |
| `RenoDXCommander/Services/IUpdateOrchestrationService.cs` | adapted | `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/GameSetup.cs` | `02cbd891cb25` |
| `RenoDXCommander/Services/IUpdateService.cs` | deferred | See explicit exclusion in JSON | `248070994cc0` |
| `RenoDXCommander/Services/IWikiService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `d3f8255f2bc7` |
| `RenoDXCommander/Services/LiliumShaderService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs` | `c317ede115fe` |
| `RenoDXCommander/Services/LumaService.cs` | deferred | See explicit exclusion in JSON | `2d98cb6d9d24` |
| `RenoDXCommander/Services/ManifestService.cs` | adapted | `RHI.Linux.Core/Catalog.cs`, `RHI.Core/Sources.cs` | `d0b15bfda55e` |
| `RenoDXCommander/Services/ModInstallService.cs` | adapted | `RHI.Linux.Core/GameSetup.cs`, `RHI.Linux.Core/Installation.cs` | `4838358610fb` |
| `RenoDXCommander/Services/MotdService.cs` | deferred | See explicit exclusion in JSON | `a17301adf6e4` |
| `RenoDXCommander/Services/NexusDownloadService.cs` | deferred | See explicit exclusion in JSON | `926285eaeceb` |
| `RenoDXCommander/Services/NexusModsService.cs` | deferred | See explicit exclusion in JSON | `d7d29b83f8de` |
| `RenoDXCommander/Services/NexusSsoService.cs` | deferred | See explicit exclusion in JSON | `079e93bda469` |
| `RenoDXCommander/Services/NexusUpdateService.cs` | deferred | See explicit exclusion in JSON | `e58e8563ab83` |
| `RenoDXCommander/Services/NormalReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `1de4e8ac6ef8` |
| `RenoDXCommander/Services/NvColorService.cs` | windows-only | See explicit exclusion in JSON | `ad780f63e14c` |
| `RenoDXCommander/Services/NxmProtocolHandler.cs` | windows-only | See explicit exclusion in JSON | `0b049af1a00d` |
| `RenoDXCommander/Services/OptiScalerPolicy.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/OptiScalerService.Coexist.cs` | adapted | `RHI.Linux.Core/OptiScaler.Coexist.cs` | `cbaf496903bd` |
| `RenoDXCommander/Services/OptiScalerService.Install.cs` | adapted | `RHI.Linux.Core/OptiScaler.Install.cs` | `a81b7009ac36` |
| `RenoDXCommander/Services/OptiScalerService.Staging.cs` | adapted | `RHI.Linux.Core/OptiScaler.Staging.cs` | `0bb9cd3119cf` |
| `RenoDXCommander/Services/OptiScalerService.Streamline.cs` | adapted | `RHI.Linux.Core/OptiScaler.Streamline.cs` | `927938f6501b` |
| `RenoDXCommander/Services/OptiScalerService.cs` | adapted | `RHI.Linux.Core/OptiScaler.cs` | `4096e228e457` |
| `RenoDXCommander/Services/OptiScalerWikiService.cs` | deferred | See explicit exclusion in JSON | `e806d86538d9` |
| `RenoDXCommander/Services/OsPresetService.cs` | deferred | See explicit exclusion in JSON | `7bed8ee94c05` |
| `RenoDXCommander/Services/PcgwService.cs` | deferred | See explicit exclusion in JSON | `f1752fc3fdc0` |
| `RenoDXCommander/Services/PeHeaderService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `d5d69e22c7f4` |
| `RenoDXCommander/Services/PresetValidator.cs` | deferred | See explicit exclusion in JSON | `1ca6db14eb61` |
| `RenoDXCommander/Services/REFrameworkArchive.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/REFrameworkService.cs` | adapted | `RHI.Linux.Core/REFramework.cs` | `67ece0f1fdc3` |
| `RenoDXCommander/Services/ReShadeNightlyService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `d5892e5ab35a` |
| `RenoDXCommander/Services/ReShadeUpdateService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/GameSetup.cs` | `de91f05c3da4` |
| `RenoDXCommander/Services/RenoDXDbService.cs` | deferred | See explicit exclusion in JSON | `c023fe3641f7` |
| `RenoDXCommander/Services/Renodx5AddonService.cs` | adapted | `RHI.Linux.Core/NeuralRendering.Staging.cs`, `RHI.Linux.Core/NeuralRendering.Install.cs` | `eff41d5f1900` |
| `RenoDXCommander/Services/ResolutionToggleService.cs` | windows-only | See explicit exclusion in JSON | `283e8b513c0d` |
| `RenoDXCommander/Services/Rtx40MfgService.cs` | deferred | See explicit exclusion in JSON | `6f246e6b21a5` |
| `RenoDXCommander/Services/SeenLumaModsService.cs` | deferred | See explicit exclusion in JSON | `48a05691305b` |
| `RenoDXCommander/Services/SeenUltraPlusModsService.cs` | deferred | See explicit exclusion in JSON | `94878fc32451` |
| `RenoDXCommander/Services/SeenWikiModsService.cs` | adapted | `RHI.Linux.Core/Catalog.cs`, `RHI.Linux.Core/Game.cs` | `f544f209dc7f` |
| `RenoDXCommander/Services/SentinelFiles.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/SevenZipExtractor.cs` | adapted | `RHI.Linux.Core/ArchiveTools.cs`, `RHI.Linux.Core/Downloads.cs`, `scripts/bundle-linux-dependencies.sh` | `a7bd5b985a50` |
| `RenoDXCommander/Services/ShaderPackCatalog.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `local-only` |
| `RenoDXCommander/Services/ShaderPackService.Deploy.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/ShaderArchives.cs` | `745d57be8061` |
| `RenoDXCommander/Services/ShaderPackService.Download.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/ShaderArchives.cs` | `8e41add537e6` |
| `RenoDXCommander/Services/ShaderPackService.IncludeScan.cs` | deferred | See explicit exclusion in JSON | `2f3079222704` |
| `RenoDXCommander/Services/ShaderPackService.cs` | adapted | `RHI.Linux.Core/Downloads.cs`, `RHI.Linux.Core/Installation.cs`, `RHI.Linux.Core/ShaderArchives.cs` | `c6ecbaea762e` |
| `RenoDXCommander/Services/ShaderProfileService.cs` | deferred | See explicit exclusion in JSON | `58c286158205` |
| `RenoDXCommander/Services/ShaderResolver.cs` | deferred | See explicit exclusion in JSON | `334d47591000` |
| `RenoDXCommander/Services/SingleInstanceService.cs` | windows-only | See explicit exclusion in JSON | `c49f47337bab` |
| `RenoDXCommander/Services/SteamAppIdResolver.cs` | adapted | `RHI.Linux.Core/GameDiscovery.cs` | `7cbda07b0fcb` |
| `RenoDXCommander/Services/TechniquesParser.cs` | deferred | See explicit exclusion in JSON | `325583bcbccd` |
| `RenoDXCommander/Services/TrayIconService.cs` | windows-only | See explicit exclusion in JSON | `35b079f74a11` |
| `RenoDXCommander/Services/UltimateAsiLoaderService.cs` | deferred | See explicit exclusion in JSON | `6a6478f19605` |
| `RenoDXCommander/Services/UltraPlusService.cs` | deferred | See explicit exclusion in JSON | `bc2051ca7952` |
| `RenoDXCommander/Services/UltrawideFixService.cs` | deferred | See explicit exclusion in JSON | `e7b3272b50a8` |
| `RenoDXCommander/Services/UpdateOrchestrationService.cs` | adapted | `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/GameSetup.cs` | `fa6c9edbc511` |
| `RenoDXCommander/Services/UpdateService.cs` | deferred | See explicit exclusion in JSON | `9066709a8a3b` |
| `RenoDXCommander/Services/VulkanFootprintService.cs` | windows-only | See explicit exclusion in JSON | `0e996fa89f38` |
| `RenoDXCommander/Services/VulkanLayerService.cs` | windows-only | See explicit exclusion in JSON | `9b7feb0a099d` |
| `RenoDXCommander/Services/WikiService.cs` | shared | `RHI.Core/SharedSources.props`, `RHI.Core/RHI.Core.csproj` | `21c17cb00e0d` |
| `RenoDXCommander/SettingsHandler.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `db7e1d226326` |
| `RenoDXCommander/SetupWindow.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `89e1f40041db` |
| `RenoDXCommander/SetupWindow.xaml.cs` | adapted | `RHI.Linux/Program.cs`, `RHI.Linux/MainWindow.cs` | `360ca810311c` |
| `RenoDXCommander/ShaderPopupHelper.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux.Core/Downloads.cs` | `46d173b58347` |
| `RenoDXCommander/Themes/DarkTheme.xaml` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `25e277e52d85` |
| `RenoDXCommander/UIFactory.cs` | adapted | `RHI.Linux/Theme.axaml`, `RHI.Linux/MainWindow.cs` | `b329e0484a71` |
| `RenoDXCommander/UpdateInclusionHelper.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux/MainWindow.Dialogs.cs` | `5f0b4dff1ab3` |
| `RenoDXCommander/ViewModels/FilterViewModel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `30300b8eb29c` |
| `RenoDXCommander/ViewModels/GameCardViewModel.DisplayCommander.cs` | deferred | See explicit exclusion in JSON | `322624119172` |
| `RenoDXCommander/ViewModels/GameCardViewModel.DlssStreamline.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `96e5a0557b4a` |
| `RenoDXCommander/ViewModels/GameCardViewModel.DofFix.cs` | deferred | See explicit exclusion in JSON | `e656de4c0e49` |
| `RenoDXCommander/ViewModels/GameCardViewModel.Dxvk.cs` | deferred | See explicit exclusion in JSON | `d340874c0034` |
| `RenoDXCommander/ViewModels/GameCardViewModel.Luma.cs` | deferred | See explicit exclusion in JSON | `6170edfda7fe` |
| `RenoDXCommander/ViewModels/GameCardViewModel.NormalReShade.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `7c567838d444` |
| `RenoDXCommander/ViewModels/GameCardViewModel.NvidiaProfile.cs` | adapted | `RHI.Linux/MainWindow.Overrides.Dlss.cs`, `RHI.Linux/MainWindow.Overrides.DriverSettings.cs`, `RHI.Linux.Core/DlssProfile.cs` | `516d19b9596b` |
| `RenoDXCommander/ViewModels/GameCardViewModel.OptiScaler.cs` | adapted | `RHI.Linux/MainWindow.OptiScaler.cs` | `2ffe5c4d4482` |
| `RenoDXCommander/ViewModels/GameCardViewModel.REFramework.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `b738d7b30441` |
| `RenoDXCommander/ViewModels/GameCardViewModel.ReShade.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `6b0a696671c1` |
| `RenoDXCommander/ViewModels/GameCardViewModel.RenoDX.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `1243ebe80cea` |
| `RenoDXCommander/ViewModels/GameCardViewModel.UI.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `7e9c01922751` |
| `RenoDXCommander/ViewModels/GameCardViewModel.UltraLimiter.cs` | deferred | See explicit exclusion in JSON | `5603e5c57ee0` |
| `RenoDXCommander/ViewModels/GameCardViewModel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `0244e2e7070f` |
| `RenoDXCommander/ViewModels/MainViewModel.BackgroundScan.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `4ff0d0d5ab9a` |
| `RenoDXCommander/ViewModels/MainViewModel.BuildCards.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `8f54d8f07315` |
| `RenoDXCommander/ViewModels/MainViewModel.CacheLoad.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `4c5135e06406` |
| `RenoDXCommander/ViewModels/MainViewModel.Dxvk.cs` | deferred | See explicit exclusion in JSON | `3f6da04d621c` |
| `RenoDXCommander/ViewModels/MainViewModel.GameMatching.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `8088fa9174b4` |
| `RenoDXCommander/ViewModels/MainViewModel.Init.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `715a1f256af8` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.Components.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `b6ee52a16154` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.Luma.cs` | deferred | See explicit exclusion in JSON | `99ad61fc9b45` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.Nexus.cs` | deferred | See explicit exclusion in JSON | `9c5c9335d4fc` |
| `RenoDXCommander/ViewModels/MainViewModel.Install.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `66c6719ebe27` |
| `RenoDXCommander/ViewModels/MainViewModel.Settings.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `e4b5bb9b8863` |
| `RenoDXCommander/ViewModels/MainViewModel.Update.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `80a703ef718b` |
| `RenoDXCommander/ViewModels/MainViewModel.cs` | adapted | `RHI.Linux/MainWindow.cs`, `RHI.Linux/MainWindow.Game.cs`, `RHI.Linux.Core/Game.cs`, `RHI.Linux.Core/Catalog.cs` | `b3340052206e` |
| `RenoDXCommander/ViewModels/SettingsViewModel.cs` | adapted | `RHI.Linux/MainWindow.Dialogs.cs`, `RHI.Linux/AdvancedWindow.cs`, `RHI.Linux.Core/Game.cs` | `54c2270366ed` |
| `RenoDXCommander/WindowStateManager.cs` | windows-only | See explicit exclusion in JSON | `8b598752a9ab` |
| `RenoDXCommander/app.manifest` | windows-only | See explicit exclusion in JSON | `0a022b747e71` |
| `RenoDXCommander/dxvk.conf` | windows-only | See explicit exclusion in JSON | `492043b2a8cb` |
| `RenoDXCommander/relimiter.ini` | windows-only | See explicit exclusion in JSON | `909a602c4110` |
| `RenoDXCommander/reshade.rdr2.ini` | windows-only | See explicit exclusion in JSON | `5858aa5aff29` |
| `database/RenoDXdb-unity.json` | deferred | See explicit exclusion in JSON | `987a0e35c651` |
| `database/pcgw_data.json` | deferred | See explicit exclusion in JSON | `614de218480a` |
| `dlss_manifest.json` | data | `RHI.Linux.Core/Dlss.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `38fef444b40e` |
| `docs/RenoDXdb.json` | deferred | See explicit exclusion in JSON | `d653817ffca6` |
| `engine-files/black-myth-wukong.ini` | deferred | See explicit exclusion in JSON | `8cda6b6ebd2a` |
| `game-db/game_db.json` | deferred | See explicit exclusion in JSON | `fd3e7dc48ad9` |
| `manifest.json` | data | `RHI.Linux.Core/Catalog.cs`, `RHI.Linux.Core/RHI.Linux.Core.csproj` | `b91c438d5c99` |
<!-- END SOURCE MAP -->
