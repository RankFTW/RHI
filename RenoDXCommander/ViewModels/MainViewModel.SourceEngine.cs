// MainViewModel.SourceEngine.cs -- RenoDX's Source engine mod: matching (RenoDXdb-source.json), the install folder
// (bin\ for 32-bit games), and the DXVK proxy setup that installs, updates and uninstalls with the addon.
// File work lives in Services/SourceEngineSetup.cs.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Engine badge text for Source games.</summary>
    public const string SourceEngineHint = "Source Engine";

    /// <summary>UI callback: the Source install dialog (what gets set up, VAC / -insecure, HDR Full). False = cancelled.</summary>
    public Func<GameCardViewModel, Task<bool>>? ConfirmSourceEngineInstall { get; set; }

    /// <summary>
    /// The Source engine DB entry for a game, or null. Exact name first, then ignoring punctuation and
    /// spacing like wiki matching (the DB has "Half Life 2" / "Counter Strike: Source"; Steam has hyphens).
    /// </summary>
    public RenoDXDbSourceEntry? GetDbSourceEntry(string gameName)
    {
        var entries = _dbSourceEntries;
        if (entries.TryGetValue(gameName, out var e)) return e;
        var norm = NormalizeForLookup(gameName);
        if (norm.Length == 0) return null;
        foreach (var (name, entry) in entries)
            if (NormalizeForLookup(name) == norm) return entry;
        return null;
    }

    /// <summary>The shared Source engine mod for a game in RenoDXdb-source.json, else null.</summary>
    private GameMod? MakeGenericSourceFor(string gameName)
    {
        var entry = GetDbSourceEntry(gameName);
        if (entry == null) return null;
        return new GameMod
        {
            Name            = "Generic Source Engine",
            Maintainer      = "CommandGenius",
            SnapshotUrl     = SourceEngineSetup.AddonUrl64,
            SnapshotUrl32   = SourceEngineSetup.AddonUrl32,
            Status          = string.Equals(entry.Status, "WIP", StringComparison.OrdinalIgnoreCase) ? "🚧" : "✅",
            IsGenericSource = true,
        };
    }

    /// <summary>
    /// Both init paths, after the manifest and before the folder moves: 32-bit Source games install into bin\,
    /// Garry's Mod into bin\win64\ (ReShade, the addon and the DXVK proxy go beside the engine), so they get an install-folder override like
    /// the manifest's. A manifest entry or a folder the user picked wins. The existing folder-move pass then moves
    /// anything RHI already installed in the main folder. File-system work: background thread.
    /// </summary>
    private void ApplySourceEngineFolders(List<DetectedGame> games)
    {
        if (_dbSourceEntries.Count == 0) return;
        foreach (var game in games)
        {
            if (string.IsNullOrEmpty(game.InstallPath) || GetDbSourceEntry(game.Name) == null) continue;
            if (_installPathOverrides.ContainsKey(game.Name) || _manifestWikiUnlinks.Contains(game.Name)) continue;
            var key = GameKey.From(game.Name, game.Source ?? "").ToKey();
            if (_folderOverrides.ContainsKey(key) || _folderOverrides.ContainsKey(game.Name)) continue;
            var sub = SourceEngineSetup.PrimarySubfolder(game.InstallPath);
            if (sub == null) continue;
            _installPathOverrides[game.Name] = sub;
            _crashReporter.Log($"[MainViewModel.ApplySourceEngineFolders] '{game.Name}': Source engine — install folder '{sub}'");
        }
    }

    /// <summary>
    /// Before the Source addon downloads (UI thread): refuses when RHI's DXVK is on (its direct mode replaces
    /// the proxy setup), shows the install dialog on a first install, and makes sure ReShade is installed here
    /// as d3d9.dll (installing or renaming it). False = stop.
    /// </summary>
    private async Task<bool> PrepareSourceInstallAsync(GameCardViewModel card)
    {
        if (card.DxvkRecord != null)
        {
            card.ActionMessage = "Turn off DXVK for this game first. The Source mod sets up its own DXVK.";
            return false;
        }
        if (card.InstalledRecord == null && ConfirmSourceEngineInstall != null && !await ConfirmSourceEngineInstall(card))
            return false;

        // The mod runs on the game's DirectX 9 renderer: ReShade goes in as d3d9.dll, never the Vulkan layer
        if (card.RequiresVulkanInstall)
        {
            _crashReporter.Log($"[MainViewModel.PrepareSourceInstallAsync] '{card.GameName}': was set up for Vulkan — switching to DirectX 9");
            card.GraphicsApi = GraphicsApiType.DirectX9;
            card.DetectedApis = new HashSet<GraphicsApiType>(card.DetectedApis.Where(a => a != GraphicsApiType.Vulkan)) { GraphicsApiType.DirectX9 };
            card.IsDualApiGame = false;
            if (card.VulkanRenderingPath == "Vulkan") SetVulkanRenderingPath(card.GameName, "DirectX", card.Source ?? "");
            VulkanFootprintService.Delete(card.InstallPath);
            card.NotifyAll();
        }
        if (card.RequiresVulkanInstall)
        {
            card.ActionMessage = "This game is set to Vulkan in RHI. Set its graphics API to DirectX 9 in Game Overrides, then install again.";
            return false;
        }

        var rs = card.RsRecord;
        bool reShadeReady = rs != null
            && string.Equals(rs.InstalledAs, SourceEngineSetup.ReShadeDll, StringComparison.OrdinalIgnoreCase)
            && InstallFolderMover.SamePath(rs.InstallPath, card.InstallPath)
            && File.Exists(Path.Combine(card.InstallPath, SourceEngineSetup.ReShadeDll));
        if (!reShadeReady)
        {
            _crashReporter.Log($"[MainViewModel.PrepareSourceInstallAsync] '{card.GameName}': installing ReShade as {SourceEngineSetup.ReShadeDll}");
            card.ActionMessage = "Installing ReShade...";
            await InstallReShadeInternalAsync(card, SourceEngineSetup.ReShadeDll);
        }
        if (!File.Exists(Path.Combine(card.InstallPath, SourceEngineSetup.ReShadeDll)))
        {
            card.ActionMessage = "ReShade couldn't be installed as d3d9.dll. See the ReShade row.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Deploys the Source mod's DXVK proxy setup for an installed addon: stable DXVK d3d9.dll as dxgi_dxvk.dll
    /// beside ReShade, the reshade.ini lines, dxvk.conf in the main folder, and (Garry's Mod) the 64-bit
    /// ReShade, addon and proxy in bin\win64\. Safe to run again (updates refresh the files). Throws when
    /// DXVK or the second addon can't be downloaded.
    /// </summary>
    internal async Task ApplySourceSetupAsync(GameCardViewModel card)
    {
        var installDir = card.InstallPath;
        var root = card.DetectedGame?.InstallPath is { Length: > 0 } r ? r : installDir;
        bool is32 = card.Is32Bit;
        var channel = card.UseNormalReShade ? AuxInstallService.ChannelStable : ResolveReShadeChannel(card.GameName, card.Source ?? "");

        // Stable DXVK, whatever variant the DXVK section uses
        var previousVariant = _dxvkService.SelectedVariant;
        _dxvkService.SelectedVariant = DxvkVariant.Stable;
        try { await _dxvkService.EnsureStagingAsync().ConfigureAwait(false); }
        finally { _dxvkService.SelectedVariant = previousVariant; }
        var stableDir = DxvkService.GetStagingDirForVariant(DxvkVariant.Stable);
        string StagedD3d9(bool x64) => Path.Combine(stableDir, x64 ? "x64" : "x32", "d3d9.dll");

        // The other build's folder (Garry's Mod: bin\win64 next to the 32-bit bin\)
        var extraTargets = await Task.Run(() => SourceEngineSetup.DetectTargets(root)
            .Where(t => t.Is64Bit == is32 && !InstallFolderMover.SamePath(t.Dir, installDir))
            .ToList()).ConfigureAwait(false);

        if (!File.Exists(StagedD3d9(!is32)) || extraTargets.Any(t => !File.Exists(StagedD3d9(t.Is64Bit))))
            throw new InvalidOperationException("DXVK couldn't be downloaded. Check your connection or antivirus, then try again.");

        var extraAddons = new Dictionary<bool, string>();
        foreach (var t in extraTargets)
            if (!extraAddons.ContainsKey(t.Is64Bit))
                extraAddons[t.Is64Bit] = await DownloadSourceAddonAsync(t.Is64Bit).ConfigureAwait(false);

        await Task.Run(() =>
        {
            SourceEngineSetup.DeployOwnedFile(installDir, SourceEngineSetup.ProxyDllName, StagedD3d9(!is32));
            SourceEngineSetup.ApplyReShadeIni(installDir);
            SourceEngineSetup.EnsureSecondRuntimeIni(installDir); // before the proxy makes its own copy

            foreach (var t in extraTargets)
            {
                var rsSource = AuxInstallService.GetStagedPathForChannel(channel, use32Bit: !t.Is64Bit);
                if (!File.Exists(rsSource)) rsSource = AuxInstallService.GetStagedPathForChannel(AuxInstallService.ChannelStable, use32Bit: !t.Is64Bit);
                SourceEngineSetup.DeployOwnedFile(t.Dir, SourceEngineSetup.ReShadeDll, rsSource);
                var mainIni = Path.Combine(installDir, "reshade.ini");
                if (!File.Exists(Path.Combine(t.Dir, "reshade.ini")) && File.Exists(mainIni))
                    SourceEngineSetup.DeployOwnedFile(t.Dir, "reshade.ini", mainIni);
                SourceEngineSetup.ApplyReShadeIni(t.Dir);
                SourceEngineSetup.EnsureSecondRuntimeIni(t.Dir);
                SourceEngineSetup.ShareShaderPaths(t.Dir, installDir); // shaders live in the main install only
                SourceEngineSetup.DeployOwnedFile(t.Dir, SourceEngineSetup.ProxyDllName, StagedD3d9(t.Is64Bit));
                SourceEngineSetup.DeployOwnedFile(t.Dir, t.Is64Bit ? SourceEngineSetup.AddonName64 : SourceEngineSetup.AddonName32, extraAddons[t.Is64Bit]);
            }

            SourceEngineSetup.EnsureDxvkConf(root);
        }).ConfigureAwait(false);

        _crashReporter.Log($"[MainViewModel.ApplySourceSetupAsync] '{card.GameName}': DXVK proxy in '{installDir}'"
            + (extraTargets.Count > 0 ? $" and {string.Join(", ", extraTargets.Select(t => t.Dir))}" : "")
            + $", dxvk.conf in '{root}'");
    }

    /// <summary>Downloads the other build of the Source addon (Garry's Mod's second folder) into RHI's download cache.</summary>
    private async Task<string> DownloadSourceAddonAsync(bool x64)
    {
        var url = x64 ? SourceEngineSetup.AddonUrl64 : SourceEngineSetup.AddonUrl32;
        Directory.CreateDirectory(DownloadPaths.RenoDX);
        var dest = Path.Combine(DownloadPaths.RenoDX, Path.GetFileName(url));
        var tmp = dest + ".tmp";
        using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmp);
            await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
        }
        File.Move(tmp, dest, overwrite: true);
        return dest;
    }

    /// <summary>Adds -insecure to the game's RHI launch options (UI thread).</summary>
    private void AddSourceLaunchArg(GameCardViewModel card)
    {
        var current = _gameNameService.LaunchArgsOverrides.TryGetValue(card.GameName, out var v) ? v : "";
        var next = SourceEngineSetup.AddLaunchArg(current);
        if (next == current) return;
        _gameNameService.LaunchArgsOverrides[card.GameName] = next;
        SaveNameMappings();
        RequestOverridesPanelRebuild?.Invoke(card);
        _crashReporter.Log($"[MainViewModel.AddSourceLaunchArg] '{card.GameName}': launch options '{next}'");
    }

    /// <summary>Takes -insecure back out of the game's RHI launch options (UI thread).</summary>
    private void RemoveSourceLaunchArg(GameCardViewModel card)
    {
        if (!_gameNameService.LaunchArgsOverrides.TryGetValue(card.GameName, out var current)
            || !SourceEngineSetup.HasLaunchArg(current)) return;
        var next = SourceEngineSetup.RemoveLaunchArg(current);
        if (next.Length == 0) _gameNameService.LaunchArgsOverrides.Remove(card.GameName);
        else _gameNameService.LaunchArgsOverrides[card.GameName] = next;
        SaveNameMappings();
        RequestOverridesPanelRebuild?.Invoke(card);
    }

    /// <summary>
    /// Uninstall: removes the DXVK proxy setup wherever RHI placed it (sentinel files), the reshade.ini lines,
    /// the dxvk.conf lines, Garry's Mod's second ReShade / addon, and -insecure. ReShade itself stays.
    /// </summary>
    private void RemoveSourceSetup(GameCardViewModel card)
    {
        var installDir = card.InstallPath;
        if (string.IsNullOrEmpty(installDir)) return;
        var root = card.DetectedGame?.InstallPath is { Length: > 0 } r ? r : installDir;
        var dirs = new List<string> { installDir };
        dirs.AddRange(SourceEngineSetup.DetectTargets(root).Select(t => t.Dir));

        bool removedAny = false;
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!SourceEngineSetup.IsOwned(dir, SourceEngineSetup.ProxyDllName)) continue;
                SourceEngineSetup.RemoveOwnedFile(dir, SourceEngineSetup.ProxyDllName);
                SourceEngineSetup.RemoveReShadeIni(dir);
                SourceEngineSetup.RemoveOwnedFile(dir, SourceEngineSetup.SecondRuntimeIni); // only when RHI made it
                if (!InstallFolderMover.SamePath(dir, installDir))
                {
                    SourceEngineSetup.RemoveOwnedFile(dir, SourceEngineSetup.AddonName64);
                    SourceEngineSetup.RemoveOwnedFile(dir, SourceEngineSetup.AddonName32);
                    SourceEngineSetup.RemoveOwnedFile(dir, "reshade.ini");
                    // ReShade2.ini etc.: ReShade made them for RHI's ReShade here, which goes too
                    var rsSentinel = new FileInfo(Path.Combine(dir, SourceEngineSetup.ReShadeDll + SourceEngineSetup.SentinelSuffix));
                    if (rsSentinel.Exists && rsSentinel.Length == 0) // the folder had no d3d9.dll before RHI's
                        foreach (var extra in SourceEngineSetup.ExtraRuntimeConfigs(dir).ToList()) File.Delete(extra);
                    SourceEngineSetup.RemoveOwnedFile(dir, SourceEngineSetup.ReShadeDll);
                }
                removedAny = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _crashReporter.Log($"[MainViewModel.RemoveSourceSetup] '{card.GameName}' '{dir}' — {ex.Message}");
            }
        }
        if (removedAny)
        {
            try { SourceEngineSetup.RemoveDxvkConf(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[MainViewModel.RemoveSourceSetup] '{card.GameName}' dxvk.conf — {ex.Message}"); }
        }
        RemoveSourceLaunchArg(card);
        _crashReporter.Log($"[MainViewModel.RemoveSourceSetup] '{card.GameName}': DXVK proxy setup {(removedAny ? "removed" : "not found")}");
    }
}
