// MainViewModel.Install.Luma.cs -- ReShade install/uninstall, RE Framework, and Luma commands.

using CommunityToolkit.Mvvm.Input;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
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
        if (detectedApis.Contains(GraphicsApiType.DirectX8))
            return "d3d8.dll";
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
            var effectiveChannel = card.UseNormalReShade ? "(Normal/NoAddons)" : ResolveReShadeChannel(card.GameName);
            var filenameSource = card.DllOverrideEnabled ? "UserDllOverride"
                : (GetManifestDllNames(card.GameName)?.ReShade is { Length: > 0 } ? "ManifestDllOverride" : "AutoDetect");
            _crashReporter.Log($"[InstallReShadeAsync] {card.GameName}: " +
                $"channel={effectiveChannel}, useNormalReShade={card.UseNormalReShade}, " +
                $"DllOverrideEnabled={card.DllOverrideEnabled}, filenameSource={filenameSource}, " +
                $"filenameOverride={rsFilenameOverride ?? "dxgi.dll"}, " +
                $"DetectedApis=[{string.Join(",", card.DetectedApis)}], is32Bit={card.Is32Bit}");

            var record = await _auxInstaller.InstallReShadeAsync(card.GameName, card.InstallPath,
                shaderModeOverride: card.ShaderModeOverride,
                use32Bit:       card.Is32Bit,
                filenameOverride: rsFilenameOverride,
                selectedPackIds: selectedPacks,
                progress:       progress,
                screenshotSavePath: BuildScreenshotSavePath(card.GameName),
                useNormalReShade: card.UseNormalReShade,
                overlayHotkey: _settingsViewModel.OverlayHotkey,
                screenshotHotkey: _settingsViewModel.ScreenshotHotkey,
                channel: card.UseNormalReShade ? null : ResolveReShadeChannel(card.GameName));

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

            // Deploy shaders to the game folder
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
        if (card == null) return;

        // GAC symlink games may not have an RsRecord (the install skips creating one)
        var gacPath = GetGacSymlinkPath(card.GameName);
        bool isGacGame = gacPath != null;

        // For non-GAC games, RsRecord is required
        if (!isGacGame && card.RsRecord == null) return;

        // GAC symlink removal requires admin (files are in C:\Windows\...)
        if (isGacGame && !IsRunningAsAdminFunc())
        {
            card.RsActionMessage = "⚠ Administrator privileges required to remove GAC symlinks. Enable Admin Mode in Settings.";
            card.NotifyAll();
            return;
        }

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
            if (isGacGame && !string.IsNullOrEmpty(card.RsInstalledFile))
            {
                AuxInstallService.UninstallGacSymlink(card.InstallPath, gacPath!, card.RsInstalledFile);
            }

            // Remove reshade.ini from the game folder (GAC games stage it there)
            if (isGacGame && !string.IsNullOrEmpty(card.InstallPath))
            {
                var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
                if (File.Exists(iniPath)) File.Delete(iniPath);
            }

            if (card.RsRecord != null)
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

            // Remove RenoDX mod if installed (skip for lumaRenodxCompat games)
            if (card.InstalledRecord != null && !card.LumaRenodxCompatible)
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
                                && !fn.StartsWith("renodx-devkit", StringComparison.OrdinalIgnoreCase)
                                && !fn.StartsWith("renodx-dlssfix", StringComparison.OrdinalIgnoreCase)
                                && !fn.StartsWith("renodx-universal_ue", StringComparison.OrdinalIgnoreCase))
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

    public async Task InstallLumaAsync(GameCardViewModel? card, bool skipWarning = false)
    {
        if (card?.LumaMod == null || string.IsNullOrEmpty(card.InstallPath)) return;

        // Check for manifest-driven install warning (skip during Update All)
        if (!skipWarning && !await CheckInstallWarningAsync(card.GameName, "luma")) return;

        card.IsLumaInstalling = true;
        card.LumaActionMessage = "Installing Luma...";
        try
        {
            var selectedPacks = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);
            var record = await _lumaService.InstallAsync(
                card.LumaMod,
                card.InstallPath,
                selectedPacks,
                BuildScreenshotSavePath(card.GameName),
                _settingsViewModel.OverlayHotkey,
                _settingsViewModel.ScreenshotHotkey,
                card.GameName,
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

    // ── Install warning helper ───────────────────────────────────────────────────

    /// <summary>
    /// Checks the manifest for a per-game, per-component install warning.
    /// If one exists, shows a dialog. Returns true if install should proceed, false if cancelled.
    /// </summary>
    public async Task<bool> CheckInstallWarningAsync(string gameName, string component)
    {
        if (_manifest?.InstallWarnings == null) return true;
        if (!_manifest.InstallWarnings.TryGetValue(gameName, out var warnings)) return true;
        if (!warnings.TryGetValue(component, out var message)) return true;
        if (string.IsNullOrEmpty(message)) return true;

        try
        {
            Microsoft.UI.Xaml.XamlRoot? xamlRoot = null;
            if (Microsoft.UI.Xaml.Application.Current is App app)
            {
                var field = typeof(App).GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(app) is MainWindow mw)
                    xamlRoot = mw.Content?.XamlRoot;
            }
            if (xamlRoot == null) return true;

            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = $"⚠ Install Note — {gameName}",
                Content = message,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            var result = await dialog.ShowAsync();
            return result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckInstallWarningAsync] Dialog failed — {ex.Message}");
            return true; // Proceed on error
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