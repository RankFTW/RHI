// SteamControllerPad.cs -- native Steam Controller support for the controller feature
// (MainWindow.Controller.cs). A Steam Controller has no XInput layer of its own: outside Steam games
// Windows only sees it as a keyboard and mouse. So, like PlayStation pads (PlayStationPad.cs), it's read
// straight from its HID input reports and turned into the same XInput-style state. Read-only and
// shared: RHI never sends the controller anything, so Steam keeps full control of it.
// Report layouts follow SDL's hidapi drivers (SDL_hidapi_steam.c, SDL_hidapi_steam_triton.c and
// steam/controller_structs.h).
//   Steam Controller (2026, "Triton"): USB 28DE:1302, Bluetooth 1303, wireless dongles 1304 / 1305.
//   Original Steam Controller (2015): USB 28DE:1102 (and the early 1101), wireless dongle 1142.
//   Its Bluetooth mode (1105 / 1106) uses a different, chunked format and isn't read.

using Microsoft.Win32.SafeHandles;

namespace RenoDXCommander.Services;

/// <summary>Which Steam Controller, for the report layout.</summary>
public enum SteamControllerModel { Original, Triton }

/// <summary>Turns Steam Controller HID input reports into <see cref="PadState"/>. Pure, so it's unit-tested.</summary>
public static class SteamControllerReport
{
    // Triton report ids (byte 0)
    internal const byte TritonState = 0x42, TritonStateBle = 0x45, TritonStateTimestamp = 0x47;
    internal const byte TritonWirelessX = 0x46, TritonWireless = 0x79;

    // Triton button bits (SDL's TritonButtons)
    internal const uint TA = 0x1, TB = 0x2, TX = 0x4, TY = 0x8, TR3 = 0x20, TView = 0x40, TR = 0x200,
        TDown = 0x400, TRight = 0x800, TLeft = 0x1000, TUp = 0x2000, TMenu = 0x4000, TL3 = 0x8000, TL = 0x80000;

    // Original controller: a "ValveInReport" (version 1, type, length, payload); state = type 1 or 7
    internal const byte OriginalState = 1, OriginalBleState = 7, OriginalWireless = 3;
    internal const uint ORT = 0x1, OLT = 0x2, ORB = 0x4, OLB = 0x8, OY = 0x10, OB = 0x20, OX = 0x40, OA = 0x80,
        OUp = 0x100, ORight = 0x200, OLeft = 0x400, ODown = 0x800, OMenu = 0x1000, OEscape = 0x4000,
        OLeftPadClick = 0x20000, ORightPadClick = 0x40000, OLeftFinger = 0x80000, ORightFinger = 0x100000,
        OStickClick = 0x400000, OPadAndStick = 0x800000;

    /// <summary>
    /// The state in <paramref name="report"/> (as Windows reads it: byte 0 = report id, 0 when the device
    /// has none), or null for a report that carries no buttons. <paramref name="previous"/> is the last
    /// state from this controller (the original one sometimes leaves the stick out of a report).
    /// </summary>
    public static PadState? Parse(SteamControllerModel model, ReadOnlySpan<byte> report, PadState? previous = null) =>
        model == SteamControllerModel.Triton ? ParseTriton(report) : ParseOriginal(report, previous);

    /// <summary>True for a wireless dongle's "controller disconnected" message.</summary>
    public static bool IsDisconnect(SteamControllerModel model, ReadOnlySpan<byte> report)
    {
        if (model == SteamControllerModel.Triton)
            return report.Length >= 2 && report[0] is TritonWireless or TritonWirelessX && report[1] == 1;
        int o = OriginalOffset(report);
        return o >= 0 && report.Length > o + 4 && report[o + 2] == OriginalWireless && report[o + 4] == 1;
    }

    // Triton: id, seq, buttons (u32), L2 R2 (0…32767), LX LY RX RY (+Y up), then pads and motion
    private static PadState? ParseTriton(ReadOnlySpan<byte> r)
    {
        if (r.Length < 18 || r[0] is not (TritonState or TritonStateBle or TritonStateTimestamp)) return null;
        uint t = U32(r, 2);
        ushort b = 0;
        if ((t & TA) != 0) b |= ControllerPoller.A;
        if ((t & TB) != 0) b |= ControllerPoller.B;
        if ((t & TX) != 0) b |= ControllerPoller.X;
        if ((t & TY) != 0) b |= ControllerPoller.Y;
        if ((t & TL) != 0) b |= ControllerPoller.LeftShoulder;
        if ((t & TR) != 0) b |= ControllerPoller.RightShoulder;
        if ((t & TL3) != 0) b |= ControllerPoller.LeftThumb;
        if ((t & TR3) != 0) b |= ControllerPoller.RightThumb;
        // SDL maps these two bits this way round: MENU → Back, VIEW → Start
        if ((t & TMenu) != 0) b |= ControllerPoller.BackBtn;
        if ((t & TView) != 0) b |= ControllerPoller.Start;
        if ((t & TUp) != 0) b |= ControllerPoller.DPadUp;
        if ((t & TDown) != 0) b |= ControllerPoller.DPadDown;
        if ((t & TLeft) != 0) b |= ControllerPoller.DPadLeft;
        if ((t & TRight) != 0) b |= ControllerPoller.DPadRight;
        return new PadState(b, Trigger(S16(r, 6)), Trigger(S16(r, 8)), S16(r, 10), S16(r, 12), S16(r, 14), S16(r, 16));
    }

