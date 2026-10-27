using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // Heartbeat log throttle (only touched by the heartbeat timer callback).
    private string? _heartbeatLoggedAction;
    private long _heartbeatLoggedAtTicks;
    // 1 while an auto-restart is pending, so later failing ticks don't schedule more restarts.
    private int _restartPending;

    /// <summary>
    /// Starts a 1-second heartbeat timer. On each tick it posts a High-priority probe to the UI thread.
    /// Two consecutive failures trigger the freeze diagnostics and fast-restart path (~5s total).
    /// Non-matching stalls (CPU pegged, pump dead, managed frames present) keep the slow 45s path.
    /// Sleep-resume grace: ignores failures for 30s after WM_POWERBROADCAST.
    /// </summary>
    internal void StartHeartbeatTimer()
    {
        lock (_backgroundTimerLock)
        {
            if (_backgroundStopped || _heartbeatTimer != null) return;

            // ── 5-minute resource counter timer ─────────────────────────────────
            _resourceLogTimer = new System.Threading.Timer(_ =>
            {
                if (_backgroundStopped) return;
                LogResourceCounters("periodic-5min");
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

            // ── DispatcherQueueTimer last-tick tracker ───────────────────────────
            _dispatcherTimerLastTickUtc = DateTime.UtcNow.Ticks;
            DispatcherQueue?.TryEnqueue(() =>
            {
                _freezeDiagTimer = DispatcherQueue!.CreateTimer();
                _freezeDiagTimer.Interval = TimeSpan.FromSeconds(1);
                _freezeDiagTimer.IsRepeating = true;
                _freezeDiagTimer.Tick += (_, _) =>
                {
                    System.Threading.Interlocked.Exchange(ref _dispatcherTimerLastTickUtc, DateTime.UtcNow.Ticks);
                };
                _freezeDiagTimer.Start();
                CrashReporter.Log("[Heartbeat] DispatcherQueueTimer started successfully");
            });

            // ── Main heartbeat — 1s tick, High-priority probe, 1s timeout ────────
            _heartbeatTimer = new System.Threading.Timer(async _ =>
            {
                if (_backgroundStopped) return;

                // ── Sleep-resume grace: skip freeze detection for 30s after wake ──
                long lastResumeTicks = WindowStateManagerRef != null
                    ? System.Threading.Interlocked.Read(ref WindowStateManagerRef.LastResumeUtcTicks)
                    : 0L;
                if (lastResumeTicks > 0)
                {
                    var resumeAge = DateTime.UtcNow - new DateTime(lastResumeTicks, DateTimeKind.Utc);
                    if (resumeAge.TotalSeconds < 30)
                    {
                        System.Threading.Interlocked.Exchange(ref _consecutiveProbeFailures, 0);
                        System.Threading.Interlocked.Exchange(ref _freezeStackCaptured, 0);
                        return;
                    }
                }

                // ── High-priority probe with 1s timeout ──────────────────────────
                var probe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () => probe.TrySetResult(true)) != true) return;
                bool probeTimedOut;
                try
                {
                    await probe.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    probeTimedOut = false;
                }
                catch (TimeoutException) { probeTimedOut = true; }

                if (!probeTimedOut)
                {
                    // UI is responsive — reset counters
                    System.Threading.Interlocked.Exchange(ref _consecutiveProbeFailures, 0);
                    System.Threading.Interlocked.Exchange(ref _freezeStackCaptured, 0);
                    // Log when the last UI action changes, plus once a minute — not every second
                    // (~3,600 lines/hour buried real events). Repeated rebuilds of the same panel
                    // still show up as repeated [UIAction] lines.
                    var lastAction = _lastUiAction;
                    var nowTicks = DateTime.UtcNow.Ticks;
                    if (!string.Equals(lastAction, _heartbeatLoggedAction, StringComparison.Ordinal)
                        || nowTicks - _heartbeatLoggedAtTicks >= TimeSpan.TicksPerMinute)
                    {
                        _heartbeatLoggedAction = lastAction;
                        _heartbeatLoggedAtTicks = nowTicks;
                        _crashReporter.Log($"[Heartbeat] UI responsive — last action: {lastAction}");
                    }
                    return;
                }

                // ── Probe failed — check if this is the 2nd consecutive failure ──
                var failures = System.Threading.Interlocked.Increment(ref _consecutiveProbeFailures);
                if (failures < 2)
                {
                    _crashReporter.Log($"[Heartbeat] Probe timeout #{failures} — waiting for 2nd consecutive failure before acting");
                    return;
                }

                if (_backgroundStopped) return;

                // ── 1. Last N UI actions ──────────────────────────────────────────
                var recentActions = CrashReporter.GetRecentUiActions(20);

                // ── 2. CPU sample (1s delta) ──────────────────────────────────────
                string cpuInfo = "unknown";
                bool cpuIdle = false;
                try
                {
                    var uiNativeId = UiThreadNativeId;
                    if (uiNativeId > 0)
                    {
                        using var proc0 = System.Diagnostics.Process.GetCurrentProcess();
                        var thread0 = proc0.Threads.Cast<System.Diagnostics.ProcessThread>()
                                          .FirstOrDefault(t => t.Id == (int)uiNativeId);
                        var t0 = thread0?.TotalProcessorTime;
                        await Task.Delay(1000).ConfigureAwait(false);
                        using var proc1 = System.Diagnostics.Process.GetCurrentProcess();
                        var thread1 = proc1.Threads.Cast<System.Diagnostics.ProcessThread>()
                                          .FirstOrDefault(t => t.Id == (int)uiNativeId);
                        if (t0.HasValue && thread1 != null)
                        {
                            var delta = thread1.TotalProcessorTime - t0.Value;
                            cpuIdle = delta.TotalMilliseconds < 900;
                            cpuInfo = cpuIdle
                                ? $"IDLE ({delta.TotalMilliseconds:F0}ms/1s — waiting on lock or native call)"
                                : $"PEGGED ({delta.TotalMilliseconds:F0}ms/1s — layout or compute loop)";
                        }
                        else cpuInfo = "UI thread not found in process";
                    }
                    else cpuInfo = "UI thread ID not captured";
                }
                catch (Exception ex) { cpuInfo = $"CPU sample failed: {ex.Message}"; }

                // ── 3. WM_NULL pump probe ─────────────────────────────────────────
                bool pumpAlive = false;
                string pumpInfo = "hwnd not captured";
                try
                {
                    var hwnd = MainWindowHwnd;
                    if (hwnd != IntPtr.Zero)
                    {
                        var smtResult = NativeInterop.SendMessageTimeoutW(
                            hwnd, NativeInterop.WM_NULL, IntPtr.Zero, IntPtr.Zero,
                            NativeInterop.SMTO_ABORTIFHUNG | NativeInterop.SMTO_BLOCK,
                            1000, out IntPtr _);
                        pumpAlive = smtResult != IntPtr.Zero;
                        bool isHung = NativeInterop.IsHungAppWindow(hwnd);
                        pumpInfo = pumpAlive
                            ? "responded (pump alive)"
                            : $"timed out (pump not responding) | IsHungAppWindow={isHung}";
                    }
                }
                catch (Exception ex) { pumpInfo = $"probe failed: {ex.Message}"; }

                // ── Write the frozen line synchronously ───────────────────────────
                CrashReporter.LogSync($"[Heartbeat] *** UI FROZEN *** last action: {_lastUiAction} | UI thread CPU: {cpuInfo} | Pump: {pumpInfo}");
                if (recentActions.Count > 0)
                {
                    var summary = string.Join(" → ", recentActions);
                    if (summary.Length > 4096) summary = summary.Substring(0, 4096) + "… (truncated)";
                    CrashReporter.LogSync($"[Heartbeat] Recent UI actions: {summary}");
                }

                // ── Dump dispatcher enqueue ring ──────────────────────────────────
                var enqueueEvents = CrashReporter.GetRecentEnqueueActions(50);
                if (enqueueEvents.Count > 0)
                {
                    var enqueueStr = string.Join(" | ", enqueueEvents);
                    if (enqueueStr.Length > 8192) enqueueStr = enqueueStr.Substring(0, 8192) + "… (truncated)";
                    CrashReporter.LogSync($"[Heartbeat.Enqueue] Last {enqueueEvents.Count} dispatcher events: {enqueueStr}");
                }

                // ── Classify the stall ────────────────────────────────────────────
                // Known signature: CPU IDLE + pump alive + High probe failed.
                // This is the fast-restart path (~5s total).
                // Non-matching stalls keep the slow 45s path with full diagnostics.
                bool isKnownSignature = cpuIdle && pumpAlive;

                // ── Once-per-stall detailed diagnostics ───────────────────────────
                // dumpCompleted is declared here so the restart task (below the gate) can await it.
                var dumpCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (System.Threading.Interlocked.CompareExchange(ref _freezeStackCaptured, 1, 0) == 0)
                {
                    // Timer last-tick
                    var timerLastTick = new DateTime(System.Threading.Interlocked.Read(ref _dispatcherTimerLastTickUtc), DateTimeKind.Utc);
                    var timerAgo = DateTime.UtcNow - timerLastTick;
                    var timerLastTickLocal = timerLastTick.ToLocalTime();
                    CrashReporter.LogSync($"[Heartbeat.Timer] DispatcherQueueTimer last ticked {timerAgo.TotalSeconds:F1}s ago (at {timerLastTickLocal:HH:mm:ss.fff} local / {timerLastTick:HH:mm:ss.fff} UTC)");

                    LogResourceCounters("at-freeze");

                    // Window inventory at freeze — compare class/handle state against startup
                    try { WindowStateManagerRef?.LogWindowInventory("at-freeze"); }
                    catch (Exception winvEx) { CrashReporter.LogSync($"[Heartbeat.WindowInventory] Failed: {winvEx.GetType().Name}: {winvEx.Message}"); }

                    // ThreadState + WaitReason
                    try
                    {
                        var uiNativeId = UiThreadNativeId;
                        if (uiNativeId > 0)
                        {
                            using var procWR = System.Diagnostics.Process.GetCurrentProcess();
                            procWR.Refresh();
                            var uiThread = procWR.Threads.Cast<System.Diagnostics.ProcessThread>()
                                .FirstOrDefault(t => t.Id == (int)uiNativeId);
                            if (uiThread != null)
                            {
                                var state = uiThread.ThreadState;
                                string waitInfo = state == System.Diagnostics.ThreadState.Wait
                                    ? $"WaitReason={uiThread.WaitReason} ({(int)uiThread.WaitReason})"
                                    : "N/A (not in Wait state)";
                                CrashReporter.LogSync($"[Heartbeat.Wait] UI thread state={state} | {waitInfo}");
                            }
                            else CrashReporter.LogSync($"[Heartbeat.Wait] UI thread not found (OSId={uiNativeId})");
                        }
                        else CrashReporter.LogSync("[Heartbeat.Wait] UiThreadNativeId not captured");
                    }
                    catch (Exception wrEx) { CrashReporter.LogSync($"[Heartbeat.Wait] Failed: {wrEx.GetType().Name}: {wrEx.Message}"); }

                    // Modules
                    try
                    {
                        using var procMod = System.Diagnostics.Process.GetCurrentProcess();
                        var allModules = procMod.Modules.Cast<System.Diagnostics.ProcessModule>().ToList();
                        static bool IsGpuDriver(string name) {
                            var n = name.ToLowerInvariant();
                            return n.StartsWith("nvapi") || n.StartsWith("nvwgf2") || n.StartsWith("nvcuda")
                                || n.StartsWith("amdxx") || n.StartsWith("atio") || n.StartsWith("atig")
                                || n.StartsWith("ig7icd") || n.StartsWith("ig75icd") || n.StartsWith("igdusc")
                                || n.StartsWith("d3d12core") || n.StartsWith("dxgi")
                                || n.StartsWith("dxcore") || n.StartsWith("directml");
                        }
                        var gpuMods = allModules
                            .Where(m => IsGpuDriver(System.IO.Path.GetFileName(m.FileName ?? "")))
                            .Select(m => { var fn = m.FileName ?? ""; string ver = "?", date = "?";
                                try { ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(fn).FileVersion ?? "?"; } catch { }
                                try { date = System.IO.File.GetLastWriteTime(fn).ToString("yyyy-MM-dd"); } catch { }
                                return $"{System.IO.Path.GetFileName(fn)} v{ver} ({date})"; })
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                        if (gpuMods.Count > 0)
                            CrashReporter.LogSync($"[Heartbeat.Modules] GPU/driver DLLs ({gpuMods.Count}): {string.Join(", ", gpuMods)}");
                        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        var thirdParty = allModules
                            .Where(m => { var p = m.FileName ?? "";
                                return !p.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)
                                    && !p.Contains(@"\dotnet\", StringComparison.OrdinalIgnoreCase)
                                    && !p.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                                    && !p.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase); })
                            .Select(m => System.IO.Path.GetFileName(m.FileName ?? "?"))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                        CrashReporter.LogSync(thirdParty.Count > 0
                            ? $"[Heartbeat.Modules] Third-party modules ({thirdParty.Count}): {string.Join(", ", thirdParty)}"
                            : "[Heartbeat.Modules] No third-party modules detected");
                    }
                    catch (Exception modEx) { CrashReporter.LogSync($"[Heartbeat.Modules] Module enumeration failed: {modEx.GetType().Name}: {modEx.Message}"); }

                    // Multi-priority probes (parallel)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var probeHigh   = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            var probeNormal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            var probeLow    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            bool qHigh   = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High,   () => probeHigh.TrySetResult(true))   == true;
                            bool qNormal = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => probeNormal.TrySetResult(true)) == true;
                            bool qLow    = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,    () => probeLow.TrySetResult(true))    == true;
                            CrashReporter.LogSync($"[Heartbeat.Probes] Enqueued: High={qHigh} Normal={qNormal} Low={qLow} — waiting 2s each in parallel...");
                            // Run all three in parallel
                            bool rHigh = false, rNormal = false, rLow = false;
                            var t1 = probeHigh.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => { if (!t.IsFaulted) rHigh = true; });
                            var t2 = probeNormal.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => { if (!t.IsFaulted) rNormal = true; });
                            var t3 = probeLow.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => { if (!t.IsFaulted) rLow = true; });
                            await Task.WhenAll(t1, t2, t3).ConfigureAwait(false);
                            CrashReporter.LogSync($"[Heartbeat.Probes] Completed: High={rHigh} Normal={rNormal} Low={rLow}");
                        }
                        catch (Exception ex) { CrashReporter.LogSync($"[Heartbeat.Probes] Failed: {ex.GetType().Name}: {ex.Message}"); }
                    });

                    // ClrMD stack capture
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var clrMdPath = System.IO.Path.Combine(
                                AppPaths.ExeDir,
                                "Microsoft.Diagnostics.Runtime.dll");
                            if (!System.IO.File.Exists(clrMdPath)) { CrashReporter.LogSync($"[Heartbeat.Stack] ClrMD not found at '{clrMdPath}'"); return; }
                            var clrMdAsm = System.Reflection.Assembly.LoadFrom(clrMdPath);
                            var loadedVer = clrMdAsm.GetName().Version?.ToString() ?? "unknown";
                            CrashReporter.LogSync($"[Heartbeat.Stack] ClrMD loaded: {loadedVer}");
                            var dataTargetType = clrMdAsm.GetType("Microsoft.Diagnostics.Runtime.DataTarget");
                            if (dataTargetType == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: DataTarget type not found"); return; }
                            var createSnapshot = dataTargetType.GetMethod("CreateSnapshotAndAttach",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                                null, new[] { typeof(int) }, null);
                            if (createSnapshot == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: CreateSnapshotAndAttach not found"); return; }
                            using var target = (IDisposable)createSnapshot.Invoke(null, new object[] { Environment.ProcessId })!;
                            var clrVersionsProp = target.GetType().GetProperty("ClrVersions");
                            var clrVersions = clrVersionsProp?.GetValue(target) as System.Collections.IEnumerable;
                            object? firstVersion = null;
                            if (clrVersions != null) foreach (var v in clrVersions) { firstVersion = v; break; }
                            if (firstVersion == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: no CLR runtime found"); return; }
                            var createRuntime = firstVersion.GetType().GetMethod("CreateRuntime", Type.EmptyTypes);
                            var runtime = createRuntime?.Invoke(firstVersion, null);
                            if (runtime == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: CreateRuntime returned null"); return; }
                            var threads = runtime.GetType().GetProperty("Threads")?.GetValue(runtime) as System.Collections.IEnumerable;
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine("[Heartbeat.Stack] Managed thread stacks at time of freeze:");
                            int threadCount = 0;
                            if (threads != null)
                            {
                                foreach (var thread in threads)
                                {
                                    threadCount++;
                                    var osId = (uint)(thread.GetType().GetProperty("OSThreadId")?.GetValue(thread) ?? 0u);
                                    var managedId = (int)(thread.GetType().GetProperty("ManagedThreadId")?.GetValue(thread) ?? 0);
                                    bool isUiThread = UiThreadNativeId > 0 && osId == UiThreadNativeId;
                                    sb.AppendLine($"  Thread OSId={osId}{(isUiThread ? " <-- UI THREAD" : "")} ManagedId={managedId}");
                                    try
                                    {
                                        var enumStackTrace = thread.GetType().GetMethod("EnumerateStackTrace", new[] { typeof(bool) });
                                        var frames = enumStackTrace?.Invoke(thread, new object[] { true }) as System.Collections.IEnumerable;
                                        int frameCount = 0;
                                        if (frames != null) foreach (var frame in frames) { sb.AppendLine($"    {frame}"); if (++frameCount >= 40) { sb.AppendLine("    ... (truncated)"); break; } }
                                        if (frameCount == 0) sb.AppendLine($"    (0 managed frames)");
                                    }
                                    catch (Exception frameEx) { sb.AppendLine($"    (frame enumeration threw: {frameEx.GetType().Name}: {frameEx.Message})"); }
                                }
                            }
                            sb.AppendLine($"[Heartbeat.Stack] Total threads enumerated: {threadCount}");
                            CrashReporter.LogSync(sb.ToString());
                        }
                        catch (Exception ex) { CrashReporter.LogSync($"[Heartbeat.Stack] ClrMD capture failed: {ex.GetType().Name}: {ex.Message}"); }
                    });

                    // Native stack capture
                    _ = Task.Run(CaptureNativeUiThreadStack);

                    // ── 5. Minidump — dev only, ~2s after freeze detection ────────
                    // Real freezes write freeze_*.dmp to dumps\, keep last 3, no skip.
                    // Test-button freezes write test_*.dmp to test_dumps\, never pruned or suppressed.
                    // dumpCompleted is signalled when the dump finishes so the restart task can wait.
                    if (DevUnlockService.IsUnlocked)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(2_000).ConfigureAwait(false);
                            if (_backgroundStopped) return;

                            // Re-probe to confirm still frozen
                            var reProbe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            if (DispatcherQueue?.TryEnqueue(() => reProbe.TrySetResult(true)) != true) return;
                            bool stillFrozen;
                            try   { await reProbe.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); stillFrozen = false; }
                            catch (TimeoutException) { stillFrozen = true; }
                            if (!stillFrozen) { CrashReporter.LogSync("[Heartbeat.Dump] UI recovered before 2s — skipping minidump"); return; }

                            bool isTest = IsTestFreezeActive;
                            var dumpTs = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                            string dumpDir, dumpPath;

                            if (isTest)
                            {
                                // Test dump — separate folder, no pruning, no pending marker, never suppressed
                                dumpDir  = System.IO.Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "test_dumps");
                                System.IO.Directory.CreateDirectory(dumpDir);
                                dumpPath = System.IO.Path.Combine(dumpDir, $"test_{dumpTs}.dmp");
                                CrashReporter.LogSync($"[Heartbeat.Dump] Writing TEST minidump to '{dumpPath}'...");
                            }
                            else
                            {
                                // Real freeze dump — keep last 3, no skip
                                dumpDir  = System.IO.Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "dumps");
                                System.IO.Directory.CreateDirectory(dumpDir);
                                dumpPath = System.IO.Path.Combine(dumpDir, $"freeze_{dumpTs}.dmp");

                                // Prune to keep last 3
                                try
                                {
                                    var existing = System.IO.Directory.GetFiles(dumpDir, "freeze_*.dmp").OrderBy(f => f).ToArray();
                                    while (existing.Length >= 3) { System.IO.File.Delete(existing[0]); CrashReporter.LogSync($"[Heartbeat.Dump] Pruned: {System.IO.Path.GetFileName(existing[0])}"); existing = existing.Skip(1).ToArray(); }
                                }
                                catch (Exception pruneEx) { CrashReporter.LogSync($"[Heartbeat.Dump] Prune failed: {pruneEx.Message}"); }

                                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
                                CrashReporter.LogSync($"[Heartbeat.Dump] Writing minidump at {timestamp} to '{dumpPath}'...");
                                var pendingMarker = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rhi_pending_dump");
                                try { System.IO.File.WriteAllText(pendingMarker, dumpPath); } catch { }
                            }

                            var hProcess = System.Diagnostics.Process.GetCurrentProcess().Handle;
                            var pid = (uint)Environment.ProcessId;
                            const NativeInterop.PssCaptureFlags captureFlags =
                                NativeInterop.PssCaptureFlags.PSS_CAPTURE_VA_CLONE         |
                                NativeInterop.PssCaptureFlags.PSS_CAPTURE_HANDLES          |
                                NativeInterop.PssCaptureFlags.PSS_CAPTURE_HANDLE_BASIC_INFORMATION |
                                NativeInterop.PssCaptureFlags.PSS_CAPTURE_THREADS          |
                                NativeInterop.PssCaptureFlags.PSS_CAPTURE_THREAD_CONTEXT   |
                                NativeInterop.PssCaptureFlags.PSS_CREATE_BREAKAWAY_OPTIONAL;
                            IntPtr snapshotHandle = IntPtr.Zero;
                            uint pssErr = NativeInterop.PssCaptureSnapshot(hProcess, captureFlags, NativeInterop.CONTEXT_ALL_X64, out snapshotHandle);
                            if (pssErr != 0) { CrashReporter.LogSync($"[Heartbeat.Dump] PssCaptureSnapshot failed ({pssErr}) — falling back to direct dump"); snapshotHandle = IntPtr.Zero; }
                            try
                            {
                                using var fs = new System.IO.FileStream(dumpPath, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
                                var dumpHandle = snapshotHandle != IntPtr.Zero ? snapshotHandle : hProcess;
                                bool ok = NativeInterop.MiniDumpWriteDump(dumpHandle, pid, fs.SafeFileHandle.DangerousGetHandle(),
                                    NativeInterop.MiniDumpType.MiniDumpWithFullMemory | NativeInterop.MiniDumpType.MiniDumpWithThreadInfo,
                                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                                if (ok)
                                {
                                    var sizeMb = new System.IO.FileInfo(dumpPath).Length / 1024 / 1024;
                                    CrashReporter.LogSync($"[Heartbeat.Dump] {(isTest ? "Test" : "Real")} minidump written ({sizeMb} MB) — '{dumpPath}'");
                                    if (!isTest)
                                    {
                                        var pendingMarker2 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rhi_pending_dump");
                                        try { System.IO.File.WriteAllText(pendingMarker2, $"{dumpPath}|{sizeMb}MB"); } catch { }
                                    }
                                }
                                else
                                {
                                    CrashReporter.LogSync($"[Heartbeat.Dump] MiniDumpWriteDump failed — Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                                    if (!isTest) { var pendingMarker3 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rhi_pending_dump"); try { System.IO.File.Delete(pendingMarker3); } catch { } }
                                }
                            }
                            finally { if (snapshotHandle != IntPtr.Zero) NativeInterop.PssFreeSnapshot(hProcess, snapshotHandle); }
                        }
                        catch (Exception ex) { CrashReporter.LogSync($"[Heartbeat.Dump] Minidump failed: {ex.GetType().Name}: {ex.Message}"); }
                        finally { dumpCompleted.TrySetResult(true); }
                    });
                    else
                        dumpCompleted.TrySetResult(true); // dumps disabled — signal immediately so restart doesn't wait

                } // end once-per-stall diagnostics gate
                else
                    dumpCompleted.TrySetResult(true); // subsequent tick — no new dump, signal immediately

                // ── 6. Auto-restart ───────────────────────────────────────────────
                // Known signature → fast path (~5s total including diagnostics above).
                // Non-matching stalls → slow 45s path (gives time to collect more evidence).
                var restartDelaySec = isKnownSignature ? 3 : 45;
                if (System.Threading.Interlocked.CompareExchange(ref _restartPending, 1, 0) != 0)
                    return; // a restart is already scheduled from an earlier tick
                _ = Task.Run(async () =>
                {
                    bool exiting = false;
                    try
                    {
                        await Task.Delay(restartDelaySec * 1000).ConfigureAwait(false);
                        if (_backgroundStopped) return;

                        // Re-confirm High probe is still dead
                        var h2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var n2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var l2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High,   () => h2.TrySetResult(true));
                        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => n2.TrySetResult(true));
                        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,    () => l2.TrySetResult(true));
                        bool hOk = false, nOk = false, lOk = false;
                        var rt1 = h2.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => { if (!t.IsFaulted) hOk = true; });
                        var rt2 = n2.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => { if (!t.IsFaulted) nOk = true; });
                        var rt3 = l2.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => { if (!t.IsFaulted) lOk = true; });
                        await Task.WhenAll(rt1, rt2, rt3).ConfigureAwait(false);

                        if (hOk || nOk || lOk)
                        {
                            CrashReporter.LogSync($"[Heartbeat.Restart] Dispatcher recovered (High={hOk} Normal={nOk} Low={lOk}) — restart cancelled");
                            return;
                        }

                        // ── Install-active defer (cap 30s) ────────────────────────
                        var installWaitStart = DateTime.UtcNow;
                        while ((DateTime.UtcNow - installWaitStart).TotalSeconds < 30)
                        {
                            bool anyInstalling = _allCards.Any(c =>
                                c.IsInstalling || c.RsIsInstalling || c.UlIsInstalling ||
                                c.DcIsInstalling || c.OsIsInstalling || c.DxvkIsInstalling || c.RefIsInstalling);
                            if (!anyInstalling) break;
                            CrashReporter.LogSync("[Heartbeat.Restart] Install/download in progress — deferring restart (checking again in 5s)");
                            await Task.Delay(5_000).ConfigureAwait(false);
                        }

                        // Loop guard
                        var restartLogPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "restart_log.txt");
                        var now = DateTime.UtcNow;
                        var recentRestarts = 0;
                        try
                        {
                            if (System.IO.File.Exists(restartLogPath))
                            {
                                var lines = System.IO.File.ReadAllLines(restartLogPath);
                                recentRestarts = lines.Count(l =>
                                    DateTime.TryParse(l, System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.AssumeUniversal, out var t) && t > now.AddMinutes(-5));
                            }
                        }
                        catch { }

                        if (recentRestarts >= 2)
                        {
                            CrashReporter.LogSync($"[Heartbeat.Restart] Loop guard triggered — {recentRestarts} restarts in the last 5 minutes. Not restarting.");
                            return;
                        }

                        try { System.IO.File.AppendAllText(restartLogPath, now.ToString("O") + Environment.NewLine); } catch { }
                        var uncleanMarkerPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rhi_unclean_restart");
                        try { System.IO.File.WriteAllText(uncleanMarkerPath, now.ToString("O")); } catch { }

                        CrashReporter.LogSync($"[Heartbeat.Restart] Restarting RHI — {(isKnownSignature ? "known-signature fast" : "slow")} path, restart #{recentRestarts + 1} in 5-min window");
                        // Wait for the dump and log flush to finish before exiting — cap at 5s
                        try { await dumpCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
                        exiting = true;
                        CrashReporter.Shutdown();
                        var exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
                        Environment.Exit(2);
                    }
                    catch (Exception ex) { CrashReporter.LogSync($"[Heartbeat.Restart] Auto-restart failed: {ex.GetType().Name}: {ex.Message}"); }
                    finally
                    {
                        // Cancelled (dispatcher recovered, loop guard, shutdown) or failed: allow a later stall to schedule again.
                        if (!exiting) System.Threading.Interlocked.Exchange(ref _restartPending, 0);
                    }
                });

            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }
}

