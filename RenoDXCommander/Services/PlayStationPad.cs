// PlayStationPad.cs -- native DualShock 4 / DualSense / DualSense Edge support for the controller
// feature (MainWindow.Controller.cs), over USB or Bluetooth, with nothing to install. Windows only offers
// XInput for Xbox pads, so PlayStation pads are read straight from their HID input reports and turned
// into the same state XInput gives (same button bits), so the rest of the controller code doesn't care
// which pad it is. Report layouts follow SDL's hidapi drivers (SDL_hidapi_ps4.c / SDL_hidapi_ps5.c).

using Microsoft.Win32.SafeHandles;

namespace RenoDXCommander.Services;

/// <summary>Which PlayStation pad, for the report layout and the button names in the hints.</summary>
public enum PlayStationModel { DualShock4, DualSense }

/// <summary>A pad's state in XInput terms: XInput button bits, triggers 0–255, sticks −32768…32767 with +Y up.</summary>
public readonly record struct PadState(ushort Buttons, byte LeftTrigger, byte RightTrigger, short LX, short LY, short RX, short RY);

/// <summary>Turns PlayStation HID input reports into <see cref="PadState"/>. Pure, so it's unit-tested.</summary>
public static class PlayStationReport
{
    /// <summary>
    /// The state in <paramref name="report"/> (byte 0 = report id), or null for a report that carries no
    /// buttons. <paramref name="usb"/>: the pad is on USB (its reports are 64 bytes), else Bluetooth.
    /// </summary>
    public static PadState? Parse(PlayStationModel model, bool usb, ReadOnlySpan<byte> report)
    {
        if (report.Length < 10) return null;
        byte id = report[0];
        if (model == PlayStationModel.DualShock4)
        {
            // USB and Bluetooth "basic" report 0x01: sticks at 1–4, buttons 5–7, triggers 8–9.
            // Bluetooth "full" report 0x11 (once Steam or a game turned it on): the same, 2 bytes later.
            if (id == 0x01) return Ds4Layout(report[1..]);
            if (id == 0x11 && report.Length >= 12) return Ds4Layout(report[3..]);
            return null;
        }

        // DualSense. USB report 0x01 and Bluetooth extended report 0x31 (1 byte later): sticks at 1–4,
        // triggers 5–6, a counter, then buttons. Bluetooth "simple" report 0x01 uses the DualShock 4 layout.
        if (id == 0x01) return usb ? DualSenseLayout(report[1..]) : Ds4Layout(report[1..]);
        if (id == 0x31 && report.Length >= 12) return DualSenseLayout(report[2..]);
        return null;
    }

    // d: LX LY RX RY [hat+face] [shoulders, share, options, sticks] [PS, touchpad] L2 R2
    private static PadState Ds4Layout(ReadOnlySpan<byte> d) =>
        Build(d[0], d[1], d[2], d[3], d[4], d[5], d[7], d[8]);

    // d: LX LY RX RY L2 R2 counter [hat+face] [shoulders, create, options, sticks] [PS, touchpad, mute]
    private static PadState DualSenseLayout(ReadOnlySpan<byte> d) =>
        Build(d[0], d[1], d[2], d[3], d[7], d[8], d[4], d[5]);

    private static PadState Build(byte lx, byte ly, byte rx, byte ry, byte hatFace, byte misc, byte l2, byte r2)
    {
        ushort b = 0;
        // D-pad: a hat, 0 = up, clockwise to 7 = up-left, 8 = released
        switch (hatFace & 0x0F)
        {
            case 0: b |= ControllerPoller.DPadUp; break;
            case 1: b |= ControllerPoller.DPadUp | ControllerPoller.DPadRight; break;
            case 2: b |= ControllerPoller.DPadRight; break;
            case 3: b |= ControllerPoller.DPadDown | ControllerPoller.DPadRight; break;
            case 4: b |= ControllerPoller.DPadDown; break;
            case 5: b |= ControllerPoller.DPadDown | ControllerPoller.DPadLeft; break;
            case 6: b |= ControllerPoller.DPadLeft; break;
            case 7: b |= ControllerPoller.DPadUp | ControllerPoller.DPadLeft; break;
        }
        // Face buttons in Xbox positions: Cross = A, Circle = B, Square = X, Triangle = Y
        if ((hatFace & 0x10) != 0) b |= ControllerPoller.X;
        if ((hatFace & 0x20) != 0) b |= ControllerPoller.A;
        if ((hatFace & 0x40) != 0) b |= ControllerPoller.B;
        if ((hatFace & 0x80) != 0) b |= ControllerPoller.Y;
        // L1 R1 (L2 R2 digital bits skipped: the analogue values are used) Share/Create Options L3 R3
        if ((misc & 0x01) != 0) b |= ControllerPoller.LeftShoulder;
        if ((misc & 0x02) != 0) b |= ControllerPoller.RightShoulder;
        if ((misc & 0x10) != 0) b |= ControllerPoller.BackBtn;
        if ((misc & 0x20) != 0) b |= ControllerPoller.Start;
        if ((misc & 0x40) != 0) b |= ControllerPoller.LeftThumb;
        if ((misc & 0x80) != 0) b |= ControllerPoller.RightThumb;
        return new PadState(b, l2, r2, Axis(lx), (short)-Axis(ly), Axis(rx), (short)-Axis(ry)); // PlayStation: 0 = up
    }