    /// <summary>
    /// Original: [report id 0] version (u16 = 1), type, length, then the state: packet number (u32),
    /// buttons (bytes 0–2) with L2 / R2 in bytes 3–4, left X / Y (stick or left pad), right pad X / Y.
    /// </summary>
    private static PadState? ParseOriginal(ReadOnlySpan<byte> r, PadState? previous)
    {
        int o = OriginalOffset(r);
        if (o < 0 || r[o + 2] is not (OriginalState or OriginalBleState)) return null;
        int p = o + 4;
        if (r.Length < p + 20) return null;
        uint t = (uint)(r[p + 4] | r[p + 5] << 8 | r[p + 6] << 16);
        byte l2 = r[p + 7], r2 = r[p + 8];
        short leftX = S16(r, p + 12), leftY = S16(r, p + 14), rightPadY = S16(r, p + 18);

        ushort b = 0;
        if ((t & OA) != 0) b |= ControllerPoller.A;
        if ((t & OB) != 0) b |= ControllerPoller.B;
        if ((t & OX) != 0) b |= ControllerPoller.X;
        if ((t & OY) != 0) b |= ControllerPoller.Y;
        if ((t & OLB) != 0) b |= ControllerPoller.LeftShoulder;
        if ((t & ORB) != 0) b |= ControllerPoller.RightShoulder;
        if ((t & OMenu) != 0) b |= ControllerPoller.BackBtn;
        if ((t & OEscape) != 0) b |= ControllerPoller.Start;
        if ((t & OStickClick) != 0) b |= ControllerPoller.LeftThumb;
        if ((t & ORightPadClick) != 0) b |= ControllerPoller.RightThumb;
        if ((t & OUp) != 0) b |= ControllerPoller.DPadUp;
        if ((t & ODown) != 0) b |= ControllerPoller.DPadDown;
        if ((t & OLeft) != 0) b |= ControllerPoller.DPadLeft;
        if ((t & ORight) != 0) b |= ControllerPoller.DPadRight;

        // The left axes carry the stick, or the left pad while a finger is on it
        short lx = 0, ly = 0;
        bool finger = (t & OLeftFinger) != 0, both = (t & OPadAndStick) != 0;
        if (!finger)
        {
            lx = leftX;
            ly = leftY;
            // With the pad untouched, older firmware reports a stick click as a pad click
            if (!both && (t & OLeftPadClick) != 0) b |= ControllerPoller.LeftThumb;
        }
        else
        {
            if (both && previous is { } prev) { lx = prev.LX; ly = prev.LY; } // stick held too: keep where it was
            // Clicking the left pad in a direction works as the D-pad
            const ushort dpad = ControllerPoller.DPadUp | ControllerPoller.DPadDown | ControllerPoller.DPadLeft | ControllerPoller.DPadRight;
            if ((t & OLeftPadClick) != 0 && (b & dpad) == 0)
            {
                if (Math.Abs((int)leftY) >= Math.Abs((int)leftX)) b |= leftY > 0 ? ControllerPoller.DPadUp : ControllerPoller.DPadDown;
                else b |= leftX > 0 ? ControllerPoller.DPadRight : ControllerPoller.DPadLeft;
            }
        }
        // The right pad scrolls like a right stick, only while touched
        short ry = (t & ORightFinger) != 0 ? rightPadY : (short)0;
        return new PadState(b, l2, r2, lx, ly, 0, ry);
    }

    /// <summary>Where the report header starts: 1 after Windows' report id 0, 0 without it, −1 if it isn't one.</summary>
    private static int OriginalOffset(ReadOnlySpan<byte> r)
    {
        if (r.Length >= 5 && r[0] == 0 && r[1] == 1 && r[2] == 0) return 1;
        if (r.Length >= 4 && r[0] == 1 && r[1] == 0) return 0;
        return -1;
    }

    private static uint U32(ReadOnlySpan<byte> r, int i) => (uint)(r[i] | r[i + 1] << 8 | r[i + 2] << 16 | r[i + 3] << 24);
    private static short S16(ReadOnlySpan<byte> r, int i) => (short)(r[i] | r[i + 1] << 8);
    /// <summary>0…32767 to XInput's 0…255.</summary>
    private static byte Trigger(short v) => (byte)Math.Clamp(v / 128, 0, 255);
}

