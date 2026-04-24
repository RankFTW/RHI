// MainViewModel.Install.cs -- Install/uninstall commands for RenoDX, ReShade, ReLimiter, RE Framework, and Luma.

using CommunityToolkit.Mvvm.Input;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Toggles wiki exclusion for a game and updates its card in-place — no full rescan.
    /// Excluded games show a Discord link instead of the install button.
    /// </summary>
    /// <summary>
    /// Toggles wiki exclusion for a game and updates its card synchronously in-place.
    /// This is always called from the UI thread (via dialog ContinueWith on the
    /// synchronisation context), so we update card properties directly — no
    /// DispatcherQueue.TryEnqueue needed, and the UI reflects the change immediately
    /// when the dialog closes without requiring a manual refresh.
    /// </summary>
    public void ToggleWikiExclusion(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return;

        bool nowExcluded;
        if (_wikiExclusions.Contains(gameName))
        {
            _wikiExclusions.Remove(gameName);
            nowExcluded = false;
        }
        else
        {
            _wikiExclusions.Add(gameName);
            nowExcluded = true;
        }
        SaveNameMappings();

        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null)
        {
            DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
            return;
        }

        if (nowExcluded)
        {
            // Exclude: strip wiki mod and show Discord button
            card.Mod           = null;
            card.IsExternalOnly = true;
            card.ExternalUrl   = "https://discord.gg/gF4GRJWZ2A";
            card.ExternalLabel = "Download from Discord";
            card.DiscordUrl    = "https://discord.gg/gF4GRJWZ2A";
            card.WikiStatus    = "💬";
            card.Notes         = "";
            card.IsGenericMod  = false;
            if (card.Status != GameStatus.Installed)
                card.Status = GameStatus.Available;
        }
        else
        {
            // Un-exclude: re-run wiki match in-place and restore the card
            var game = card.DetectedGame;
            if (game == null)
            {
                DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
                return;
            }
            var (_, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
            // Apply manifest engine override (takes priority over auto-detection)
            var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);
            if (engineOverrideLabel != null) engine = engineOverride;
            var mod         = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
            // Wiki unlink: completely disconnect the game from wiki — no mod, no generic fallback
            bool isWikiUnlinked1 = _manifestWikiUnlinks.Contains(game.Name);
            if (isWikiUnlinked1) mod = null;
            var fallback    = (mod == null && !isWikiUnlinked1) ? (engine == EngineType.Unreal ? MakeGenericUnreal()
                                            : engine == EngineType.Unity  ? MakeGenericUnity()
                                            : null) : null;

            // Wiki mod matched but has no download URL — inject generic engine addon URL
            if (mod != null && mod.SnapshotUrl == null && mod.NexusUrl == null && mod.DiscordUrl == null)
            {
                var engineFallback = engine == EngineType.Unreal ? MakeGenericUnreal()
                                   : engine == EngineType.Unity  ? MakeGenericUnity() : null;
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

            var effectiveMod = mod ?? fallback;

            // Apply manifest snapshot override
            if (_manifest?.SnapshotOverrides != null
                && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOvUrl)
                && !string.IsNullOrEmpty(snapshotOvUrl))
            {
                if (effectiveMod != null)
                    effectiveMod.SnapshotUrl = snapshotOvUrl;
                else
                    effectiveMod = new GameMod { Name = game.Name, SnapshotUrl = snapshotOvUrl, Status = "✅" };
            }

            card.Mod            = effectiveMod;
            card.IsExternalOnly = effectiveMod?.SnapshotUrl == null &&
                                  (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null);
            card.ExternalUrl    = effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "";
            card.ExternalLabel  = effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord";
            card.NexusUrl       = effectiveMod?.NexusUrl;
            card.DiscordUrl     = effectiveMod?.DiscordUrl;
            card.WikiStatus     = (mod == null && fallback != null && !card.UseUeExtended && !card.IsNativeHdrGame)
                                  ? "?"
                                  : effectiveMod?.Status ?? "—";
            card.Notes          = effectiveMod != null
                                  ? BuildNotes(game.Name, effectiveMod, fallback, _genericNotes, card.IsNativeHdrGame)
                                  : "";
            card.IsGenericMod   = card.UseUeExtended || (fallback != null && mod == null);
            if (card.Status != GameStatus.Installed)
                card.Status = effectiveMod != null ? GameStatus.Available : GameStatus.Available;
        }

        card.NotifyAll();
    }

    public const string UeExtendedUrl    = "https://marat569.github.io/renodx/renodx-ue-extended.addon64";
    public const string UeExtendedFile   = "renodx-ue-extended.addon64";
    public const string GenericUnrealFile = "renodx-unrealengine.addon64";

    /// <summary>
    /// Toggles the UE-Extended mode for a Generic UE card.
    /// When ON: Mod.SnapshotUrl → marat569 URL; if the standard generic file is on disk it is deleted.
    /// When OFF: Mod.SnapshotUrl → standard WikiService.GenericUnrealUrl; the extended file is deleted.
    /// Card updates synchronously — no refresh needed.
    /// </summary>
    public void ToggleUeExtended(GameCardViewModel card)
    {
        if (card == null) return;
        // Allow toggle for any UE card that shows the button:
        // IsGenericMod covers most cases, but also allow cards where Mod is null or IsGenericUnreal
        bool isEligible = card.IsGenericMod
                          || card.Mod == null
                          || (card.Mod?.IsGenericUnreal == true);
        if (!isEligible) return;

        bool nowExtended = !card.UseUeExtended;

        if (nowExtended)
            _ueExtendedGames.Add(card.GameName);
        else
            _ueExtendedGames.Remove(card.GameName);
        SaveNameMappings();

        // Swap the SnapshotUrl on the card's Mod in-place
        if (card.Mod != null)
            card.Mod.SnapshotUrl = nowExtended ? UeExtendedUrl : WikiService.GenericUnrealUrl;

        // Delete the opposing addon file from disk (if present)
        if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
        {
            try
            {
                var deleteFile = nowExtended ? GenericUnrealFile : UeExtendedFile;
                var deletePath = Path.Combine(card.InstallPath, deleteFile);
                if (File.Exists(deletePath))
                {
                    File.Delete(deletePath);
                    _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Deleted {deleteFile} from {card.InstallPath}");
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Failed to delete file — {ex.Message}");
            }
        }

        // The toggle has swapped the target addon file. The old file was deleted above,
        // so the card is no longer "installed" — reset to Available and clear the record.
        // Leaving a stale InstalledRecord with the old RemoteFileSize would cause
        // CheckForUpdateAsync to compare the new URL's size against the old addon's size
        // and fire a false "update available" on the next refresh.
        if (card.InstalledRecord != null)
        {
            _installer.RemoveRecord(card.InstalledRecord);
            card.InstalledRecord        = null;
            card.InstalledAddonFileName = null;
            card.RdxInstalledVersion    = null;
            card.Status                 = GameStatus.Available;
        }

        card.UseUeExtended = nowExtended;
        card.NotifyAll();
    }

    [RelayCommand] public void SetFilter(string filter) => _filterViewModel.SetFilter(filter);

    [RelayCommand]
    public void NavigateToSettings() => CurrentPage = AppPage.Settings;

    [RelayCommand]
    public void NavigateToGameView() => CurrentPage = AppPage.GameView;

    [RelayCommand]
    public void NavigateToAbout() => CurrentPage = AppPage.About;

    [RelayCommand]
    public void ToggleShowHidden()
    {
        _filterViewModel.ShowHidden = !_filterViewModel.ShowHidden;
        _filterViewModel.ApplyFilter();
    }

    [RelayCommand]
    public void ToggleHideGame(GameCardViewModel? card)
    {
        if (card == null) return;
        var key = card.GameName;
        _crashReporter.Log($"[MainViewModel.ToggleHide] {key} (currently hidden={card.IsHidden})");
        if (_hiddenGames.Contains(key))
            _hiddenGames.Remove(key);
        else
            _hiddenGames.Add(key);

        card.IsHidden = _hiddenGames.Contains(key);
        SaveLibrary();
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public void ToggleFavourite(GameCardViewModel? card)
    {
        if (card == null) return;
        var key = card.GameName;
        if (_favouriteGames.Contains(key))
            _favouriteGames.Remove(key);
        else
            _favouriteGames.Add(key);

        card.IsFavourite = _favouriteGames.Contains(key);
        SaveLibrary();
        // Only re-filter if on the Favourites tab (unfavouriting removes the card from view)
        if (_filterViewModel.ActiveFilters.Contains("Favourites"))
            _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public void RemoveManualGame(GameCardViewModel? card)
    {
        if (card == null) return;
        if (!card.IsManuallyAdded)
            return;

        // Remove manual entries and the corresponding card
        _manualGames.RemoveAll(g => g.Name.Equals(card.GameName, StringComparison.OrdinalIgnoreCase));
        _allCards.RemoveAll(c => c.IsManuallyAdded && c.GameName.Equals(card.GameName, StringComparison.OrdinalIgnoreCase));
        SaveLibrary();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public void AddManualGame(DetectedGame game)
    {
        if (_manualGames.Any(g => g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase))) return;
        _manualGames.Add(game);

        // Build card for this game immediately
        var (installPath, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
        // Apply manifest engine override (takes priority over auto-detection)
        var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);
        if (engineOverrideLabel != null) engine = engineOverride;

        // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
        if (_installPathOverrides.TryGetValue(game.Name, out var manualSubPath))
        {
            var overridePath = Path.Combine(game.InstallPath, manualSubPath);
            if (Directory.Exists(overridePath))
                installPath = overridePath;
        }

        var mod = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
        // Wiki unlink: completely disconnect the game from wiki — no mod, no generic fallback
        bool isWikiUnlinked2 = _manifestWikiUnlinks.Contains(game.Name);
        if (isWikiUnlinked2) mod = null;
        var genericUnreal = MakeGenericUnreal();
        var genericUnity  = MakeGenericUnity();
        var fallback = (mod == null && !isWikiUnlinked2) ? (engine == EngineType.Unreal      ? genericUnreal
                                   : engine == EngineType.Unity       ? genericUnity : null) : null;

        // Wiki mod matched but has no download URL — inject generic engine addon URL
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

        var effectiveMod = mod ?? fallback; // null for unknown-engine / legacy games not on wiki

        var records = _installer.LoadAll();
        var record  = records.FirstOrDefault(r => r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

        // Fallback: match by InstallPath for records saved with mod name instead of game name
        var scanPath = installPath.Length > 0 ? installPath : game.InstallPath;
        if (record == null)
        {
            record = records.FirstOrDefault(r =>
                r.InstallPath.Equals(scanPath, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                record.GameName = game.Name;
                _installer.SaveRecordPublic(record);
            }
        }

        // Scan disk for any renodx-* addon file already installed
        var addonOnDisk = ScanForInstalledAddon(scanPath, effectiveMod);
        if (addonOnDisk != null && record == null)
        {
            record = new InstalledModRecord
            {
                GameName      = game.Name,
                InstallPath   = scanPath,
                AddonFileName = addonOnDisk,
                InstalledAt   = File.GetLastWriteTimeUtc(Path.Combine(scanPath, addonOnDisk)),
                SnapshotUrl   = ResolveAddonUrl(addonOnDisk),
            };
            _installer.SaveRecordPublic(record);
        }

        // Patch effectiveMod SnapshotUrl if installed addon has an override URL
        if (addonOnDisk != null && effectiveMod?.SnapshotUrl != null
            && _addonFileUrlOverrides.TryGetValue(addonOnDisk, out var addonOverrideUrlM))
        {
            effectiveMod = new GameMod
            {
                Name        = effectiveMod.Name,
                Maintainer  = effectiveMod.Maintainer,
                SnapshotUrl = addonOverrideUrlM,
                Status      = effectiveMod.Status,
                Notes       = effectiveMod.Notes,
                NexusUrl    = effectiveMod.NexusUrl,
                DiscordUrl  = effectiveMod.DiscordUrl,
                NameUrl     = effectiveMod.NameUrl,
                IsGenericUnreal = effectiveMod.IsGenericUnreal,
                IsGenericUnity  = effectiveMod.IsGenericUnity,
            };
        }

        // Named addon found on disk but no wiki entry → show Discord link
        if (addonOnDisk != null && effectiveMod == null)
        {
            effectiveMod = new GameMod
            {
                Name       = game.Name,
                Status     = "💬",
                DiscordUrl = "https://discord.gg/gF4GRJWZ2A",
            };
        }

        // ── Manifest snapshot override (same logic as BuildCards) ─────────────
        if (_manifest?.SnapshotOverrides != null
            && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOverrideUrlM)
            && !string.IsNullOrEmpty(snapshotOverrideUrlM))
        {
            if (effectiveMod != null)
            {
                effectiveMod.SnapshotUrl = snapshotOverrideUrlM;
            }
            else
            {
                effectiveMod = new GameMod
                {
                    Name        = game.Name,
                    SnapshotUrl = snapshotOverrideUrlM,
                    Status      = "✅",
                };
            }
        }

        // ── Apply NativeHdr / UE-Extended whitelist (same logic as BuildCards) ────
        bool isNativeHdr = IsNativeHdrGameMatch(game.Name);
        bool useUeExt = (addonOnDisk == UeExtendedFile)
                        || IsUeExtendedGameMatch(game.Name)
                        || (isNativeHdr && (effectiveMod?.IsGenericUnreal == true || engine == EngineType.Unreal));
        if (useUeExt && effectiveMod != null)
        {
            effectiveMod = new GameMod
            {
                Name            = effectiveMod?.Name ?? "Generic Unreal Engine",
                Maintainer      = effectiveMod?.Maintainer ?? "ShortFuse",
                SnapshotUrl     = UeExtendedUrl,
                Status          = effectiveMod?.Status ?? "✅",
                Notes           = effectiveMod?.Notes,
                IsGenericUnreal = true,
            };
            if (addonOnDisk == UeExtendedFile || isNativeHdr)
                _ueExtendedGames.Add(game.Name);
        }
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

        // UE-Extended whitelist supersedes Nexus/Discord external links
        if (useUeExt && effectiveMod != null)
        {
            effectiveMod.NexusUrl   = null;
            effectiveMod.DiscordUrl = null;
        }

        var auxRecordsManual = _auxInstaller.LoadAll();
        var rsRecManual = auxRecordsManual.FirstOrDefault(r =>
            r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType == AuxInstallService.TypeReShade);

        // Drop stale records whose files no longer exist on disk
        if (rsRecManual != null && !File.Exists(Path.Combine(rsRecManual.InstallPath, rsRecManual.InstalledAs)))
        {
            _auxInstaller.RemoveRecord(rsRecManual);
            rsRecManual = null;
        }

        // Detect bitness for the manually added game
        var manualMachine = _peHeaderService.DetectGameArchitecture(scanPath);
        _bitnessCache[scanPath.ToLowerInvariant()] = manualMachine;

        var card = new GameCardViewModel
        {
            GameName       = game.Name,
            Mod            = effectiveMod,
            DetectedGame   = game,
            InstallPath    = scanPath,
            Source         = "Manual",
            InstalledRecord = record,
            Status         = record != null ? GameStatus.Installed : GameStatus.Available,
            WikiStatus     = (_wikiExclusions.Contains(game.Name)
                               || (effectiveMod?.SnapshotUrl == null && effectiveMod?.DiscordUrl != null && effectiveMod?.NexusUrl == null))
                              ? "💬"
                              : (mod == null && fallback != null && !useUeExt && !isNativeHdr)
                                ? "?"
                                : effectiveMod?.Status ?? "—",
            Maintainer     = effectiveMod?.Maintainer ?? "",
            IsGenericMod   = useUeExt || (fallback != null && mod == null),
            EngineHint     = engineOverrideLabel != null
                           ? (useUeExt && engine == EngineType.Unknown ? "Unreal Engine" : engineOverrideLabel)
                           : (useUeExt && engine == EngineType.Unknown) ? "Unreal Engine"
                           : engine == EngineType.Unreal       ? "Unreal Engine"
                           : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                           : engine == EngineType.Unity        ? "Unity"
                           : engine == EngineType.REEngine     ? "RE Engine" : "",
            Notes          = effectiveMod != null ? BuildNotes(game.Name, effectiveMod, fallback, _genericNotes, isNativeHdr) : "",
            InstalledAddonFileName = record?.AddonFileName,
            RdxInstalledVersion = record != null ? AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName) : null,
            IsExternalOnly  = _wikiExclusions.Contains(game.Name)
                              ? true
                              : effectiveMod?.SnapshotUrl == null &&
                                (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null),
            ExternalUrl     = _wikiExclusions.Contains(game.Name)
                              ? "https://discord.gg/gF4GRJWZ2A"
                              : effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "",
            ExternalLabel   = _wikiExclusions.Contains(game.Name)
                              ? "Download from Discord"
                              : effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord",
            NexusUrl        = effectiveMod?.NexusUrl,
            DiscordUrl      = _wikiExclusions.Contains(game.Name)
                              ? "https://discord.gg/gF4GRJWZ2A"
                              : effectiveMod?.DiscordUrl,
            NameUrl         = effectiveMod?.NameUrl,
            IsManuallyAdded = true,
            IsFavourite            = _favouriteGames.Contains(game.Name),
            UseUeExtended          = useUeExt,
            IsNativeHdrGame        = isNativeHdr,
            IsManifestUeExtended   = useUeExt && !isNativeHdr,
            ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(game.Name),
            ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(game.Name),
            ExcludeFromUpdateAllUl      = _gameNameService.UpdateAllExcludedUl.Contains(game.Name),
            ExcludeFromUpdateAllRef     = _gameNameService.UpdateAllExcludedRef.Contains(game.Name),
            ShaderModeOverride     = _perGameShaderMode.TryGetValue(game.Name, out var smO) ? smO : null,
            Is32Bit                = ResolveIs32Bit(game.Name, manualMachine),
            GraphicsApi            = DetectGraphicsApi(scanPath, engine, game.Name),
            DetectedApis           = _DetectAllApisForCard(scanPath, game.Name),
            VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(game.Name, out var vrpManual) ? vrpManual : "DirectX",
            LumaFeatureEnabled     = LumaFeatureEnabled,
            RsRecord        = rsRecManual,
            RsStatus        = rsRecManual != null ? GameStatus.Installed : GameStatus.NotInstalled,
            RsInstalledFile = rsRecManual?.InstalledAs,
            RsInstalledVersion = rsRecManual != null ? AuxInstallService.ReadInstalledVersion(rsRecManual.InstallPath, rsRecManual.InstalledAs) : null,
            IsREEngineGame     = engine == EngineType.REEngine,
        };

        card.IsDualApiGame = GraphicsApiDetector.IsDualApi(card.DetectedApis);

        // For Vulkan games, RS is installed when reshade.ini exists in the game folder.
        if (card.RequiresVulkanInstall)
        {
            bool rsIniExists = File.Exists(Path.Combine(card.InstallPath, "reshade.ini"));
            card.RsStatus = rsIniExists ? GameStatus.Installed : GameStatus.NotInstalled;
            card.RsInstalledVersion = rsIniExists
                ? AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName)
                : null;
        }

        // ReLimiter detection for manually added game
        if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
        {
            var ulDeployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var ulFileName = GetUlFileName(card.Is32Bit);
            var legacyUlFileName = card.Is32Bit ? LegacyUltraLimiterFileName32 : LegacyUltraLimiterFileName;
            if (File.Exists(Path.Combine(ulDeployPath, ulFileName))
                || File.Exists(Path.Combine(card.InstallPath, ulFileName))
                || File.Exists(Path.Combine(ulDeployPath, legacyUlFileName))
                || File.Exists(Path.Combine(card.InstallPath, legacyUlFileName)))
            {
                card.UlStatus = GameStatus.Installed;
                card.UlInstalledFile = ulFileName;
                card.UlInstalledVersion = ReadUlInstalledVersion(card.Is32Bit);
            }
        }

        // RE Framework record matching for manually added game
        if (card.IsREEngineGame)
        {
            var refRecords = _refService.GetRecords();
            var refRec = refRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));
            if (refRec != null)
            {
                card.RefRecord = refRec;
                card.RefStatus = GameStatus.Installed;
                card.RefInstalledVersion = refRec.InstalledVersion;
            }
        }

        _allCards.Add(card);
        _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        SaveLibrary();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public async Task InstallModAsync(GameCardViewModel? card)
    {
        // Install invoked
        if (card?.Mod?.SnapshotUrl == null) return;

        // 32-bit toggle: swap URL before install, restore after
        string? originalSnapshotUrl = card.Mod.SnapshotUrl;
        bool swappedTo32 = card.Is32Bit && card.Mod.SnapshotUrl32 != null;
        if (swappedTo32)
            card.Mod.SnapshotUrl = card.Mod.SnapshotUrl32;
        if (string.IsNullOrEmpty(card.InstallPath))
        {
            card.ActionMessage = "No install path — use 📁 to pick the game folder.";
            return;
        }
        card.IsInstalling = true;
        card.ActionMessage = "Starting download...";
        _crashReporter.Log($"[MainViewModel.InstallModAsync] Install started: {card.GameName} → {card.InstallPath}");
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.ActionMessage   = p.msg;
                card.InstallProgress = p.pct;
            });
            var record = await _installer.InstallAsync(card.Mod, card.InstallPath, progress, card.GameName);

            // Update only this card's observable properties in-place.
            // The card is already in DisplayedGames — WinUI bindings update the
            // card visually the moment each property changes. No collection
            // manipulation (Clear/Add) is needed, so the rest of the UI is untouched.
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.InstalledRecord        = record;
                card.InstalledAddonFileName = record.AddonFileName;
                card.RdxInstalledVersion    = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName);
                card.Status                 = GameStatus.Installed;
                card.FadeMessage(m => card.ActionMessage = m, "✅ Installed! Press Home in-game to open ReShade.");
                _crashReporter.Log($"[MainViewModel.InstallModAsync] Install complete: {card.GameName} — {record.AddonFileName}");
                // Update the addon file cache so the next Refresh finds the installed file
                // instead of using the stale "no addon" entry from before the install.
                if (!string.IsNullOrEmpty(card.InstallPath))
                    _addonFileCache[card.InstallPath.ToLowerInvariant()] = record.AddonFileName;
                card.NotifyAll();
                SaveLibrary();
                // Recalculate counts only — do NOT call ApplyFilter() which
                // would Clear() + re-add every card and flash the whole UI.
                _filterViewModel.UpdateCounts();
            });
        }
        catch (Exception ex)
        {
            card.ActionMessage = $"❌ Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallModAsync", ex, note: $"Game: {card.GameName}, Path: {card.InstallPath}");
        }
        finally
        {
            card.IsInstalling = false;
            // Restore original URL if we swapped to 32-bit for the install
            if (swappedTo32 && card.Mod != null && originalSnapshotUrl != null)
                card.Mod.SnapshotUrl = originalSnapshotUrl;
        }
    }

    [RelayCommand]
    public async Task InstallMod32Async(GameCardViewModel? card)
    {
        if (card?.Mod?.SnapshotUrl32 == null) return;
        var orig = card.Mod.SnapshotUrl;
        card.Mod.SnapshotUrl = card.Mod.SnapshotUrl32;
        await InstallModAsync(card);
        card.Mod.SnapshotUrl = orig;
    }

    [RelayCommand]
    public void UninstallMod(GameCardViewModel? card)
    {
        if (card?.InstalledRecord == null) return;
        _crashReporter.Log($"[MainViewModel.UninstallMod] Uninstalling: {card.GameName}");
        _installer.Uninstall(card.InstalledRecord);
        card.InstalledRecord        = null;
        card.InstalledAddonFileName = null;
        card.RdxInstalledVersion    = null;
        card.Status                 = GameStatus.Available;
        card.ActionMessage          = "✖ Mod removed.";
        card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
        // Clear the addon file cache so the next Refresh doesn't think a file is still there.
        if (!string.IsNullOrEmpty(card.InstallPath))
            _addonFileCache[card.InstallPath.ToLowerInvariant()] = "";
        SaveLibrary();
        _filterViewModel.UpdateCounts();
    }

    // ── ReLimiter commands ────────────────────────────────────────────────────

    private const string UltraLimiterFileName64 = "relimiter.addon64";
    private const string UltraLimiterFileName32 = "relimiter.addon32";
    private const string LegacyUltraLimiterFileName = "ultra_limiter.addon64";
    private const string LegacyUltraLimiterFileName32 = "ultra_limiter.addon32";
    private const string UltraLimiterReleasesApiUrl =
        "https://api.github.com/repos/RankFTW/ReLimiter/releases/latest";

    internal static string GetUlFileName(bool is32Bit) =>
        is32Bit ? UltraLimiterFileName32 : UltraLimiterFileName64;

    internal static string GetUlCachePath(bool is32Bit) =>
        Path.Combine(DownloadPaths.FrameLimiter, GetUlFileName(is32Bit));

    private static readonly string UlMetaPath = Path.Combine(
        DownloadPaths.FrameLimiter, "ul_meta.json");

    /// <summary>
    /// Downloads ReLimiter from GitHub (or uses cache) and deploys to the game folder.
    /// Stores file size + SHA-256 hash for update detection.
    /// </summary>
    public async Task InstallUlAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Mutual exclusion guard: ReLimiter cannot be installed when DC is installed
        if (card.IsDcInstalled) return;

        card.UlIsInstalling = true;
        card.UlActionMessage = "Downloading ReLimiter...";
        card.UlProgress = 0;
        try
        {
            // Force fresh download on reinstall (but not update — the check already cached the new file)
            if (card.UlStatus == GameStatus.Installed)
            {
                if (File.Exists(GetUlCachePath(card.Is32Bit))) File.Delete(GetUlCachePath(card.Is32Bit));
            }

            // Download to cache if not already cached
            await EnsureUlCachedAsync(card.Is32Bit, new Progress<(string msg, double pct)>(p =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    card.UlActionMessage = p.msg;
                    card.UlProgress = p.pct;
                });
            }));

            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var destPath = Path.Combine(deployPath, GetUlFileName(card.Is32Bit));
            File.Copy(GetUlCachePath(card.Is32Bit), destPath, overwrite: true);

            // Remove legacy ultra_limiter.addon64 / ultra_limiter.addon32 if present
            var legacyPath = Path.Combine(deployPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            var legacyDirect = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyDirect)) File.Delete(legacyDirect);
            var legacyPath32 = Path.Combine(deployPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyPath32)) File.Delete(legacyPath32);
            var legacyDirect32 = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyDirect32)) File.Delete(legacyDirect32);

            // Save version metadata after successful install
            if (!string.IsNullOrEmpty(_latestUlVersion))
                SaveUlMeta(_latestUlVersion, card.Is32Bit);

            // Deploy relimiter.ini from AppData if not already present in game folder
            AuxInstallService.DeployUlIniIfAbsent(card.InstallPath);

            DispatcherQueue?.TryEnqueue(() =>
            {
                card.UlInstalledFile = GetUlFileName(card.Is32Bit);
                card.UlInstalledVersion = _latestUlVersion?.TrimStart('v', 'V')
                    ?? ReadUlInstalledVersion(card.Is32Bit);
                card.UlStatus = GameStatus.Installed;
                card.UlActionMessage = "✅ ReLimiter installed!";
                card.UlIsInstalling = false;
                card.NotifyAll();
                card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.UlActionMessage = $"❌ Install failed: {ex.Message}";
                card.UlIsInstalling = false;
                card.NotifyAll();
            });
            _crashReporter.WriteCrashReport("InstallUl", ex, note: $"Game: {card.GameName}");
        }
    }

    /// <summary>
    /// Downloads ReLimiter to the cache directory if not already present.
    /// Fetches the latest release info from GitHub if not already cached from an update check.
    /// </summary>
    private async Task EnsureUlCachedAsync(bool is32Bit, IProgress<(string msg, double pct)>? progress = null)
    {
        if (File.Exists(GetUlCachePath(is32Bit)))
        {
            progress?.Report(("Installing from cache...", 50));
            return;
        }

        // If we don't have a download URL yet (fresh install, not from update check), fetch it
        var currentUrl = is32Bit ? _latestUlDownloadUrl32 : _latestUlDownloadUrl;
        if (string.IsNullOrEmpty(currentUrl))
        {
            await FetchLatestUlReleaseInfoAsync(is32Bit);
            currentUrl = is32Bit ? _latestUlDownloadUrl32 : _latestUlDownloadUrl;
        }

        var url = currentUrl;
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("Could not determine ReLimiter download URL from GitHub releases.");
        }

        Directory.CreateDirectory(DownloadPaths.FrameLimiter);
        var tempPath = GetUlCachePath(is32Bit) + ".tmp";

        progress?.Report(("Downloading...", 0));
        var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;
        var buffer = new byte[1024 * 1024];

        using (var net = await resp.Content.ReadAsStreamAsync())
        using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            int read;
            while ((read = await net.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report(($"Downloading... {downloaded / 1024} KB", (double)downloaded / total * 100));
            }
        }

        if (File.Exists(GetUlCachePath(is32Bit))) File.Delete(GetUlCachePath(is32Bit));
        File.Move(tempPath, GetUlCachePath(is32Bit));

        // Save version metadata for update detection
        if (!string.IsNullOrEmpty(_latestUlVersion))
            SaveUlMeta(_latestUlVersion, is32Bit);
        progress?.Report(("Downloaded!", 100));
    }

    private async Task FetchLatestUlReleaseInfoAsync(bool is32Bit)
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, UltraLimiterReleasesApiUrl).ConfigureAwait(false);
            if (json == null) return;

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                _latestUlVersion = tagEl.GetString();

            var targetFileName = GetUlFileName(is32Bit);
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        name.GetString()?.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) == true &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        if (is32Bit)
                            _latestUlDownloadUrl32 = urlEl.GetString();
                        else
                            _latestUlDownloadUrl = urlEl.GetString();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[FetchLatestUlReleaseInfoAsync] Failed — {ex.Message}");
        }
    }

    public void UninstallUl(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var filePath = Path.Combine(deployPath, GetUlFileName(card.Is32Bit));
            if (File.Exists(filePath))
                File.Delete(filePath);

            // Also check the game folder directly if AddonPath was different
            var directPath = Path.Combine(card.InstallPath, GetUlFileName(card.Is32Bit));
            if (File.Exists(directPath))
                File.Delete(directPath);

            // Remove legacy ultra_limiter.addon64 if present
            var legacyPath = Path.Combine(deployPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            var legacyDirect = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyDirect)) File.Delete(legacyDirect);

            // Remove legacy ultra_limiter.addon32 if present
            var legacyPath32 = Path.Combine(deployPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyPath32)) File.Delete(legacyPath32);
            var legacyDirect32 = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyDirect32)) File.Delete(legacyDirect32);

            card.UlInstalledFile = null;
            card.UlInstalledVersion = null;
            card.UlStatus = GameStatus.NotInstalled;
            card.UlActionMessage = "✖ ReLimiter removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
        }
        catch (Exception ex)
        {
            card.UlActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallUl", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── Display Commander commands ────────────────────────────────────────────────

    private const string DcFileName64 = "zzz_display_commander.addon64";
    private const string DcFileName32 = "zzz_display_commander.addon32";
    // Legacy filenames for migration from DC Lite
    private const string LegacyDcFileName64 = "zzz_display_commander_lite.addon64";
    private const string LegacyDcFileName32 = "zzz_display_commander_lite.addon32";
    private const string DcReleasesUrl =
        "https://github.com/pmnoxx/display-commander/releases/tag/latest_build";
    private const string DcReleasesApiUrl =
        "https://api.github.com/repos/pmnoxx/display-commander/releases/tags/latest_build";

    internal static string GetDcFileName(bool is32Bit) =>
        is32Bit ? DcFileName32 : DcFileName64;

    internal static string GetLegacyDcFileName(bool is32Bit) =>
        is32Bit ? LegacyDcFileName32 : LegacyDcFileName64;

    internal static string GetDcCachePath(bool is32Bit) =>
        Path.Combine(DownloadPaths.FrameLimiter, GetDcFileName(is32Bit));

    /// <summary>
    /// Resolves the effective DC filename for a game using the priority chain:
    /// user DLL override > manifest override > default variant filename.
    /// </summary>
    internal string ResolveDcFileName(GameCardViewModel card)
    {
        // Priority 1: user DLL override
        if (card.DllOverrideEnabled)
        {
            var overrideCfg = GetDllOverride(card.GameName);
            if (overrideCfg != null && !string.IsNullOrEmpty(overrideCfg.DcFileName))
                return overrideCfg.DcFileName;
        }

        // Priority 2: manifest override
        var manifestNames = GetManifestDllNames(card.GameName);
        if (manifestNames?.Dc is { Length: > 0 } mDc)
            return mDc;

        // Priority 3: default
        return GetDcFileName(card.Is32Bit);
    }

    private static readonly string DcMetaPath = Path.Combine(
        DownloadPaths.FrameLimiter, "dc_meta.json");

    /// <summary>
    /// Downloads Display Commander to the cache directory if not already present.
    /// Fetches the latest release info from GitHub if not already cached from an update check.
    /// </summary>
    private async Task EnsureDcCachedAsync(bool is32Bit, IProgress<(string msg, double pct)>? progress = null)
    {
        // Clean up legacy DC Lite cache files
        var legacyCachePath64 = Path.Combine(DownloadPaths.FrameLimiter, LegacyDcFileName64);
        var legacyCachePath32 = Path.Combine(DownloadPaths.FrameLimiter, LegacyDcFileName32);
        if (File.Exists(legacyCachePath64)) { File.Delete(legacyCachePath64); _crashReporter.Log("[DC] Cleaned up legacy lite cache (64-bit)"); }
        if (File.Exists(legacyCachePath32)) { File.Delete(legacyCachePath32); _crashReporter.Log("[DC] Cleaned up legacy lite cache (32-bit)"); }

        if (File.Exists(GetDcCachePath(is32Bit)))
        {
            progress?.Report(("Installing from cache...", 50));
            return;
        }

        // If we don't have a download URL yet (fresh install, not from update check), fetch it
        var currentUrl = is32Bit ? _latestDcDownloadUrl32 : _latestDcDownloadUrl;
        if (string.IsNullOrEmpty(currentUrl))
        {
            await FetchLatestDcReleaseInfoAsync(is32Bit);
            currentUrl = is32Bit ? _latestDcDownloadUrl32 : _latestDcDownloadUrl;
        }

        var url = currentUrl;
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("Could not determine Display Commander download URL from GitHub releases.");
        }

        Directory.CreateDirectory(DownloadPaths.FrameLimiter);
        var tempPath = GetDcCachePath(is32Bit) + ".tmp";

        progress?.Report(("Downloading...", 0));
        var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;
        var buffer = new byte[1024 * 1024];

        using (var net = await resp.Content.ReadAsStreamAsync())
        using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            int read;
            while ((read = await net.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report(($"Downloading... {downloaded / 1024} KB", (double)downloaded / total * 100));
            }
        }

        if (File.Exists(GetDcCachePath(is32Bit))) File.Delete(GetDcCachePath(is32Bit));
        File.Move(tempPath, GetDcCachePath(is32Bit));

        // Save version metadata for update detection
        if (!string.IsNullOrEmpty(_latestDcVersion))
            SaveDcMeta(_latestDcVersion, is32Bit);
        progress?.Report(("Downloaded!", 100));
    }

    private async Task FetchLatestDcReleaseInfoAsync(bool is32Bit)
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, DcReleasesApiUrl).ConfigureAwait(false);
            if (json == null) return;

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
            {
                var tag = tagEl.GetString();
                // DC uses a fixed "latest_build" tag — extract real version from release body
                if (tag == "latest_build" && doc.RootElement.TryGetProperty("body", out var bodyEl))
                {
                    var body = bodyEl.GetString() ?? "";
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(
                        body, @"\*{0,2}Version in binaries\*{0,2}:\s*([\d.]+)");
                    if (versionMatch.Success)
                        _latestDcVersion = versionMatch.Groups[1].Value;
                    else if (doc.RootElement.TryGetProperty("name", out var nameEl))
                        _latestDcVersion = nameEl.GetString();
                    else
                        _latestDcVersion = tag;
                }
                else
                    _latestDcVersion = tag;
            }

            var targetFileName = GetDcFileName(is32Bit);
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        name.GetString()?.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) == true &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        if (is32Bit)
                            _latestDcDownloadUrl32 = urlEl.GetString();
                        else
                            _latestDcDownloadUrl = urlEl.GetString();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[FetchLatestDcReleaseInfoAsync] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads Display Commander from GitHub (or uses cache) and deploys to the game folder.
    /// Creates an AuxInstalledRecord with AddonType "DisplayCommander".
    /// Mutual exclusion: returns early if ReLimiter is already installed.
    /// </summary>
    public async Task InstallDcAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Mutual exclusion guard: DC cannot be installed when ReLimiter is installed
        if (card.IsUlInstalled) return;

        card.DcIsInstalling = true;
        card.DcActionMessage = "Downloading Display Commander...";
        card.DcProgress = 0;
        try
        {
            // Force fresh download on reinstall (but not update — the check already cached the new file)
            if (card.DcStatus == GameStatus.Installed)
            {
                if (File.Exists(GetDcCachePath(card.Is32Bit))) File.Delete(GetDcCachePath(card.Is32Bit));
            }

            // Download to cache if not already cached
            await EnsureDcCachedAsync(card.Is32Bit, new Progress<(string msg, double pct)>(p =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    card.DcActionMessage = p.msg;
                    card.DcProgress = p.pct;
                });
            }));

            // Resolve the target filename using the priority chain
            var targetFileName = ResolveDcFileName(card);

            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var destPath = Path.Combine(deployPath, targetFileName);

            // Migration: remove legacy DC Lite file if present
            var legacyName = GetLegacyDcFileName(card.Is32Bit);
            var legacyPath = Path.Combine(deployPath, legacyName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
                _crashReporter.Log($"[InstallDcAsync] Removed legacy DC Lite file '{legacyName}' from '{card.GameName}'");
            }
            // Also check game root if deploy path differs
            if (!deployPath.Equals(card.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                var legacyRootPath = Path.Combine(card.InstallPath, legacyName);
                if (File.Exists(legacyRootPath))
                {
                    File.Delete(legacyRootPath);
                    _crashReporter.Log($"[InstallDcAsync] Removed legacy DC Lite file '{legacyName}' from game root '{card.GameName}'");
                }
            }

            File.Copy(GetDcCachePath(card.Is32Bit), destPath, overwrite: true);

            // Save version metadata after successful install
            if (!string.IsNullOrEmpty(_latestDcVersion))
                SaveDcMeta(_latestDcVersion, card.Is32Bit);

            // Deploy DisplayCommander.ini from AppData if not already present in game folder
            AuxInstallService.DeployDcIniIfAbsent(card.InstallPath);

            // Create and persist AuxInstalledRecord for DC tracking
            var dcRecord = new AuxInstalledRecord
            {
                GameName    = card.GameName,
                InstallPath = card.InstallPath,
                AddonType   = "DisplayCommander",
                InstalledAs = targetFileName,
                InstalledAt = DateTime.UtcNow,
            };
            _auxInstaller.SaveAuxRecord(dcRecord);

            DispatcherQueue?.TryEnqueue(() =>
            {
                card.DcInstalledFile = targetFileName;
                // Try PE file version first, then cached version, then meta
                var peVersion = Services.AuxInstallService.ReadInstalledVersion(
                    ModInstallService.GetAddonDeployPath(card.InstallPath), targetFileName);
                var cachedVersion = _latestDcVersion?.TrimStart('v', 'V');
                // Don't use the tag name "latest_build" as a version
                if (cachedVersion == "latest_build") cachedVersion = null;
                card.DcInstalledVersion = peVersion ?? cachedVersion ?? ReadDcInstalledVersion(card.Is32Bit);
                card.DcStatus = GameStatus.Installed;
                card.DcActionMessage = "✅ Display Commander installed!";
                card.DcIsInstalling = false;
                card.NotifyAll();
                card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.DcActionMessage = $"❌ Install failed: {ex.Message}";
                card.DcIsInstalling = false;
                card.NotifyAll();
            });
            _crashReporter.WriteCrashReport("InstallDc", ex, note: $"Game: {card.GameName}");
        }
    }

    public void UninstallDc(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            // Remove the DC file using the filename it was installed as
            if (!string.IsNullOrEmpty(card.DcInstalledFile))
            {
                var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
                var filePath = Path.Combine(deployPath, card.DcInstalledFile);
                if (File.Exists(filePath))
                    File.Delete(filePath);

                // Also check the game folder directly if AddonPath was different
                var directPath = Path.Combine(card.InstallPath, card.DcInstalledFile);
                if (File.Exists(directPath))
                    File.Delete(directPath);
            }

            // Remove the AuxInstalledRecord for DisplayCommander
            var dcRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, "DisplayCommander");
            if (dcRecord != null)
                _auxInstaller.RemoveRecord(dcRecord);

            card.DcInstalledFile = null;
            card.DcInstalledVersion = null;
            card.DcStatus = GameStatus.NotInstalled;
            card.DcActionMessage = "✖ Display Commander removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallDc", ex, note: $"Game: {card.GameName}");
        }
    }

    // Cached latest DC release info from the update check
    private string? _latestDcVersion;
    private string? _latestDcDownloadUrl;
    private string? _latestDcDownloadUrl32;

    // ── ReShade helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the ReShade DLL filename implied by the game's detected graphics APIs,
    /// or <c>null</c> when the default <c>dxgi.dll</c> should be used.
    /// DX9 takes precedence over OpenGL if both are present.
    /// </summary>
    internal static string? ResolveAutoReShadeFilename(HashSet<GraphicsApiType> detectedApis)
    {
        // DX11/DX12 take precedence — many games import d3d9.dll for legacy reasons
        // even though they primarily use DX11/DX12.
        if (detectedApis.Contains(GraphicsApiType.DirectX11) || detectedApis.Contains(GraphicsApiType.DirectX12))
            return null; // fall through to default dxgi.dll
        if (detectedApis.Contains(GraphicsApiType.DirectX9))
            return "d3d9.dll";
        if (detectedApis.Count == 1 && detectedApis.Contains(GraphicsApiType.OpenGL))
            return "opengl32.dll";
        return null; // fall through to default dxgi.dll
    }

    [RelayCommand]
    public async Task InstallReShadeAsync(GameCardViewModel? card)
    {
        if (card == null) return;

        if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            card.RsActionMessage = "No install path — use 📁 to pick the game folder.";
            return;
        }

        // RE Engine games require REFramework before ReShade (unless in Luma mode
        // or user has excluded REF via Update Inclusion toggle)
        if (card.IsREEngineGame && !card.IsRefInstalled
            && !(card.LumaFeatureEnabled && card.IsLumaMode)
            && !card.ExcludeFromUpdateAllRef)
        {
            card.RsActionMessage = "⚠ Install RE Framework first.";
            return;
        }

        // ── Vulkan ReShade install flow ───────────────────────────────────────────
        if (card.RequiresVulkanInstall)
        {
            await InstallReShadeVulkanAsync(card);
            return;
        }

        // ── GAC symlink install flow (XNA Framework games like Terraria) ──────────
        var gacPath = GetGacSymlinkPath(card.GameName);
        if (gacPath != null)
        {
            await InstallReShadeGacAsync(card, gacPath);
            return;
        }

        // Check for foreign dxgi.dll before overwriting
        {
            var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
            if (File.Exists(dxgiPath))
            {
                // Skip the warning entirely if OptiScaler is installed for this game —
                // the dxgi.dll is OptiScaler's and ReShade will be deployed as ReShade64.dll
                var osRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, OptiScalerService.AddonType);
                if (osRecord == null)
                {
                    var fileType = AuxInstallService.IdentifyDxgiFile(dxgiPath);
                    // Skip the warning for known managed files (ReShade, OptiScaler)
                    if (fileType == AuxInstallService.DxgiFileType.Unknown)
                    {
                        if (ConfirmForeignDxgiOverwrite != null)
                        {
                            var confirmed = await ConfirmForeignDxgiOverwrite(card, dxgiPath);
                            if (!confirmed)
                            {
                                card.RsActionMessage = "⚠ Skipped — unknown dxgi.dll found. Use Overrides to proceed.";
                                return;
                            }
                        }
                        else
                        {
                            card.RsActionMessage = "⚠ Skipped — unknown dxgi.dll found.";
                            return;
                        }
                    }
                }
            }
        }

        card.RsIsInstalling  = true;
        card.RsActionMessage = "Starting ReShade download...";
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.RsActionMessage = p.msg;
                card.RsProgress      = p.pct;
            });
            var selectedPacks = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);
            // Ensure needed shader packs are downloaded before install deploys them
            if (selectedPacks != null)
                await _shaderPackService.EnsurePacksAsync(selectedPacks);

            var rsFilenameOverride = card.DllOverrideEnabled
                    ? (GetDllOverride(card.GameName)?.ReShadeFileName)
                    : (GetManifestDllNames(card.GameName)?.ReShade is { Length: > 0 } mRs
                        ? mRs
                        : ResolveAutoReShadeFilename(card.DetectedApis));
            _crashReporter.Log($"[InstallReShadeAsync] {card.GameName}: DllOverrideEnabled={card.DllOverrideEnabled}, " +
                $"DetectedApis=[{string.Join(",", card.DetectedApis)}], filenameOverride={rsFilenameOverride ?? "(null → dxgi.dll)"}");

            var record = await _auxInstaller.InstallReShadeAsync(card.GameName, card.InstallPath,
                shaderModeOverride: card.ShaderModeOverride,
                use32Bit:       card.Is32Bit,
                filenameOverride: rsFilenameOverride,
                selectedPackIds: selectedPacks,
                progress:       progress,
                screenshotSavePath: BuildScreenshotSavePath(card.GameName),
                useNormalReShade: card.UseNormalReShade,
                overlayHotkey: _settingsViewModel.OverlayHotkey);
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.RsRecord           = record;
                card.RsInstalledFile    = record.InstalledAs;
                card.RsInstalledVersion = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);
                card.RsStatus           = GameStatus.Installed;
                card.RsActionMessage    = "✅ ReShade installed!";
                card.NotifyAll();
                card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);

                // Deploy managed addons now that ReShade is present
                DeployAddonsForCard(card.GameName);
            });
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ ReShade Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallReShadeAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.RsIsInstalling = false; }
    }

    /// <summary>
    /// Vulkan-specific ReShade install flow. Installs the global Vulkan implicit layer,
    /// deploys reshade.ini and ReShadePreset.ini to the game directory, and updates card status.
    /// </summary>
    internal async Task InstallReShadeVulkanAsync(GameCardViewModel card)
    {
        // ── Lightweight deploy path — layer already present, no admin needed ──
        if (IsVulkanLayerInstalledFunc())
        {
            card.RsIsInstalling  = true;
            card.RsActionMessage = "Installing Vulkan ReShade...";
            try
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, BuildScreenshotSavePath(card.GameName), _settingsViewModel.OverlayHotkey, _settingsViewModel.ScreenshotHotkey);
                AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
                VulkanFootprintService.Create(card.InstallPath);
                _shaderPackService.SyncGameFolder(card.InstallPath,
                    ResolveShaderSelection(card.GameName, card.ShaderModeOverride));

                var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                    VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                Action updateCard = () =>
                {
                    card.RsInstalledVersion = vulkanVersion;
                    card.RsStatus        = GameStatus.Installed;
                    card.RsActionMessage = "✅ Vulkan ReShade installed!";
                    card.NotifyAll();
                    card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);

                    // Deploy managed addons now that ReShade is present
                    DeployAddonsForCard(card.GameName);
                };
                if (DispatchUiAction != null) DispatchUiAction(updateCard);
                else DispatcherQueue?.TryEnqueue(() => updateCard());
            }
            catch (Exception ex)
            {
                card.RsActionMessage = $"❌ Vulkan ReShade Failed: {ex.Message}";
                _crashReporter.WriteCrashReport("InstallReShadeVulkanAsync", ex, note: $"Game: {card.GameName}");
            }
            finally { card.RsIsInstalling = false; }
            return;
        }

        // ── Full install path — layer absent, requires admin + InstallLayer() ──

        // 1. Check admin privileges
        if (!IsRunningAsAdminFunc())
        {
            if (ShowVulkanAdminRequiredDialog != null)
                await ShowVulkanAdminRequiredDialog();
            else
                card.RsActionMessage = "⚠ Administrator privileges are required for Vulkan layer installation. Restart RHI as admin.";
            return;
        }

        // 2. If warning not yet shown this session, show global warning
        if (!_vulkanLayerWarningShownThisSession)
        {
            if (ShowVulkanLayerWarningDialog != null)
            {
                var proceed = await ShowVulkanLayerWarningDialog();
                if (!proceed)
                {
                    card.RsActionMessage = "Vulkan layer install cancelled.";
                    return;
                }
            }
        }

        card.RsIsInstalling  = true;
        card.RsActionMessage = "Installing Vulkan ReShade layer...";
        try
        {
            // 3. Install the global Vulkan layer (copies DLL, writes manifest, registers in registry)
            await Task.Run(() => InstallLayerAction());

            // 4. Deploy reshade.vulkan.ini (as reshade.ini) to game directory
            AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, BuildScreenshotSavePath(card.GameName), _settingsViewModel.OverlayHotkey, _settingsViewModel.ScreenshotHotkey);

            // 5. Deploy ReShadePreset.ini if present
            AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);

            // 5b. Create Vulkan footprint file so RDXC can detect this game later
            VulkanFootprintService.Create(card.InstallPath);

            // 5c. Deploy shaders locally to the game folder
            _shaderPackService.SyncGameFolder(card.InstallPath,
                ResolveShaderSelection(card.GameName, card.ShaderModeOverride));

            // 6. Mark warning as shown for this session
            _vulkanLayerWarningShownThisSession = true;

            // 7. Update card RS status
            var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
            Action updateCard = () =>
            {
                card.RsInstalledVersion = vulkanVersion;
                card.RsStatus        = GameStatus.Installed;
                card.RsActionMessage = "✅ ReShade installed (Vulkan Layer)!";
                card.NotifyAll();
                card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);

                // Deploy managed addons now that ReShade is present
                DeployAddonsForCard(card.GameName);
            };
            if (DispatchUiAction != null) DispatchUiAction(updateCard);
            else DispatcherQueue?.TryEnqueue(() => updateCard());
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ Vulkan ReShade Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallReShadeVulkanAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.RsIsInstalling = false; }
    }

    /// <summary>
    /// GAC symlink install flow for XNA Framework games (e.g. Terraria).
    /// Creates symbolic links in the GAC directory pointing to staged files in the game folder.
    /// Requires admin privileges.
    /// </summary>
    internal async Task InstallReShadeGacAsync(GameCardViewModel card, string gacDirectory)
    {
        // 1. Check admin privileges (same pattern as Vulkan)
        if (!IsRunningAsAdminFunc())
        {
            if (ShowVulkanAdminRequiredDialog != null)
                await ShowVulkanAdminRequiredDialog();
            else
                card.RsActionMessage = "⚠ Administrator privileges are required for GAC symlink installation. Restart RHI as admin.";
            return;
        }

        // Resolve DLL filename from manifest or auto-detection
        var dllFileName = card.DllOverrideEnabled
            ? (GetDllOverride(card.GameName)?.ReShadeFileName)
            : (GetManifestDllNames(card.GameName)?.ReShade is { Length: > 0 } mRs
                ? mRs
                : ResolveAutoReShadeFilename(card.DetectedApis));
        dllFileName ??= "dxgi.dll"; // default — DX11/DX12 games use dxgi.dll

        card.RsIsInstalling = true;
        card.RsActionMessage = "Installing ReShade (GAC symlink)...";
        try
        {
            AuxInstallService.EnsureReShadeStaging();

            await Task.Run(() =>
            {
                AuxInstallService.InstallGacSymlink(
                    card.InstallPath,
                    gacDirectory,
                    dllFileName,
                    use32Bit: card.Is32Bit,
                    screenshotSavePath: BuildScreenshotSavePath(card.GameName),
                    overlayHotkey: _settingsViewModel.OverlayHotkey);
            });

            // Deploy preset and shaders to the game folder
            AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
            _shaderPackService.SyncGameFolder(card.InstallPath,
                ResolveShaderSelection(card.GameName, card.ShaderModeOverride));

            // Read version from the staged DLL in the game folder
            var version = AuxInstallService.ReadInstalledVersion(card.InstallPath, dllFileName);

            Action updateCard = () =>
            {
                card.RsInstalledFile = dllFileName;
                card.RsInstalledVersion = version;
                card.RsStatus = GameStatus.Installed;
                card.RsActionMessage = "✅ ReShade installed (GAC symlink)!";
                card.NotifyAll();
                card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
                DeployAddonsForCard(card.GameName);
            };
            if (DispatchUiAction != null) DispatchUiAction(updateCard);
            else DispatcherQueue?.TryEnqueue(() => updateCard());
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ GAC ReShade Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallReShadeGacAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.RsIsInstalling = false; }
    }

    [RelayCommand]
    public void UninstallReShade(GameCardViewModel? card)
    {
        if (card?.RsRecord == null) return;

        try
        {
            // Remove the RDXC-managed reshade-shaders folder BEFORE calling Uninstall.
            if (!string.IsNullOrEmpty(card.InstallPath))
                _shaderPackService.RemoveFromGameFolder(card.InstallPath);

            // Remove managed addons — they require ReShade to function
            if (!string.IsNullOrEmpty(card.InstallPath))
                _addonPackService.DeployAddonsForGame(card.GameName, card.InstallPath, card.Is32Bit,
                    useGlobalSet: true, perGameSelection: new List<string>());

            // Clean up GAC symlinks if this was a GAC symlink install (e.g. Terraria)
            var gacPath = GetGacSymlinkPath(card.GameName);
            if (gacPath != null && !string.IsNullOrEmpty(card.RsInstalledFile))
            {
                AuxInstallService.UninstallGacSymlink(card.InstallPath, gacPath, card.RsInstalledFile);
            }

            _auxInstaller.Uninstall(card.RsRecord);
            card.RsRecord           = null;
            card.RsInstalledFile    = null;
            card.RsInstalledVersion = null;
            card.RsStatus           = GameStatus.NotInstalled;
            card.RsActionMessage    = "✖ ReShade removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallReShade", ex, note: $"Game: {card.GameName}");
        }
    }

    [RelayCommand]
    public void UninstallVulkanReShade(GameCardViewModel? card)
    {
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        try
        {
            // 1. Delete reshade.ini from the game folder
            var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
            if (File.Exists(iniPath))
                File.Delete(iniPath);

            // 2. Delete the Vulkan footprint file
            VulkanFootprintService.Delete(card.InstallPath);

            // 3. Remove RDXC-managed reshade-shaders folder
            _shaderPackService.RemoveFromGameFolder(card.InstallPath);

            // 4. Restore reshade-shaders-original if it exists
            _shaderPackService.RestoreOriginalIfPresent(card.InstallPath);

            // 5. Remove managed addons — they require ReShade to function
            _addonPackService.DeployAddonsForGame(card.GameName, card.InstallPath, card.Is32Bit,
                useGlobalSet: true, perGameSelection: new List<string>());

            // 6. Update card status — do NOT touch the global Vulkan layer
            card.RsStatus        = GameStatus.NotInstalled;
            card.RsActionMessage = "✖ Vulkan ReShade removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallVulkanReShade", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── RE Framework commands ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task InstallREFrameworkAsync(GameCardViewModel? card)
    {
        if (card == null) return;

        if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            card.RefActionMessage = "No install path — use 📁 to pick the game folder.";
            return;
        }

        card.RefIsInstalling = true;
        card.RefActionMessage = "Starting RE Framework download...";
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.RefActionMessage = p.msg;
                card.RefProgress = p.pct;
            });
            var record = await _refService.InstallAsync(card.GameName, card.InstallPath, progress);
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.RefRecord = record;
                card.RefInstalledVersion = record.InstalledVersion;
                card.RefStatus = GameStatus.Installed;
                card.RefActionMessage = "✅ RE Framework installed!";
                card.NotifyAll();
                card.FadeMessage(m => card.RefActionMessage = m, card.RefActionMessage);
            });
        }
        catch (Exception ex)
        {
            card.RefActionMessage = $"❌ RE Framework Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallREFrameworkAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.RefIsInstalling = false; }
    }

    [RelayCommand]
    public void UninstallREFramework(GameCardViewModel? card)
    {
        if (card == null || card.RefRecord == null) return;
        try
        {
            _refService.Uninstall(card.GameName, card.InstallPath);
            card.RefRecord = null;
            card.RefInstalledVersion = null;
            card.RefStatus = GameStatus.NotInstalled;
            card.RefActionMessage = "✖ RE Framework removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.RefActionMessage = m, card.RefActionMessage);
        }
        catch (Exception ex)
        {
            card.RefActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallREFramework", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── Luma Framework commands ───────────────────────────────────────────────────

    /// <summary>Fuzzy-matches a game name against the Luma completed mods list.
    /// Also honours _nameMappings so the wiki name override box works for Luma games.</summary>
    private LumaMod? MatchLumaGame(string gameName)
    {
        // 0. User-defined name mappings take priority (same logic as RenoDX wiki matching).
        if (_nameMappings.Count > 0)
        {
            string? mapped = null;
            if (_nameMappings.TryGetValue(gameName, out var m))
                mapped = m;
            else
            {
                var gameNorm = _gameDetectionService.NormalizeName(gameName);
                foreach (var kv in _nameMappings)
                {
                    if (_gameDetectionService.NormalizeName(kv.Key) == gameNorm && !string.IsNullOrEmpty(kv.Value))
                    { mapped = kv.Value; break; }
                }
            }
            if (!string.IsNullOrEmpty(mapped))
            {
                var mappedNorm = _gameDetectionService.NormalizeName(mapped);
                foreach (var lm in _lumaMods)
                    if (_gameDetectionService.NormalizeName(lm.Name) == mappedNorm) return lm;
                var mappedLookup = NormalizeForLookup(mapped);
                foreach (var lm in _lumaMods)
                    if (NormalizeForLookup(lm.Name) == mappedLookup) return lm;
            }
        }

        var norm = _gameDetectionService.NormalizeName(gameName);
        foreach (var lm in _lumaMods)
        {
            if (_gameDetectionService.NormalizeName(lm.Name) == norm)
                return lm;
        }
        // Also try the tolerant NormalizeForLookup which strips edition suffixes,
        // parenthetical text, etc. — but still requires a full match, not a
        // substring check, to avoid false positives like "Nioh 3" matching "Nioh".
        var normLookup = NormalizeForLookup(gameName);
        foreach (var lm in _lumaMods)
        {
            if (NormalizeForLookup(lm.Name) == normLookup)
                return lm;
        }
        return null;
    }

    public bool IsLumaEnabled(string gameName) => _lumaEnabledGames.Contains(gameName);

    /// <summary>
    /// Toggles Luma mode for a game. When enabling: uninstalls RenoDX, ReShade, and
    /// DC (if installed as dxgi.dll). When disabling: uninstalls Luma files.
    /// </summary>
    public void ToggleLumaMode(GameCardViewModel card)
    {
        if (card.LumaMod == null) return;

        card.IsLumaMode = !card.IsLumaMode;

        if (card.IsLumaMode)
        {
            _lumaEnabledGames.Add(card.GameName);
            _lumaDisabledGames.Remove(card.GameName);

            // Remove RenoDX mod if installed
            if (card.InstalledRecord != null)
            {
                try
                {
                    _installer.Uninstall(card.InstalledRecord);
                    card.InstalledRecord = null;
                    card.InstalledAddonFileName = null;
                    card.RdxInstalledVersion = null;
                    card.Status = GameStatus.Available;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] RenoDX uninstall failed — {ex.Message}"); }
            }

            // Remove ReShade if installed
            if (card.RsRecord != null)
            {
                try
                {
                    _auxInstaller.Uninstall(card.RsRecord);
                    card.RsRecord           = null;
                    card.RsInstalledFile    = null;
                    card.RsInstalledVersion = null;
                    card.RsStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] ReShade uninstall failed — {ex.Message}"); }
            }
        }
        else
        {
            _lumaEnabledGames.Remove(card.GameName);
            _lumaDisabledGames.Add(card.GameName);

            // Uninstall Luma files if installed
            if (card.LumaRecord != null)
            {
                try
                {
                    _lumaService.Uninstall(card.LumaRecord);
                    card.LumaRecord = null;
                    card.LumaStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] Luma uninstall failed — {ex.Message}"); }
            }
            else
            {
                // Fallback: even without a record, try to clean up known Luma artifacts
                // (handles cases where record was lost or never saved)
                try
                {
                    var rsDir = Path.Combine(card.InstallPath, "reshade-shaders");
                    if (Directory.Exists(rsDir))
                    {
                        _shaderPackService.RemoveFromGameFolder(card.InstallPath);
                        if (Directory.Exists(rsDir))
                            Directory.Delete(rsDir, true);
                    }
                    var rsIni = Path.Combine(card.InstallPath, "reshade.ini");
                    if (File.Exists(rsIni)) File.Delete(rsIni);

                    // Try to find and remove Luma dll files (common names)
                    foreach (var pattern in new[] { "dxgi.dll", "d3d11.dll", "Luma*.dll", "Luma*.addon*" })
                    {
                        foreach (var f in Directory.GetFiles(card.InstallPath, pattern))
                        {
                            // Only remove if it looks like a Luma file (not ReShade/DC)
                            var fn = Path.GetFileName(f);
                            if ((fn.StartsWith("Luma", StringComparison.OrdinalIgnoreCase)
                                || fn.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                                || fn.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
                                && !fn.StartsWith("renodx-devkit", StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(f); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] Failed to delete '{f}' — {ex.Message}"); }
                            }
                        }
                    }
                    card.LumaStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] Fallback cleanup failed — {ex.Message}"); }
            }

            // Always clear the persisted record if it exists on disk
            LumaService.RemoveRecordByPath(card.InstallPath);

            // Luma's uninstall removes its bundled ReShade (dxgi.dll).
            // Reset RS status if there's no standalone ReShade install.
            var rsRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShade)
                        ?? _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShadeNormal);
            if (rsRecord == null)
            {
                card.RsRecord           = null;
                card.RsInstalledFile    = null;
                card.RsInstalledVersion = null;
                card.RsStatus           = GameStatus.Available;
            }

            // Uninstall ReLimiter when leaving Luma mode
            if (card.IsUlInstalled)
            {
                try
                {
                    UninstallUl(card);
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] ReLimiter uninstall failed — {ex.Message}"); }
            }
        }

        SaveNameMappings();
        card.NotifyAll();
    }

    [RelayCommand]
    public async Task InstallLumaAsync(GameCardViewModel? card)
    {
        if (card?.LumaMod == null || string.IsNullOrEmpty(card.InstallPath)) return;

        card.IsLumaInstalling = true;
        card.LumaActionMessage = "Installing Luma...";
        try
        {
            var record = await _lumaService.InstallAsync(
                card.LumaMod,
                card.InstallPath,
                new Progress<(string msg, double pct)>(p =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        card.LumaActionMessage = p.msg;
                        card.LumaProgress = p.pct;
                    });
                }));

            card.LumaRecord = record;
            card.LumaStatus = GameStatus.Installed;
            card.LumaActionMessage = "Luma installed!";
            // Luma bundles its own ReShade — update RS status so ReLimiter/DC
            // buttons become available immediately without needing a refresh.
            if (card.RsStatus == GameStatus.NotInstalled || card.RsStatus == GameStatus.Available)
                card.RsStatus = GameStatus.Installed;
            card.FadeMessage(m => card.LumaActionMessage = m, card.LumaActionMessage);
        }
        catch (Exception ex)
        {
            card.LumaActionMessage = $"❌ Install failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallLuma", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.IsLumaInstalling = false;
            card.NotifyAll();
        }
    }

    [RelayCommand]
    public void UninstallLuma(GameCardViewModel? card)
    {
        if (card?.LumaRecord == null) return;
        try
        {
            _lumaService.Uninstall(card.LumaRecord);
            card.LumaRecord = null;
            card.LumaStatus = GameStatus.NotInstalled;
            card.LumaActionMessage = "✖ Luma removed.";
            // Luma's uninstall removes its bundled ReShade (dxgi.dll).
            // If there's no standalone ReShade install record, RS is no longer installed.
            var rsRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShade)
                        ?? _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShadeNormal);
            if (rsRecord == null)
                card.RsStatus = GameStatus.Available;
            card.NotifyAll();
            card.FadeMessage(m => card.LumaActionMessage = m, card.LumaActionMessage);
        }
        catch (Exception ex)
        {
            card.LumaActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallLuma", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── Normal ReShade toggle orchestration ──────────────────────────────────────

    /// <summary>
    /// Toggles a game between addon-enabled ReShade and normal (non-addon) ReShade.
    /// When <paramref name="enable"/> is true: uninstalls existing ReShade (if any),
    /// removes managed addons, persists the flag. Does NOT install normal ReShade.
    /// When false: uninstalls existing ReShade (if any), clears the flag. Does NOT
    /// install addon ReShade. In both cases the user must click "Install ReShade"
    /// to get the correct version installed.
    /// </summary>
    public void SetUseNormalReShade(GameCardViewModel card, bool enable)
    {
        if (enable)
        {
            // ── Enable: flag for normal (non-addon) ReShade ───────────────────

            // 1. Uninstall existing addon ReShade (if installed)
            if (card.RsRecord != null)
            {
                try
                {
                    // Remove reshade-shaders folder
                    if (!string.IsNullOrEmpty(card.InstallPath))
                        _shaderPackService.RemoveFromGameFolder(card.InstallPath);

                    _auxInstaller.Uninstall(card.RsRecord);
                    card.RsRecord = null;
                    card.RsInstalledFile = null;
                    card.RsInstalledVersion = null;
                    _crashReporter.Log($"[SetUseNormalReShade] Uninstalled addon RS for '{card.GameName}'");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[SetUseNormalReShade] Addon RS uninstall failed — {ex.Message}");
                }
            }

            // 2. Remove all managed addon files from game folder
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                _addonPackService.DeployAddonsForGame(card.GameName, card.InstallPath, card.Is32Bit,
                    useGlobalSet: true, perGameSelection: new List<string>());
            }

            // 2b. Uninstall RenoDX mod (if installed)
            if (card.InstalledRecord != null)
            {
                try { UninstallMod(card); }
                catch (Exception ex) { _crashReporter.Log($"[SetUseNormalReShade] RenoDX uninstall failed — {ex.Message}"); }
            }

            // 2c. Uninstall ReLimiter (if installed)
            if (card.UlStatus == GameStatus.Installed || card.UlStatus == GameStatus.UpdateAvailable)
            {
                try { UninstallUl(card); }
                catch (Exception ex) { _crashReporter.Log($"[SetUseNormalReShade] ReLimiter uninstall failed — {ex.Message}"); }
            }

            // 2d. Uninstall Display Commander (if installed)
            if (card.DcStatus == GameStatus.Installed || card.DcStatus == GameStatus.UpdateAvailable)
            {
                try { UninstallDc(card); }
                catch (Exception ex) { _crashReporter.Log($"[SetUseNormalReShade] Display Commander uninstall failed — {ex.Message}"); }
            }

            // 3. Persist the flag — do NOT install normal ReShade
            _normalReShadeGames.Add(card.GameName);
            SaveNameMappings();

            card.UseNormalReShade = true;
            card.RsStatus = GameStatus.NotInstalled;
            card.RsActionMessage = "Normal ReShade selected — click Install to deploy.";
            card.NotifyAll();
            card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
            _crashReporter.Log($"[SetUseNormalReShade] '{card.GameName}' flagged for normal ReShade (not installed yet)");
        }
        else
        {
            // ── Disable: clear the normal ReShade flag ────────────────────────

            // 1. Uninstall existing normal ReShade (if installed)
            if (card.RsRecord != null)
            {
                try
                {
                    // Remove reshade-shaders folder
                    if (!string.IsNullOrEmpty(card.InstallPath))
                        _shaderPackService.RemoveFromGameFolder(card.InstallPath);

                    _auxInstaller.Uninstall(card.RsRecord);
                    card.RsRecord = null;
                    card.RsInstalledFile = null;
                    card.RsInstalledVersion = null;
                    _crashReporter.Log($"[SetUseNormalReShade] Uninstalled normal RS for '{card.GameName}'");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[SetUseNormalReShade] Normal RS uninstall failed — {ex.Message}");
                }
            }

            // 2. Clear the flag — do NOT install addon ReShade
            _normalReShadeGames.Remove(card.GameName);
            SaveNameMappings();

            card.UseNormalReShade = false;
            card.RsStatus = GameStatus.NotInstalled;
            card.RsActionMessage = "Addon ReShade selected — click Install to deploy.";
            card.NotifyAll();
            card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
            _crashReporter.Log($"[SetUseNormalReShade] '{card.GameName}' flagged for addon ReShade (not installed yet)");
        }
    }
}