    /// <summary>0…255 (128 = centre) to XInput's −32768…32767.</summary>
    private static short Axis(byte v) => (short)Math.Clamp((v - 128) * 256, short.MinValue + 1, short.MaxValue);
}

/// <summary>
/// Finds a connected PlayStation pad and reads it on a background thread; <see cref="TryGetState"/>
/// returns the latest state. Opened shared, read-only, so Steam, DS4Windows and games keep working.
/// </summary>
internal sealed class PlayStationPadReader : IDisposable
{
    private const ushort SonyVendor = 0x054C;
    private static readonly Dictionary<ushort, PlayStationModel> Products = new()
    {
        [0x05C4] = PlayStationModel.DualShock4, // DualShock 4 (first model)
        [0x09CC] = PlayStationModel.DualShock4, // DualShock 4 (second model)
        [0x0BA0] = PlayStationModel.DualShock4, // DualShock 4 USB wireless adapter
        [0x0CE6] = PlayStationModel.DualSense,
        [0x0DF2] = PlayStationModel.DualSense,  // DualSense Edge
    };

    private readonly object _lock = new();
    private SafeFileHandle? _handle;
    private Thread? _thread;
    private PadState? _state;
    private volatile bool _connected;
    private bool _loggedRefused;

    public bool Connected => _connected;
    public PlayStationModel Model { get; private set; }

    /// <summary>The latest state while a pad is connected (null before its first report).</summary>
    public bool TryGetState(out PadState state)
    {
        lock (_lock)
        {
            state = _state ?? default;
            return _connected && _state != null;
        }
    }

    /// <summary>Looks for a PlayStation pad and starts reading it. Slow (device enumeration): call off the UI thread.</summary>
    public bool TryConnect()
    {
        if (_connected) return true;
        foreach (var path in HidNative.DevicePaths())
        {
            // Query-only handle first (no read access needed to read the ids), so other devices aren't opened for reading
            ushort vendor, product;
            using (var query = HidNative.Open(path, read: false))
                if (query == null || !HidNative.TryGetIds(query, out vendor, out product)) continue;
            if (vendor != SonyVendor || !Products.TryGetValue(product, out var model)) continue;

            var handle = HidNative.Open(path, read: true);
            if (handle == null)
            {
                if (!_loggedRefused) // once: scans repeat every few seconds
                    CrashReporter.Log($"[PlayStationPadReader] {model} found but couldn't be opened (another program may be using it exclusively)");
                _loggedRefused = true;
                continue;
            }
            if (HidNative.InputReportLength(handle) is not (> 0 and var length))
            {
                handle.Dispose();
                continue;
            }
            Start(handle, model, length);
            CrashReporter.Log($"[PlayStationPadReader] {model} connected ({(length == 64 ? "USB" : "Bluetooth")}, product 0x{product:X4})");
            return true;
        }
        return false;
    }

    private void Start(SafeFileHandle handle, PlayStationModel model, int reportLength)
    {
        _handle = handle;
        Model = model;
        _state = null;
        _connected = true;
        bool usb = reportLength == 64;
        _thread = new Thread(() => ReadLoop(handle, model, usb, reportLength)) { IsBackground = true, Name = "RHI PlayStation pad" };
        _thread.Start();
    }

    private void ReadLoop(SafeFileHandle handle, PlayStationModel model, bool usb, int length)
    {
        var buffer = new byte[length];
        try
        {
            while (_connected)
            {
                if (!HidNative.ReadFile(handle, buffer, (uint)buffer.Length, out var read, IntPtr.Zero) || read == 0)
                    break; // unplugged, Bluetooth dropped, or Dispose cancelled the read
                if (PlayStationReport.Parse(model, usb, buffer.AsSpan(0, (int)read)) is { } s)
                    lock (_lock) _state = s;
            }
        }
        catch (ObjectDisposedException) { /* closed by Dispose */ }
        if (_connected) CrashReporter.Log("[PlayStationPadReader] Controller disconnected");
        Disconnect();
    }

    private void Disconnect()
    {
        lock (_lock)
        {
            _connected = false;
            _state = null;
        }
        var h = _handle;
        _handle = null;
        if (h != null)
        {
            if (!h.IsInvalid && !h.IsClosed) HidNative.CancelIoEx(h, IntPtr.Zero);
            h.Dispose();
        }
    }

    public void Dispose() => Disconnect();
}
