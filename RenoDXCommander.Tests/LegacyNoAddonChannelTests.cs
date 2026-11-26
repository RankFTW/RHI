using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// The "Legacy No Addons..." ReShade channel: an older ReShade version without add-on support, stored as the
/// channel value "6.3.3 (No Addons)".
/// </summary>
public class LegacyNoAddonChannelTests
{
    [Fact]
    public void ChannelValue_IsTheVersionPlusTheSuffix()
    {
        Assert.Equal("6.3.3 (No Addons)", AuxInstallService.MakeLegacyNoAddonChannel("6.3.3"));
    }

    [Theory]
    [InlineData("6.3.3 (No Addons)", true)]
    [InlineData("6.3.3 (no addons)", true)]
    [InlineData("6.3.3", false)]
    [InlineData("Stable", false)]
    [InlineData("Nightly", false)]
    [InlineData("Custom", false)]
    [InlineData(" (No Addons)", false)] // no version in front
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLegacyNoAddonChannel(string? channel, bool expected)
        => Assert.Equal(expected, AuxInstallService.IsLegacyNoAddonChannel(channel));

    [Fact]
    public void ItIsALegacyVersion_SoItIsPinnedLikeOne()
    {
        // SetReShadeChannelOverride leaves legacy versions out of ReShade updates by this test
        Assert.True(AuxInstallService.IsLegacyVersion("6.3.3 (No Addons)"));
    }

    [Theory]
    [InlineData("6.3.3 (No Addons)", "6.3.3")]
    [InlineData("6.3.3", "6.3.3")]
    public void VersionNumber(string channel, string expected)
        => Assert.Equal(expected, AuxInstallService.LegacyVersionNumber(channel));

    [Theory]
    [InlineData("6.3.3", "https://reshade.me/downloads/ReShade_Setup_6.3.3_Addon.exe")]
    [InlineData("6.3.3 (No Addons)", "https://reshade.me/downloads/ReShade_Setup_6.3.3.exe")]
    public void DownloadUrl_HasAddonOnlyForTheAddonBuild(string channel, string expected)
        => Assert.Equal(expected, AuxInstallService.LegacyDownloadUrl(channel));

    [Fact]
    public void StagedFiles_AreInTheirOwnFolder_ApartFromTheAddonBuild()
    {
        var addon = AuxInstallService.GetStagedPathForChannel("6.3.3", use32Bit: false);
        var noAddon = AuxInstallService.GetStagedPathForChannel("6.3.3 (No Addons)", use32Bit: false);

        Assert.NotEqual(addon, noAddon);
        Assert.EndsWith(Path.Combine("LegacyReshade", "6.3.3 (No Addons)", "ReShade64.dll"), noAddon);
    }

    [Fact]
    public void InstalledRecord_NeverShowsAnUpdate()
    {
        // The file doesn't matter: a legacy channel on the record returns before any file is read
        var record = new AuxInstalledRecord
        {
            GameName = "Some Game",
            InstallPath = Path.GetTempPath(),
            AddonType = AuxInstallService.TypeReShadeNormal,
            InstalledAs = "dxgi.dll",
            Channel = "6.3.3 (No Addons)",
        };

        Assert.False(AuxInstallService.CheckReShadeUpdateLocal(record));
    }
}
