// MainViewModel.CacheLoad.cs -- Helper methods, library persistence, and cache-based card loading.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{

    /// <summary>Formats the engine hint with UE version if detectable. E.g. "Unreal Engine 5.4".</summary>
    private static string FormatEngineHint(EngineType engine, string installPath)
    {
        if (engine == EngineType.Unreal)
        {
            var version = GameDetectionService.DetectUeVersion(installPath);
            return version != null ? $"Unreal Engine {version}" : "Unreal Engine";
        }
        return "Unreal Engine";
    }

    private string BuildNotes(string gameName, GameMod effectiveMod, GameMod? fallback, Dictionary<string, string> genericNotes, bool isNativeHdr = false)
    {
        // Native HDR / UE-Extended whitelisted games always get the HDR warning,
        // whether they have a specific wiki mod or are using the generic UE fallback.
        if (isNativeHdr)
        {
            var parts = new List<string>();
            parts.Add("⚠ In-game HDR must be turned ON for UE-Extended to work correctly in this title.");

            // Include wiki tooltip if present (from a specific mod entry)
            if (fallback == null && !string.IsNullOrWhiteSpace(effectiveMod.Notes))
            {
                parts.Add("");
                parts.Add(effectiveMod.Notes);
            }

            // Do NOT include generic UE game-specific settings — these are for the
            // generic addon, not UE-Extended. UE-Extended whitelisted games don't
            // need generic addon installation guidance.

            return string.Join("\n", parts);
        }

        // Specific mod — wiki tooltip note (may be null/empty if no tooltip)
        if (fallback == null) return effectiveMod.Notes ?? "";

        var notesParts = new List<string>();

        if (effectiveMod.IsGenericUnreal)
        {
            var specific = GetGenericNote(gameName, genericNotes);
            if (!string.IsNullOrEmpty(specific))
            {
                notesParts.Add("📋 Game-specific settings:");
                notesParts.Add(specific);
            }
            notesParts.Add(UnrealWarnings);
        }
        else // Unity
        {
            var specific = GetGenericNote(gameName, genericNotes);
            if (!string.IsNullOrEmpty(specific))
            {
                notesParts.Add("📋 Game-specific settings:");
                notesParts.Add(specific);
            }
        }

        return string.Join("\n", notesParts);
    }

    /// <summary>
    /// Returns true if the filename is a game-specific RenoDX addon (not a global utility addon).
    /// Global addons like renodx-devkit, renodx-dlssfix, renodx-upgrade, renodx-universal_ue_dof_fix
    /// are excluded — they're managed by the addon system, not game-specific mods.
    /// </summary>
    private static bool IsGameSpecificRenodxAddon(string fileName)
    {
        if (!fileName.StartsWith("renodx", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.StartsWith("renodx-devkit", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.StartsWith("renodx-dlssfix", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.StartsWith("renodx-upgrade", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.StartsWith("renodx-dlss.", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.StartsWith("renodx-universal_ue", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string? ScanForInstalledAddon(string installPath, GameMod? mod)
    {
        if (!Directory.Exists(installPath)) return null;
        // Skip WindowsApps paths — always access-denied for file scanning
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            // Check the AddonPath subfolder from reshade.ini first
            var addonSearchPath = ModInstallService.ResolveAddonSearchPath(installPath);
            if (addonSearchPath != null && Directory.Exists(addonSearchPath))
            {
                if (mod?.AddonFileName != null && File.Exists(Path.Combine(addonSearchPath, mod.AddonFileName)))
                    return mod.AddonFileName;
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = Directory.GetFiles(addonSearchPath, ext)
                        .FirstOrDefault(f => IsGameSpecificRenodxAddon(Path.GetFileName(f)));
                    if (found != null) return Path.GetFileName(found);
                }
            }

            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            // First try direct files in the folder
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => IsGameSpecificRenodxAddon(Path.GetFileName(f)));
                if (found != null) return Path.GetFileName(found);
            }

            // Search common subdirectories (Binaries/Win64, Binaries/Win32) and fallback to a limited recursive search
            var commonPaths = new[] { "Binaries\\Win64", "Binaries\\Win32", "Binaries\\x86", "x64", "x86" };
            foreach (var sub in commonPaths)
            {
                try
                {
                    var sp = Path.Combine(installPath, sub);
                    if (!Directory.Exists(sp)) continue;
                    foreach (var ext in new[] { "*.addon64", "*.addon32" })
                    {
                        var found = Directory.GetFiles(sp, ext)
                            .FirstOrDefault(f => IsGameSpecificRenodxAddon(Path.GetFileName(f)));
                        if (found != null) return Path.GetFileName(found);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Subdir scan failed for '{sub}' in '{installPath}' — {ex.Message}"); }
            }

            // Last resort: depth-limited recursive search (catch and ignore access issues).
            // Addon files are always near the game exe, so 4 levels is sufficient.
            try
            {
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = ScanAddonShallow(installPath, ext, 4);
                    if (found != null) return found;
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Recursive scan failed for '{installPath}' — {ex.Message}"); }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Top-level scan failed for '{installPath}' — {ex.Message}"); }
        return null;
    }

    private static string? ScanAddonShallow(string dir, string pattern, int depth)
    {
        if (depth < 0 || !Directory.Exists(dir)) return null;
        try
        {
            var found = Directory.GetFiles(dir, pattern)
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
            if (found != null) return Path.GetFileName(found);
            if (depth > 0)
            {
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch (DirectoryNotFoundException) { return null; }
                catch (UnauthorizedAccessException) { return null; }

                foreach (var sub in subdirs)
                {
                    // Skip subdirectories that no longer exist (symlinks, junctions, race conditions)
                    if (!Directory.Exists(sub)) continue;
                    var r = ScanAddonShallow(sub, pattern, depth - 1);
                    if (r != null) return r;
                }
            }
        }
        catch (DirectoryNotFoundException) { /* Expected for broken symlinks/junctions — suppress noise */ }
        catch (UnauthorizedAccessException) { /* Expected for protected directories — suppress noise */ }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanAddonShallow] Scan failed for '{dir}' — {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Lightweight addon scan: checks the direct folder and common subdirs only.
    /// Skips the expensive depth-limited recursive search. Used on normal Refresh
    /// when the cache indicates no addon was previously found. Full Refresh forces
    /// a deep rescan via ScanForInstalledAddon.
    /// </summary>
    private static string? ScanForInstalledAddonQuick(string installPath, GameMod? mod)
    {
        if (!Directory.Exists(installPath)) return null;
        // Skip WindowsApps paths — always access-denied for file scanning
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            // Check the AddonPath subfolder from reshade.ini first
            var addonSearchPath = ModInstallService.ResolveAddonSearchPath(installPath);
            if (addonSearchPath != null && Directory.Exists(addonSearchPath))
            {
                if (mod?.AddonFileName != null && File.Exists(Path.Combine(addonSearchPath, mod.AddonFileName)))
                    return mod.AddonFileName;
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = Directory.GetFiles(addonSearchPath, ext)
                        .FirstOrDefault(f => IsGameSpecificRenodxAddon(Path.GetFileName(f)));
                    if (found != null) return Path.GetFileName(found);
                }
            }

            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => IsGameSpecificRenodxAddon(Path.GetFileName(f)));
                if (found != null) return Path.GetFileName(found);
            }
            var commonPaths = new[] { "Binaries\\Win64", "Binaries\\Win32", "Binaries\\x86", "x64", "x86" };
            foreach (var sub in commonPaths)
            {
                try
                {
                    var sp = Path.Combine(installPath, sub);
                    if (!Directory.Exists(sp)) continue;
                    foreach (var ext in new[] { "*.addon64", "*.addon32" })
                    {
                        var found = Directory.GetFiles(sp, ext)
                            .FirstOrDefault(f => IsGameSpecificRenodxAddon(Path.GetFileName(f)));
                        if (found != null) return Path.GetFileName(found);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddonQuick] Subdir scan failed for '{sub}' in '{installPath}' — {ex.Message}"); }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddonQuick] Scan failed for '{installPath}' — {ex.Message}"); }
        return null;
    }

    public void SaveLibraryPublic() => SaveLibrary();
    private void SaveLibrary()
    {
        var detectedGames = _allCards
            .Where(c => !c.IsManuallyAdded && c.DetectedGame != null)
            .Select(c => c.DetectedGame!)
            .ToList();

        // Build addon cache safely — multiple DLC cards can share the same install path,
        // so use a plain dict with [] assignment instead of ToDictionary (which throws on dupes).
        var addonCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
            addonCache[c.InstallPath.ToLowerInvariant()] = !string.IsNullOrEmpty(c.InstalledAddonFileName);

        // Keep _addonFileCache in sync with current card state so that installs and
        // uninstalls performed since the last BuildCards are reflected on the next Refresh.
        foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
        {
            var key = c.InstallPath.ToLowerInvariant();
            if (!string.IsNullOrEmpty(c.InstalledAddonFileName))
                _addonFileCache[key] = c.InstalledAddonFileName;
            else if (!_addonFileCache.ContainsKey(key))
                _addonFileCache[key] = "";
        }

        // Collect DXVK state from cards for persistence (using composite keys: "GameName|Store")
        var dxvkEnabledGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dxvkInstalledVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var excludeFromUpdateAllDxvk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            var compositeKey = GameKey.FromCard(c.GameName, c.Source).ToKey();
            if (c.DxvkEnabled)
                dxvkEnabledGames.Add(compositeKey);
            if (!string.IsNullOrEmpty(c.DxvkInstalledVersion))
                dxvkInstalledVersions[compositeKey] = c.DxvkInstalledVersion;
            if (c.ExcludeFromUpdateAllDxvk)
                excludeFromUpdateAllDxvk.Add(compositeKey);
        }

        // Collect update-available snapshot from cards for persistence across restarts (using composite keys)
        var updateSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            var compositeKey = GameKey.FromCard(c.GameName, c.Source).ToKey();
            var flags = new List<string>();
            // Don't persist RDX UpdateAvailable for external-only games — those are
            // handled by NexusUpdateService baselines and shouldn't trigger Update All
            if (c.Status == GameStatus.UpdateAvailable && !c.IsExternalOnly) flags.Add("RDX");
            if (c.RsStatus == GameStatus.UpdateAvailable) flags.Add("RS");
            if (c.UlStatus == GameStatus.UpdateAvailable) flags.Add("UL");
            if (c.DcStatus == GameStatus.UpdateAvailable) flags.Add("DC");
            if (c.OsStatus == GameStatus.UpdateAvailable) flags.Add("OS");
            if (c.RefStatus == GameStatus.UpdateAvailable) flags.Add("REF");
            if (c.DxvkStatus == GameStatus.UpdateAvailable) flags.Add("DXVK");
            if (c.LumaStatus == GameStatus.UpdateAvailable) flags.Add("LUMA");
            if (flags.Count > 0)
                updateSnapshot[compositeKey] = string.Join(",", flags);
        }

        // Collect DLSS/Streamline path cache from cards for fast restore on next launch (using composite keys)
        var dlssPathsCache = new Dictionary<string, DlssPathCache>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            if (c.DlssDetection != null && c.DlssDetection.HasAny)
            {
                var compositeKey = GameKey.FromCard(c.GameName, c.Source).ToKey();
                dlssPathsCache[compositeKey] = new DlssPathCache
                {
                    DlssPath = c.DlssDetection.DlssPath,
                    DlssdPath = c.DlssDetection.DlssdPath,
                    DlssgPath = c.DlssDetection.DlssgPath,
                    DlssnrPath = c.DlssDetection.DlssnrPath,
                    StreamlineFolder = c.DlssDetection.StreamlineFolder,
                    StreamlineFiles = c.DlssDetection.StreamlineFiles.Count > 0 ? c.DlssDetection.StreamlineFiles : null,
                };
            }
        }

        // Collect RS/RDX installed versions from cards for instant display on next startup (using composite keys)
        var rsVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rdxVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            var compositeKey = GameKey.FromCard(c.GameName, c.Source).ToKey();
            if (!string.IsNullOrEmpty(c.RsInstalledVersion))
                rsVersions[compositeKey] = c.RsInstalledVersion;
            if (!string.IsNullOrEmpty(c.RdxInstalledVersion))
                rdxVersions[compositeKey] = c.RdxInstalledVersion;
        }

        _gameLibraryService.Save(detectedGames, addonCache, _hiddenGames, _favouriteGames, _manualGames,
            _engineTypeCache, _resolvedPathCache, _addonFileCache, _bitnessCache, LastSelectedGameName,
            dxvkEnabledGames, dxvkInstalledVersions, excludeFromUpdateAllDxvk, updateSnapshot, dlssPathsCache,
            rsVersions, rdxVersions);
    }

    /// <summary>
    /// Phase 1 fast path: loads cached data from the saved library and builds
    /// cards immediately without any network calls or filesystem traversal.
    /// Creates lightweight GameCardViewModel objects directly from saved library
    /// data + installed records. Skips PE header scanning, ReShade detection,
    /// addon scanning, PCGW/Nexus/Lyall lookups, and wiki matching.
    /// Phase 2's MergeCards fills in the remaining data.
    /// </summary>
    private Task LoadCacheAndBuildCardsAsync(SavedGameLibrary savedLib)
    {
        _crashReporter.Log("[MainViewModel.LoadCacheAndBuildCardsAsync] Starting cache-based card load...");

        // 1. Restore hidden/favourite sets from savedLib
        if (savedLib.HiddenGames != null)
            foreach (var g in savedLib.HiddenGames) _hiddenGames.Add(g);
        if (savedLib.FavouriteGames != null)
            foreach (var g in savedLib.FavouriteGames) _favouriteGames.Add(g);

        // 2. Restore manual games
        _manualGames = _gameLibraryService.ToManualGames(savedLib);

        // 3. Restore all caches from the saved library
        _engineTypeCache   = savedLib.EngineTypeCache   ?? new(StringComparer.OrdinalIgnoreCase);
        _resolvedPathCache = savedLib.ResolvedPathCache ?? new(StringComparer.OrdinalIgnoreCase);
        _addonFileCache    = savedLib.AddonFileCache    ?? new(StringComparer.OrdinalIgnoreCase);
        _bitnessCache      = savedLib.BitnessCache      ?? new(StringComparer.OrdinalIgnoreCase);
        LastSelectedGameName = savedLib.LastSelectedGame;

        // 4. Convert cached games to DetectedGame list and deduplicate
        //    (the saved library may contain duplicates from older versions)
        var cachedGames = _gameLibraryService.ToDetectedGames(savedLib);
        cachedGames = cachedGames
            .GroupBy(g => (
                Name: _gameDetectionService.NormalizeName(g.Name),
                Source: (g.Source ?? "").ToLowerInvariant()))
            .Select(grp => grp.First())
            .ToList();

        // 5. Apply game renames and folder overrides
        ApplyGameRenames(cachedGames);
        ApplyFolderOverrides(cachedGames);

        // 6. Combine with manual games (manual games override auto-detected with same name)
        var manualNames = _manualGames.Select(g => _gameDetectionService.NormalizeName(g.Name))
                                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allGames = cachedGames
            .Where(g => !manualNames.Contains(_gameDetectionService.NormalizeName(g.Name)))
            .Concat(_manualGames)
            .ToList();

        // 7. Load cached manifest and apply blacklist so DLC/non-game entries
        //    don't appear during the cache phase.
        var cachedManifest = _manifestService.LoadCached();
        if (cachedManifest?.Blacklist != null)
        {
            var blacklist = new HashSet<string>(cachedManifest.Blacklist, StringComparer.OrdinalIgnoreCase);
            allGames = allGames.Where(g => !blacklist.Contains(g.Name)).ToList();
            _crashReporter.Log($"[MainViewModel.LoadCacheAndBuildCardsAsync] Applied cached blacklist ({blacklist.Count} entries), {allGames.Count} games remaining");
        }

        // Apply 32-bit/64-bit overrides from cached manifest so bitness is correct before card build
        if (cachedManifest?.ThirtyTwoBitGames != null)
            foreach (var game in cachedManifest.ThirtyTwoBitGames)
                _manifest32BitGames.Add(game);
        if (cachedManifest?.SixtyFourBitGames != null)
            foreach (var game in cachedManifest.SixtyFourBitGames)
                _manifest64BitGames.Add(game);

        // Apply manifest DLSS preset overrides from cache so dropdowns are correct during Phase 1
        DlssPresetService.ApplyManifestPresets(cachedManifest);
        FeatureFlags.ApplyManifest(cachedManifest?.FeatureFlags);

        // Apply install path overrides from cached manifest so Phase 1 uses the correct subfolder
        // (e.g. Witcher 2 → bin\, Cyberpunk → bin\x64). Without this, games added to the manifest
        // after the cache was written would show the wrong path until a Refresh.
        if (cachedManifest?.InstallPathOverrides != null)
            foreach (var (key, value) in cachedManifest.InstallPathOverrides)
                _installPathOverrides.TryAdd(key, value);

        // 8. Load installed records and aux records from disk (fast local reads)
        var records    = _installer.LoadAll();
        var auxRecords = _auxInstaller.LoadAll();

        // 9. Build cards from cached data — lightweight path that creates
        //    GameCardViewModel objects directly from saved library data +
        //    installed records. NO filesystem access (no PE scanning, no
        //    ReShade detection, no addon scanning, no PCGW/Nexus/Lyall lookups).
        //    Phase 2's MergeCards will fill in wiki status, URLs, and other
        //    network/filesystem-dependent data.
        _crashReporter.Log($"[MainViewModel.LoadCacheAndBuildCardsAsync] Building lightweight cards for {allGames.Count} cached games...");

        // Load RE Framework + Luma records for matching
        var refRecords = _refService.GetRecords();

        // Load DXVK installed records for matching
        var dxvkRecords = _dxvkService.LoadAllRecords();

        var cards = new List<GameCardViewModel>(allGames.Count);

        foreach (var game in allGames)
        {
            var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();

            // Resolve install path from cache (no filesystem fallback)
            var installPath = _resolvedPathCache.TryGetValue(rootKey, out var cachedPath)
                ? cachedPath
                : game.InstallPath;

            // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
            // Supports pipe-separated paths (try each, use first that exists)
            if (_installPathOverrides.TryGetValue(game.Name, out var subPath))
            {
                var candidates = subPath.Split('|');
                string? resolvedOverride = null;
                foreach (var candidate in candidates)
                {
                    var trimmed = candidate.Trim();
                    var overridePath = Path.GetFullPath(Path.Combine(game.InstallPath, trimmed));
                    if (trimmed.StartsWith("..") || Directory.Exists(overridePath))
                    {
                        resolvedOverride = overridePath;
                        break;
                    }
                }
                // Trust first candidate if none exist on disk (Phase 2 will verify)
                installPath = resolvedOverride ?? Path.Combine(game.InstallPath, candidates[0].Trim());
            }

            // Resolve engine from cache
            var engine = EngineType.Unknown;
            if (_engineTypeCache.TryGetValue(rootKey, out var cachedEngine))
                engine = Enum.TryParse<EngineType>(cachedEngine, out var e) ? e : EngineType.Unknown;

            // Resolve engine override label (manifest overrides)
            var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);
            if (engineOverrideLabel != null)
                engine = engineOverride;

            // Resolve bitness from cache
            var resolvedKey = installPath.ToLowerInvariant();
            var machineType = _bitnessCache.TryGetValue(resolvedKey, out var cachedMachine)
                ? cachedMachine
                : MachineType.x64; // default to 64-bit when no cache

            // Resolve graphics API from game API cache (no filesystem scanning)
            var graphicsApi = GraphicsApiType.Unknown;
            HashSet<GraphicsApiType> detectedApis = new();
            if (_gameApiCache.TryGetValue(installPath, out var cachedApi))
            {
                graphicsApi = cachedApi.Primary;
                detectedApis = cachedApi.All;
            }

            // For WindowsApps (Xbox/Game Pass) Unreal Engine 5+ games, infer DX12.
            // Overrides both cache misses AND stale DX11 cache entries since WindowsApps
            // paths are access-denied and the DX11 value was a fallback, never scanned.
            if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                && (graphicsApi == GraphicsApiType.Unknown || graphicsApi == GraphicsApiType.DirectX11))
            {
                string? hint = null;
                cachedManifest?.EngineHintOverrides?.TryGetValue(game.Name, out hint);
                if (hint != null
                    && System.Text.RegularExpressions.Regex.IsMatch(hint, @"Unreal Engine 5\."))
                    graphicsApi = GraphicsApiType.DirectX12;
            }

            // Look up installed RenoDX record: prefer Name+Store match, fallback to Name+InstallPath
            var record = records.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.Store.Equals(game.Source ?? "", StringComparison.OrdinalIgnoreCase))
                ?? records.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
            // Final fallback: match by install path only for records saved with mod name
            if (record == null)
            {
                record = records.FirstOrDefault(r =>
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
            }

            // Look up aux records (ReShade, DC, OptiScaler): prefer Name+Store match, fallback to Name+InstallPath
            var rsRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.Store.Equals(game.Source ?? "", StringComparison.OrdinalIgnoreCase) &&
                (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal))
                ?? auxRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase) &&
                    (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal));
            var dcRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.Store.Equals(game.Source ?? "", StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == "DisplayCommander")
                ?? auxRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase) &&
                    r.AddonType == "DisplayCommander");
            var osRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.Store.Equals(game.Source ?? "", StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == OptiScalerService.AddonType)
                ?? auxRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase) &&
                    r.AddonType == OptiScalerService.AddonType);

            // Build engine hint string
            var engineHint = engineOverrideLabel != null
                ? engineOverrideLabel
                : engine == EngineType.Unreal       ? FormatEngineHint(EngineType.Unreal, installPath)
                : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                : engine == EngineType.Unity        ? "Unity"
                : engine == EngineType.REEngine     ? "RE Engine" : "";

            var is32Bit = ResolveIs32Bit(game.Name, machineType, game.Source ?? "");

            // Build composite key for savedLib lookups (collections now use "GameName|Store" format)
            var savedLibKey = GameKey.FromCard(game.Name, game.Source).ToKey();

            var newCard = new GameCardViewModel
            {
                GameName               = game.Name,
                DetectedGame           = game,
                InstallPath            = installPath,
                Source                 = game.Source,
                InstalledRecord        = record,
                Status                 = record != null ? GameStatus.Installed : GameStatus.Available,
                InstalledAddonFileName = record?.AddonFileName,
                RdxInstalledVersion    = savedLib.RdxInstalledVersions?.TryGetValue(savedLibKey, out var rdxVer) == true ? rdxVer : null, // Cached from last session; Phase 2 updates if file changed
                EngineHint             = engineHint,
                Is32Bit                = is32Bit,
                GraphicsApi            = graphicsApi,
                DetectedApis           = detectedApis,
                IsHidden               = _hiddenGames.Contains(savedLibKey),
                IsFavourite            = _favouriteGames.Contains(savedLibKey),
                IsManuallyAdded        = game.IsManuallyAdded,
                IsREEngineGame         = engine == EngineType.REEngine,

                // ReShade state from aux records
                RsRecord               = rsRec,
                RsStatus               = rsRec != null ? GameStatus.Installed : GameStatus.NotInstalled,
                RsInstalledFile        = rsRec?.InstalledAs,
                RsInstalledVersion     = savedLib.RsInstalledVersions?.TryGetValue(savedLibKey, out var rsVer) == true ? rsVer : null, // Cached from last session; Phase 2 updates if file changed

                // Per-game settings from GameNameService
                ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(savedLibKey),
                ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(savedLibKey),
                ExcludeFromUpdateAllUl      = _gameNameService.UpdateAllExcludedUl.Contains(savedLibKey),
                ExcludeFromUpdateAllDc      = _gameNameService.UpdateAllExcludedDc.Contains(savedLibKey),
                ExcludeFromUpdateAllOs      = _gameNameService.UpdateAllExcludedOs.Contains(savedLibKey),
                ExcludeFromUpdateAllRef     = _gameNameService.UpdateAllExcludedRef.Contains(savedLibKey),
                UseNormalReShade           = _gameNameService.NormalReShadeGames.Contains(savedLibKey),
                ShaderModeOverride     = _perGameShaderMode.TryGetValue(savedLibKey, out var smCache) ? smCache : null,
                VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(savedLibKey, out var vrpCache) ? vrpCache : "DirectX",
                DllOverrideEnabled     = _dllOverrides.ContainsKey(game.Name),
                LumaFeatureEnabled     = LumaFeatureEnabled,
                IsLumaMode             = false,
                LumaRenodxCompatible   = cachedManifest?.LumaRenodxCompat?.Contains(game.Name) == true,

                // Wiki/mod data left empty — Phase 2 MergeCards will fill these in:
                // Mod, WikiStatus, Maintainer, Notes, IsGenericMod, IsExternalOnly,
                // ExternalUrl, ExternalLabel, NexusUrl, DiscordUrl, NameUrl,
                // NexusModsUrl, PcgwUrl, UwFixUrl, UseUeExtended, IsNativeHdrGame
                WikiStatus             = "—",
            };

            // Dual-API state
            newCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(newCard.DetectedApis);

            // ── Emulator detection (cached path) ───────────────────────────────
            if (game.Name.Equals("Ryubing", StringComparison.OrdinalIgnoreCase))
            {
                newCard.IsEmulator = true;
                newCard.VulkanRenderingPath = "Vulkan";
                if (_manifest?.EmulatorGames?.TryGetValue("Ryubing", out var emuConfigCache) == true)
                {
                    newCard.EmulatorAddonNames = emuConfigCache.Addons;
                    newCard.Mod = new GameMod
                    {
                        Name = "Ryubing (9 games)",
                        Maintainer = "Souperman9",
                        SnapshotUrl = "emulator-bundle",
                        Status = "✅",
                    };
                }
            }

            // Display Commander from aux record (no filesystem scanning)
            if (dcRec != null)
            {
                newCard.DcStatus = GameStatus.Installed;
                newCard.DcInstalledFile = dcRec.InstalledAs;
                newCard.DcInstalledVersion = null; // Filled by Phase 2
            }

            // OptiScaler from aux record (no filesystem scanning)
            if (osRec != null && !is32Bit)
            {
                newCard.OsStatus = GameStatus.Installed;
                newCard.OsInstalledFile = osRec.InstalledAs;
                newCard.OsInstalledVersion = osRec.OsVariant switch {
                    "Nightly" => _optiScalerService.StagedVersionNightly,
                    "DlssNr"  => _optiScalerService.StagedVersionDlssNr,
                    _         => _optiScalerService.StagedVersion
                };
            }

            // RE Framework from records: prefer Name+Store match, fallback to Name+InstallPath
            if (newCard.IsREEngineGame)
            {
                var refRec = refRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.Store.Equals(game.Source ?? "", StringComparison.OrdinalIgnoreCase))
                    ?? refRecords.FirstOrDefault(r =>
                        r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                        r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
                if (refRec != null)
                {
                    newCard.RefRecord = refRec;
                    newCard.RefStatus = GameStatus.Installed;
                    newCard.RefInstalledVersion = refRec.InstalledVersion;
                }
            }

            // DXVK state from tracking records + saved library: prefer Name+Store match, fallback to Name+InstallPath
            var dxvkRec = dxvkRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.Store.Equals(game.Source ?? "", StringComparison.OrdinalIgnoreCase))
                ?? dxvkRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
            if (dxvkRec != null)
            {
                newCard.DxvkRecord = dxvkRec;
                newCard.DxvkStatus = GameStatus.Installed;
                newCard.DxvkInstalledVersion = dxvkRec.DxvkVersion;

                // Direct DX9 mode (any variant): game is operating in Vulkan mode via DXVK
                if (dxvkRec.IsLiliumHdrMode || dxvkRec.InstalledDlls.Contains("d3d9.dll"))
                {
                    newCard.VulkanRenderingPath = "Vulkan";
                    newCard.GraphicsApi = GraphicsApiType.Vulkan;
                }
            }
            // Game-name-keyed collections use composite keys: "GameName|Store"
            if (savedLib.DxvkEnabledGames.Contains(savedLibKey))
                newCard.DxvkEnabled = true;
            if (savedLib.DxvkInstalledVersions.TryGetValue(savedLibKey, out var savedDxvkVer) && newCard.DxvkInstalledVersion == null)
                newCard.DxvkInstalledVersion = savedDxvkVer;
            if (savedLib.ExcludeFromUpdateAllDxvk.Contains(savedLibKey))
                newCard.ExcludeFromUpdateAllDxvk = true;

            // Vulkan RS detection: if Lilium HDR mode set the game to Vulkan, RS status
            // depends on reshade.ini existence (not aux record — Vulkan layer has no aux record)
            if (newCard.RequiresVulkanInstall && rsRec == null)
            {
                bool rsIniExists = File.Exists(Path.Combine(game.InstallPath, "reshade.ini"));
                if (rsIniExists)
                {
                    newCard.RsStatus = GameStatus.Installed;
                    newCard.RsInstalledVersion = savedLib.RsInstalledVersions?.TryGetValue(savedLibKey, out var vulkanVer) == true
                        ? vulkanVer
                        : AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                }
            }

            // Engine version manifest override (for Game Pass / undetectable games)
            if (cachedManifest?.EngineHintOverrides != null
                && cachedManifest.EngineHintOverrides.TryGetValue(game.Name, out var manifestEngineHint2))
                newCard.EngineHint = manifestEngineHint2;

            // Engine version user override (for games where detection failed)
            if (newCard.EngineHint == "Unreal Engine" && _gameNameService.EngineVersionOverrides.TryGetValue(game.Name, out var evOverride2))
                newCard.EngineHint = evOverride2;

            // DOF Fix detection (lightweight — single File.Exists check)
            newCard.IsDofFixEligible = _dofFixService.IsGameEligible(newCard.EngineHint, newCard.Is32Bit, game.Name);
            if (newCard.IsDofFixEligible && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                if (_dofFixService.IsInstalledIn(installPath))
                {
                    newCard.DofFixStatus = GameStatus.Installed;
                    newCard.DofFixInstalledVersion = _dofFixService.StagedVersion;
                }
            }

            // DLSS / Streamline: restore from cached paths (fast — just reads file versions, no directory scan)
            if (savedLib.DlssPathsCache != null && savedLib.DlssPathsCache.TryGetValue(savedLibKey, out var dlssCache))
            {
                var detection = new DlssDetectionResult
                {
                    DlssPath = dlssCache.DlssPath,
                    DlssdPath = dlssCache.DlssdPath,
                    DlssgPath = dlssCache.DlssgPath,
                    DlssnrPath = dlssCache.DlssnrPath,
                    StreamlineFolder = dlssCache.StreamlineFolder,
                    StreamlineFiles = dlssCache.StreamlineFiles ?? new(),
                };
                // Set the interposer path from the folder — fall back to sl.common.dll if interposer absent (EA builds)
                if (dlssCache.StreamlineFolder != null)
                {
                    var commonPath     = Path.Combine(dlssCache.StreamlineFolder, "sl.common.dll");
                    var interposerPath = Path.Combine(dlssCache.StreamlineFolder, "sl.interposer.dll");
                    detection.StreamlineInterposerPath = File.Exists(commonPath) ? commonPath
                        : File.Exists(interposerPath) ? interposerPath : null;
                }

                // Read current versions from the cached paths (fast File.Exists + FileVersionInfo)
                if (detection.DlssPath != null && File.Exists(detection.DlssPath))
                    detection.DlssVersion = _dlssStreamlineService.GetFileVersion(detection.DlssPath);
                if (detection.DlssdPath != null && File.Exists(detection.DlssdPath))
                    detection.DlssdVersion = _dlssStreamlineService.GetFileVersion(detection.DlssdPath);
                if (detection.DlssgPath != null && File.Exists(detection.DlssgPath))
                    detection.DlssgVersion = _dlssStreamlineService.GetFileVersion(detection.DlssgPath);
                if (detection.DlssnrPath != null && File.Exists(detection.DlssnrPath))
                    detection.DlssnrVersion = _dlssStreamlineService.GetFileVersion(detection.DlssnrPath);
                if (detection.StreamlineInterposerPath != null && File.Exists(detection.StreamlineInterposerPath))
                    detection.StreamlineVersion = _dlssStreamlineService.GetFileVersion(detection.StreamlineInterposerPath);

                if (detection.HasAny)
                    newCard.ApplyDlssDetection(detection);
            }

            // DLSS / Streamline detection is NOT done via full recursive scan here (cache path)
            // because it's too slow for the UI thread. The background scan handles fresh detection.

            // ReLimiter detection (single File.Exists check + local JSON read for version)
            if (!string.IsNullOrEmpty(installPath))
            {
                var ulFileName = GetUlFileName(is32Bit);
                var ulDeployPath = ModInstallService.GetAddonDeployPath(installPath);
                if (File.Exists(Path.Combine(ulDeployPath, ulFileName))
                    || File.Exists(Path.Combine(installPath, ulFileName)))
                {
                    newCard.UlStatus = GameStatus.Installed;
                    newCard.UlInstalledFile = ulFileName;
                    newCard.UlInstalledVersion = ReadUlInstalledVersion(is32Bit);
                }
            }

            // Strip DX11 from UE5+ games in cache phase too
            var clApiKey = GameKey.FromCard(game.Name, game.Source).ToKey();
            bool clHasUserApiOverride = _apiOverrides.ContainsKey(clApiKey) || _apiOverrides.ContainsKey(game.Name);
            if (engine == EngineType.Unreal
                && newCard.EngineHint.Contains("Unreal Engine 5.", StringComparison.OrdinalIgnoreCase)
                && !clHasUserApiOverride
                && (_manifest?.GraphicsApiOverrides?.ContainsKey(game.Name) != true))
            {
                detectedApis.Remove(GraphicsApiType.DirectX11);
                if (graphicsApi == GraphicsApiType.DirectX11)
                    graphicsApi = GraphicsApiType.DirectX12;
                newCard.DetectedApis = detectedApis;
                newCard.GraphicsApi = graphicsApi;
            }

            // PCGW upgrade: promote DX11→DX12 when PCGW confirms DX12 (no override)
            if (!clHasUserApiOverride && _manifest?.GraphicsApiOverrides?.ContainsKey(game.Name) != true)
            {
                var pcgwInfo = _pcgwService.GetCachedApiInfo(game.Name);
                if (pcgwInfo != null && pcgwInfo.HasDirectX12
                    && (newCard.GraphicsApi == GraphicsApiType.DirectX11
                        || newCard.GraphicsApi == GraphicsApiType.Unknown))
                {
                    newCard.GraphicsApi = GraphicsApiType.DirectX12;
                    newCard.DetectedApis.Add(GraphicsApiType.DirectX12);
                    bool isUnreal = engine == EngineType.Unreal
                        || (newCard.EngineHint?.Contains("Unreal", StringComparison.OrdinalIgnoreCase) == true);
                    if (!isUnreal)
                        newCard.DetectedApis.Remove(GraphicsApiType.DirectX11);
                }
            }

            // Luma matching (in-memory only, no filesystem)
            var lumaMatch = MatchLumaGame(game.Name);
            if (lumaMatch != null)
            {
                newCard.LumaMod = lumaMatch;
                newCard.IsLumaMode = false;
                newCard.LumaRenodxCompatible = true;
                // Luma install record is checked by path — uses a local JSON file read
                var lumaRec = LumaService.GetRecordByPath(installPath);
                if (lumaRec != null)
                {
                    newCard.LumaRecord = lumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
            }
            else if (LumaFeatureEnabled
                && (engine == EngineType.Unreal || engine == EngineType.UnrealLegacy)
                && (graphicsApi == GraphicsApiType.DirectX11
                    || detectedApis.Contains(GraphicsApiType.DirectX11))
                && !IsUe5OrHigher(game.Name))
            {
                bool lumaBlocked = _lumaGenericEntries.TryGetValue(game.Name, out var blockCheck)
                    && blockCheck.DlssFsrBlocked && !blockCheck.HdrSupported;
                if (!lumaBlocked)
                {
                // Generic Luma UE mod — available for all DX11 Unreal Engine games
                var genericLuma = new LumaMod
                {
                    Name = game.Name,
                    IsGenericLuma = true,
                    DownloadUrl = "https://github.com/Filoppi/Luma-Framework/releases/latest/download/Luma-Unreal_Engine.zip",
                    Status = "✅",
                };
                // Notes from the scraped UE wiki table — populated when Phase 2 merge runs
                // (_lumaGenericEntries may be empty during cache phase; MergeCards will update)
                if (_lumaGenericEntries.TryGetValue(game.Name, out var cachedGenericEntry))
                    genericLuma.SpecialNotes = cachedGenericEntry.Notes;
                newCard.LumaMod = genericLuma;
                newCard.LumaRenodxCompatible = true;
                newCard.IsLumaMode = false;
                if (_lumaGenericEntries.TryGetValue(game.Name, out var clEntry))
                {
                    newCard.LumaHdrSupported = clEntry.HdrSupported;
                    newCard.LumaDlssFsrSupported = clEntry.DlssFsrSupported;
                }
                var lumaRec = LumaService.GetRecordByPath(installPath);
                if (lumaRec != null)
                {
                    newCard.LumaRecord = lumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
                }
            }

            // ── Bespoke drag-dropped Luma fallback ─────────────────────────────
            if (newCard.LumaMod == null)
            {
                var bespokeLumaRec = LumaService.GetRecordByPath(installPath);
                if (bespokeLumaRec != null)
                {
                    newCard.LumaMod = new LumaMod
                    {
                        Name = game.Name,
                        IsGenericLuma = false,
                        Status = "✅",
                    };
                    newCard.LumaRenodxCompatible = true;
                    newCard.LumaRecord = bespokeLumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
            }

            // Restore update-available statuses from the saved snapshot
            if (savedLib.UpdateAvailableSnapshot != null
                && savedLib.UpdateAvailableSnapshot.TryGetValue(savedLibKey, out var updateFlags))
            {
                var flags = updateFlags.Split(',');
                foreach (var flag in flags)
                {
                    switch (flag.Trim())
                    {
                        case "RDX":  if (newCard.Status != GameStatus.NotInstalled && !newCard.ExcludeFromUpdateAllRenoDx) newCard.Status = GameStatus.UpdateAvailable; break;
                        case "RS":   if (newCard.RsStatus != GameStatus.NotInstalled && !newCard.ExcludeFromUpdateAllReShade) newCard.RsStatus = GameStatus.UpdateAvailable; break;
                        case "UL":   if (newCard.UlStatus != GameStatus.NotInstalled && !newCard.ExcludeFromUpdateAllUl) newCard.UlStatus = GameStatus.UpdateAvailable; break;
                        case "DC":   if (newCard.DcStatus != GameStatus.NotInstalled && !newCard.ExcludeFromUpdateAllDc) newCard.DcStatus = GameStatus.UpdateAvailable; break;
                        case "OS":   if (newCard.OsStatus != GameStatus.NotInstalled && !newCard.ExcludeFromUpdateAllOs) newCard.OsStatus = GameStatus.UpdateAvailable; break;
                        case "REF":  if (!newCard.ExcludeFromUpdateAllRef) newCard.RefStatus = GameStatus.UpdateAvailable; break;
                        case "DXVK": if (newCard.DxvkStatus != GameStatus.NotInstalled && !newCard.ExcludeFromUpdateAllDxvk) newCard.DxvkStatus = GameStatus.UpdateAvailable; break;
                        case "LUMA": newCard.LumaStatus = GameStatus.UpdateAvailable; break;
                    }
                }
            }

            cards.Add(newCard);
        }

        _allCards = cards;
        _crashReporter.Log($"[MainViewModel.LoadCacheAndBuildCardsAsync] Lightweight card build complete: {_allCards.Count} cards");

        // 10. Apply card overrides and manifest card overrides
        //     (manifest is null during cache phase — ApplyManifestCardOverrides is a no-op with null)
        ApplyCardOverrides(_allCards);
        ApplyManifestCardOverrides(_manifest, _allCards);

        // Reconcile default naming for games without overrides
        ReconcileDefaultNaming();

        // Sort cards by game name
        _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();

        // 11. Push to FilterViewModel, apply filter
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.UpdateCounts();
        _filterViewModel.ApplyFilter();

        // Refresh the Update All button state from cached update statuses
        // Restore Nexus update indicators from persisted baselines
        var cachedNexusUpdates = _nexusUpdateService.GetCachedUpdates();
        if (cachedNexusUpdates.Count > 0)
        {
            foreach (var card in _allCards.Where(c => cachedNexusUpdates.Contains(c.GameName)
                && c.IsExternalOnly && c.Status == GameStatus.Installed && !c.ExcludeFromUpdateAllRenoDx))
            {
                card.Status = GameStatus.UpdateAvailable;
            }
            _crashReporter.Log($"[MainViewModel] Restored {cachedNexusUpdates.Count} Nexus update indicator(s) from cache");
        }
        NotifyUpdateButtonChanged();

        // 12. Cards are ready — suppress skeleton and show game list simultaneously.
        // IsLoading must go false BEFORE MarkInitialized so the UISync handler
        // sees HasInitialized=false and calls RemoveSkeletons().
        IsLoading = false;

        // 13. Restore selection from LastSelectedGameName (composite key format: "GameName|Store")
        if (!string.IsNullOrEmpty(LastSelectedGameName))
        {
            var key = GameKey.Parse(LastSelectedGameName);
            _crashReporter.Log($"[CacheLoad.Selection] Restoring from LastSelectedGameName='{LastSelectedGameName}' → parsed key Name='{key.Name}', Store='{key.Store}'");
            
            // Prefer exact match (name + store), fallback to name-only match
            var exactMatch = _allCards.FirstOrDefault(c => key.Matches(c.GameName, c.Source));
            var nameOnlyMatch = _allCards.FirstOrDefault(c => key.MatchesName(c.GameName));
            
            _crashReporter.Log($"[CacheLoad.Selection] exactMatch={(exactMatch != null ? $"'{exactMatch.GameName}|{exactMatch.Source}'" : "null")}, nameOnlyMatch={(nameOnlyMatch != null ? $"'{nameOnlyMatch.GameName}|{nameOnlyMatch.Source}'" : "null")}");
            
            var match = exactMatch ?? nameOnlyMatch;
            if (match != null)
            {
                _crashReporter.Log($"[CacheLoad.Selection] Selected '{match.GameName}|{match.Source}'");
                SelectedGame = match;
            }
        }

        // 14. Set StatusText to show cached game count
        StatusText    = $"{_allCards.Count} games";
        SubStatusText = "";

        _crashReporter.Log($"[MainViewModel.LoadCacheAndBuildCardsAsync] Cache phase complete — {_allCards.Count} cards displayed");

        return Task.CompletedTask;
    }

}
