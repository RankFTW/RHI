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
            // Exclude: strip wiki mod — "No RenoDX mod available"
            card.Mod           = null;
            card.IsExternalOnly = false;
            card.ExternalUrl   = "";
            card.ExternalLabel = "";
            card.DiscordUrl    = null;
            card.WikiStatus    = "—";
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

    public const string DefaultUeExtendedUrl = "https://marat569.github.io/renodx/renodx-ue-extended.addon64";
    public string UeExtendedUrl => _manifest?.ComponentUrls?.TryGetValue("ueExtended", out var url) == true && !string.IsNullOrEmpty(url) ? url : DefaultUeExtendedUrl;
    public const string UeExtendedFile   = "renodx-ue-extended.addon64";
    public const string GenericUnrealFile = "renodx-unrealengine.addon64";

    /// <summary>Stores the original named mod SnapshotUrl per game when UE-Extended is toggled ON, so it can be restored when toggled OFF.</summary>
    private readonly Dictionary<string, string> _ueExtendedOriginalUrls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Toggles the UE-Extended mode for a Generic UE card.
    /// When ON: Mod.SnapshotUrl → marat569 URL; if the standard generic file is on disk it is deleted.
    /// When OFF: Mod.SnapshotUrl → standard WikiService.GenericUnrealUrl; the extended file is deleted.
    /// Card updates synchronously — no refresh needed.
    /// </summary>
    public void ToggleUeExtended(GameCardViewModel card)
    {
        if (card == null) return;
        // Allow toggle for any UE card that shows the button
        if (card.UeExtendedToggleVisibility != Microsoft.UI.Xaml.Visibility.Visible) return;

        bool nowExtended = !card.UseUeExtended;

        if (nowExtended)
            _ueExtendedGames.Add(card.GameName);
        else
            _ueExtendedGames.Remove(card.GameName);
        SaveNameMappings();

        // Store the original named mod URL before swapping (for restoring later)
        string? originalModUrl = card.Mod?.SnapshotUrl;

        // Swap the SnapshotUrl on the card's Mod in-place
        if (nowExtended)
        {
            // Store original URL for restoration (may be null for Nexus-only games)
            _ueExtendedOriginalUrls[card.GameName] = originalModUrl ?? "";

            if (card.Mod != null)
                card.Mod.SnapshotUrl = UeExtendedUrl;
            else
                card.Mod = new GameMod
                {
                    Name = "Generic Unreal Engine",
                    Maintainer = "ShortFuse",
                    SnapshotUrl = UeExtendedUrl,
                    Status = "✅",
                    IsGenericUnreal = true,
                };
        }
        else
        {
            // Restore original named mod URL
            if (card.Mod != null)
            {
                if (_ueExtendedOriginalUrls.TryGetValue(card.GameName, out var savedUrl))
                {
                    card.Mod.SnapshotUrl = string.IsNullOrEmpty(savedUrl) ? null : savedUrl;
                    _ueExtendedOriginalUrls.Remove(card.GameName);
                }
                else
                {
                    // No saved URL — try to recover from wiki data
                    var wikiMod = _gameDetectionService.MatchGame(
                        new DetectedGame { Name = card.GameName, InstallPath = card.InstallPath ?? "" },
                        _allMods, _nameMappings);
                    if (wikiMod?.SnapshotUrl != null)
                    {
                        card.Mod.SnapshotUrl = wikiMod.SnapshotUrl;
                    }
                    else if (card.EngineHint?.Contains("Unreal") == true)
                    {
                        // Generic UE fallback — same URL the card would get during normal build
                        card.Mod.SnapshotUrl = WikiService.GenericUnrealUrl;
                        card.Mod.IsGenericUnreal = true;
                    }
                }
            }
        }

        // Delete the opposing addon file from disk (if present)
        if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
        {
            try
            {
                // When toggling ON: delete the named mod file or generic UE file
                // When toggling OFF: delete the UE-Extended file
                if (nowExtended)
                {
                    // Delete whatever addon is currently installed (named mod or generic)
                    if (card.InstalledAddonFileName != null)
                    {
                        var deletePath = Path.Combine(card.InstallPath, card.InstalledAddonFileName);
                        if (File.Exists(deletePath))
                        {
                            File.Delete(deletePath);
                            _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Deleted {card.InstalledAddonFileName} from {card.InstallPath}");
                        }
                    }
                    // Also try deleting the generic UE file
                    var genericPath = Path.Combine(card.InstallPath, GenericUnrealFile);
                    if (File.Exists(genericPath)) File.Delete(genericPath);
                }
                else
                {
                    // Delete UE-Extended file
                    var extPath = Path.Combine(card.InstallPath, UeExtendedFile);
                    if (File.Exists(extPath))
                    {
                        File.Delete(extPath);
                        _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Deleted {UeExtendedFile} from {card.InstallPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Failed to delete file — {ex.Message}");
            }
        }

        // Check if mod was installed before we clear the record
        bool wasInstalled = card.InstalledRecord != null;

        // Clean up UE-Extended auto-configured settings when toggling OFF
        if (!nowExtended && wasInstalled && !string.IsNullOrEmpty(card.InstallPath))
        {
            AuxInstallService.RemoveRenoDxNativeHdrSettings(card.InstallPath);
            AuxInstallService.RemoveEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName);
        }

        // Clear the install record — the old addon was deleted
        if (card.InstalledRecord != null)
        {
            _installer.RemoveRecord(card.InstalledRecord);
            card.InstalledRecord        = null;
            card.InstalledAddonFileName = null;
            card.RdxInstalledVersion    = null;
            card.Status                 = GameStatus.Available;
        }

        card.UseUeExtended = nowExtended;
        if (nowExtended)
        {
            card.IsGenericMod = true;
            card.IsExternalOnly = false; // UE-Extended has a direct download URL
        }
        else
        {
            // Restore IsGenericMod — it's a named mod if the URL isn't the generic UE one
            bool hasNamedMod = card.Mod?.SnapshotUrl != null
                && card.Mod.SnapshotUrl != WikiService.GenericUnrealUrl
                && card.Mod.SnapshotUrl != UeExtendedUrl;
            bool isNexusOnly = card.Mod?.SnapshotUrl == null
                && (card.NexusUrl != null || card.DiscordUrl != null);
            card.IsGenericMod = !hasNamedMod && !isNexusOnly;
            card.IsExternalOnly = isNexusOnly;
        }
        card.NotifyAll();

        // Auto-install if the previous addon was installed AND the new target has a direct download
        // REMOVED: No auto-install on toggle. User must click Install manually.
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

        // Scan disk for any renodx-* addon file already installed (skip for emulator cards)
        var addonOnDisk = game.Name.Equals("Ryubing", StringComparison.OrdinalIgnoreCase)
            ? null
            : ScanForInstalledAddon(scanPath, effectiveMod);
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
            WikiStatus     = _wikiExclusions.Contains(game.Name)
                              ? "—"
                              : (effectiveMod?.SnapshotUrl == null && effectiveMod?.DiscordUrl != null && effectiveMod?.NexusUrl == null)
                              ? "💬"
                              : (mod == null && fallback != null && !useUeExt && !isNativeHdr)
                                ? "?"
                                : effectiveMod?.Status ?? "—",
            Maintainer     = effectiveMod?.Maintainer ?? "",
            IsGenericMod   = useUeExt || (fallback != null && mod == null),
            EngineHint     = engineOverrideLabel != null
                           ? (useUeExt && engine == EngineType.Unknown ? FormatEngineHint(EngineType.Unreal, scanPath) : engineOverrideLabel)
                           : (useUeExt && engine == EngineType.Unknown) ? FormatEngineHint(EngineType.Unreal, scanPath)
                           : engine == EngineType.Unreal       ? FormatEngineHint(EngineType.Unreal, scanPath)
                           : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                           : engine == EngineType.Unity        ? "Unity"
                           : engine == EngineType.REEngine     ? "RE Engine" : "",
            Notes          = effectiveMod != null ? BuildNotes(game.Name, effectiveMod, fallback, _genericNotes, isNativeHdr) : "",
            InstalledAddonFileName = record?.AddonFileName,
            RdxInstalledVersion = record != null ? AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName) : null,
            IsExternalOnly  = _wikiExclusions.Contains(game.Name)
                              ? false
                              : effectiveMod?.SnapshotUrl == null &&
                                (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null),
            ExternalUrl     = _wikiExclusions.Contains(game.Name)
                              ? ""
                              : effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "",
            ExternalLabel   = _wikiExclusions.Contains(game.Name)
                              ? ""
                              : effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord",
            NexusUrl        = effectiveMod?.NexusUrl,
            DiscordUrl      = _wikiExclusions.Contains(game.Name)
                              ? null
                              : effectiveMod?.DiscordUrl,
            NameUrl         = effectiveMod?.NameUrl,
            IsManuallyAdded = true,
            IsFavourite            = _favouriteGames.Contains(game.Name),
            UseUeExtended          = useUeExt,
            IsNativeHdrGame        = isNativeHdr,
            IsManifestUeExtended   = useUeExt && !isNativeHdr,
            LumaRenodxCompatible   = _manifest?.LumaRenodxCompat?.Contains(game.Name) == true,
            EngineIniProjectOverride = _manifest?.EngineIniPathOverrides?.TryGetValue(game.Name, out var eiOverride2) == true ? eiOverride2 : null,
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

        // ── Emulator setup ────────────────────────────────────────────────────
        if (game.Name.Equals("Ryubing", StringComparison.OrdinalIgnoreCase))
        {
            card.IsEmulator = true;
            card.VulkanRenderingPath = "Vulkan"; // Force Vulkan ReShade
            if (_manifest?.EmulatorGames?.TryGetValue("Ryubing", out var emuConfig) == true)
            {
                card.EmulatorAddonNames = emuConfig.Addons;
                // Set up a synthetic Mod so the RenoDX row shows an install button
                card.Mod = new GameMod
                {
                    Name = "Ryubing (9 games)",
                    Maintainer = "Souperman9",
                    SnapshotUrl = "emulator-bundle", // Sentinel — triggers bundle install
                    Status = "✅",
                };

                // Detect if addons are already installed
                var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
                int foundCount = 0;
                foreach (var wikiName in emuConfig.Addons)
                {
                    var emuMod = _allMods.FirstOrDefault(m => m.Name.Equals(wikiName, StringComparison.OrdinalIgnoreCase));
                    if (emuMod?.SnapshotUrl == null) continue;
                    var fileName = Path.GetFileName(emuMod.SnapshotUrl);
                    if (File.Exists(Path.Combine(deployPath, fileName)) || File.Exists(Path.Combine(card.InstallPath, fileName)))
                        foundCount++;
                }
                if (foundCount > 0)
                {
                    card.Status = GameStatus.Installed;
                    card.InstalledAddonFileName = $"{foundCount} addons";
                }
            }
        }

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

        // Background scan for components not detected synchronously (DC, OptiScaler, DXVK, DLSS/Streamline)
        _ = Task.Run(() => ScanManualGameComponentsAsync(card));
    }

    [RelayCommand]
    public async Task InstallModAsync(GameCardViewModel? card)
    {
        // ── Emulator bundle install ───────────────────────────────────────────
        if (card != null && card.IsEmulator)
        {
            // Resolve addon names from manifest if not already set on the card
            if (card.EmulatorAddonNames == null || card.EmulatorAddonNames.Count == 0)
            {
                if (_manifest?.EmulatorGames?.TryGetValue("Ryubing", out var emuCfg) == true)
                    card.EmulatorAddonNames = emuCfg.Addons;
            }
            if (card.EmulatorAddonNames?.Count > 0)
            {
                await InstallEmulatorAddonsAsync(card);
                return;
            }
        }

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

            // Apply [renodx] Native HDR settings for UE-Extended games
            if (card.UseUeExtended)
                AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath);

            // Pre-populate [renodx] key placeholders for generic UE/Unity addons (addon fills values on first launch)
            if (!card.UseUeExtended && card.EngineHint?.Contains("Unreal") == true)
                AuxInstallService.ApplyRenodxKeyPlaceholders(card.InstallPath, "Unreal");
            else if (!card.UseUeExtended && card.EngineHint?.Contains("Unity") == true)
                AuxInstallService.ApplyRenodxKeyPlaceholders(card.InstallPath, "Unity");

            // Apply per-game [renodx] INI overrides from manifest
            if (_manifest?.RenodxIniOverrides != null
                && _manifest.RenodxIniOverrides.TryGetValue(card.GameName, out var iniOverrides))
                AuxInstallService.ApplyRenodxIniOverrides(card.InstallPath, iniOverrides);

            // Deploy Engine.ini HDR settings for UE-Extended games (skip if user disabled it)
            if (card.UseUeExtended && card.InstalledRecord?.EngineIniHdr != false)
                AuxInstallService.ApplyEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName);

            // Always deploy r.LUT.UpdateEveryFrame=1 for any Unreal Engine game with a RenoDX mod (skip if user disabled it)
            if (card.EngineHint?.Contains("Unreal") == true && card.InstalledRecord?.EngineIniLut != false)
                AuxInstallService.ApplyEngineIniLutSetting(card.InstallPath, card.EngineIniProjectOverride, card.GameName);

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
                // Reset Nexus baseline so update indicator clears after install
                _nexusUpdateService.ResetBaseline(card.GameName);
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
        // ── Emulator bundle uninstall ─────────────────────────────────────────
        if (card != null && card.IsEmulator && card.EmulatorAddonNames?.Count > 0)
        {
            UninstallEmulatorAddons(card);
            return;
        }

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

        // Clean up UE-Extended auto-configured settings when uninstalling
        if (card.UseUeExtended && !string.IsNullOrEmpty(card.InstallPath))
        {
            AuxInstallService.RemoveRenoDxNativeHdrSettings(card.InstallPath);
            AuxInstallService.RemoveEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName);
        }

        SaveLibrary();
        _filterViewModel.UpdateCounts();
    }

    // ── ReLimiter commands ────────────────────────────────────────────────────
}