/// <summary>
/// Finds a connected Steam Controller and reads it on background threads; <see cref="TryGetState"/>
/// returns the latest state. A wireless dongle has one interface per controller slot, so every
/// controller interface is read and the state comes from whichever reported last.
/// </summary>
internal sealed class SteamControllerReader : IDisposable
{
    private const ushort ValveVendor = 0x28DE;
    private static readonly Dictionary<ushort, SteamControllerModel> Products = new()
    {
        [0x1101] = SteamControllerModel.Original, // early wired model
        [0x1102] = SteamControllerModel.Original, // wired
        [0x1142] = SteamControllerModel.Original, // wireless dongle
        [0x1302] = SteamControllerModel.Triton,   // wired
        [0x1303] = SteamControllerModel.Triton,   // Bluetooth
        [0x1304] = SteamControllerModel.Triton,   // wireless dongle
        [0x1305] = SteamControllerModel.Triton,   // wireless dongle
    };

    private sealed class Source
    {
        public required SafeFileHandle Handle;
        public required SteamControllerModel Model;
        public required ushort Product;
        public PadState? State;
        public bool Reported;
    }

    private readonly object _lock = new();
    private readonly List<Source> _sources = new();
    private Source? _active;
    private bool _loggedRefused;

    public bool Connected { get { lock (_lock) return _sources.Count > 0; } }

    /// <summary>The latest state, from the interface that reported last (false before any report).</summary>
    public bool TryGetState(out PadState state)
    {
        lock (_lock)
        {
            if (_active?.State is { } s) { state = s; return true; }
            state = default;
            return false;
        }
    }

    /// <summary>Looks for a Steam Controller and starts reading it. Slow (device enumeration): call off the UI thread.</summary>
    public bool TryConnect()
    {
        if (Connected) return true;
        foreach (var path in HidNative.DevicePaths())
        {
            ushort vendor, product;
            int length;
            ushort usagePage;
            using (var query = HidNative.Open(path, read: false))
            {
                if (query == null || !HidNative.TryGetIds(query, out vendor, out product)) continue;
                if (vendor != ValveVendor || !Products.ContainsKey(product)) continue;
                // Only the controller interface (vendor-defined page); its keyboard and mouse ones belong to Windows
                if (!HidNative.TryGetCaps(query, out length, out usagePage) || usagePage < 0xFF00 || length <= 0) continue;
            }
            var model = Products[product];

            var handle = HidNative.Open(path, read: true);
            if (handle == null)
            {
                if (!_loggedRefused) // once: scans repeat every few seconds
                    CrashReporter.Log($"[SteamControllerReader] Steam Controller ({model}) found but couldn't be opened (another program may be using it exclusively)");
                _loggedRefused = true;
                continue;
            }
            var source = new Source { Handle = handle, Model = model, Product = product };
            lock (_lock) _sources.Add(source);
            var thread = new Thread(() => ReadLoop(source, length)) { IsBackground = true, Name = "RHI Steam Controller" };
            thread.Start();
            CrashReporter.Log($"[SteamControllerReader] Steam Controller ({model}, product 0x{product:X4}) interface opened");
        }
        return Connected;
    }

    private void ReadLoop(Source source, int length)
    {
        var buffer = new byte[length];
        try
        {
            while (true)
            {
                if (!HidNative.ReadFile(source.Handle, buffer, (uint)buffer.Length, out var read, IntPtr.Zero) || read == 0)
                    break; // unplugged, Bluetooth dropped, or Dispose cancelled the read
                var span = buffer.AsSpan(0, (int)read);
                if (SteamControllerReport.IsDisconnect(source.Model, span))
                {
                    lock (_lock)
                    {
                        source.State = null;
                        if (_active == source) _active = null;
                    }
                    CrashReporter.Log("[SteamControllerReader] Wireless controller disconnected");
                    source.Reported = false;
                    continue;
                }
                PadState? previous;
                lock (_lock) previous = source.State;
                if (SteamControllerReport.Parse(source.Model, span, previous) is not { } s) continue;
                lock (_lock)
                {
                    source.State = s;
                    _active = source;
                }
                if (!source.Reported)
                {
                    source.Reported = true;
                    CrashReporter.Log($"[SteamControllerReader] Steam Controller ({source.Model}) connected");
                }
            }
        }
        catch (ObjectDisposedException) { /* closed by Dispose */ }
        Close(source);
    }

    private void Close(Source source)
    {
        bool removed, none;
        lock (_lock)
        {
            removed = _sources.Remove(source);
            if (_active == source) _active = null;
            none = _sources.Count == 0;
        }
        if (removed && source.Reported && none) CrashReporter.Log("[SteamControllerReader] Controller disconnected");
        CloseHandle(source.Handle);
    }

    private static void CloseHandle(SafeFileHandle h)
    {
        if (!h.IsInvalid && !h.IsClosed) HidNative.CancelIoEx(h, IntPtr.Zero);
        h.Dispose();
    }

    public void Dispose()
    {
        List<Source> all;
        lock (_lock)
        {
            all = new List<Source>(_sources);
            _sources.Clear();
            _active = null;
        }
        foreach (var s in all) CloseHandle(s.Handle);
    }
}
