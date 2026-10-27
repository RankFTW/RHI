namespace RenoDXCommander.Services;
/// <summary>UI-thread-owned receiver for the installer's asynchronous focus handoff.</summary>
internal sealed class ForegroundActivationTarget : IDisposable
{
    private readonly IntPtr _window;
    private readonly Func<Action, bool> _enqueue;
    private readonly Action _activate;
    private readonly NativeInterop.SubclassProc _procedure; // keep native callback alive
    private bool _disposed;
    internal ForegroundActivationTarget(IntPtr window, Func<Action, bool> enqueue, Action activate)
    {
        _window = window;
        _enqueue = enqueue;
        _activate = activate;
        _procedure = WindowProcedure;
        if (!NativeInterop.SetWindowSubclass(window, _procedure, new UIntPtr(1), UIntPtr.Zero))
        {
            _disposed = true;
            CrashReporter.Log("[Foreground] Could not register the installer activation receiver");
            return;
        }
        ForegroundActivation.PublishReady(window);
    }
    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        UIntPtr subclassId, UIntPtr referenceData)
    {
        if (message == 0x0082) // WM_NCDESTROY: also clean up if the owner skips explicit disposal
            Dispose();
        else if (ForegroundActivation.RequestMessage != 0 && message == ForegroundActivation.RequestMessage)
        {
            // Return from the native callback before touching WinUI. No cross-process
            // SendMessage or input-queue attachment; installer granted the exact PID.
            try
            {
                CrashReporter.Log("[Foreground] Installer activation request received");
                _enqueue(() => { if (!_disposed) _activate(); });
            }
            catch (Exception ex)
            {
                // Never unwind a managed dispatcher exception through a native WndProc.
                CrashReporter.Log($"[Foreground] Could not queue activation — {ex.Message}");
            }
            return IntPtr.Zero;
        }
        return NativeInterop.DefSubclassProc(window, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ForegroundActivation.RemoveReady(_window);
        NativeInterop.RemoveWindowSubclass(_window, _procedure, new UIntPtr(1));
    }
}
