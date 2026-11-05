using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>PlayStation HID reports → XInput-style state. Layouts as in SDL's hidapi PS4 / PS5 drivers.</summary>
public class PlayStationReportTests
{
    // Pressed: Cross, L1, Options; D-pad released (8); left stick full up; right stick full down; L2 at 200
    private const byte HatReleasedCross = 0x08 | 0x20, L1Options = 0x01 | 0x20;
    private const ushort Expected = ControllerPoller.A | ControllerPoller.LeftShoulder | ControllerPoller.Start;

    private static byte[] Report(int length, params byte[] start)
    {
        var r = new byte[length];
        start.CopyTo(r, 0);
        return r;
    }

    private static void AssertCommon(PadState? s)
    {
        Assert.NotNull(s);
        Assert.Equal(Expected, s!.Value.Buttons);
        Assert.Equal(200, s.Value.LeftTrigger);
        Assert.Equal(0, s.Value.RightTrigger);
        Assert.True(s.Value.LY > 30000);   // up is positive, as XInput
        Assert.True(s.Value.RY < -30000);
        Assert.Equal(0, s.Value.LX);
    }

    [Fact]
    public void DualShock4_Usb()
    {
        var r = Report(64, 0x01, 128, 0, 128, 255, HatReleasedCross, L1Options, 0, 200, 0);
        AssertCommon(PlayStationReport.Parse(PlayStationModel.DualShock4, usb: true, r));
    }

    [Fact]
    public void DualShock4_BluetoothFullReport_IsTwoBytesLater()
    {
        var r = Report(78, 0x11, 0xC0, 0x00, 128, 0, 128, 255, HatReleasedCross, L1Options, 0, 200, 0);
        AssertCommon(PlayStationReport.Parse(PlayStationModel.DualShock4, usb: false, r));
    }

    [Fact]
    public void DualShock4_BluetoothBasicReport()
    {
        var r = Report(78, 0x01, 128, 0, 128, 255, HatReleasedCross, L1Options, 0, 200, 0);
        AssertCommon(PlayStationReport.Parse(PlayStationModel.DualShock4, usb: false, r));
    }

    [Fact]
    public void DualSense_Usb()
    {
        // LX LY RX RY L2 R2 counter hat+face misc
        var r = Report(64, 0x01, 128, 0, 128, 255, 200, 0, 0x5A, HatReleasedCross, L1Options, 0);
        AssertCommon(PlayStationReport.Parse(PlayStationModel.DualSense, usb: true, r));
    }

    [Fact]
    public void DualSense_BluetoothExtendedReport_IsOneByteLater()
    {
        var r = Report(78, 0x31, 0x10, 128, 0, 128, 255, 200, 0, 0x5A, HatReleasedCross, L1Options, 0);
        AssertCommon(PlayStationReport.Parse(PlayStationModel.DualSense, usb: false, r));
    }

    [Fact]
    public void DualSense_BluetoothSimpleReport_UsesDualShock4Layout()
    {
        var r = Report(78, 0x01, 128, 0, 128, 255, HatReleasedCross, L1Options, 0, 200, 0);
        AssertCommon(PlayStationReport.Parse(PlayStationModel.DualSense, usb: false, r));
    }

    [Theory]
    [InlineData(0, ControllerPoller.DPadUp)]
    [InlineData(2, ControllerPoller.DPadRight)]
    [InlineData(3, ControllerPoller.DPadDown | ControllerPoller.DPadRight)]
    [InlineData(6, ControllerPoller.DPadLeft)]
    [InlineData(8, 0)]
    public void DPad_FromHat(byte hat, int expected)
    {
        var r = Report(64, 0x01, 128, 128, 128, 128, hat, 0, 0, 0, 0);
        Assert.Equal((ushort)expected, PlayStationReport.Parse(PlayStationModel.DualShock4, usb: true, r)!.Value.Buttons);
    }

    [Fact]
    public void FaceButtons_MapToXboxPositions()
    {
        // Square, Circle, Triangle (no Cross), sticks clicked, Share
        var r = Report(64, 0x01, 128, 128, 128, 128, 0x08 | 0x10 | 0x40 | 0x80, 0x10 | 0x40 | 0x80, 0, 0, 0);
        var b = PlayStationReport.Parse(PlayStationModel.DualShock4, usb: true, r)!.Value.Buttons;
        Assert.Equal(ControllerPoller.X | ControllerPoller.B | ControllerPoller.Y
                     | ControllerPoller.BackBtn | ControllerPoller.LeftThumb | ControllerPoller.RightThumb, b);
    }

    [Fact]
    public void OtherReports_AreIgnored()
    {
        Assert.Null(PlayStationReport.Parse(PlayStationModel.DualSense, usb: false, Report(78, 0x05)));
        Assert.Null(PlayStationReport.Parse(PlayStationModel.DualShock4, usb: true, Report(4, 0x01)));
    }
}
