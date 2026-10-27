using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // DbgHelp is single-threaded — serialize SymFromAddr calls.
    private static readonly object _nativeStackLock = new();

    internal void CaptureNativeUiThreadStackPublic() => CaptureNativeUiThreadStack();

    /// <summary>
    /// Captures a native call stack of the UI thread using StackWalk64.
    /// Safe to call from a background thread. The suspend window is minimal:
    /// SuspendThread → GetThreadContext (copies CONTEXT blob) → ResumeThread.
    /// All further work (stack walking, symbol resolution) happens after resume.
    ///
    /// Alignment: x64 CONTEXT requires 16-byte alignment. We allocate SIZE+16 extra
    /// bytes and align the pointer manually — stackalloc alone does not guarantee it.
    /// </summary>
    private unsafe void CaptureNativeUiThreadStack()
    {
        var uiNativeId = UiThreadNativeId;
        if (uiNativeId == 0) { CrashReporter.LogSync("[Heartbeat.Native] UiThreadNativeId not captured"); return; }

        IntPtr hThread = IntPtr.Zero;
        try
        {
            hThread = NativeInterop.OpenThread(
                NativeInterop.THREAD_SUSPEND_RESUME | NativeInterop.THREAD_GET_CONTEXT | NativeInterop.THREAD_QUERY_INFORMATION,
                false, uiNativeId);
            if (hThread == IntPtr.Zero)
            {
                CrashReporter.LogSync($"[Heartbeat.Native] OpenThread failed (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                return;
            }

            // Allocate CONTEXT + 16 extra bytes for alignment, then align to 16-byte boundary.
            // stackalloc guarantees no heap allocation — zero-init via Unsafe.InitBlock.
            // CONTEXT_X64_SIZE = 1232 bytes; with 16 extra the aligned pointer is always valid.
            byte* ctxRaw = stackalloc byte[NativeInterop.CONTEXT_X64_SIZE + 16];
            // Align up to next 16-byte boundary
            var rawAddr = (ulong)ctxRaw;
            var alignedAddr = (rawAddr + 15UL) & ~15UL;
            byte* ctxBuf = (byte*)alignedAddr;
            System.Runtime.CompilerServices.Unsafe.InitBlock(ctxBuf, 0, (uint)NativeInterop.CONTEXT_X64_SIZE);
            // ContextFlags at byte offset 48 in x64 CONTEXT — CONTEXT_FULL = 0x10007
            *(uint*)(ctxBuf + 48) = 0x00010007u;
            var ctxPtr = (IntPtr)ctxBuf;

            // ── SUSPEND WINDOW: zero managed allocations ────────────────────────
            // Only GetThreadContext runs inside the window — no module lookups,
            // no locks, no allocations. GetModuleHandleExW may take the loader
            // lock; calling it while the UI thread is suspended could deadlock
            // if the UI thread holds that lock (e.g. during a DLL load freeze).
            // All module resolution and name lookups run after ResumeThread.
            var suspendSw = System.Diagnostics.Stopwatch.StartNew();
            NativeInterop.SuspendThread(hThread);
            bool ctxOk;
            try
            {
                ctxOk = NativeInterop.GetThreadContext(hThread, ctxPtr);
            }
            finally
            {
                // ResumeThread is ALWAYS called — even if GetThreadContext throws.
                // Safety limit: if we somehow reach here after 200ms, resume anyway.
                NativeInterop.ResumeThread(hThread);
                suspendSw.Stop();
                if (suspendSw.ElapsedMilliseconds > 200)
                    CrashReporter.LogSync($"[Heartbeat.Native] WARNING: suspend window took {suspendSw.ElapsedMilliseconds}ms — possible deadlock risk");
            }
            // ── END SUSPEND WINDOW ──────────────────────────────────────────────

            if (!ctxOk)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                CrashReporter.LogSync($"[Heartbeat.Native] GetThreadContext failed — Win32 error {err} (998=misalignment, 5=access denied)");
                return;
            }

            // Extract Rip (offset 248), Rsp (offset 152), Rbp (offset 160) from x64 CONTEXT
            ulong rip = *(ulong*)(ctxBuf + 248);
            ulong rsp = *(ulong*)(ctxBuf + 152);
            ulong rbp = *(ulong*)(ctxBuf + 160);
            CrashReporter.LogSync($"[Heartbeat.Native] Context captured: RIP=0x{rip:X16} RSP=0x{rsp:X16} RBP=0x{rbp:X16}");

            var frames   = new ulong[1]; // top frame only — walk is disabled pending stability fix
            var imgBases = new ulong[1];
            int frameCount = 1;
            frames[0]   = rip;

            // Resolve module for the top frame via GetModuleHandleExW(FROM_ADDRESS)
            if (NativeInterop.GetModuleHandleExW(
                    NativeInterop.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                    NativeInterop.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                    (IntPtr)(long)rip, out IntPtr _hmodTop) && _hmodTop != IntPtr.Zero)
            {
                imgBases[0] = (ulong)(long)_hmodTop;
                // Prefer the image base from RtlLookupFunctionEntry (more precise than module handle)
                NativeInterop.RtlLookupFunctionEntry(rip, out ulong imgBaseTop, IntPtr.Zero);
                if (imgBaseTop != 0) imgBases[0] = imgBaseTop;
            }

            CrashReporter.LogSync("[Heartbeat.Native] Walk: top-frame-only mode (RtlVirtualUnwind disabled — crashes on managed frames)");

            // Resolve frames: use GetModuleHandleExW(FROM_ADDRESS) for accurate module lookup
            // (doesn't miss KnownDlls or late-loaded modules like Process.Modules can).
            // SymFromAddr still attempted first; PE export fallback handles the rest.
            var hProcess2 = System.Diagnostics.Process.GetCurrentProcess().Handle;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Heartbeat.Native] Native stack ({frameCount} frames):");

            // Pre-allocate SYMBOL_INFO on the stack
            NativeInterop.SYMBOL_INFO symInfo;
            symInfo.SizeOfStruct = (uint)sizeof(NativeInterop.SYMBOL_INFO);
            symInfo.MaxNameLen   = NativeInterop.MAX_SYM_NAME;

            // Track which modules we've already logged version info for
            var loggedModules = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < frameCount; i++)
            {
                var addr = frames[i];

                // Resolve module from address — accurate even for KnownDlls / late-loaded modules
                string modName     = "?";
                string modFullPath = "";
                ulong  modBase     = imgBases[i]; // from RtlLookupFunctionEntry

                // GetModuleHandleExW(FROM_ADDRESS) for name — doesn't increment refcount
                if (NativeInterop.GetModuleHandleExW(
                        NativeInterop.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                        NativeInterop.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                        (IntPtr)addr, out IntPtr hMod) && hMod != IntPtr.Zero)
                {
                    var sb2 = new System.Text.StringBuilder(512);
                    if (NativeInterop.GetModuleFileNameW(hMod, sb2, (uint)sb2.Capacity) > 0)
                    {
                        modFullPath = sb2.ToString();
                        modName     = System.IO.Path.GetFileName(modFullPath);
                        // Sync imgBase from module handle if RtlLookupFunctionEntry returned 0
                        if (modBase == 0) modBase = (ulong)hMod.ToInt64();
                    }
                }

                // Log PE version + TimeDateStamp + SizeOfImage once per unique module
                if (modName != "?" && loggedModules.Add(modName))
                {
                    try
                    {
                        string fileVer = "?";
                        try { fileVer = System.Diagnostics.FileVersionInfo
                            .GetVersionInfo(modFullPath).FileVersion ?? "?"; } catch { }

                        uint ts = 0, soi = 0;
                        try
                        {
                            var basePtr = (byte*)modBase;
                            int lfanew  = *(int*)(basePtr + 0x3C);
                            ts  = *(uint*)(basePtr + lfanew + 0x08);
                            soi = *(uint*)(basePtr + lfanew + 0x50);
                        }
                        catch { }

                        sb.AppendLine($"       [{modName}  ver={fileVer}  ts=0x{ts:X8}  soi=0x{soi:X}]");
                    }
                    catch { }
                }

                // Try SymFromAddr first
                string frameName;
                ulong displacement = 0;
                System.Runtime.CompilerServices.Unsafe.InitBlock(symInfo.Name, 0, (uint)(NativeInterop.MAX_SYM_NAME * 2));
                bool symOk;
                lock (_nativeStackLock)
                {
                    symOk = NativeInterop.SymFromAddr(hProcess2, addr, out displacement, &symInfo);
                }

                if (symOk && symInfo.NameLen > 0)
                {
                    var name = new string(symInfo.Name, 0, (int)symInfo.NameLen);
                    frameName = $"{name}+0x{displacement:X}";
                }
                else if (modBase != 0)
                {
                    // PE export table fallback: find nearest exported name at or below addr.
                    string? exportName = null;
                    uint    exportDisp = 0;
                    try
                    {
                        var basePtr = (byte*)modBase;
                        var rva     = (uint)(addr - modBase);
                        int lfanew  = *(int*)(basePtr + 0x3C);
                        uint expDirRva  = *(uint*)(basePtr + lfanew + 0x88);
                        uint expDirSize = *(uint*)(basePtr + lfanew + 0x8C);
                        if (expDirRva != 0 && expDirSize != 0)
                        {
                            var expDir  = (uint*)(basePtr + expDirRva);
                            uint numFuncs = expDir[5];
                            uint numNames = expDir[6];
                            uint* funcs   = (uint*)(basePtr + expDir[7]);
                            uint* names   = (uint*)(basePtr + expDir[8]);
                            ushort* ords  = (ushort*)(basePtr + expDir[9]);
                            uint bestRva = 0;
                            uint bestIdx = uint.MaxValue;
                            for (uint n = 0; n < numNames; n++)
                            {
                                ushort ord  = ords[n];
                                if (ord >= numFuncs) continue;
                                uint funcRva = funcs[ord];
                                if (funcRva >= expDirRva && funcRva < expDirRva + expDirSize) continue;
                                if (funcRva <= rva && funcRva > bestRva) { bestRva = funcRva; bestIdx = n; }
                            }
                            if (bestIdx != uint.MaxValue)
                            {
                                exportName = new string((sbyte*)(basePtr + names[bestIdx]));
                                exportDisp = rva - bestRva;
                            }
                        }
                    }
                    catch { }

                    frameName = exportName != null
                        ? $"{exportName}+0x{exportDisp:X}"
                        : (modName != "?" ? $"{modName}+0x{addr - modBase:X}" : $"0x{addr:X16}");
                }
                else
                {
                    frameName = $"0x{addr:X16}";
                }

                sb.AppendLine($"  {i,2}: {frameName}");
            }
            if (frameCount == 0)
                sb.AppendLine("  (no frames captured — thread may be in kernel wait or context was invalid)");
            CrashReporter.LogSync(sb.ToString());

            // ── RSP stack scan — heuristic return-address search ─────────────────
            // ReadProcessMemory copies the stack while the thread is already resumed.
            // Uses VirtualQuery to clamp the read to committed memory (avoids guard pages).
            // Uses bytes-actually-read even on partial copy (error 299) so shallow stacks work.
            // Export-offset threshold: if the nearest export is > 0x1000 bytes away, prints
            // "module+0xRVA (no nearby export)" instead of a misleading name.
            // All module resolution happens here, post-resume — no loader lock risk.
            if (rsp != 0)
            {
                try
                {
                    const int MaxScanBytes = 12 * 1024; // 12 KB ceiling
                    const int MaxHits      = 10;
                    const uint ExportOffsetThreshold = 0x1000; // max offset before name is misleading

                    // Clamp read to committed memory region starting at RSP
                    int scanBytes = MaxScanBytes;
                    if (NativeInterop.VirtualQuery((IntPtr)(long)rsp,
                            out NativeInterop.MEMORY_BASIC_INFORMATION mbi,
                            (UIntPtr)System.Runtime.InteropServices.Marshal.SizeOf<NativeInterop.MEMORY_BASIC_INFORMATION>()) != UIntPtr.Zero
                        && mbi.State == 0x1000 /* MEM_COMMIT */)
                    {
                        ulong regionEnd  = (ulong)(long)mbi.BaseAddress + (ulong)mbi.RegionSize;
                        ulong available  = regionEnd > rsp ? regionEnd - rsp : 0;
                        if (available < (ulong)MaxScanBytes)
                            scanBytes = (int)available;
                    }

                    if (scanBytes < 8) goto skipScan;

                    var stackCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(scanBytes);
                    try
                    {
                        var hProc = System.Diagnostics.Process.GetCurrentProcess().Handle;
                        bool readOk = NativeInterop.ReadProcessMemory(
                            hProc, (IntPtr)(long)rsp, stackCopy,
                            (UIntPtr)scanBytes, out UIntPtr bytesRead);

                        int actualBytes = (int)bytesRead;

                        // Use bytes actually read even on partial copy (error 299 = partial)
                        if (!readOk && actualBytes < 8)
                        {
                            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            CrashReporter.LogSync($"[Heartbeat.Scan] ReadProcessMemory failed (error {err}, bytesRead={actualBytes})");
                        }
                        else
                        {
                            var scanSb = new System.Text.StringBuilder();
                            scanSb.AppendLine($"[Heartbeat.Scan] Probable return addresses (heuristic, {actualBytes} bytes scanned, may include stale values):");
                            int hits = 0;
                            unsafe
                            {
                                byte* buf = (byte*)stackCopy;
                                for (int off = 0; off + 8 <= actualBytes && hits < MaxHits; off += 8)
                                {
                                    ulong candidate = *(ulong*)(buf + off);
                                    if (candidate < 0x10000UL || candidate > 0x7FFF_FFFF_FFFFUL) continue;

                                    // Call-prefix filter: E8 at candidate-5 (near call)
                                    // or FF at candidate-6 (indirect call)
                                    bool likelyReturn = false;
                                    try
                                    {
                                        byte prefix = 0;
                                        UIntPtr nRead;
                                        if (NativeInterop.ReadProcessMemory(hProc,
                                            (IntPtr)(long)(candidate - 5), (IntPtr)(&prefix), (UIntPtr)1, out nRead)
                                            && nRead == (UIntPtr)1 && prefix == 0xE8)
                                            likelyReturn = true;
                                        else if (!likelyReturn && NativeInterop.ReadProcessMemory(hProc,
                                            (IntPtr)(long)(candidate - 6), (IntPtr)(&prefix), (UIntPtr)1, out nRead)
                                            && nRead == (UIntPtr)1 && prefix == 0xFF)
                                            likelyReturn = true;
                                    }
                                    catch { likelyReturn = true; }

                                    if (!likelyReturn) continue;

                                    if (!NativeInterop.GetModuleHandleExW(
                                            NativeInterop.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                            NativeInterop.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                            (IntPtr)(long)candidate, out IntPtr hScanMod) || hScanMod == IntPtr.Zero)
                                        continue;

                                    string scanModName = "?";
                                    ulong  scanModBase = (ulong)(long)hScanMod;
                                    var sb3 = new System.Text.StringBuilder(512);
                                    if (NativeInterop.GetModuleFileNameW(hScanMod, sb3, (uint)sb3.Capacity) > 0)
                                        scanModName = System.IO.Path.GetFileName(sb3.ToString());

                                    NativeInterop.RtlLookupFunctionEntry(candidate, out ulong scanImgBase, IntPtr.Zero);
                                    if (scanImgBase != 0) scanModBase = scanImgBase;

                                    // Export name lookup with offset threshold
                                    string scanFrame;
                                    try
                                    {
                                        uint scanRva = (uint)(candidate - scanModBase);
                                        byte* scanBase = (byte*)scanModBase;
                                        int scanLfanew = *(int*)(scanBase + 0x3C);
                                        uint expRva  = *(uint*)(scanBase + scanLfanew + 0x88);
                                        uint expSize = *(uint*)(scanBase + scanLfanew + 0x8C);
                                        string? expName = null;
                                        uint   expDisp  = 0;
                                        if (expRva != 0 && expSize != 0)
                                        {
                                            var expDir   = (uint*)(scanBase + expRva);
                                            uint nFuncs  = expDir[5], nNames = expDir[6];
                                            uint* funcs  = (uint*)(scanBase + expDir[7]);
                                            uint* names  = (uint*)(scanBase + expDir[8]);
                                            ushort* ords = (ushort*)(scanBase + expDir[9]);
                                            uint bestRva2 = 0; uint bestIdx2 = uint.MaxValue;
                                            for (uint n = 0; n < nNames; n++)
                                            {
                                                ushort ord = ords[n];
                                                if (ord >= nFuncs) continue;
                                                uint fr = funcs[ord];
                                                if (fr >= expRva && fr < expRva + expSize) continue;
                                                if (fr <= scanRva && fr > bestRva2) { bestRva2 = fr; bestIdx2 = n; }
                                            }
                                            if (bestIdx2 != uint.MaxValue && (scanRva - bestRva2) <= ExportOffsetThreshold)
                                            {
                                                expName = new string((sbyte*)(scanBase + names[bestIdx2]));
                                                expDisp = scanRva - bestRva2;
                                            }
                                        }
                                        // Apply threshold: large offsets get RVA only (not a misleading name)
                                        scanFrame = expName != null
                                            ? $"{expName}+0x{expDisp:X}"
                                            : $"{scanModName}+0x{scanRva:X} (no nearby export)";
                                    }
                                    catch { scanFrame = $"{scanModName}+0x{candidate - scanModBase:X}"; }

                                    scanSb.AppendLine($"  {hits,2}: {scanFrame}  (rsp+0x{off:X})");
                                    hits++;
                                }
                            }
                            if (hits == 0)
                                scanSb.AppendLine("  (no return addresses found)");
                            CrashReporter.LogSync(scanSb.ToString());
                        }
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(stackCopy);
                    }
                    skipScan:;
                }
                catch (Exception scanEx)
                {
                    CrashReporter.LogSync($"[Heartbeat.Scan] Stack scan failed: {scanEx.GetType().Name}: {scanEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.LogSync($"[Heartbeat.Native] Native stack capture failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (hThread != IntPtr.Zero)
                NativeInterop.CloseHandle(hThread);
        }
    }
}

