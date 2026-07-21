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
                        .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                    if (found != null) return Path.GetFileName(found);
                }
            }

            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            // First try direct files in the folder
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
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
                            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
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
                        .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                    if (found != null) return Path.GetFileName(found);
                }
            }

            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
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
                            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
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

        // Collect DXVK state from cards for persistence
        var dxvkEnabledGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dxvkInstalledVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var excludeFromUpdateAllDxvk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            if (c.DxvkEnabled)
                dxvkEnabledGames.Add(c.GameName);
            if (!string.IsNullOrEmpty(c.DxvkInstalledVersion))
                dxvkInstalledVersions[c.GameName] = c.DxvkInstalledVersion;
            if (c.ExcludeFromUpdateAllDxvk)
                excludeFromUpdateAllDxvk.Add(c.GameName);
        }

        // Collect update-available snapshot from cards for persistence across restarts
        var updateSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
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
                updateSnapshot[c.GameName] = string.Join(",", flags);
        }

        // Collect DLSS/Streamline path cache from cards for fast restore on next launch
        var dlssPathsCache = new Dictionary<string, DlssPathCache>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            if (c.DlssDetection != null && c.DlssDetection.HasAny)
            {
                dlssPathsCache[c.GameName] = new DlssPathCache
                {
                    DlssPath = c.DlssDetection.DlssPath,
                    DlssdPath = c.DlssDetection.DlssdPath,
                    DlssgPath = c.DlssDetection.DlssgPath,
                    StreamlineFolder = c.DlssDetection.StreamlineFolder,
                    StreamlineFiles = c.DlssDetection.StreamlineFiles.Count > 0 ? c.DlssDetection.StreamlineFiles : null,
                };
            }
        }

        // Collect RS/RDX installed versions from cards for instant display on next startup
        var rsVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rdxVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
        {
            if (!string.IsNullOrEmpty(c.RsInstalledVersion))
                rsVersions[c.GameName] = c.RsInstalledVersion;
            if (!string.IsNullOrEmpty(c.RdxInstalledVersion))
                rdxVersions[c.GameName] = c.RdxInstalledVersion;
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

        // Pre-index records by game name for O(1) lookup
        var recordsByName = records
            .GroupBy(r => r.GameName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var auxByNameType = auxRecords
            .GroupBy(r => (r.GameName.ToLowerInvariant(), r.AddonType))
            .ToDictionary(g => g.Key, g => g.First());

        // Load RE Framework + Luma records for matching
        var refRecords = _refService.GetRecords();
        var refByName = refRecords
            .GroupBy(r => r.GameName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Load DXVK installed records for matching
        var dxvkRecords = _dxvkService.LoadAllRecords();
        var dxvkByName = dxvkRecords
            .GroupBy(r => r.GameName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var cards = new List<GameCardViewModel>(allGames.Count);

        foreach (var game in allGames)
        {
            var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();

            // Resolve install path from cache (no filesystem fallback)
            var installPath = _resolvedPathCache.TryGetValue(rootKey, out var cachedPath)
                ? cachedPath
                : game.InstallPath;

            // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
            if (_installPathOverrides.TryGetValue(game.Name, out var subPath))
            {
                var overridePath = Path.Combine(game.InstallPath, subPath);
                // Trust the override without checking Directory.Exists — Phase 2 will verify
                installPath = overridePath;
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

            // Look up installed RenoDX record
            recordsByName.TryGetValue(game.Name, out var record);
            // Fallback: match by install path for records saved with mod name
            if (record == null)
            {
                record = records.FirstOrDefault(r =>
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
            }

            // Look up aux records (ReShade, DC, OptiScaler)
            auxByNameType.TryGetValue((game.Name.ToLowerInvariant(), AuxInstallService.TypeReShade), out var rsRec);
            if (rsRec == null)
                auxByNameType.TryGetValue((game.Name.ToLowerInvariant(), AuxInstallService.TypeReShadeNormal), out rsRec);
            auxByNameType.TryGetValue((game.Name.ToLowerInvariant(), "DisplayCommander"), out var dcRec);
            auxByNameType.TryGetValue((game.Name.ToLowerInvariant(), OptiScalerService.AddonType), out var osRec);

            // Build engine hint string
            var engineHint = engineOverrideLabel != null
                ? engineOverrideLabel
                : engine == EngineType.Unreal       ? FormatEngineHint(EngineType.Unreal, installPath)
                : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                : engine == EngineType.Unity        ? "Unity"
                : engine == EngineType.REEngine     ? "RE Engine" : "";

            var is32Bit = ResolveIs32Bit(game.Name, machineType);

            var newCard = new GameCardViewModel
            {
                GameName               = game.Name,
                DetectedGame           = game,
                InstallPath            = installPath,
                Source                 = game.Source,
                InstalledRecord        = record,
                Status                 = record != null ? GameStatus.Installed : GameStatus.Available,
                InstalledAddonFileName = record?.AddonFileName,
                RdxInstalledVersion    = savedLib.RdxInstalledVersions?.TryGetValue(game.Name, out var rdxVer) == true ? rdxVer : null, // Cached from last session; Phase 2 updates if file changed
                EngineHint             = engineHint,
                Is32Bit                = is32Bit,
                GraphicsApi            = graphicsApi,
                DetectedApis           = detectedApis,
                IsHidden               = _hiddenGames.Contains(game.Name),
                IsFavourite            = _favouriteGames.Contains(game.Name),
                IsManuallyAdded        = game.IsManuallyAdded,
                IsREEngineGame         = engine == EngineType.REEngine,

                // ReShade state from aux records
                RsRecord               = rsRec,
                RsStatus               = rsRec != null ? GameStatus.Installed : GameStatus.NotInstalled,
                RsInstalledFile        = rsRec?.InstalledAs,
                RsInstalledVersion     = savedLib.RsInstalledVersions?.TryGetValue(game.Name, out var rsVer) == true ? rsVer : null, // Cached from last session; Phase 2 updates if file changed

                // Per-game settings from GameNameService
                ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(game.Name),
                ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(game.Name),
                ExcludeFromUpdateAllUl      = _gameNameService.UpdateAllExcludedUl.Contains(game.Name),
                ExcludeFromUpdateAllDc      = _gameNameService.UpdateAllExcludedDc.Contains(game.Name),
                ExcludeFromUpdateAllOs      = _gameNameService.UpdateAllExcludedOs.Contains(game.Name),
                ExcludeFromUpdateAllRef     = _gameNameService.UpdateAllExcludedRef.Contains(game.Name),
                UseNormalReShade           = _gameNameService.NormalReShadeGames.Contains(game.Name),
                ShaderModeOverride     = _perGameShaderMode.TryGetValue(game.Name, out var smCache) ? smCache : null,
                VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(game.Name, out var vrpCache) ? vrpCache : "DirectX",
                DllOverrideEnabled     = _dllOverrides.ContainsKey(game.Name),
                LumaFeatureEnabled     = LumaFeatureEnabled,
                IsLumaMode             = _lumaEnabledGames.Contains(game.Name),

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
                newCard.OsInstalledVersion = _optiScalerService.StagedVersion;
            }

            // RE Framework from records
            if (newCard.IsREEngineGame && refByName.TryGetValue(game.Name, out var refRec))
            {
                newCard.RefRecord = refRec;
                newCard.RefStatus = GameStatus.Installed;
                newCard.RefInstalledVersion = refRec.InstalledVersion;
            }

            // DXVK state from tracking records + saved library
            if (dxvkByName.TryGetValue(game.Name, out var dxvkRec))
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
            if (savedLib.DxvkEnabledGames.Contains(game.Name))
                newCard.DxvkEnabled = true;
            if (savedLib.DxvkInstalledVersions.TryGetValue(game.Name, out var savedDxvkVer) && newCard.DxvkInstalledVersion == null)
                newCard.DxvkInstalledVersion = savedDxvkVer;
            if (savedLib.ExcludeFromUpdateAllDxvk.Contains(game.Name))
                newCard.ExcludeFromUpdateAllDxvk = true;

            // Vulkan RS detection: if Lilium HDR mode set the game to Vulkan, RS status
            // depends on reshade.ini existence (not aux record — Vulkan layer has no aux record)
            if (newCard.RequiresVulkanInstall && rsRec == null)
            {
                bool rsIniExists = File.Exists(Path.Combine(game.InstallPath, "reshade.ini"));
                if (rsIniExists)
                {
                    newCard.RsStatus = GameStatus.Installed;
                    newCard.RsInstalledVersion = savedLib.RsInstalledVersions?.TryGetValue(game.Name, out var vulkanVer) == true
                        ? vulkanVer
                        : AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                }
            }

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
            if (savedLib.DlssPathsCache != null && savedLib.DlssPathsCache.TryGetValue(game.Name, out var dlssCache))
            {
                var detection = new DlssDetectionResult
                {
                    DlssPath = dlssCache.DlssPath,
                    DlssdPath = dlssCache.DlssdPath,
                    DlssgPath = dlssCache.DlssgPath,
                    StreamlineFolder = dlssCache.StreamlineFolder,
                    StreamlineFiles = dlssCache.StreamlineFiles ?? new(),
                };
                // Set the interposer path from the folder
                if (dlssCache.StreamlineFolder != null)
                    detection.StreamlineInterposerPath = Path.Combine(dlssCache.StreamlineFolder, "sl.interposer.dll");

                // Read current versions from the cached paths (fast File.Exists + FileVersionInfo)
                if (detection.DlssPath != null && File.Exists(detection.DlssPath))
                    detection.DlssVersion = _dlssStreamlineService.GetFileVersion(detection.DlssPath);
                if (detection.DlssdPath != null && File.Exists(detection.DlssdPath))
                    detection.DlssdVersion = _dlssStreamlineService.GetFileVersion(detection.DlssdPath);
                if (detection.DlssgPath != null && File.Exists(detection.DlssgPath))
                    detection.DlssgVersion = _dlssStreamlineService.GetFileVersion(detection.DlssgPath);
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

            // Luma matching (in-memory only, no filesystem)
            var lumaMatch = MatchLumaGame(game.Name);
            if (lumaMatch != null)
            {
                newCard.LumaMod = lumaMatch;
                newCard.IsLumaMode = _lumaEnabledGames.Contains(game.Name);
                // Luma install record is checked by path — uses a local JSON file read
                var lumaRec = LumaService.GetRecordByPath(installPath);
                if (lumaRec != null)
                {
                    newCard.LumaRecord = lumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
            }

            // Restore update-available statuses from the saved snapshot
            if (savedLib.UpdateAvailableSnapshot != null
                && savedLib.UpdateAvailableSnapshot.TryGetValue(game.Name, out var updateFlags))
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

        // 13. Restore selection from LastSelectedGameName
        if (!string.IsNullOrEmpty(LastSelectedGameName))
        {
            var match = _allCards.FirstOrDefault(c =>
                c.GameName.Equals(LastSelectedGameName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                SelectedGame = match;
        }

        // 14. Set StatusText to show cached game count
        StatusText    = $"{_allCards.Count} games";
        SubStatusText = "";

        _crashReporter.Log($"[MainViewModel.LoadCacheAndBuildCardsAsync] Cache phase complete — {_allCards.Count} cards displayed");

        return Task.CompletedTask;
    }

}