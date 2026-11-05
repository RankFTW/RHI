using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class ControllerPollerTests
{
    private static List<PadAction> Poll(ControllerPoller p, ushort buttons, long now, short lx = 0, short ly = 0, short ry = 0)
        => p.Update(buttons, lx, ly, ry, now, out _);

    [Fact]
    public void Button_FiresOncePerPress()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        Assert.Equal(new[] { PadAction.Select }, Poll(p, ControllerPoller.A, 0));
        Assert.Empty(Poll(p, ControllerPoller.A, 1000));   // held: no repeat
        Assert.Empty(Poll(p, 0, 1100));                    // released
        Assert.Equal(new[] { PadAction.Select }, Poll(p, ControllerPoller.A, 1200));
    }

    [Fact]
    public void Mapping_MatchesTheButtonLayout()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        Assert.Equal(new[] { PadAction.Back }, Poll(p, ControllerPoller.B, 0)); Poll(p, 0, 1);
        Assert.Equal(new[] { PadAction.Launch }, Poll(p, ControllerPoller.Y, 2)); Poll(p, 0, 3);
        Assert.Equal(new[] { PadAction.GameMenu }, Poll(p, ControllerPoller.X, 4)); Poll(p, 0, 5);
        Assert.Equal(new[] { PadAction.PrevGame }, Poll(p, ControllerPoller.LeftShoulder, 6)); Poll(p, 0, 7);
        Assert.Equal(new[] { PadAction.NextGame }, Poll(p, ControllerPoller.RightShoulder, 8)); Poll(p, 0, 9);
        Assert.Equal(new[] { PadAction.Settings }, Poll(p, ControllerPoller.Start, 10)); Poll(p, 0, 11);
        Assert.Equal(new[] { PadAction.Filters }, Poll(p, ControllerPoller.BackBtn, 12));
    }

    [Fact]
    public void HeldWhenReset_IgnoredUntilReleased()
    {
        var p = new ControllerPoller();
        p.Reset(ControllerPoller.A | ControllerPoller.DPadDown, 0, 0); // pressed in a game, then alt-tab to RHI
        Assert.Empty(Poll(p, ControllerPoller.A | ControllerPoller.DPadDown, 0));
        Assert.Empty(Poll(p, ControllerPoller.A | ControllerPoller.DPadDown, 2000)); // no repeat either
        Assert.Empty(Poll(p, 0, 2100));
        Assert.Equal(new[] { PadAction.Select }, Poll(p, ControllerPoller.A, 2200));
    }

    [Fact]
    public void Direction_RepeatsAfterDelay()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        Assert.Equal(new[] { PadAction.Down }, Poll(p, ControllerPoller.DPadDown, 0));
        Assert.Empty(Poll(p, ControllerPoller.DPadDown, ControllerPoller.RepeatDelayMs - 1));
        Assert.Equal(new[] { PadAction.Down }, Poll(p, ControllerPoller.DPadDown, ControllerPoller.RepeatDelayMs));
        Assert.Empty(Poll(p, ControllerPoller.DPadDown, ControllerPoller.RepeatDelayMs + ControllerPoller.RepeatIntervalMs - 1));
        Assert.Equal(new[] { PadAction.Down }, Poll(p, ControllerPoller.DPadDown, ControllerPoller.RepeatDelayMs + ControllerPoller.RepeatIntervalMs));
    }

    [Fact]
    public void Stick_MovesPastThreshold_LargerAxisWins()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        Assert.Empty(Poll(p, 0, 0, lx: 9000));                                  // light touch
        Assert.Equal(new[] { PadAction.Right }, Poll(p, 0, 10, lx: 30000, ly: 12000));
        Assert.Empty(Poll(p, 0, 20));
        Assert.Equal(new[] { PadAction.Up }, Poll(p, 0, 30, lx: 5000, ly: 30000)); // XInput: +Y is up
    }

    [Fact]
    public void Triggers_AndStickClicks_ArePresses()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        Assert.Empty(Poll(p, ControllerPoller.WithTriggers(0, 60, 0), 0));            // resting finger
        Assert.Equal(new[] { PadAction.PageBack }, Poll(p, ControllerPoller.WithTriggers(0, 200, 0), 1));
        Assert.Empty(Poll(p, ControllerPoller.WithTriggers(0, 255, 0), 2));           // held: once
        Assert.Equal(new[] { PadAction.PageForward }, Poll(p, ControllerPoller.WithTriggers(0, 0, 255), 3));
        Poll(p, 0, 4);
        Assert.Equal(new[] { PadAction.CycleFilter }, Poll(p, ControllerPoller.LeftThumb, 5)); Poll(p, 0, 6);
        Assert.Equal(new[] { PadAction.Home }, Poll(p, ControllerPoller.RightThumb, 7));
    }

    [Fact]
    public void HeldTrigger_Repeats_OtherButtonsDont()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        ushort rt = ControllerPoller.WithTriggers(0, 0, 255);
        Assert.Equal(new[] { PadAction.PageForward }, Poll(p, rt, 0));
        Assert.Empty(Poll(p, rt, ControllerPoller.TriggerRepeatDelayMs - 1));
        Assert.Equal(new[] { PadAction.PageForward }, Poll(p, rt, ControllerPoller.TriggerRepeatDelayMs));
        Assert.Equal(new[] { PadAction.PageForward },
            Poll(p, rt, ControllerPoller.TriggerRepeatDelayMs + ControllerPoller.TriggerRepeatIntervalMs));
        Poll(p, 0, 5000);
        Assert.Equal(new[] { PadAction.Select }, Poll(p, ControllerPoller.A, 5001));
        Assert.Empty(Poll(p, ControllerPoller.A, 9000)); // A never repeats
    }

    [Fact]
    public void RightStick_Scrolls()
    {
        var p = new ControllerPoller();
        p.Reset(0, 0, 0);
        p.Update(0, 0, 0, -32767, 0, out var down);
        p.Update(0, 0, 0, 2000, 1, out var none);
        Assert.True(down > 0);
        Assert.Equal(0, none);
    }
}
