// MainViewModel.Update.cs -- Update-all commands, update checking, UL caching, and version management.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private System.Threading.Timer? _updateCheckTimer;
    private System.Threading.Timer? _heartbeatTimer;
    private volatile string _lastUiAction = "none";
    private readonly object _backgroundTimerLock = new();
    private volatile bool _backgroundStopped;
    private readonly CancellationTokenSource _backgroundLifetime = new();

    /// <summary>Native thread ID of the UI thread — captured once at startup for CPU sampling.</summary>
    internal uint UiThreadNativeId { get; set; }

    /// <summary>HWND of the main window — captured once at startup for pump-responsiveness probe.</summary>
    internal IntPtr MainWindowHwnd { get; set; }

    /// <summary>WindowStateManager — used to read LastResumeUtcTicks for sleep-resume grace period.</summary>
    internal WindowStateManager? WindowStateManagerRef { get; set; }

    /// <summary>Prevents more than one stack capture per freeze event.</summary>
    private int _freezeStackCaptured; // 0 = not captured, 1 = captured; Interlocked

    /// <summary>Timestamp of the last DispatcherQueueTimer tick — for true freeze onset detection.</summary>
    private long _dispatcherTimerLastTickUtc; // DateTime.UtcNow.Ticks — read/write via Interlocked

    /// <summary>Held in a field to prevent GC collection — DispatcherQueueTimer is a COM object.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _freezeDiagTimer;

    private System.Threading.Timer? _resourceLogTimer;

    /// <summary>
    /// Consecutive High-probe failures needed before freeze diagnostics fire.
    /// Reset to 0 on any successful probe. Interlocked.
    /// </summary>
    private int _consecutiveProbeFailures;

    /// <summary>Set to true by dev test buttons (IDLE, PEGGED, Native Block, Known Sig) so the
    /// dump path writes test_*.dmp to a separate folder, never affecting real freeze dumps.</summary>
    internal bool IsTestFreezeActive;

    /// <summary>Logs process resource counters — private bytes, GC heap, handles, GDI/USER objects, system memory.</summary>
    private static void LogResourceCounters(string context)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            proc.Refresh();
            long privateBytes  = proc.PrivateMemorySize64;
            long workingSet    = proc.WorkingSet64;
            long gcHeap        = GC.GetTotalMemory(false);
            int  handleCount   = proc.HandleCount;
            int  threadCount   = proc.Threads.Count;

            // GDI and USER objects — diagnostic for WinUI dispatcher failure from resource exhaustion
            var hProc = proc.Handle;
            uint gdiObj  = NativeInterop.GetGuiResources(hProc, NativeInterop.GR_GDIOBJECTS);
            uint userObj = NativeInterop.GetGuiResources(hProc, NativeInterop.GR_USEROBJECTS);

            // System memory
            var mem = new NativeInterop.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeInterop.MEMORYSTATUSEX>() };
            NativeInterop.GlobalMemoryStatusEx(ref mem);
            long availPhysMB   = (long)(mem.ullAvailPhys   / 1024 / 1024);
            long availPageMB   = (long)(mem.ullAvailPageFile / 1024 / 1024);
            uint memLoadPct    = mem.dwMemoryLoad;

            // OS build + GPU driver (at-freeze only, too slow for periodic)
            string osBuild = "", gpuDriver = "";
            if (context == "at-freeze")
            {
                osBuild = App.GetWindowsBuildNumber();
                try
                {
                    var gd = proc.Modules.Cast<System.Diagnostics.ProcessModule>()
                        .FirstOrDefault(m => m.ModuleName?.StartsWith("nvwgf2", StringComparison.OrdinalIgnoreCase) == true
                                          || m.ModuleName?.StartsWith("atig", StringComparison.OrdinalIgnoreCase) == true
                                          || m.ModuleName?.StartsWith("atio", StringComparison.OrdinalIgnoreCase) == true);
                    if (gd?.FileName != null)
                    {
                        var fi = System.Diagnostics.FileVersionInfo.GetVersionInfo(gd.FileName);
                        gpuDriver = $" | GPU driver: {gd.ModuleName} v{fi.FileVersion}";
                    }
                }
                catch (Exception) { /* at-freeze diagnostic only; driver version is optional */ }
            }

            CrashReporter.LogSync(
                $"[Resources] {context} | " +
                $"Private={privateBytes/1024/1024}MB WS={workingSet/1024/1024}MB GC={gcHeap/1024/1024}MB | " +
                $"Handles={handleCount} Threads={threadCount} GDI={gdiObj} USER={userObj} | " +
                $"SysAvailPhys={availPhysMB}MB AvailPage={availPageMB}MB MemLoad={memLoadPct}%" +
                (osBuild.Length > 0 ? $" | OS={osBuild}{gpuDriver}" : ""));
        }
        catch (Exception ex)
        {
            CrashReporter.LogSync($"[Resources] {context} — failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal void StopBackgroundWork()
    {
        lock (_backgroundTimerLock)
        {
            _backgroundStopped = true;
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _updateCheckTimer?.Dispose();
            _updateCheckTimer = null;
            _resourceLogTimer?.Dispose();
            _resourceLogTimer = null;
        }
        _backgroundLifetime.Cancel();
        PeriodicAppUpdateCheck = null;
        _autoUpdateService.Stop();
    }

    /// <summary>Tracks the last action dispatched to the UI thread for freeze diagnostics.</summary>
    internal void SetLastUiAction(string action)
    {
        _lastUiAction = action;
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] [UIAction] {action}";
        _crashReporter.Log($"[UIAction] {action}");
        CrashReporter.RecordUiAction(entry); // dedicated ring buffer, never contaminated by diagnostic output
        // Also record as a callback-start in the enqueue ring so it appears in the dispatcher timeline
        var callerThread = System.Threading.Thread.CurrentThread;
        CrashReporter.RecordEnqueueAction($"[{DateTime.Now:HH:mm:ss.fff}] [Callback:Start] N UI({callerThread.ManagedThreadId}) {action}");
    }

    /// <summary>
    /// Starts a repeating 4-hour timer that re-runs all update checks.
    /// Called after the initial background scan completes.
    /// </summary>
    internal void StartPeriodicUpdateCheckTimer()
    {
        lock (_backgroundTimerLock)
        {
            if (_backgroundStopped || _updateCheckTimer != null) return;
            _updateCheckTimer = CreatePeriodicUpdateCheckTimer();
            _crashReporter.Log("[MainViewModel] Periodic update check timer started (4h interval)");
        }
    }

    private System.Threading.Timer CreatePeriodicUpdateCheckTimer()
    {
        var interval = TimeSpan.FromHours(4);
        return new System.Threading.Timer(async _ =>
        {
            if (_backgroundStopped) return;
            try
            {
                _crashReporter.Log("[MainViewModel] Periodic update check triggered (4h timer)");

                // Re-fetch manifest (picks up new game entries, overrides, etc.)
                try
                {
                    var freshManifest = await _manifestService.FetchAsync();
                    if (freshManifest != null)
                    {
                        _manifest = freshManifest;
                        AuxInstallService.GlobalManifest = _manifest;
                        ApplyManifest(_manifest);
                        _pcgwService.CheckManifestCacheVersion(_manifest);
                        _crashReporter.Log("[MainViewModel] Periodic manifest refresh complete");
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic manifest fetch failed — {ex.Message}"); }

                // Re-fetch wiki to detect new mods
                try
                {
                    var wikiResult = await _wikiService.FetchAllAsync();
                    if (wikiResult.Mods != null)
                    {
                        _allMods = wikiResult.Mods;
                        _genericNotes = wikiResult.GenericNotes ?? new();
                        _crashReporter.Log($"[MainViewModel] Periodic wiki refresh complete — {_allMods.Count} mods");

                        // Detect new wiki mods
                        var currentModNames = _allMods
                            .Where(m => m.SnapshotUrl != null || m.NexusUrl != null)
                            .Select(m => m.Name)
                            .ToList();
                        _crashReporter.Log($"[MainViewModel] Periodic wiki mods check: {currentModNames.Count} downloadable mods");
                        var newMods = _seenWikiModsService.GetNewMods(currentModNames);
                        _crashReporter.Log($"[MainViewModel] Periodic new wiki mods: {newMods.Count} (seen: {_seenWikiModsService.GetSeenMods().Count})");
                        if (newMods.Count > 0)
                        {
                            _crashReporter.Log($"[MainViewModel] Periodic new mods: {string.Join(", ", newMods.Take(10))}{(newMods.Count > 10 ? "..." : "")}");
                            DispatcherQueue?.TryEnqueue(() => NewWikiMods = newMods);
                        }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic wiki fetch failed — {ex.Message}"); }

                // Re-fetch Ultra+ mods and detect new ones
                try
                {
                    await _ultraPlusService.InitAsync();
                    var currentUltraPlusMods = _ultraPlusService.GetAllGameNames().ToList();
                    _crashReporter.Log($"[MainViewModel] Periodic Ultra+ mods check: {currentUltraPlusMods.Count} mods");
                    var newUltraPlusMods = _seenUltraPlusModsService.GetNewMods(currentUltraPlusMods);
                    _crashReporter.Log($"[MainViewModel] Periodic new Ultra+ mods: {newUltraPlusMods.Count} (seen: {_seenUltraPlusModsService.GetSeenMods().Count})");
                    if (newUltraPlusMods.Count > 0)
                    {
                        _crashReporter.Log($"[MainViewModel] Periodic new Ultra+ mods: {string.Join(", ", newUltraPlusMods.Take(10))}{(newUltraPlusMods.Count > 10 ? "..." : "")}");
                        DispatcherQueue?.TryEnqueue(() => NewUltraPlusMods = newUltraPlusMods);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic Ultra+ fetch failed — {ex.Message}"); }

                // Re-fetch Luma mods and detect new ones
                try
                {
                    var lumaTask = _lumaService.FetchCompletedModsAsync();
                    _lumaMods = await lumaTask;
                    var currentLumaMods = _lumaMods.Select(m => m.Name).ToList();
                    _crashReporter.Log($"[MainViewModel] Periodic Luma mods check: {currentLumaMods.Count} mods");
                    var newLumaMods = _seenLumaModsService.GetNewMods(currentLumaMods);
                    _crashReporter.Log($"[MainViewModel] Periodic new Luma mods: {newLumaMods.Count} (seen: {_seenLumaModsService.GetSeenMods().Count})");
                    if (newLumaMods.Count > 0)
                    {
                        _crashReporter.Log($"[MainViewModel] Periodic new Luma mods: {string.Join(", ", newLumaMods.Take(10))}{(newLumaMods.Count > 10 ? "..." : "")}");
                        DispatcherQueue?.TryEnqueue(() => NewLumaMods = newLumaMods);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic Luma fetch failed — {ex.Message}"); }
                try { await _dlssStreamlineService.FetchManifestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic DLSS manifest fetch failed — {ex.Message}"); }

                // Check and re-stage Cost Scaler + RTX40MFG, auto-deploy to installed games
                try
                {
                    await _nrCostScalerService.CheckForUpdateAsync(forceRefresh: true);
                    if (_nrCostScalerService.HasUpdate)
                    {
                        await _nrCostScalerService.EnsureStagingAsync();
                        if (_nrCostScalerService.IsStagingReady)
                            foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)
                                && DlssNrCostScalerService.IsInstalled(c.InstallPath)))
                                _nrCostScalerService.Install(c.InstallPath);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic NR Cost Scaler check failed — {ex.Message}"); }

                try
                {
                    await _rtx40MfgService.CheckForUpdateAsync(forceRefresh: true);
                    if (_rtx40MfgService.HasUpdate)
                        await _rtx40MfgService.EnsureStagingAsync();
                    _rtx40MfgService.UpdateInstalledGames(GetRtx40MfgInstalls());
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic RTX40MFG check failed — {ex.Message}"); }

                try
                {
                    await _dlssg2030Service.CheckForUpdateAsync();
                    if (_dlssg2030Service.HasUpdate)
                    {
                        await _dlssg2030Service.EnsureStagingAsync();
                        if (_dlssg2030Service.IsStagingReady)
                            foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
                            {
                                var installedAs = GetDlssg2030InstalledAs(c.GameName, c.Source ?? "");
                                if (!string.IsNullOrEmpty(installedAs) && File.Exists(Path.Combine(c.InstallPath!, installedAs)))
                                    _dlssg2030Service.Update(c.InstallPath!, installedAs, GetDlssg2030GpuGen(c.GameName, c.Source ?? ""));
                            }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic Dlssg2030 check failed — {ex.Message}"); }

                try
                {
                    var rtxEncoreSvc = App.Services.GetRequiredService<RtxEncoreService>();
                    await rtxEncoreSvc.CheckForUpdateAsync(forceRefresh: true);
                    if (rtxEncoreSvc.HasUpdate)
                    {
                        await rtxEncoreSvc.EnsureStagingAsync();
                        if (rtxEncoreSvc.IsStagingReady)
                            foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
                            {
                                var installedAs = GetRtxEncoreInstalledAs(c.GameName, c.Source ?? "");
                                if (!string.IsNullOrEmpty(installedAs) && rtxEncoreSvc.IsInstalledIn(c.InstallPath!, installedAs))
                                    await rtxEncoreSvc.InstallAsync(c.InstallPath!, installedAs, installedAs);
                            }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic RTX Encore check failed — {ex.Message}"); }

                _forceUpdateCheck = true; // bypass cooldown since we ARE the cooldown
                var records = _installer.LoadAll();
                var auxRecords = _auxInstaller.LoadAll();
                await CheckForUpdatesAsync(_allCards, records, auxRecords, userInitiated: false);

                // Check for custom ReShade DLL changes and redeploy
                try
                {
                    var redeployed = _customReShadeHashService.CheckAndRedeploy(_allCards);
                    if (redeployed > 0)
                        _crashReporter.Log($"[MainViewModel] Periodic custom ReShade redeploy — {redeployed} game(s) updated");
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Custom ReShade hash check failed — {ex.Message}"); }

                // Silent component auto-update (runs after update check flags pending updates)
                try { _autoUpdateService.TriggerAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic component auto-update trigger failed — {ex.Message}"); }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel] Periodic update check failed — {ex.Message}");
            }

            // Also check for RHI app updates on the UI thread
            DispatcherQueue?.TryEnqueue(() =>
            {
                CrashReporter.Log("[RefreshTrigger] 4h periodic timer — PeriodicAppUpdateCheck dispatched to UI thread");
                PeriodicAppUpdateCheck?.Invoke();
            });
        }, null, interval, interval);
    }

    /// <summary>Callback set by MainWindow to trigger app update check from the periodic timer.</summary>
    public Action? PeriodicAppUpdateCheck { get; set; }

    /// <summary>Triggers a silent auto-install pass — safe to call from any thread.</summary>
    public void TriggerAutoUpdate() => _autoUpdateService.TriggerAsync();

    internal void NotifyUpdateButtonChanged()
    {
        HasUpdatesAvailable = AnyUpdateAvailable;
        OnPropertyChanged(nameof(AnyUpdateAvailable));
        OnPropertyChanged(nameof(UpdateAllBtnBackground));
        OnPropertyChanged(nameof(UpdateAllBtnForeground));
        OnPropertyChanged(nameof(UpdateAllBtnBorder));
    }

    /// <summary>
    /// Reads the bundled RHI_PatchNotes.md and extracts the last N version sections.
    /// Each section starts with "## vX.Y.Z".
    /// </summary>
    public static string GetRecentPatchNotes(int count = 3)
    {
        try
        {
            var path = AppPaths.InExeDir("RHI_PatchNotes.md");
            if (!File.Exists(path)) return "Patch notes file not found.";

            var lines = File.ReadAllLines(path);
            var sections = new List<string>();
            var currentSection = new List<string>();
            bool inSection = false;
            var preamble = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("## v"))
                {
                    if (inSection && currentSection.Count > 0)
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                    }
                    inSection = true;
                    currentSection.Add(line);
                }
                else if (inSection)
                {
                    // Stop at the "---" separator between versions (but don't include it)
                    if (line.Trim() == "---")
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                        inSection = false;
                    }
                    else
                    {
                        currentSection.Add(line);
                    }
                }
                else
                {
                    preamble.Add(line);
                }
            }

            // Capture final section if still in progress
            if (inSection && currentSection.Count > 0 && sections.Count < count)
                sections.Add(string.Join("\n", currentSection));

            var body = sections.Count > 0
                ? string.Join("\n\n---\n\n", sections)
                : "No patch notes available.";

            // Prepend preamble (e.g. the GitHub API warning banner) if present
            var preambleText = string.Join("\n", preamble).Trim();
            return string.IsNullOrEmpty(preambleText)
                ? body
                : preambleText + "\n\n---\n\n" + body;
        }
        catch (Exception ex)
        {
            return $"Error reading patch notes: {ex.Message}";
        }
    }
}

