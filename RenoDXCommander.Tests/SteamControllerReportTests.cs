using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Steam Controller HID reports → XInput-style state. Layouts as in SDL's hidapi Steam / Steam Triton drivers.</summary>
public class SteamControllerReportTests
{
    private static void Put16(byte[] r, int i, int v) { r[i] = (byte)v; r[i + 1] = (byte)(v >> 8); }
    private static void Put32(byte[] r, int i, uint v) { for (int k = 0; k < 4; k++) r[i + k] = (byte)(v >> (8 * k)); }

    // ── Steam Controller (2026, Triton) ──

    private static byte[] Triton(byte id, uint buttons, int l2 = 0, int r2 = 0, int lx = 0, int ly = 0, int rx = 0, int ry = 0)
    {
        var r = new byte[64];
        r[0] = id;
        Put32(r, 2, buttons);
        Put16(r, 6, l2); Put16(r, 8, r2);
        Put16(r, 10, lx); Put16(r, 12, ly); Put16(r, 14, rx); Put16(r, 16, ry);
        return r;
    }

    [Theory]
    [InlineData(SteamControllerReport.TritonState)]
    [InlineData(SteamControllerReport.TritonStateBle)]
    [InlineData(SteamControllerReport.TritonStateTimestamp)]
    public void Triton_StateReports(byte id)
    {
        var s = SteamControllerReport.Parse(SteamControllerModel.Triton,
            Triton(id, SteamControllerReport.TA | SteamControllerReport.TL | SteamControllerReport.TView, l2: 32767, ly: 32767, ry: -32767));
        Assert.NotNull(s);
        Assert.Equal(ControllerPoller.A | ControllerPoller.LeftShoulder | ControllerPoller.Start, s!.Value.Buttons);
        Assert.Equal(255, s.Value.LeftTrigger);
        Assert.Equal(0, s.Value.RightTrigger);
        Assert.True(s.Value.LY > 30000); // up is positive, as XInput
        Assert.True(s.Value.RY < -30000);
    }

    [Fact]
    public void Triton_ButtonsMapToXboxPositions()
    {
        uint all = SteamControllerReport.TB | SteamControllerReport.TX | SteamControllerReport.TY | SteamControllerReport.TR
                   | SteamControllerReport.TL3 | SteamControllerReport.TR3 | SteamControllerReport.TMenu
                   | SteamControllerReport.TUp | SteamControllerReport.TLeft;
        var b = SteamControllerReport.Parse(SteamControllerModel.Triton, Triton(SteamControllerReport.TritonState, all))!.Value.Buttons;
        Assert.Equal(ControllerPoller.B | ControllerPoller.X | ControllerPoller.Y | ControllerPoller.RightShoulder
                     | ControllerPoller.LeftThumb | ControllerPoller.RightThumb | ControllerPoller.BackBtn
                     | ControllerPoller.DPadUp | ControllerPoller.DPadLeft, b);
    }

    [Fact]
    public void Triton_OtherReports_AreIgnored()
    {
        Assert.Null(SteamControllerReport.Parse(SteamControllerModel.Triton, Triton(0x43, SteamControllerReport.TA))); // battery
        Assert.Null(SteamControllerReport.Parse(SteamControllerModel.Triton, new byte[] { SteamControllerReport.TritonState, 0, 1 }));
    }

    [Fact]
    public void Triton_WirelessDisconnect()
    {
        Assert.True(SteamControllerReport.IsDisconnect(SteamControllerModel.Triton, new byte[] { SteamControllerReport.TritonWireless, 1 }));
        Assert.False(SteamControllerReport.IsDisconnect(SteamControllerModel.Triton, new byte[] { SteamControllerReport.TritonWireless, 2 }));
    }

    // ── Original Steam Controller (2015) ──

    /// <summary>A state report as Windows reads it: report id 0, then version 1, type, length, payload.</summary>
    private static byte[] Original(uint buttons, byte l2 = 0, byte r2 = 0, int lx = 0, int ly = 0, int rpy = 0, byte type = 1)
    {
        var r = new byte[65];
        r[1] = 1; r[3] = type; r[4] = 60;
        const int p = 5;
        r[p + 4] = (byte)buttons; r[p + 5] = (byte)(buttons >> 8); r[p + 6] = (byte)(buttons >> 16);
        r[p + 7] = l2; r[p + 8] = r2;
        Put16(r, p + 12, lx); Put16(r, p + 14, ly); Put16(r, p + 18, rpy);
        return r;
    }

    [Fact]
    public void Original_State()
    {
        var s = SteamControllerReport.Parse(SteamControllerModel.Original,
            Original(SteamControllerReport.OA | SteamControllerReport.OLB | SteamControllerReport.OEscape, l2: 200, ly: 32767));
        Assert.NotNull(s);
        Assert.Equal(ControllerPoller.A | ControllerPoller.LeftShoulder | ControllerPoller.Start, s!.Value.Buttons);
        Assert.Equal(200, s.Value.LeftTrigger);
        Assert.True(s.Value.LY > 30000);
    }

    [Fact]
    public void Original_WithoutReportIdByte()
    {
        var r = Original(SteamControllerReport.OB)[1..];
        Assert.Equal(ControllerPoller.B, SteamControllerReport.Parse(SteamControllerModel.Original, r)!.Value.Buttons);
    }

    [Fact]
    public void Original_LeftPadTouched_IsNotTheStick()
    {
        var s = SteamControllerReport.Parse(SteamControllerModel.Original,
            Original(SteamControllerReport.OLeftFinger, lx: 20000, ly: 20000))!.Value;
        Assert.Equal(0, s.LX);
        Assert.Equal(0, s.LY);
    }

    [Fact]
    public void Original_LeftPadClick_IsTheDPad()
    {
        var s = SteamControllerReport.Parse(SteamControllerModel.Original,
            Original(SteamControllerReport.OLeftFinger | SteamControllerReport.OLeftPadClick, lx: 1000, ly: -25000))!.Value;
        Assert.Equal(ControllerPoller.DPadDown, s.Buttons);
    }

    [Fact]
    public void Original_RightPad_ScrollsOnlyWhileTouched()
    {
        Assert.Equal(0, SteamControllerReport.Parse(SteamControllerModel.Original, Original(0, rpy: 30000))!.Value.RY);
        Assert.Equal(30000, SteamControllerReport.Parse(SteamControllerModel.Original,
            Original(SteamControllerReport.ORightFinger, rpy: 30000))!.Value.RY);
    }

    [Fact]
    public void Original_OtherReports_AreIgnored_AndDisconnectIsSeen()
    {
        var wireless = Original(0, type: SteamControllerReport.OriginalWireless);
        wireless[5] = 1; // disconnect
        Assert.Null(SteamControllerReport.Parse(SteamControllerModel.Original, wireless));
        Assert.True(SteamControllerReport.IsDisconnect(SteamControllerModel.Original, wireless));
        Assert.Null(SteamControllerReport.Parse(SteamControllerModel.Original, new byte[] { 0, 2, 0, 1, 0 }));
    }
}
