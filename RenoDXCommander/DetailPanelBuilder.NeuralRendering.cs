// DetailPanelBuilder.NeuralRendering.cs — Self-contained Neural Rendering section.
// Shown between Game Overrides and NVIDIA Profile Overrides.
// Handles DLSS5 Tool, DLSS5 Tool + DX11 Bridge, ShortFuse DLSS Tool, and DLSS5 Feeder.
// All files are deployed automatically — no addon picker required.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    // ── Section IDs for the Bridge and Feeder addons (from manifest addonPacks) ──
    private const string BridgePackageName  = "DLSS5 DX11 Bridge";
    private const string FeederPackageName  = "DLSS5 Feeder";
    private const string BridgeDeployFile   = "dlss5-bridge.addon64";
    private const string FeederDeployFile64 = "dlss5-feed.addon64";
    private const string FeederDeployFile32 = "dlss5-feed.addon32";

    /// <summary>
    /// Strips a display-only parenthetical suffix from a version string so it can be used
    /// as a staging directory name. e.g. "310.8.0 (50xx)" → "310.8.0".
    /// Also handles versions like "310.8.2 (20/30/40/50)" where the slash causes
    /// path mangling on disk — always strips at the first '(' or space before '('.
    /// </summary>
    private static string StripVersionSuffix(string version)
    {
        var idx = version.IndexOf('(');
        return idx > 0 ? version[..idx].TrimEnd() : version;
    }

    /// <summary>
    /// Resolves the cached nvngx_dlssnr.dll path for a given version string.
    /// Handles the case where the staging directory name contains forward slashes
    /// (e.g. "310.8.2 (20/30/40/50)") that Windows mangles into nested folders on disk.
    /// Searches for any subdirectory that starts with the stripped version number.
    /// </summary>
    private static string? ResolveCachedNrDllPath(string versionString)
    {
        var nrBase = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "DLSS-NR");
        if (!Directory.Exists(nrBase)) return null;

        var stripped = StripVersionSuffix(versionString);

        // 1. Exact match on stripped version
        var exact = System.IO.Path.Combine(nrBase, stripped, "nvngx_dlssnr.dll");
        if (File.Exists(exact)) return exact;

        // 2. Full recursive search under any subdir starting with the stripped version.
        // Handles forward-slash-mangled paths like "310.8.2 (20/30/40/50)" which become
        // nested dirs "310.8.2 (20\30\40\50\" on disk.
        foreach (var dir in Directory.GetDirectories(nrBase))
        {
            var dirName = System.IO.Path.GetFileName(dir);
            if (dirName.StartsWith(stripped, StringComparison.OrdinalIgnoreCase))
            {
                var found = Directory.GetFiles(dir, "nvngx_dlssnr.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (found != null) return found;
            }
        }
        return null;
    }

    // ── Method constants ──────────────────────────────────────────────────────
    private const string NrMethodDlss5Tool        = "DLSS5Tool";
    private const string NrMethodDlss5ToolBridge  = "DLSS5ToolBridge";
    private const string NrMethodShortFuse         = "ShortFuse";
    private const string NrMethodFeeder            = "Feeder";

    public void BuildNeuralRenderingSection(GameCardViewModel card)
    {
        _window.NeuralRenderingPanel.Children.Clear();

        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var installPath = card.InstallPath;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();
        var addonSvc    = _window.ViewModel.AddonPackServiceInstance;
        var dlssSvc     = _dlssStreamlineService;

        // ── Detect current install state (off the UI thread — all File.Exists calls) ──
        var scanToken = _panelScanCts.Token;  // capture BEFORE Task.Run — CTS may be replaced by the time lambda executes
        _ = Task.Run(async () =>
        {
            CrashReporter.Log($"[BuildNeuralRenderingSection] Waiting for semaphore: '{card.GameName}'");
            try { await _panelScanSemaphore.WaitAsync(scanToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { CrashReporter.Log($"[BuildNeuralRenderingSection] Semaphore cancelled: '{card.GameName}'"); return; }
            CrashReporter.Log($"[BuildNeuralRenderingSection] Semaphore acquired, scanning files: '{card.GameName}'");

            bool dlss5Installed, sfInstalled, nrDllPresent, nrDllOwnedByRhi, bridgePresent, feederPresent;
            string? nrDllVersion;
            try
            {
                dlss5Installed  = rdx5Svc.IsInstalledIn(installPath);
                sfInstalled     = rdx5Svc.IsSfInstalledIn(installPath);
                nrDllPresent    = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll"));
                nrDllOwnedByRhi = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll.original"));
                nrDllVersion    = null;
                if (nrDllPresent)
                    nrDllVersion = DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssnr.dll")));
                bridgePresent = File.Exists(Path.Combine(installPath, BridgeDeployFile));
                feederPresent = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));
            }
            finally
            {
                // Release the semaphore immediately after file scans — before TryEnqueue.
                // Holding it across the TryEnqueue causes a dispatcher deadlock when the UI thread
                // is busy running BuildNvidiaProfileBody (which calls BuildDriverProfileSection,
                // which waits on this semaphore via Task.Run, starving the dispatcher).
                _panelScanSemaphore.Release();
                CrashReporter.Log($"[BuildNeuralRenderingSection] Semaphore released: '{card.GameName}'");
            }

            _window.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                // Guard: stale scan (game navigated away while scan was in flight)
                if (scanToken.IsCancellationRequested) return;
                // Guard: Settings panel open or dialog showing — don't touch the live tree while it's hidden
                if (_window.SettingsPanel.Visibility == Microsoft.UI.Xaml.Visibility.Visible
                    || DialogService.IsDialogOpen) return;
                // Guard: user navigated to a different game before this callback fired.
                // Without this, rapid A→B→A navigation queues two callbacks for A; both pass
                // a name check but the first one clears + rebuilds the NR panel just as the
                // second is about to do the same, producing cascading WinUI layout hangs.
                if (_window.ViewModel.SelectedGame != card) return;

                _window.ViewModel.SetLastUiAction($"BuildNeuralRenderingSectionWithData({card.GameName})");
                var __sw = System.Diagnostics.Stopwatch.StartNew();
                var nrContainerWidth = _window.NeuralRenderingContainer.ActualWidth;
                BuildNeuralRenderingSectionWithData(card, dlss5Installed, sfInstalled,
                    nrDllPresent, nrDllOwnedByRhi, nrDllVersion, bridgePresent, feederPresent, nrContainerWidth);
                __sw.Stop();
                if (__sw.ElapsedMilliseconds > 30)
                    CrashReporter.Log($"[BuildNeuralRenderingSectionWithData] SLOW: '{card.GameName}' took {__sw.ElapsedMilliseconds}ms on UI thread");
                else
                    CrashReporter.Log($"[BuildNeuralRenderingSectionWithData] done: '{card.GameName}' in {__sw.ElapsedMilliseconds}ms");
            });
        });
    }

    // ── Utility helpers ───────────────────────────────────────────────────────

    private static void DeployNrDllSentinel(string installPath, string cachedNrPath)
    {
        var dest     = Path.Combine(installPath, "nvngx_dlssnr.dll");
        var sentinel = dest + ".original";
        if (File.Exists(sentinel)) return;          // already placed by RHI
        if (File.Exists(dest))     return;          // game-original — don't touch
        File.Copy(cachedNrPath, dest, overwrite: false);
        File.WriteAllBytes(sentinel, Array.Empty<byte>());
        CrashReporter.Log($"[NeuralRendering] Deployed nvngx_dlssnr.dll to '{installPath}' (sentinel written)");
    }

    private static string? FindStagedAddon(string packageName, string extension)
    {
        var stagingDir = AddonPackService.GetStagingDir();
        // Try sanitized package name first (AddonPackService.SanitizeFileName pattern)
        var safeName = new string(packageName.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        var candidate = Path.Combine(stagingDir, safeName + extension);
        if (File.Exists(candidate)) return candidate;
        // Also check versions.json OriginalName entries via directory scan
        foreach (var f in Directory.EnumerateFiles(stagingDir, $"*{extension}"))
        {
            var fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            if (packageName.ToLowerInvariant().Contains(fn) || fn.Contains("bridge") || fn.Contains("feed"))
                return f;
        }
        return null;
    }

    private void RemoveFeederShaders(string installPath, string gameName, string store, GameCardViewModel card)
    {
        try
        {
            var gameKey     = Models.GameKey.FromCard(gameName, store).ToKey();
            var shadersDir  = Path.Combine(installPath, ShaderPackService.GameReShadeShaders, "Shaders");
            var texturesDir = Path.Combine(installPath, ShaderPackService.GameReShadeShaders, "Textures");

            // Delete specific pack files
            foreach (var packId in new[] { "DLSS5Feeder", "LumeniteFX" })
            {
                foreach (var f in _shaderPackService.GetPackShaderFiles(new[] { packId }))
                    try { if (File.Exists(Path.Combine(shadersDir, f))) File.Delete(Path.Combine(shadersDir, f)); } catch (Exception) { /* per-file best-effort cleanup; locked or missing shader files are skipped */ }
            }
            // Lumenite textures
            if (Directory.Exists(texturesDir))
                foreach (var f in Directory.GetFiles(texturesDir, "lumenite_*"))
                    try { File.Delete(f); } catch (Exception) { /* per-file best-effort cleanup; locked or missing textures are skipped */ }
            // Subfolders + loose files we deployed
            try { if (Directory.Exists(Path.Combine(shadersDir, "DLSS5Feeder")))  Directory.Delete(Path.Combine(shadersDir, "DLSS5Feeder"),  true); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RemoveFeederShaders] Failed to delete DLSS5Feeder folder — {ex.Message}"); }
            try { if (Directory.Exists(Path.Combine(shadersDir, "LumeniteFX")))   Directory.Delete(Path.Combine(shadersDir, "LumeniteFX"),   true); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RemoveFeederShaders] Failed to delete LumeniteFX folder — {ex.Message}"); }
            try { if (File.Exists(Path.Combine(shadersDir, "DLSS5_Feed.fx")))     File.Delete(Path.Combine(shadersDir, "DLSS5_Feed.fx")); }     catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RemoveFeederShaders] Failed to delete DLSS5_Feed.fx — {ex.Message}"); }
            try { if (File.Exists(Path.Combine(shadersDir, "lumenite_Kernel.fx"))) File.Delete(Path.Combine(shadersDir, "lumenite_Kernel.fx")); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RemoveFeederShaders] Failed to delete lumenite_Kernel.fx — {ex.Message}"); }
            try { if (Directory.Exists(Path.Combine(shadersDir, "include")))      Directory.Delete(Path.Combine(shadersDir, "include"),       true); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RemoveFeederShaders] Failed to delete include folder — {ex.Message}"); }

            // Update persisted shader selection — remove our packs, keep others
            var current = _gameNameService.PerGameShaderSelection.TryGetValue(gameKey, out var sel)
                ? sel.ToList() : new List<string>();
            var remaining = current
                .Where(p => !p.Equals("DLSS5Feeder", StringComparison.OrdinalIgnoreCase)
                         && !p.Equals("LumeniteFX",  StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (remaining.Count > 0)
                _gameNameService.PerGameShaderSelection[gameKey] = remaining;
            else
            {
                _gameNameService.PerGameShaderSelection.Remove(gameKey);
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    _window.ViewModel.SetPerGameShaderMode(gameName, "Global", store);
                    card.ShaderModeOverride = null;
                });
            }
            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                _window.ViewModel.SaveSettingsPublic();
                _window.ViewModel.DeployShadersForCard(gameName);
            });
        }
        catch (Exception ex) { CrashReporter.Log($"[NeuralRendering.RemoveFeederShaders] Failed for '{gameName}' — {ex.Message}"); }
    }

    private void RemoveNrConflictingAddonsFromPerGameSelection(string gameName, string store, string[] conflicting)
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (!_gameNameService.PerGameAddonSelection.TryGetValue(key, out var perGame) || perGame == null) return;
        bool changed = false;
        foreach (var c in conflicting)
            if (perGame.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0)
                changed = true;
        if (changed)
        {
            _window.ViewModel.SaveSettingsPublic();
            CrashReporter.Log($"[NeuralRendering] Removed conflicting addons (DLSS5 Tool / ShortFuse) from per-game selection for '{gameName}'");
        }
    }

    private static void RemoveAddonFile(string installPath, string fileName, string logCtx)
    {
        var path = Path.Combine(installPath, fileName);
        try
        {
            if (File.Exists(path)) { File.Delete(path); CrashReporter.Log($"[{logCtx}] Deleted '{path}'"); }
        }
        catch (Exception ex) { CrashReporter.Log($"[{logCtx}] Delete failed '{path}' — {ex.Message}"); }
    }

    private Task<T> DispatchAsync<T>(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<T> func)
    {
        return UiDispatch.InvokeAsync(action => dispatcher.TryEnqueue(() => action()), func, _window.LifetimeToken);
    }
}
