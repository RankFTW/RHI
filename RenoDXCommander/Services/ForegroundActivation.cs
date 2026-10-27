namespace RenoDXCommander.Services;
/// <summary>Requests focus once, without joining input queues or changing topmost state.</summary>
internal static class ForegroundActivation
{
    // Shared with the installer. The property is published only after the UI is ready.
    internal const string ReadyProperty = "RHI.ForegroundReady.v1";
    internal const string MessageName = "RHI.ForegroundRequest.v1";
    internal static readonly uint RequestMessage = NativeInterop.RegisterWindowMessage(MessageName);
    internal static void PublishReady(IntPtr window)
    {
        if (RequestMessage == 0 || !NativeInterop.SetProp(window, ReadyProperty, new IntPtr(1)))
            CrashReporter.Log("[Foreground] Could not advertise window readiness to the installer");
    }
    internal static void RemoveReady(IntPtr window) => NativeInterop.RemoveProp(window, ReadyProperty);
    internal static bool Request(IntPtr window)
    {
        return RequestCore(
            () => NativeInterop.IsIconic(window),
            () => NativeInterop.ShowWindow(window, NativeInterop.SW_RESTORE),
            () => NativeInterop.GetForegroundWindow() == window || NativeInterop.SetForegroundWindow(window),
            () => FlashTaskbar(window));
    }
    // Separates the policy from user32 so refusal/minimized cases can be tested
    // without taking focus from the person running the tests.
    internal static bool RequestCore(Func<bool> isMinimized, Action restore,
        Func<bool> requestForeground, Action flashTaskbar)
    {
        if (isMinimized()) restore();
        if (requestForeground()) return true;
        flashTaskbar();
        return false;
    }
    private static void FlashTaskbar(IntPtr window)
    {
        var info = new NativeInterop.FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeInterop.FLASHWINFO>(),
            hwnd = window,
            dwFlags = 2, // FLASHW_TRAY; finite count, not FLASHW_TIMERNOFG
            uCount = 3,
        };
        NativeInterop.FlashWindowEx(ref info);
        CrashReporter.Log("[Foreground] Windows declined activation — flashing the taskbar instead");
    }
}
