// ControllerInput.cs -- reads an Xbox-style controller through XInput and turns it into actions for the
// Spotlight layout (MainWindow.Controller.cs). XInput is part of Windows 10/11 (xinput1_4.dll), so
// nothing is installed. PlayStation pads work when Steam Input or DS4Windows presents them as Xbox pads.

using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>What a controller press does in RHI.</summary>
public enum PadAction
{
    Up, Down, Left, Right,
    Select,     // A
    Back,       // B
    Launch,     // Y
    GameMenu,   // X
    PrevGame,   // LB
    NextGame,   // RB
    Settings,   // Menu (Start)
    Filters,    // View (Back)
    PageBack,   // LT: 10 games back
    PageForward,// RT: 10 games on
    CycleFilter,// left stick click: All / Installed / Favourites
    Home,       // right stick click: next collection, or back to the selected game's cover
}

/// <summary>
/// Turns controller state into actions: buttons fire once per press, directions (D-pad or left stick)
/// repeat while held. Pure logic, no XInput calls, so it's unit-tested.
/// </summary>
public sealed class ControllerPoller
{
    // XInput button bits
    public const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008;
    public const ushort Start = 0x0010, BackBtn = 0x0020, LeftShoulder = 0x0100, RightShoulder = 0x0200;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    public const ushort LeftThumb = 0x0040, RightThumb = 0x0080;
    // XInput doesn't use these two bits: the triggers are folded in as buttons (see WithTriggers)
    public const ushort LeftTrigger = 0x0400, RightTrigger = 0x0800;
    internal const byte TriggerThreshold = 100; // of 255: a resting finger doesn't press it

    internal const short StickThreshold = 16000;   // about half way: a light touch doesn't move
    internal const short ScrollDeadZone = 8000;
    internal const long RepeatDelayMs = 400;
    internal const long RepeatIntervalMs = 80;
    internal const double ScrollPixelsPerTick = 28;
    // Held triggers keep jumping (10 games at a time), a little slower than a held direction
    internal const long TriggerRepeatDelayMs = 350;
    internal const long TriggerRepeatIntervalMs = 110;
    private const ushort RepeatingButtons = LeftTrigger | RightTrigger;
    private readonly Dictionary<ushort, long> _buttonRepeatAt = new();

    private static readonly (ushort Bit, PadAction Action)[] Presses =
    {
        (A, PadAction.Select), (B, PadAction.Back), (Y, PadAction.Launch), (X, PadAction.GameMenu),
        (LeftShoulder, PadAction.PrevGame), (RightShoulder, PadAction.NextGame),
        (Start, PadAction.Settings), (BackBtn, PadAction.Filters),
        (LeftTrigger, PadAction.PageBack), (RightTrigger, PadAction.PageForward),
        (LeftThumb, PadAction.CycleFilter), (RightThumb, PadAction.Home),
    };

    /// <summary>The buttons with the triggers added as two more (pressed past about 40%).</summary>
    public static ushort WithTriggers(ushort buttons, byte leftTrigger, byte rightTrigger)
    {
        buttons &= unchecked((ushort)~(LeftTrigger | RightTrigger));
        if (leftTrigger >= TriggerThreshold) buttons |= LeftTrigger;
        if (rightTrigger >= TriggerThreshold) buttons |= RightTrigger;
        return buttons;
    }

    private ushort _prev;
    private ushort _blocked;          // held when RHI became active: ignored until released
    private PadAction? _dir;
    private bool _dirBlocked;
    private long _nextRepeat;

    /// <summary>
    /// Takes the controller's current state as the starting point: anything already held (a button
    /// pressed in a game just before switching back to RHI) does nothing until it's released.
    /// </summary>
    public void Reset(ushort buttons, short lx, short ly)
    {
        _prev = buttons;
        _blocked = buttons;
        _buttonRepeatAt.Clear();
        _dir = Direction(buttons, lx, ly);
        _dirBlocked = _dir != null;
        _nextRepeat = long.MaxValue;
    }

    /// <summary>Actions for this poll. <paramref name="scroll"/> is the right stick's vertical scroll in pixels (down = positive).</summary>
    public List<PadAction> Update(ushort buttons, short lx, short ly, short ry, long nowMs, out double scroll)
    {
        var actions = new List<PadAction>();
        _blocked &= buttons; // released buttons work again

        foreach (var (bit, action) in Presses)
        {
            bool down = (buttons & bit) != 0, wasDown = (_prev & bit) != 0;
            if (!down || (_blocked & bit) != 0) continue;
            if (!wasDown)
            {
                actions.Add(action);
                if ((bit & RepeatingButtons) != 0) _buttonRepeatAt[bit] = nowMs + TriggerRepeatDelayMs;
            }
            else if ((bit & RepeatingButtons) != 0 && _buttonRepeatAt.TryGetValue(bit, out var at) && nowMs >= at)
            {
                actions.Add(action);
                _buttonRepeatAt[bit] = nowMs + TriggerRepeatIntervalMs;
            }
        }
        _prev = buttons;

        var dir = Direction(buttons, lx, ly);
        if (dir == null)
        {
            _dir = null;
            _dirBlocked = false;
        }
        else if (dir != _dir)
        {
            // A new direction (or a change of direction) moves straight away
            _dir = dir;
            _dirBlocked = false;
            actions.Add(dir.Value);
            _nextRepeat = nowMs + RepeatDelayMs;
        }
        else if (!_dirBlocked && nowMs >= _nextRepeat)
        {
            actions.Add(dir.Value);
            _nextRepeat = nowMs + RepeatIntervalMs;
        }

        scroll = Math.Abs((int)ry) > ScrollDeadZone ? -ry / 32767.0 * ScrollPixelsPerTick : 0;
        return actions;
    }

    /// <summary>The D-pad wins over the stick; on the stick the larger axis wins.</summary>
    internal static PadAction? Direction(ushort buttons, short lx, short ly)
    {
        if ((buttons & DPadUp) != 0) return PadAction.Up;
        if ((buttons & DPadDown) != 0) return PadAction.Down;
        if ((buttons & DPadLeft) != 0) return PadAction.Left;
        if ((buttons & DPadRight) != 0) return PadAction.Right;
        int ax = Math.Abs((int)lx), ay = Math.Abs((int)ly);
        if (Math.Max(ax, ay) < StickThreshold) return null;
        if (ay >= ax) return ly > 0 ? PadAction.Up : PadAction.Down; // XInput: positive Y is up
        return lx > 0 ? PadAction.Right : PadAction.Left;
    }
}

/// <summary>XInput calls. Slots without a controller are slow to query, so callers rescan them rarely.</summary>
internal static class XInput
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    private const uint ErrorSuccess = 0;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out State state);

    /// <summary>The controller's state, or false when nothing is plugged into that slot.</summary>
    internal static bool TryGetState(int slot, out State state) => XInputGetState((uint)slot, out state) == ErrorSuccess;
}
