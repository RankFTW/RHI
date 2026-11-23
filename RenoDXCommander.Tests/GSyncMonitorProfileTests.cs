using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Per-monitor G-SYNC lives in a driver profile named after the monitor's EDID. The expected names are the
/// profiles the NVIDIA driver / app use for two real monitors (Nov 2026).
/// </summary>
public class GSyncMonitorProfileTests
{
    private static byte[] Edid(byte m0, byte m1, byte p0, byte p1)
    {
        var e = new byte[128];
        e[1] = e[2] = e[3] = e[4] = e[5] = e[6] = 0xFF;   // EDID header
        e[8] = m0; e[9] = m1; e[10] = p0; e[11] = p1;
        return e;
    }

    [Fact]
    public void Msi_monitor_matches_the_profile_the_nvidia_app_created()
        => Assert.Equal("0x6936:0x3DB2", GSyncMonitorService.ProfileNameFromEdid(Edid(0x36, 0x69, 0xB2, 0x3D)));

    [Fact]
    public void Validated_monitor_matches_its_predefined_profile()
        => Assert.Equal("0xC40D:0x3DE2", GSyncMonitorService.ProfileNameFromEdid(Edid(0x0D, 0xC4, 0xE2, 0x3D)));

    [Fact]
    public void Too_short_edid_gives_no_name()
        => Assert.Null(GSyncMonitorService.ProfileNameFromEdid(new byte[10]));
}
