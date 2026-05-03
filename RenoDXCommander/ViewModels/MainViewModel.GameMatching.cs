// MainViewModel.GameMatching.cs -- Manifest application, engine/API detection, game matching, and card overrides.

using System.Collections.Concurrent;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // ── Game-level graphics API cache ──────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, (GraphicsApiType Primary, HashSet<GraphicsApiType> All)> _gameApiCache = new(StringComparer.OrdinalIgnoreCase);

    private static string GameApiCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "game_api_cache.json");

    internal static void LoadGameApiCache()
    {
        try
        {
            if (!File.Exists(GameApiCachePath)) return;
            var json = File.ReadAllText(GameApiCachePath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, GameApiCacheEntry>>(json);
            if (entries == null) return;
            foreach (var (key, entry) in entries)
            {
                if (!Enum.TryParse<GraphicsApiType>(entry.Primary, out var primary)) continue;
                var all = new HashSet<GraphicsApiType>();
                foreach (var name in entry.All)
                    if (Enum.TryParse<GraphicsApiType>(name, out var api)) all.Add(api);
                _gameApiCache[key] = (primary, all);
            }
        }
        catch { }
    }

    internal static void SaveGameApiCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(GameApiCachePath)!;
            Directory.CreateDirectory(dir);
            var dict = new Dictionary<string, GameApiCacheEntry>(_gameApiCache.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, val) in _gameApiCache)
                dict[key] = new GameApiCacheEntry { Primary = val.Primary.ToString(), All = val.All.Select(a => a.ToString()).ToList() };
            File.WriteAllText(GameApiCachePath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.SaveGameApiCache] Failed — {ex.Message}"); }
    }

    private record GameApiCacheEntry
    {
        public string Primary { get; init; } = "";
        public List<string> All { get; init; } = new();
    }

    /// <summary>
    /// Renames installed ReShade and Display Commander files to match any manifest DLL name
    /// overrides. Called after every BuildCards so that adding a manifest override takes
    /// effect on next Refresh without the user needing to reinstall.
    ///
    /// Only runs for games that do NOT already have a user-set DLL override, since user
    /// overrides take priority and their filenames are already correct.
    /// </summary>
    private void ApplyManifestDllRenames()
    {
        if (_manifestDllNameOverrides.Count == 0) return;

        foreach (var card in _allCards)
        {
            if (card.DllOverrideEnabled) continue;             // user override takes priority
            if (_manifestDllOverrideOptOuts.Contains(card.GameName)) continue; // user opted out
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            var manifestNames = GetManifestDllNames(card.GameName);
            if (manifestNames == null) continue;

            // Determine effective filename — fall back to current installed name when manifest field is empty
            var effectiveRs = !string.IsNullOrEmpty(manifestNames.ReShade)
                ? manifestNames.ReShade
                : (card.RsRecord?.InstalledAs ?? AuxInstallService.RsNormalName);

            // ── Inject into _dllOverrides so the UI toggle turns on and filenames appear ──
            SetDllOverride(card.GameName, effectiveRs, "");
            _manifestDllOverrideGames.Add(card.GameName);
            card.DllOverrideEnabled = true;

            // ── ReShade rename (only if file exists under the old name) ────────────
            if (card.RsRecord != null
                && !card.RsRecord.InstalledAs.Equals(effectiveRs, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = Path.Combine(card.InstallPath, card.RsRecord.InstalledAs);
                var newPath = Path.Combine(card.InstallPath, effectiveRs);
                try
                {
                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath)) File.Delete(newPath);
                        File.Move(oldPath, newPath);
                        card.RsRecord.InstalledAs = effectiveRs;
                        _auxInstaller.SaveAuxRecord(card.RsRecord);
                        card.RsInstalledFile = effectiveRs;
                        _crashReporter.Log($"[MainViewModel.ApplyManifestDllRenames] RS {card.GameName}: {Path.GetFileName(oldPath)} → {effectiveRs}");
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.ApplyManifestDllRenames] RS rename failed for '{card.GameName}' — {ex.Message}");
                }
            }
        }
    }

    /// Games that default to UE-Extended and show "Extended UE Native HDR" instead of "Generic UE".
    /// These games are auto-set to UE-Extended on first build — no toggle needed.
    /// </summary>
    ///
    /// <summary>
    /// After <see cref="ApplyManifestDllRenames"/>, reconciles cards where both overrides are OFF
    /// and no manifest override exists. Ensures RS is deployed as <see cref="AuxInstallService.RsNormalName"/>
    /// and DC is deployed as the default addon name. Uses collision-safe logic: before renaming,
    /// checks if the default name is occupied and falls back if necessary.
    /// </summary>
    private void ReconcileDefaultNaming()
    {
        foreach (var card in _allCards)
        {
            if (card.DllOverrideEnabled) continue;
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            if (_manifestDllOverrideGames.Contains(card.GameName)) continue;
            if (GetManifestDllNames(card.GameName) != null
                && !_manifestDllOverrideOptOuts.Contains(card.GameName)) continue;

            // ── RS reconciliation ──────────────────────────────────────────────
            // Resolve the correct default ReShade filename for this game's API.
            // DX9 games should use d3d9.dll, OpenGL should use opengl32.dll, etc.
            // Only rename if the current filename doesn't match the API-correct default.
            var rsDefaultName = ResolveAutoReShadeFilename(card.DetectedApis) ?? AuxInstallService.RsNormalName;
            if (card.RsRecord != null
                && !string.IsNullOrEmpty(card.RsRecord.InstalledAs)
                && !card.RsRecord.InstalledAs.Equals(rsDefaultName, StringComparison.OrdinalIgnoreCase))
            {
                var rsOldPath = Path.Combine(card.InstallPath, card.RsRecord.InstalledAs);
                var rsDefaultPath = Path.Combine(card.InstallPath, rsDefaultName);

                if (File.Exists(rsOldPath))
                {
                    if (File.Exists(rsDefaultPath))
                    {
                        // Default name occupied — fall back to ReShade64/32.dll
                        var fallbackName = card.Is32Bit ? AuxInstallService.RsStaged32 : AuxInstallService.RsStaged64;
                        var fallbackPath = Path.Combine(card.InstallPath, fallbackName);
                        try
                        {
                            if (!rsOldPath.Equals(fallbackPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(fallbackPath)) File.Delete(fallbackPath);
                                File.Move(rsOldPath, fallbackPath);
                                _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] RS {card.GameName}: {card.RsRecord.InstalledAs} → {fallbackName} (default occupied)");
                                card.RsRecord.InstalledAs = fallbackName;
                                _auxInstaller.SaveAuxRecord(card.RsRecord);
                                card.RsInstalledFile = fallbackName;
                            }
                        }
                        catch (Exception ex)
                        {
                            _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] RS fallback rename failed for '{card.GameName}' — {ex.Message}");
                        }
                    }
                    else
                    {
                        // Default name free — rename to default
                        try
                        {
                            File.Move(rsOldPath, rsDefaultPath);
                            card.RsRecord.InstalledAs = rsDefaultName;
                            _auxInstaller.SaveAuxRecord(card.RsRecord);
                            card.RsInstalledFile = rsDefaultName;
                            _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] RS {card.GameName}: {Path.GetFileName(rsOldPath)} → {rsDefaultName}");
                        }
                        catch (Exception ex)
                        {
                            _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] RS rename failed for '{card.GameName}' — {ex.Message}");
                        }
                    }
                }
            }

            // ── DC reconciliation ──────────────────────────────────────────────
            if (!string.IsNullOrEmpty(card.DcInstalledFile))
            {
                var defaultDcName = GetDcFileName(card.Is32Bit);

                if (!card.DcInstalledFile.Equals(defaultDcName, StringComparison.OrdinalIgnoreCase))
                {
                    var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);

                    // Find the DC file — check addon deploy path first, then game install path
                    string? dcOldPath = null;
                    if (File.Exists(Path.Combine(deployPath, card.DcInstalledFile)))
                        dcOldPath = Path.Combine(deployPath, card.DcInstalledFile);
                    else if (File.Exists(Path.Combine(card.InstallPath, card.DcInstalledFile)))
                        dcOldPath = Path.Combine(card.InstallPath, card.DcInstalledFile);

                    if (dcOldPath != null)
                    {
                        var dcDefaultPath = Path.Combine(deployPath, defaultDcName);

                        if (File.Exists(dcDefaultPath))
                        {
                            // Default DC name occupied — keep DC under its current name
                            _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] DC {card.GameName}: default '{defaultDcName}' occupied, keeping '{card.DcInstalledFile}'");
                        }
                        else
                        {
                            try
                            {
                                File.Move(dcOldPath, dcDefaultPath);

                                // Update the AuxInstalledRecord for DC
                                var dcRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, "DisplayCommander");
                                if (dcRecord != null)
                                {
                                    dcRecord.InstalledAs = defaultDcName;
                                    _auxInstaller.SaveAuxRecord(dcRecord);
                                }
                                card.DcInstalledFile = defaultDcName;
                                _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] DC {card.GameName}: {Path.GetFileName(dcOldPath)} → {defaultDcName}");
                            }
                            catch (Exception ex)
                            {
                                _crashReporter.Log($"[MainViewModel.ReconcileDefaultNaming] DC rename failed for '{card.GameName}' — {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
    }
    private static readonly HashSet<string> NativeHdrGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Avowed",
        "Lies of P",
        "Lost Soul Aside",
        "Hell is Us",
        "Mafia: The Old Country",
        "Returnal",
        "Marvel's Midnight Suns",
        "Mortal Kombat 1",
        "Alone in the Dark",
        "Still Wakes the Deep",
    };

    /// <summary>
    /// Checks if a game name matches any entry in the NativeHdrGames whitelist
    /// or the remote manifest's native HDR list.
    /// Strips ™, ®, © symbols before comparison to handle store names like "Lost Soul Aside™".
    /// </summary>
    private bool IsNativeHdrGameMatch(string gameName)
        => MatchesGameSet(gameName, NativeHdrGames)
           || (_manifestNativeHdrGames.Count > 0 && MatchesGameSet(gameName, _manifestNativeHdrGames));

    /// <summary>
    /// Checks if a game name matches any entry in the user's _ueExtendedGames set.
    /// Strips ™, ®, © symbols before comparison.
    /// </summary>
    private bool IsUeExtendedGameMatch(string gameName)
        => MatchesGameSet(gameName, _ueExtendedGames);

    /// <summary>
    /// Returns the effective Is32Bit flag for a game.
    /// Priority: user bitness override → manifest overrides → PE header auto-detection.
    /// </summary>
    internal bool ResolveIs32Bit(string gameName, MachineType detectedMachine)
    {
        // User bitness override takes highest priority
        if (_bitnessOverrides.TryGetValue(gameName, out var bitnessOverride))
        {
            if (bitnessOverride == "32") return true;
            if (bitnessOverride == "64") return false;
        }

        if (_manifest32BitGames.Count > 0 && MatchesGameSet(gameName, _manifest32BitGames))
            return true;
        if (_manifest64BitGames.Count > 0 && MatchesGameSet(gameName, _manifest64BitGames))
            return false;
        return detectedMachine == MachineType.I386;
    }

    /// <summary>
    /// Detects the graphics API for a game install path.
    /// Checks manifest overrides first, then Unity boot.config, PE imports,
    /// engine DLLs, subdirectory exes, D3D12 Agility SDK folders, and finally
    /// the engine type as a last-resort heuristic.
    /// </summary>
    internal GraphicsApiType DetectGraphicsApi(string installPath, EngineType engine = EngineType.Unknown, string? gameName = null)
    {
        // User API override takes top priority — derive primary API from the override set
        if (gameName != null && _apiOverrides.TryGetValue(gameName, out var apiOverrideList))
        {
            var overrideSet = new HashSet<GraphicsApiType>();
            foreach (var name in apiOverrideList)
            {
                if (Enum.TryParse<GraphicsApiType>(name, out var apiType))
                    overrideSet.Add(apiType);
            }
            if (overrideSet.Count == 0)
                return GraphicsApiType.Unknown;
            if (overrideSet.Count == 1)
                return overrideSet.First();
            // Prefer highest DX version when both DX and Vulkan are present
            var dxApis = overrideSet.Where(a => a != GraphicsApiType.Vulkan && a != GraphicsApiType.OpenGL && a != GraphicsApiType.Unknown);
            if (dxApis.Any())
                return dxApis.OrderByDescending(a => a).First();
            return overrideSet.First();
        }

        // Manifest override (for games where auto-detection fails).
        // Supports comma-separated values (e.g. "DX12, VLK") — returns the first
        // non-Vulkan API for the primary badge (prefers DX for display), falling
        // back to the first entry. The full set is handled by _DetectAllApisForCard.
        if (gameName != null && _manifest?.GraphicsApiOverrides != null
            && _manifest.GraphicsApiOverrides.TryGetValue(gameName, out var apiStr))
        {
            var overrideApis = GraphicsApiDetector.ParseApiStrings(apiStr);
            if (overrideApis.Count > 0)
            {
                // Prefer a DX API for the primary badge when multiple are listed
                var primary = overrideApis.FirstOrDefault(a => a != GraphicsApiType.Vulkan && a != GraphicsApiType.OpenGL);
                return primary != GraphicsApiType.Unknown ? primary : overrideApis.First();
            }
        }

        // Skip WindowsApps for filesystem scanning — always access-denied
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
        {
            // Last resort: infer from engine type for WindowsApps games
            return engine switch
            {
                EngineType.Unreal       => GraphicsApiType.DirectX11,
                EngineType.UnrealLegacy => GraphicsApiType.DirectX9,
                EngineType.Unity        => GraphicsApiType.DirectX11,
                EngineType.REEngine     => GraphicsApiType.DirectX12,
                _                       => GraphicsApiType.Unknown,
            };
        }

        // ── Game-level cache: skip all filesystem scanning if cached ──────────
        if (_gameApiCache.TryGetValue(installPath, out var cached))
            return cached.Primary;

        // Unity: boot.config is the most reliable source (PE imports are misleading)
        var unityResult = GraphicsApiDetector.DetectUnityFromBootConfig(installPath);
        if (unityResult != GraphicsApiType.Unknown)
            return unityResult;

        // Track best detected API across all file-based checks.
        // We don't return OpenGL immediately because Unity and Unreal statically
        // link opengl32.dll as a fallback but actually render with DirectX.
        var bestDetected = GraphicsApiType.Unknown;

        var exePath = _peHeaderService.FindGameExe(installPath);
        if (exePath != null)
        {
            var result = GraphicsApiDetector.Detect(exePath);
            if (result != GraphicsApiType.Unknown && result != GraphicsApiType.OpenGL)
                return result;
            if (result != GraphicsApiType.Unknown)
                bestDetected = result;
        }

        // Fallback: scan DLLs in the install directory for graphics imports.
        // Many custom engines (Silk, Chrome, etc.) put all graphics calls in an
        // engine DLL rather than the exe. We scan any DLL that isn't a known
        // system/runtime library.
        try
        {
            foreach (var dllPath in Directory.GetFiles(installPath, "*.dll"))
            {
                var dllName = Path.GetFileName(dllPath);
                if (IsSystemOrRuntimeDll(dllName))
                    continue;
                var result = GraphicsApiDetector.Detect(dllPath);
                if (result != GraphicsApiType.Unknown && result != GraphicsApiType.OpenGL)
                    return result;
                if (result != GraphicsApiType.Unknown && bestDetected == GraphicsApiType.Unknown)
                    bestDetected = result;
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.DetectGraphicsApi] DLL scan failed for '{installPath}' — {ex.Message}"); }

        // Fallback: scan exe files in common subdirectories (some games put the
        // real exe in Bin64/, x64/, Win64/, etc. while the root has a launcher)
        string[] subDirs = ["Bin64", "Bin", "x64", "Win64", "Binaries", "Binaries\\Win64", "Binaries\\WinGDK"];
        foreach (var sub in subDirs)
        {
            var subPath = Path.Combine(installPath, sub);
            if (!Directory.Exists(subPath)) continue;
            var subExe = _peHeaderService.FindGameExe(subPath);
            if (subExe != null)
            {
                var result = GraphicsApiDetector.Detect(subExe);
                if (result != GraphicsApiType.Unknown && result != GraphicsApiType.OpenGL)
                    return result;
                if (result != GraphicsApiType.Unknown && bestDetected == GraphicsApiType.Unknown)
                    bestDetected = result;
            }
        }

        // Fallback: check for D3D12Core.dll in subdirectories (e.g. d3d12/ folder).
        // Some games (especially Game Pass/WindowsApps) ship a D3D12 Agility SDK
        // folder next to the exe, which is a strong DX12 signal.
        try
        {
            foreach (var dir in Directory.GetDirectories(installPath))
            {
                if (File.Exists(Path.Combine(dir, "D3D12Core.dll")))
                    return GraphicsApiType.DirectX12;
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.DetectGraphicsApi] D3D12Core scan failed for '{installPath}' — {ex.Message}"); }

        // If the only graphics API found was OpenGL and this is a Unity or Unreal
        // game, override to DX11. Both engines statically link opengl32.dll as a
        // fallback renderer but default to DirectX on Windows.
        if (bestDetected == GraphicsApiType.OpenGL
            && engine is EngineType.Unreal or EngineType.Unity or EngineType.REEngine)
            return GraphicsApiType.DirectX11;

        if (bestDetected != GraphicsApiType.Unknown)
            return bestDetected;

        // Last resort: infer from engine type (covers access-denied scenarios
        // like WindowsApps/Xbox Game Pass installs)
        return engine switch
        {
            EngineType.Unreal       => GraphicsApiType.DirectX11,
            EngineType.UnrealLegacy => GraphicsApiType.DirectX9,
            EngineType.Unity        => GraphicsApiType.DirectX11,
            EngineType.REEngine     => GraphicsApiType.DirectX12,
            _                       => GraphicsApiType.Unknown,
        };
    }

    /// <summary>
    /// Scans ALL executables in the install directory (and common subdirectories)
    /// and returns the union of all detected graphics APIs. This handles games
    /// like Baldur's Gate 3 that ship separate DX and Vulkan executables.
    /// </summary>
    internal HashSet<GraphicsApiType> _DetectAllApisForCard(string installPath, string? gameName = null)
    {
        // User API override takes priority — return the override set instead of scanning
        if (gameName != null && _apiOverrides.TryGetValue(gameName, out var apiOverrideList))
        {
            var overrideSet = new HashSet<GraphicsApiType>();
            foreach (var name in apiOverrideList)
            {
                if (Enum.TryParse<GraphicsApiType>(name, out var apiType))
                    overrideSet.Add(apiType);
                // Unrecognized values are silently skipped
            }
            return overrideSet;
        }

        // Manifest override — return manifest APIs if present (before filesystem scanning)
        if (gameName != null && _manifest?.GraphicsApiOverrides != null
            && _manifest.GraphicsApiOverrides.TryGetValue(gameName, out var manifestApiStr))
        {
            var manifestApis = GraphicsApiDetector.ParseApiStrings(manifestApiStr);
            if (manifestApis.Count > 0)
                return manifestApis;
        }

        // Skip WindowsApps for filesystem scanning — always access-denied
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
            return new HashSet<GraphicsApiType>();

        var result = new HashSet<GraphicsApiType>();

        // ── Game-level cache: skip filesystem scanning if cached ──────────
        if (_gameApiCache.TryGetValue(installPath, out var cached))
            return cached.All;

        // Scan all exes in the install directory
        ScanAllExesInDir(installPath, result);

        // Also scan common subdirectories (mirrors DetectGraphicsApi fallback logic)
        string[] subDirs = ["Bin64", "Bin", "x64", "Win64", "Binaries", "Binaries\\Win64", "Binaries\\WinGDK"];
        foreach (var sub in subDirs)
        {
            var subPath = Path.Combine(installPath, sub);
            if (Directory.Exists(subPath))
                ScanAllExesInDir(subPath, result);
        }

        return result;
    }

    /// <summary>
    /// Stores the detected graphics API results in the game-level cache.
    /// Called after card building to persist results for subsequent launches.
    /// </summary>
    internal static void CacheGameApi(string installPath, GraphicsApiType primary, HashSet<GraphicsApiType> all)
    {
        _gameApiCache[installPath] = (primary, all);
    }

    /// <summary>
    /// Scans all .exe files in a directory and adds their detected APIs to the result set.
    /// </summary>
    private static void ScanAllExesInDir(string dirPath, HashSet<GraphicsApiType> result)
    {
        try
        {
            // Skip WindowsApps — always access-denied, wastes time on retries
            if (dirPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                || dirPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var exeFile in Directory.GetFiles(dirPath, "*.exe"))
            {
                var apis = GraphicsApiDetector.DetectAllApis(exeFile);
                result.UnionWith(apis);
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanAllExesInDir] Exe scan failed for '{dirPath}' — {ex.Message}"); }
    }

    /// <summary>
    /// Returns true if the DLL filename is a known system, runtime, or middleware
    /// library that should be skipped when scanning for graphics API imports.
    /// </summary>
    private static bool IsSystemOrRuntimeDll(string fileName)
    {
        var name = fileName.ToLowerInvariant();

        // Known graphics DLLs — we want GraphicsApiDetector to scan these, not skip them
        // (they're handled by the DllMap inside Detect), but they won't contain
        // *other* graphics imports so scanning them is pointless.
        if (name is "d3d8.dll" or "d3d9.dll" or "d3d10.dll" or "d3d10_1.dll"
            or "d3d11.dll" or "d3d12.dll" or "dxgi.dll" or "vulkan-1.dll" or "opengl32.dll")
            return true;

        // System / CRT / VC runtime
        if (name.StartsWith("api-ms-win-") || name.StartsWith("vcruntime")
            || name.StartsWith("msvcp") || name.StartsWith("ucrtbase"))
            return true;

        // Common system DLLs
        if (name is "kernel32.dll" or "user32.dll" or "gdi32.dll" or "advapi32.dll"
            or "shell32.dll" or "ole32.dll" or "oleaut32.dll" or "shlwapi.dll"
            or "winmm.dll" or "ws2_32.dll" or "crypt32.dll" or "dbghelp.dll"
            or "imm32.dll" or "setupapi.dll" or "winhttp.dll" or "wininet.dll"
            or "normaliz.dll" or "propsys.dll" or "dwmapi.dll" or "hid.dll"
            or "dnsapi.dll" or "iphlpapi.dll" or "wldap32.dll" or "psapi.dll"
            or "version.dll" or "comctl32.dll" or "comdlg32.dll" or "wintrust.dll"
            or "secur32.dll" or "netapi32.dll" or "userenv.dll" or "bcrypt.dll")
            return true;

        // Common middleware / non-graphics game DLLs
        if (name.StartsWith("steam_api") || name.StartsWith("steamworks")
            || name.StartsWith("xinput") || name.StartsWith("dinput")
            || name.StartsWith("x3daudio") || name.StartsWith("xaudio")
            || name.StartsWith("nvngx") || name.StartsWith("sl.")
            || name.StartsWith("dstorage") || name.StartsWith("mfplat")
            || name.StartsWith("mfreadwrite") || name.StartsWith("ffx_")
            || name.StartsWith("scripthook") || name.StartsWith("bink")
            || name.StartsWith("oo2core") || name.StartsWith("amd_ags")
            || name.StartsWith("nvlowlatency"))
            return true;

        return false;
    }

    /// <summary>
    /// Returns the manifest engine override for a game, if one exists.
    /// The out parameter <paramref name="overrideEngine"/> is the EngineType to use for
    /// mod/fallback selection ("Unreal", "Unreal (Legacy)", "Unity" map to their EngineType;
    /// all other values stay Unknown so the game falls into Other).
    /// The return value is the display label to use in EngineHint (may differ from the
    /// EngineType — e.g. "Silk" stays "Silk" but overrideEngine is Unknown).
    /// Returns null if no override is defined for this game.
    /// </summary>
    private string? ResolveEngineOverride(string gameName, out EngineType overrideEngine)
    {
        overrideEngine = EngineType.Unknown;
        if (_manifestEngineOverrides.Count == 0) return null;

        string? label = null;
        if (_manifestEngineOverrides.TryGetValue(gameName, out var raw))
            label = raw;
        else
        {
            // Try stripped name (™®© removed)
            var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
            if (stripped != gameName && _manifestEngineOverrides.TryGetValue(stripped, out raw))
                label = raw;
        }

        if (label == null) return null;

        // Map known engine strings to EngineType for mod/fallback logic
        overrideEngine = label.Equals("Unreal", StringComparison.OrdinalIgnoreCase)            ? EngineType.Unreal
                       : label.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase)     ? EngineType.Unreal
                       : label.StartsWith("Unreal (Legacy)", StringComparison.OrdinalIgnoreCase) ? EngineType.UnrealLegacy
                       : label.Equals("Unity", StringComparison.OrdinalIgnoreCase)              ? EngineType.Unity
                       : label.Equals("RE Engine", StringComparison.OrdinalIgnoreCase)          ? EngineType.REEngine
                       : EngineType.Unknown;

        return label;
    }

    /// <summary>
    /// Returns the manifest-defined DLL filenames for a game, if any.
    /// User-set per-game DLL overrides always take priority over the manifest.
    /// Returns null if no manifest override is defined.
    /// </summary>
    private ManifestDllNames? GetManifestDllNames(string gameName)
    {
        if (_manifestDllNameOverrides.Count == 0) return null;
        if (_manifestDllNameOverrides.TryGetValue(gameName, out var names)) return names;
        // Try stripped name (™®© removed)
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName && _manifestDllNameOverrides.TryGetValue(stripped, out names)) return names;
        // Normalized comparison as last resort
        var norm = NormalizeForLookup(gameName);
        foreach (var (key, value) in _manifestDllNameOverrides)
        {
            if (NormalizeForLookup(key) == norm) return value;
        }
        return null;
    }

    /// <summary>
    /// Returns the GAC symlink directory for a game if it requires GAC symlink installation
    /// (e.g. XNA Framework games like Terraria). Returns null for normal games.
    /// </summary>
    internal string? GetGacSymlinkPath(string gameName)
    {
        if (_manifest?.GacSymlinkGames == null || _manifest.GacSymlinkGames.Count == 0) return null;
        if (_manifest.GacSymlinkGames.TryGetValue(gameName, out var path)) return path;
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName && _manifest.GacSymlinkGames.TryGetValue(stripped, out path)) return path;
        var norm = NormalizeForLookup(gameName);
        foreach (var (key, value) in _manifest.GacSymlinkGames)
        {
            if (NormalizeForLookup(key) == norm) return value;
        }
        return null;
    }

    /// <summary>
    /// Checks if <paramref name="gameName"/> matches any entry in <paramref name="gameSet"/>.
    /// Tries exact match first, then stripped (™®© removed), then fully normalised.
    /// </summary>
    private static bool MatchesGameSet(string gameName, IEnumerable<string> gameSet)
    {
        // Snapshot mutable collections to avoid "Collection was modified" during parallel enumeration
        IEnumerable<string> safeSet = gameSet is HashSet<string> hs ? hs.ToArray() : gameSet;

        // Fast path: exact match (works for HashSet and static lists)
        if (gameSet is ICollection<string> col && col.Contains(gameName)) return true;
        if (gameSet is not ICollection<string> && safeSet.Contains(gameName)) return true;

        // Strip trademark symbols and retry
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName)
        {
            if (gameSet is ICollection<string> col2 && col2.Contains(stripped)) return true;
            if (gameSet is not ICollection<string> && safeSet.Contains(stripped)) return true;
        }

        // Normalised comparison as last resort
        var norm = NormalizeForLookup(gameName);
        foreach (var entry in safeSet)
        {
            if (NormalizeForLookup(entry) == norm) return true;
        }
        return false;
    }

    /// <summary>
    /// Applies hardcoded per-game card overrides after BuildCards completes.
    /// Use this for games that need custom notes, forced Discord routing, or
    /// other card-level adjustments that can't be expressed in WikiService alone.
    /// </summary>
    private static void ApplyCardOverrides(List<GameCardViewModel> cards)
        => GameInitializationService.ApplyCardOverrides(cards);

    // ── Remote Manifest ───────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the working sets (_nameMappings, _ueExtendedGames, etc.)
    /// with values from the remote manifest. Local user overrides always take priority:
    /// manifest values are only applied where no local setting already exists.
    /// Must be called BEFORE BuildCards.
    /// </summary>
    private void ApplyManifest(RemoteManifest? manifest)
    {
        _gameInitializationService.ApplyManifest(
            manifest,
            _gameNameService,
            _dllOverrideService,
            _manifestNativeHdrGames,
            _manifestBlacklist,
            _manifest32BitGames,
            _manifest64BitGames,
            _manifestEngineOverrides,
            _manifestDllNameOverrides,
            _manifestWikiUnlinks,
            _installPathOverrides,
            NormalizeForLookup);
    }

    /// <summary>
    /// Applies wiki status overrides from the remote manifest to the fetched mod list.
    /// Called after _allMods is populated so that manifest-driven statuses win over
    /// the hardcoded WikiService.ApplyStatusOverrides pass.
    /// </summary>
    private void ApplyManifestStatusOverrides()
        => _gameInitializationService.ApplyManifestStatusOverrides(_manifest, _allMods);

    /// <summary>
    /// Applies manifest-driven card overrides AFTER the hardcoded ApplyCardOverrides pass.
    /// Handles GameNotes (appended/set if no hardcoded notes exist) and ForceExternalOnly.
    /// </summary>
    private static void ApplyManifestCardOverrides(RemoteManifest? manifest, List<GameCardViewModel> cards)
        => GameInitializationService.ApplyManifestCardOverrides(manifest, cards);
}

