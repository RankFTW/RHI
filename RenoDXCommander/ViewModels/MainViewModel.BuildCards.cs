// MainViewModel.BuildCards.cs -- Card building, engine detection, and game matching.

using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private List<GameCardViewModel> BuildCards(
        List<DetectedGame> detectedGames,
        List<InstalledModRecord> records,
        List<AuxInstalledRecord> auxRecords,
        Dictionary<string, bool> addonCache,
        Dictionary<string, string> genericNotes)
    {
        var cards = new List<GameCardViewModel>();
        var genericUnreal = MakeGenericUnreal();
        var genericUnity  = MakeGenericUnity();

        // ── Apply splitGames manifest entries ─────────────────────────────────
        if (_manifest?.SplitGames != null && _manifest.SplitGames.Count > 0)
        {
            var expanded = new List<DetectedGame>();
            foreach (var game in detectedGames)
            {
                if (_manifest.SplitGames.TryGetValue(game.Name, out var splits) && splits.Count > 0)
                {
                    // Replace the single entry with N split entries
                    var basePath = game.InstallPath;
                    // If the detected path already includes a subpath (e.g. from installPathOverrides),
                    // go up to the common root first
                    foreach (var split in splits)
                    {
                        var splitPath = Path.Combine(basePath, split.SubPath);
                        if (Directory.Exists(splitPath))
                        {
                            expanded.Add(new DetectedGame
                            {
                                Name = split.Name,
                                InstallPath = splitPath,
                                Source = game.Source,
                                SteamAppId = game.SteamAppId,
                                EpicCatalogNamespace = game.EpicCatalogNamespace,
                                EpicAppName = game.EpicAppName,
                            });
                        }
                    }
                    _crashReporter.Log($"[BuildCards] Split '{game.Name}' into {splits.Count} sub-games");
                }
                else
                {
                    expanded.Add(game);
                }
            }
            detectedGames = expanded;
        }

        // Load RE Framework install records for matching to cards
        var refRecords = _refService.GetRecords();

        // Thread-safe caches populated during parallel detection, saved to library afterwards.
        var newEngineTypeCache   = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newResolvedPathCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newBitnessCache      = new ConcurrentDictionary<string, MachineType>(StringComparer.OrdinalIgnoreCase);

        var gameInfos = detectedGames.AsParallel().Select(game =>
        {
            string installPath;
            EngineType engine;

            var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();

            // Check for manifest engine override first — if one exists, skip the
            // expensive filesystem-based engine detection entirely since the result
            // would be overridden anyway. This prevents repeated full directory
            // traversals for games with custom engine names (e.g. Northlight, Anvil)
            // that map to EngineType.Unknown and bypass the engine cache.
            var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);

            if (engineOverrideLabel != null
                && _resolvedPathCache.TryGetValue(rootKey, out var overrideCachedPath)
                && Directory.Exists(overrideCachedPath))
            {
                // Manifest override + cached path → skip detection completely
                installPath = overrideCachedPath;
                engine = engineOverride;
            }
            else if (engineOverrideLabel != null)
            {
                // Manifest override but no cached path → detect path only, use override engine
                (installPath, _) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
                engine = engineOverride;
            }
            else if (_engineTypeCache.TryGetValue(rootKey, out var cachedEngine)
                && !string.Equals(cachedEngine, nameof(EngineType.Unknown), StringComparison.OrdinalIgnoreCase)
                && _resolvedPathCache.TryGetValue(rootKey, out var cachedPath)
                && Directory.Exists(cachedPath))
            {
                installPath = cachedPath;
                engine = Enum.TryParse<EngineType>(cachedEngine, out var e) ? e : EngineType.Unknown;
            }
            else
            {
                (installPath, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
            }

            // Apply manifest engine override (takes priority over auto-detection and cache)
            if (engineOverrideLabel != null) engine = engineOverride;

            // Record for saving
            newEngineTypeCache[rootKey]   = engine.ToString();
            newResolvedPathCache[rootKey] = installPath;

            // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
            if (_installPathOverrides.TryGetValue(game.Name, out var subPath))
            {
                var overridePath = Path.Combine(game.InstallPath, subPath);
                if (Directory.Exists(overridePath))
                    installPath = overridePath;
            }

            // Detect bitness: use cached value if available, otherwise run PE detection.
            MachineType machineType;
            var resolvedKey = installPath.ToLowerInvariant();
            if (_bitnessCache.TryGetValue(resolvedKey, out var cachedMachine))
            {
                machineType = cachedMachine;
            }
            else
            {
                machineType = _peHeaderService.DetectGameArchitecture(installPath);
            }
            newBitnessCache[resolvedKey] = machineType;

            var mod      = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
            // Wiki unlink: completely disconnect the game from wiki — no mod, no generic fallback
            bool isWikiUnlinked = _manifestWikiUnlinks.Contains(game.Name);
            if (isWikiUnlinked) mod = null;
            // UnrealLegacy (UE3 and below) cannot use the RenoDX addon system — no fallback mod offered.
            var fallback = (mod == null && !isWikiUnlinked)
                           ? (engine == EngineType.Unreal      ? genericUnreal
                            : engine == EngineType.Unity       ? genericUnity : null) : null;

            // If the wiki mod matched but has no download URL (common for games listed
            // in the generic engine tables), inject the generic engine addon URL so the
            // install button works. The wiki mod's status and notes are preserved.
            if (mod != null && mod.SnapshotUrl == null && mod.NexusUrl == null && mod.DiscordUrl == null)
            {
                var engineFallback = engine == EngineType.Unreal ? genericUnreal
                                   : engine == EngineType.Unity  ? genericUnity : null;
                if (engineFallback != null)
                {
                    mod = new GameMod
                    {
                        Name            = mod.Name,
                        Maintainer      = engineFallback.Maintainer,
                        SnapshotUrl     = engineFallback.SnapshotUrl,
                        SnapshotUrl32   = engineFallback.SnapshotUrl32,
                        Status          = mod.Status,
                        Notes           = mod.Notes,
                        NameUrl         = mod.NameUrl,
                        IsGenericUnreal = engineFallback.IsGenericUnreal,
                        IsGenericUnity  = engineFallback.IsGenericUnity,
                    };
                    fallback = engineFallback;
                }
            }

            return (game, installPath, engine, mod, fallback, machineType, engineOverrideLabel);
        }).ToList();

        // Snapshot the new caches for SaveLibrary.
        _engineTypeCache   = new Dictionary<string, string>(newEngineTypeCache, StringComparer.OrdinalIgnoreCase);
        _resolvedPathCache = new Dictionary<string, string>(newResolvedPathCache, StringComparer.OrdinalIgnoreCase);
        _bitnessCache      = new Dictionary<string, MachineType>(newBitnessCache, StringComparer.OrdinalIgnoreCase);
        var newAddonFileCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var safeAddonCache = new ConcurrentDictionary<string, bool>(addonCache, StringComparer.OrdinalIgnoreCase);
        var cardBag = new ConcurrentBag<GameCardViewModel>();

        var slowGameThresholdMs = 500; // Log games that take longer than this
        var gameTimings = new ConcurrentBag<(string name, long ms)>();

        Parallel.ForEach(gameInfos, (item) =>
        {
            var gameStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var (game, installPath, engine, mod, origFallback, detectedMachine, engineOverrideLabel) = item;
            // Always show every detected game — even if no wiki mod exists.
            // The card will have no install button if there's no snapshot URL,
            // but a RenoDX addon already on disk will still be detected and shown.
            // Wiki exclusion overrides everything — user explicitly wants no wiki match
            var fallback     = origFallback;  // mutable local copy
            var effectiveMod = _wikiExclusions.Contains(game.Name) ? null : (mod ?? fallback);

            var record = records.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

            // ── Path reconciliation for RenoDX mod records ────────────────────────
            // Xbox/Microsoft Store games change install paths on every update
            // (e.g. version number embedded in the WindowsApps folder name).
            // When the record's GameName matches but InstallPath differs, try to
            // migrate the addon file to the new path so the mod stays detected.
            if (record != null
                && !record.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = record.InstallPath;
                var addonFile = record.AddonFileName;
                var newFilePath = string.IsNullOrEmpty(addonFile) ? null : Path.Combine(installPath, addonFile);
                var oldFilePath = string.IsNullOrEmpty(addonFile) ? null : Path.Combine(oldPath, addonFile);

                if (newFilePath != null && File.Exists(newFilePath))
                {
                    // Addon already exists at the new path (user may have reinstalled)
                    _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' path changed '{oldPath}' → '{installPath}', addon already at new path");
                }
                else if (oldFilePath != null && File.Exists(oldFilePath))
                {
                    // Try to copy the addon from the old path to the new path
                    try
                    {
                        var newDeployDir = Path.GetDirectoryName(newFilePath!)!;
                        Directory.CreateDirectory(newDeployDir);
                        File.Copy(oldFilePath, newFilePath!, overwrite: true);
                        _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' copied addon '{addonFile}' from '{oldPath}' → '{installPath}'");
                    }
                    catch (Exception ex)
                    {
                        // WindowsApps or other restricted paths may deny access — that's OK
                        _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' failed to copy addon from '{oldPath}' → '{installPath}' — {ex.Message}");
                    }
                }
                else
                {
                    _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' path changed '{oldPath}' → '{installPath}', addon not found at either path (mod lost during game update)");
                }

                // Always update the record to the new detected path
                record.InstallPath = installPath;
                _installer.SaveRecordPublic(record);
            }

            // Fallback: match by InstallPath for records saved with mod name instead of game name
            // (e.g. "Generic Unreal Engine" from before the fix).
            if (record == null)
            {
                record = records.FirstOrDefault(r =>
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
                if (record != null)
                {
                    // Fix the record's GameName so future lookups work correctly
                    record.GameName = game.Name;
                    _installer.SaveRecordPublic(record);
                }
            }

            // Always scan disk for renodx-* addon files — catches manual installs and
            // games not yet on the wiki that already have a mod installed.
            // Use the addon file cache to skip expensive recursive scans on subsequent launches.
            // Skip for emulator cards — they manage their own addon state.
            string? addonOnDisk = null;
            var cacheKey = installPath.ToLowerInvariant();
            bool isEmulatorGame = game.Name.Equals("Ryubing", StringComparison.OrdinalIgnoreCase);

            if (isEmulatorGame)
            {
                // Emulator cards skip addon scan — handled by emulator setup below
            }
            else if (record != null)
            {
                var expectedFile = record.AddonFileName;
                if (!string.IsNullOrEmpty(expectedFile)
                    && File.Exists(Path.Combine(installPath, expectedFile)))
                {
                    addonOnDisk = expectedFile;
                }
                else
                {
                    // Record exists but file not at expected location — rescan
                    addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                }
            }
            else if (_addonFileCache.TryGetValue(cacheKey, out var cachedAddonFile))
            {
                if (!string.IsNullOrEmpty(cachedAddonFile)
                    && File.Exists(Path.Combine(installPath, cachedAddonFile)))
                {
                    addonOnDisk = cachedAddonFile;
                }
                else if (!string.IsNullOrEmpty(cachedAddonFile))
                {
                    // Cache says an addon was here but the file is gone — rescan
                    addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                }
                else
                {
                    // Cache says "" (no addon on previous scan) — do a quick direct check
                    // in case the user installed one since the last full scan.
                    addonOnDisk = ScanForInstalledAddonQuick(installPath, effectiveMod);
                }
            }
            else if (safeAddonCache.TryGetValue(cacheKey, out _))
            {
                addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
            }
            else
            {
                addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                safeAddonCache[cacheKey] = addonOnDisk != null;
            }
            newAddonFileCache[cacheKey] = addonOnDisk ?? "";

            if (addonOnDisk != null && record == null)
            {
                // Use ResolveAddonUrl so files like renodx-ue-extended.addon64 get their
                // correct source URL rather than the generic CDN URL from effectiveMod.
                record = new InstalledModRecord
                {
                    GameName      = game.Name,
                    InstallPath   = installPath,
                    AddonFileName = addonOnDisk,
                    InstalledAt   = File.GetLastWriteTimeUtc(Path.Combine(installPath, addonOnDisk)),
                    SnapshotUrl   = ResolveAddonUrl(addonOnDisk),
                };
                _installer.SaveRecordPublic(record);
            }
            else if (addonOnDisk == null && record != null)
            {
                // DB record exists but addon file is no longer on disk — user manually removed it.
                // Remove the stale record so the card shows Available rather than Installed.
                _installer.RemoveRecord(record);
                record = null;
            }

            // If the installed addon on disk has a different source URL than what the
            // wiki mod specifies (e.g. renodx-ue-extended.addon64 on a generic UE card),
            // patch effectiveMod so the install/update button uses the correct URL.
            if (addonOnDisk != null && effectiveMod?.SnapshotUrl != null
                && _addonFileUrlOverrides.TryGetValue(addonOnDisk, out var addonOverrideUrl))
            {
                effectiveMod = new GameMod
                {
                    Name        = effectiveMod.Name,
                    Maintainer  = effectiveMod.Maintainer,
                    SnapshotUrl = addonOverrideUrl,
                    Status      = effectiveMod.Status,
                    Notes       = effectiveMod.Notes,
                    NexusUrl    = effectiveMod.NexusUrl,
                    DiscordUrl  = effectiveMod.DiscordUrl,
                    NameUrl     = effectiveMod.NameUrl,
                    IsGenericUnreal = effectiveMod.IsGenericUnreal,
                    IsGenericUnity  = effectiveMod.IsGenericUnity,
                };
            }

            // Named addon found on disk but no wiki entry exists → show Discord link
            // so the user can find support/info for their mod.
            if (addonOnDisk != null && effectiveMod == null)
            {
                effectiveMod = new GameMod
                {
                    Name       = game.Name,
                    Status     = "💬",
                    DiscordUrl = "https://discord.gg/gF4GRJWZ2A",
                };
            }

            // ── Manifest snapshot override ────────────────────────────────────────
            // If the manifest provides a direct snapshot URL for this game, inject it
            // into the effectiveMod. This handles cases where the wiki parser fails to
            // capture the snapshot link or the name mapping doesn't resolve correctly.
            if (_manifest?.SnapshotOverrides != null
                && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOverrideUrl)
                && !string.IsNullOrEmpty(snapshotOverrideUrl))
            {
                if (effectiveMod != null)
                {
                    effectiveMod.SnapshotUrl = snapshotOverrideUrl;
                }
                else
                {
                    effectiveMod = new GameMod
                    {
                        Name        = game.Name,
                        SnapshotUrl = snapshotOverrideUrl,
                        Status      = "✅",
                    };
                }
            }

            // Apply UE-Extended preference: if the game has it saved OR the file is on disk,
            // force the Mod URL to the marat569 source so the install button targets it.
            // Native HDR games always use UE-Extended, regardless of user toggle.
            // UE-Extended whitelist supersedes everything — hide Nexus link and force install/update/reinstall.
            bool isNativeHdr = IsNativeHdrGameMatch(game.Name);
            bool useUeExt = (addonOnDisk == UeExtendedFile)
                            || IsUeExtendedGameMatch(game.Name)
                            || (isNativeHdr && (effectiveMod?.IsGenericUnreal == true || engine == EngineType.Unreal));
            if (useUeExt && effectiveMod != null)
            {
                // Create or override the mod to use UE-Extended URL
                effectiveMod = new GameMod
                {
                    Name            = effectiveMod?.Name ?? "Generic Unreal Engine",
                    Maintainer      = effectiveMod?.Maintainer ?? "ShortFuse",
                    SnapshotUrl     = UeExtendedUrl,
                    Status          = effectiveMod?.Status ?? "✅",
                    Notes           = effectiveMod?.Notes,
                    IsGenericUnreal = true,
                };
                // Persist preference if it was detected from disk or the game is native HDR
                if (addonOnDisk == UeExtendedFile || isNativeHdr)
                    _ueExtendedGames.Add(game.Name);
            }
            // UE-Extended whitelist games that have no engine detected — force them to use UE-Extended
            else if (useUeExt && effectiveMod == null)
            {
                effectiveMod = new GameMod
                {
                    Name            = "Generic Unreal Engine",
                    Maintainer      = "ShortFuse",
                    SnapshotUrl     = UeExtendedUrl,
                    Status          = "✅",
                    IsGenericUnreal = true,
                };
                fallback = effectiveMod;
                if (isNativeHdr)
                    _ueExtendedGames.Add(game.Name);
            }

            // UE-Extended whitelist supersedes Nexus/Discord external links — force installable
            if (useUeExt && effectiveMod != null)
            {
                // Strip Nexus/Discord links so the card shows install/update/reinstall buttons
                effectiveMod.NexusUrl   = null;
                effectiveMod.DiscordUrl = null;
            }

            // Look up aux records for this game
            var rsRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal));

            // ── Path reconciliation for ReShade aux records ───────────────────────
            // Same Xbox/Microsoft Store path-change issue as RenoDX mod records above.
            if (rsRec != null
                && !rsRec.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
            {
                var oldRsPath = rsRec.InstallPath;
                var rsFile = rsRec.InstalledAs;
                var newRsFilePath = string.IsNullOrEmpty(rsFile) ? null : Path.Combine(installPath, rsFile);
                var oldRsFilePath = string.IsNullOrEmpty(rsFile) ? null : Path.Combine(oldRsPath, rsFile);

                if (newRsFilePath != null && File.Exists(newRsFilePath))
                {
                    _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' path changed '{oldRsPath}' → '{installPath}', ReShade already at new path");
                }
                else if (oldRsFilePath != null && File.Exists(oldRsFilePath))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newRsFilePath!)!);
                        File.Copy(oldRsFilePath, newRsFilePath!, overwrite: true);
                        _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' copied ReShade '{rsFile}' from '{oldRsPath}' → '{installPath}'");
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' failed to copy ReShade from '{oldRsPath}' → '{installPath}' — {ex.Message}");
                    }
                }
                else
                {
                    _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' path changed '{oldRsPath}' → '{installPath}', ReShade not found at either path");
                }

                rsRec.InstallPath = installPath;
                _auxInstaller.SaveAuxRecord(rsRec);
            }

            // Verify DB records against disk — if the file no longer exists the record is stale.
            // This handles the case where the user manually deleted files without using RDXC.
            if (rsRec != null && !File.Exists(Path.Combine(rsRec.InstallPath, rsRec.InstalledAs)))
            {
                _auxInstaller.RemoveRecord(rsRec);
                rsRec = null;
            }

            // ── Disk detection for ReShade ────────────────────────────────────────
            // If no DB record exists, scan disk for the known filenames so that
            // manually installed or previously installed instances are shown correctly.
            //
            // IMPORTANT: Skip filenames that are already claimed by DC via its
            // AuxInstalledRecord or DLL override config, to avoid misidentifying
            // a renamed DC file as ReShade.
            var dcRecForExclusion = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == "DisplayCommander");
            var dcClaimedFileName = dcRecForExclusion?.InstalledAs;

            if (rsRec == null)
            {
                // dxgi.dll — only attribute to ReShade if positively identified as ReShade
                // AND not already claimed by DC
                var dxgiPath = Path.Combine(installPath, AuxInstallService.RsNormalName);
                bool dxgiClaimedByDc = dcClaimedFileName != null &&
                    dcClaimedFileName.Equals(AuxInstallService.RsNormalName, StringComparison.OrdinalIgnoreCase);
                if (!dxgiClaimedByDc && File.Exists(dxgiPath) && AuxInstallService.IsReShadeFile(dxgiPath))
                {
                    rsRec = new AuxInstalledRecord
                    {
                        GameName    = game.Name,
                        InstallPath = installPath,
                        AddonType   = AuxInstallService.TypeReShade,
                        InstalledAs = AuxInstallService.RsNormalName,
                        InstalledAt = File.GetLastWriteTimeUtc(dxgiPath),
                    };
                }
                else
                {
                    // Content-based fallback: scan known proxy DLL names for ReShade binary signatures.
                    // ReShade can only inject via specific Windows system DLL proxies, so we only
                    // check those names rather than every DLL in the folder.
                    // Skip WindowsApps paths — always access-denied
                    bool isWinAppsRs = installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                                    || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase);
                    if (!isWinAppsRs)
                    try
                    {
                        foreach (var proxyName in DllOverrideConstants.CommonDllNames)
                        {
                            // Skip filenames already checked above
                            if (proxyName.Equals(AuxInstallService.RsNormalName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Skip filenames claimed by DC via override
                            if (dcClaimedFileName != null &&
                                proxyName.Equals(dcClaimedFileName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var candidatePath = Path.Combine(installPath, proxyName);
                            if (!File.Exists(candidatePath))
                                continue;

                            if (AuxInstallService.IsReShadeFileStrict(candidatePath))
                            {
                                rsRec = new AuxInstalledRecord
                                {
                                    GameName    = game.Name,
                                    InstallPath = installPath,
                                    AddonType   = AuxInstallService.TypeReShade,
                                    InstalledAs = proxyName,
                                    InstalledAt = File.GetLastWriteTimeUtc(candidatePath),
                                };
                                break;
                            }
                        }
                    }
                    catch (Exception) { /* Permission or IO errors — skip gracefully */ }
                }
            }

            var newCard = new GameCardViewModel
            {
                GameName               = game.Name,
                Mod                    = effectiveMod,
                DetectedGame           = game,
                InstallPath            = installPath,
                Source                 = game.Source,
                InstalledRecord        = record,
                Status                 = record != null ? GameStatus.Installed : GameStatus.Available,
                WikiStatus             = _wikiExclusions.Contains(game.Name)
                                          ? "—"
                                          : (effectiveMod?.SnapshotUrl == null && effectiveMod?.DiscordUrl != null && effectiveMod?.NexusUrl == null)
                                          ? "💬"
                                          : (mod == null && fallback != null && !useUeExt && !isNativeHdr)
                                            ? "?"
                                            : effectiveMod?.Status ?? "—",
                Maintainer             = effectiveMod?.Maintainer ?? "",
                IsGenericMod           = useUeExt || (fallback != null && mod == null),
                EngineHint             = engineOverrideLabel != null
                                       ? (useUeExt && engine == EngineType.Unknown ? FormatEngineHint(EngineType.Unreal, installPath) : engineOverrideLabel)
                                       : (useUeExt && engine == EngineType.Unknown) ? FormatEngineHint(EngineType.Unreal, installPath)
                                       : engine == EngineType.Unreal       ? FormatEngineHint(EngineType.Unreal, installPath)
                                       : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                                       : engine == EngineType.Unity        ? "Unity"
                                       : engine == EngineType.REEngine     ? "RE Engine" : "",
                Notes                  = effectiveMod != null ? BuildNotes(game.Name, effectiveMod, fallback, genericNotes, isNativeHdr) : "",
                InstalledAddonFileName = record?.AddonFileName,
                RdxInstalledVersion = record != null ? AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName) : null,
                IsHidden               = _hiddenGames.Contains(game.Name),
                IsFavourite            = _favouriteGames.Contains(game.Name),
                IsManuallyAdded        = game.IsManuallyAdded,
                UseUeExtended          = useUeExt,
                IsExternalOnly         = _wikiExclusions.Contains(game.Name)
                                         ? false
                                         : effectiveMod?.SnapshotUrl == null &&
                                           (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null),
                ExternalUrl            = _wikiExclusions.Contains(game.Name)
                                         ? ""
                                         : effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "",
                ExternalLabel          = _wikiExclusions.Contains(game.Name)
                                         ? ""
                                         : effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord",
                NexusUrl               = effectiveMod?.NexusUrl,
                DiscordUrl             = _wikiExclusions.Contains(game.Name)
                                         ? null
                                         : effectiveMod?.DiscordUrl,
                NameUrl                = effectiveMod?.NameUrl,
                ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(game.Name),
                ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(game.Name),
                ExcludeFromUpdateAllUl      = _gameNameService.UpdateAllExcludedUl.Contains(game.Name),
                ExcludeFromUpdateAllDc      = _gameNameService.UpdateAllExcludedDc.Contains(game.Name),
                ExcludeFromUpdateAllOs      = _gameNameService.UpdateAllExcludedOs.Contains(game.Name),
                ExcludeFromUpdateAllRef     = _gameNameService.UpdateAllExcludedRef.Contains(game.Name),
                UseNormalReShade           = _gameNameService.NormalReShadeGames.Contains(game.Name),
                ShaderModeOverride     = _perGameShaderMode.TryGetValue(game.Name, out var smBc) ? smBc : null,
                Is32Bit                = ResolveIs32Bit(game.Name, detectedMachine),
                GraphicsApi            = DetectGraphicsApi(installPath, engine, game.Name),
                DetectedApis           = _DetectAllApisForCard(installPath, game.Name),
                VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(game.Name, out var vrpBc) ? vrpBc : "DirectX",
                DllOverrideEnabled     = _dllOverrides.ContainsKey(game.Name),
                IsNativeHdrGame        = isNativeHdr,
                IsManifestUeExtended   = useUeExt && !isNativeHdr,
                LumaRenodxCompatible   = _manifest?.LumaRenodxCompat?.Contains(game.Name) == true,
                EngineIniProjectOverride = _manifest?.EngineIniPathOverrides?.TryGetValue(game.Name, out var eiOverride) == true ? eiOverride : null,
                RsRecord               = rsRec,
                RsStatus               = rsRec != null ? GameStatus.Installed : GameStatus.NotInstalled,
                RsInstalledFile        = rsRec?.InstalledAs,
                RsInstalledVersion     = rsRec != null ? AuxInstallService.ReadInstalledVersion(rsRec.InstallPath, rsRec.InstalledAs) : null,
                IsREEngineGame         = engine == EngineType.REEngine,
            };

            // ── Emulator detection ─────────────────────────────────────────────────
            if (game.Name.Equals("Ryubing", StringComparison.OrdinalIgnoreCase))
            {
                newCard.IsEmulator = true;
                newCard.VulkanRenderingPath = "Vulkan";
                if (_manifest?.EmulatorGames?.TryGetValue("Ryubing", out var emuConfigBc) == true)
                {
                    newCard.EmulatorAddonNames = emuConfigBc.Addons;
                    newCard.Mod = new GameMod
                    {
                        Name = "Ryubing (9 games)",
                        Maintainer = "Souperman9",
                        SnapshotUrl = "emulator-bundle",
                        Status = "✅",
                    };

                    // Detect existing addons
                    var emuDeployPath = ModInstallService.GetAddonDeployPath(installPath);
                    int emuFoundCount = 0;
                    foreach (var wikiName in emuConfigBc.Addons)
                    {
                        var emuMod = _allMods.FirstOrDefault(m => m.Name.Equals(wikiName, StringComparison.OrdinalIgnoreCase));
                        if (emuMod?.SnapshotUrl == null) continue;
                        var emuFileName = Path.GetFileName(emuMod.SnapshotUrl);
                        if (File.Exists(Path.Combine(emuDeployPath, emuFileName)) || File.Exists(Path.Combine(installPath, emuFileName)))
                            emuFoundCount++;
                    }
                    if (emuFoundCount > 0)
                    {
                        newCard.Status = GameStatus.Installed;
                        newCard.InstalledAddonFileName = $"{emuFoundCount} addons";
                    }
                }
            }

            // ── Luma matching ──────────────────────────────────────────────────────
            newCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(newCard.DetectedApis);

            // Cache the API detection results for subsequent launches
            if (!string.IsNullOrEmpty(installPath))
                CacheGameApi(installPath, newCard.GraphicsApi, newCard.DetectedApis);

            // For Vulkan games, RS is installed when reshade.ini exists in the game folder.
            if (newCard.RequiresVulkanInstall)
            {
                bool rsIniExists = File.Exists(Path.Combine(newCard.InstallPath, "reshade.ini"));
                newCard.RsStatus = rsIniExists ? GameStatus.Installed : GameStatus.NotInstalled;
                newCard.RsInstalledVersion = rsIniExists
                    ? AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName)
                    : null;
            }

            newCard.LumaFeatureEnabled = LumaFeatureEnabled;

            // ── ReLimiter detection ────────────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                var ulDeployPath = ModInstallService.GetAddonDeployPath(installPath);
                var ulFileName = GetUlFileName(newCard.Is32Bit);
                var legacyUlFileName = newCard.Is32Bit ? LegacyUltraLimiterFileName32 : LegacyUltraLimiterFileName;
                if (File.Exists(Path.Combine(ulDeployPath, ulFileName))
                    || File.Exists(Path.Combine(installPath, ulFileName))
                    || File.Exists(Path.Combine(ulDeployPath, legacyUlFileName))
                    || File.Exists(Path.Combine(installPath, legacyUlFileName)))
                {
                    newCard.UlStatus = GameStatus.Installed;
                    newCard.UlInstalledFile = ulFileName;
                    newCard.UlInstalledVersion = ReadUlInstalledVersion(newCard.Is32Bit);
                }
            }

            // ── Display Commander detection ────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                var dcDeployPath = ModInstallService.GetAddonDeployPath(installPath);
                var dcFileName = GetDcFileName(newCard.Is32Bit);

                // Check for default DC addon files on disk
                if (File.Exists(Path.Combine(dcDeployPath, dcFileName))
                    || File.Exists(Path.Combine(installPath, dcFileName)))
                {
                    newCard.DcStatus = GameStatus.Installed;
                    newCard.DcInstalledFile = dcFileName;
                    // Try PE file version first, fall back to meta
                    var dcFilePath = File.Exists(Path.Combine(dcDeployPath, dcFileName))
                        ? Path.Combine(dcDeployPath, dcFileName)
                        : Path.Combine(installPath, dcFileName);
                    var peVer = AuxInstallService.ReadInstalledVersion(
                        Path.GetDirectoryName(dcFilePath)!, Path.GetFileName(dcFilePath));
                    var metaVer = ReadDcInstalledVersion(newCard.Is32Bit);
                    if (metaVer == "latest_build") metaVer = null;
                    newCard.DcInstalledVersion = peVer ?? metaVer;
                }
                // Also detect legacy DC Lite files for migration
                else
                {
                    var legacyDcFileName = GetLegacyDcFileName(newCard.Is32Bit);
                    if (File.Exists(Path.Combine(dcDeployPath, legacyDcFileName))
                        || File.Exists(Path.Combine(installPath, legacyDcFileName)))
                    {
                        newCard.DcStatus = GameStatus.UpdateAvailable;
                        newCard.DcInstalledFile = legacyDcFileName;
                        var legacyFilePath = File.Exists(Path.Combine(dcDeployPath, legacyDcFileName))
                            ? Path.Combine(dcDeployPath, legacyDcFileName)
                            : Path.Combine(installPath, legacyDcFileName);
                        var peVer = AuxInstallService.ReadInstalledVersion(
                            Path.GetDirectoryName(legacyFilePath)!, Path.GetFileName(legacyFilePath));
                        var metaVer = ReadDcInstalledVersion(newCard.Is32Bit);
                        if (metaVer == "latest_build") metaVer = null;
                        newCard.DcInstalledVersion = peVer ?? metaVer;
                        _crashReporter.Log($"[BuildCards] Legacy DC Lite detected for '{game.Name}' — marking for migration");
                    }
                    else
                    {
                        // Check for DC with custom DLL override name via AuxInstalledRecord
                        var dcRec = auxRecords.FirstOrDefault(r =>
                            r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                            r.AddonType == "DisplayCommander");

                        // ── Path reconciliation for DC aux records ────────────────────
                        if (dcRec != null
                            && !dcRec.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
                        {
                            var oldDcPath = dcRec.InstallPath;
                            var dcFile = dcRec.InstalledAs;
                            var newDcFilePath = string.IsNullOrEmpty(dcFile) ? null : Path.Combine(installPath, dcFile);
                            var oldDcFilePath = string.IsNullOrEmpty(dcFile) ? null : Path.Combine(oldDcPath, dcFile);

                            if (newDcFilePath != null && File.Exists(newDcFilePath))
                            {
                                _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' path changed, DC already at new path");
                            }
                            else if (oldDcFilePath != null && File.Exists(oldDcFilePath))
                            {
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(newDcFilePath!)!);
                                    File.Copy(oldDcFilePath, newDcFilePath!, overwrite: true);
                                    _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' copied DC '{dcFile}' from '{oldDcPath}' → '{installPath}'");
                                }
                                catch (Exception ex)
                                {
                                    _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' failed to copy DC — {ex.Message}");
                                }
                            }
                            else
                            {
                                _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' path changed, DC not found at either path");
                            }

                            dcRec.InstallPath = installPath;
                            _auxInstaller.SaveAuxRecord(dcRec);
                        }

                        if (dcRec != null && File.Exists(Path.Combine(dcRec.InstallPath, dcRec.InstalledAs)))
                        {
                            newCard.DcStatus = GameStatus.Installed;
                            newCard.DcInstalledFile = dcRec.InstalledAs;
                            var peVer2 = AuxInstallService.ReadInstalledVersion(dcRec.InstallPath, dcRec.InstalledAs);
                            var metaVer2 = ReadDcInstalledVersion(newCard.Is32Bit);
                            if (metaVer2 == "latest_build") metaVer2 = null;
                            newCard.DcInstalledVersion = peVer2 ?? metaVer2;
                        }
                        else if (dcRec != null)
                        {
                            // Record exists but file not on disk — stale record
                            _auxInstaller.RemoveRecord(dcRec);
                        }
                    }
                }
            }

            // ── OptiScaler detection ───────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath) && !newCard.Is32Bit)
            {
                // First check for an existing tracking record
                var osRec = auxRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.AddonType == OptiScalerService.AddonType);

                // ── Path reconciliation for OptiScaler aux records ────────────────
                if (osRec != null
                    && !osRec.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
                {
                    var oldOsPath = osRec.InstallPath;
                    var osFile = osRec.InstalledAs;
                    var newOsFilePath = string.IsNullOrEmpty(osFile) ? null : Path.Combine(installPath, osFile);
                    var oldOsFilePath = string.IsNullOrEmpty(osFile) ? null : Path.Combine(oldOsPath, osFile);

                    if (newOsFilePath != null && File.Exists(newOsFilePath))
                    {
                        _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' path changed, OptiScaler already at new path");
                    }
                    else if (oldOsFilePath != null && File.Exists(oldOsFilePath))
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(newOsFilePath!)!);
                            File.Copy(oldOsFilePath, newOsFilePath!, overwrite: true);
                            _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' copied OptiScaler '{osFile}' from '{oldOsPath}' → '{installPath}'");
                        }
                        catch (Exception ex)
                        {
                            _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' failed to copy OptiScaler — {ex.Message}");
                        }
                    }
                    else
                    {
                        _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' path changed, OptiScaler not found at either path");
                    }

                    osRec.InstallPath = installPath;
                    _auxInstaller.SaveAuxRecord(osRec);
                }

                if (osRec != null && File.Exists(Path.Combine(osRec.InstallPath, osRec.InstalledAs)))
                {
                    newCard.OsStatus = GameStatus.Installed;
                    newCard.OsInstalledFile = osRec.InstalledAs;
                    newCard.OsInstalledVersion = _optiScalerService.StagedVersion;
                }
                else if (osRec != null)
                {
                    // Record exists but file not on disk — stale record (OptiScaler manually deleted)
                    _auxInstaller.RemoveRecord(osRec);

                    // If ReShade64.dll exists, rename it back to the correct ReShade filename
                    try
                    {
                        var rsCoexistPath = Path.Combine(installPath, OptiScalerService.ReShadeCoexistName);
                        if (File.Exists(rsCoexistPath))
                        {
                            var resolvedName = _dllOverrideService.GetEffectiveRsName(game.Name);
                            var resolvedPath = Path.Combine(installPath, resolvedName);

                            if (!resolvedName.Equals(OptiScalerService.ReShadeCoexistName, StringComparison.OrdinalIgnoreCase)
                                && !File.Exists(resolvedPath))
                            {
                                File.Move(rsCoexistPath, resolvedPath);
                                CrashReporter.Log($"[BuildCards] Restored ReShade '{OptiScalerService.ReShadeCoexistName}' → '{resolvedName}' for {game.Name}");

                                // Update ReShade tracking record
                                var rsRecord = auxRecords.FirstOrDefault(r =>
                                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                                    (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal));
                                if (rsRecord != null)
                                {
                                    rsRecord.InstalledAs = resolvedName;
                                    _auxInstaller.SaveAuxRecord(rsRecord);
                                }

                                // Update card RS state
                                newCard.RsInstalledFile = resolvedName;
                            }
                        }
                    }
                    catch (Exception rsEx)
                    {
                        CrashReporter.Log($"[BuildCards] ReShade restore after stale OS record failed for {game.Name} — {rsEx.Message}");
                    }
                }
                else
                {
                    // No tracking record — try binary signature detection
                    // Skip WindowsApps paths — always access-denied, wastes time on retries
                    bool isWindowsApps = installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                                      || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase);
                    var detectedDll = isWindowsApps ? null : _optiScalerService.DetectInstallation(installPath);
                    if (detectedDll != null)
                    {
                        // Create a tracking record for the detected installation
                        var newOsRec = new AuxInstalledRecord
                        {
                            GameName    = game.Name,
                            InstallPath = installPath,
                            AddonType   = OptiScalerService.AddonType,
                            InstalledAs = detectedDll,
                            InstalledAt = File.GetLastWriteTimeUtc(Path.Combine(installPath, detectedDll)),
                        };
                        _auxInstaller.SaveAuxRecord(newOsRec);

                        newCard.OsStatus = GameStatus.Installed;
                        newCard.OsInstalledFile = detectedDll;
                        newCard.OsInstalledVersion = _optiScalerService.StagedVersion;
                    }
                }
            }

            // ── RE Framework record matching ───────────────────────────────────
            if (newCard.IsREEngineGame)
            {
                var refRec = refRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));
                if (refRec != null)
                {
                    newCard.RefRecord = refRec;
                    newCard.RefStatus = GameStatus.Installed;
                    newCard.RefInstalledVersion = refRec.InstalledVersion;
                }
            }

            // ── DXVK detection ─────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                // First check for an existing tracking record
                var dxvkRec = _dxvkService.FindRecord(game.Name, installPath);
                if (dxvkRec != null)
                {
                    newCard.DxvkRecord = dxvkRec;
                    newCard.DxvkStatus = GameStatus.Installed;
                    newCard.DxvkInstalledVersion = dxvkRec.DxvkVersion;

                    // Lilium HDR mode: game is operating in Vulkan mode via DXVK
                    if (dxvkRec.IsLiliumHdrMode)
                    {
                        newCard.VulkanRenderingPath = "Vulkan";
                        newCard.GraphicsApi = GraphicsApiType.Vulkan;
                    }
                }
                else
                {
                    // No tracking record — try binary signature detection
                    bool isWindowsApps = installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                                      || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase);
                    if (!isWindowsApps)
                    {
                        var detectedVersion = _dxvkService.DetectInstallation(installPath, newCard.GraphicsApi);
                        if (detectedVersion != null)
                        {
                            // Unmanaged DXVK detected — show as installed with detected version
                            newCard.DxvkStatus = GameStatus.Installed;
                            newCard.DxvkInstalledVersion = detectedVersion;
                        }
                    }
                }

                // Restore saved DXVK overrides from GameNameService / saved library
                if (_dxvkEnabledGames.Contains(game.Name))
                    newCard.DxvkEnabled = true;
                if (_excludeFromUpdateAllDxvk.Contains(game.Name))
                    newCard.ExcludeFromUpdateAllDxvk = true;
            }

            // ── Engine version user override (for games where detection failed) ──
            if (newCard.EngineHint == "Unreal Engine" && _gameNameService.EngineVersionOverrides.TryGetValue(game.Name, out var evOverride))
                newCard.EngineHint = evOverride;

            // ── DOF Fix detection ────────────────────────────────────────────────
            newCard.IsDofFixEligible = _dofFixService.IsGameEligible(newCard.EngineHint, newCard.Is32Bit, game.Name);
            if (newCard.IsDofFixEligible && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                if (_dofFixService.IsInstalledIn(installPath))
                {
                    newCard.DofFixStatus = GameStatus.Installed;
                    newCard.DofFixInstalledVersion = _dofFixService.StagedVersion;
                }
            }

            // ── DLSS / Streamline detection ──────────────────────────────────────
            bool dlssSkipped = _manifest?.DlssSkipGames?.Contains(game.Name, StringComparer.OrdinalIgnoreCase) == true
                || _dlssStreamlineService.ShouldSkipScan(game.Name);
            if (!dlssSkipped && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                try
                {
                    // Try fast path first (trusted cached paths — no recursive scan)
                    var fastResult = _dlssStreamlineService.TryFastDetect(game.Name, installPath);
                    if (fastResult != null)
                    {
                        newCard.ApplyDlssDetection(fastResult);
                    }
                    else
                    {
                        // Full recursive scan
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var dlssDetection = _dlssStreamlineService.Detect(installPath);
                        sw.Stop();
                        if (sw.ElapsedMilliseconds > 200)
                            _crashReporter.Log($"[BuildCards] DLSS scan for '{game.Name}' took {sw.ElapsedMilliseconds}ms (full scan, fast path missed)");
                        if (dlssDetection.HasAny)
                        {
                            newCard.ApplyDlssDetection(dlssDetection);
                            _dlssStreamlineService.RecordDlssFound(game.Name);
                            _dlssStreamlineService.RecordTrustedPath(game.Name, dlssDetection);
                        }
                        else
                        {
                            _dlssStreamlineService.RecordNoDlssFound(game.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[BuildCards] DLSS detection failed for '{game.Name}' — {ex.Message}");
                }
            }

            var lumaMatch = MatchLumaGame(game.Name);
            if (lumaMatch != null)
            {
                newCard.LumaMod = lumaMatch;

                // Auto-enable Luma for manifest-listed games (unless user explicitly disabled)
                if (_manifest?.LumaDefaultGames != null
                    && !_lumaEnabledGames.Contains(game.Name)
                    && !_lumaDisabledGames.Contains(game.Name)
                    && _manifest.LumaDefaultGames.Any(g => g.Equals(game.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _lumaEnabledGames.Add(game.Name);
                }

                newCard.IsLumaMode = _lumaEnabledGames.Contains(game.Name);
                // Check if Luma is installed on disk
                var lumaRec = LumaService.GetRecordByPath(installPath);
                if (lumaRec != null)
                {
                    newCard.LumaRecord = lumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
            }

            // ── Nexus Mods & PCGW link resolution ──────────────────────────────
            try
            {
                newCard.NexusModsUrl = _nexusModsService.ResolveUrl(game.Name, _manifest);
            }
            catch (Exception ex) { _crashReporter.Log($"[BuildCards] NexusModsUrl resolve failed for '{game.Name}' — {ex.Message}"); }

            try
            {
                newCard.PcgwUrl = _pcgwService.ResolveUrlAsync(game.Name, game.SteamAppId, installPath, _manifest)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) { _crashReporter.Log($"[BuildCards] PcgwUrl resolve failed for '{game.Name}' — {ex.Message}"); }

            try
            {
                newCard.UwFixUrl = _uwFixService.ResolveUrl(game.Name, _manifest);
                newCard.UwFixSource = _uwFixService.ResolveSource(game.Name, _manifest);
            }
            catch (Exception ex) { _crashReporter.Log($"[BuildCards] UwFixUrl resolve failed for '{game.Name}' — {ex.Message}"); }

            try
            {
                newCard.UltraPlusUrl = _ultraPlusService.ResolveUrl(game.Name, _manifest);
            }
            catch (Exception ex) { _crashReporter.Log($"[BuildCards] UltraPlusUrl resolve failed for '{game.Name}' — {ex.Message}"); }

            cardBag.Add(newCard);

            gameStopwatch.Stop();
            var elapsedMs = gameStopwatch.ElapsedMilliseconds;
            gameTimings.Add((game.Name, elapsedMs));
            if (elapsedMs > slowGameThresholdMs)
                _crashReporter.Log($"[BuildCards] SLOW: '{game.Name}' took {elapsedMs}ms ({installPath})");
            });

        cards.AddRange(cardBag);

        // Log BuildCards timing summary
        var sortedTimings = gameTimings.OrderByDescending(t => t.ms).ToList();
        var totalBuildMs = sortedTimings.Sum(t => t.ms);
        var slowCount = sortedTimings.Count(t => t.ms > slowGameThresholdMs);
        _crashReporter.Log($"[BuildCards] Timing: {sortedTimings.Count} games, {slowCount} slow (>{slowGameThresholdMs}ms), total CPU time {totalBuildMs}ms");
        foreach (var (name, ms) in sortedTimings.Take(10))
            _crashReporter.Log($"[BuildCards] Top: '{name}' = {ms}ms");

        ApplyCardOverrides(cards);
        ApplyManifestCardOverrides(_manifest, cards);

        // Persist the addon file cache for next launch.
        _addonFileCache = new Dictionary<string, string>(newAddonFileCache, StringComparer.OrdinalIgnoreCase);

        return cards;
    }
}