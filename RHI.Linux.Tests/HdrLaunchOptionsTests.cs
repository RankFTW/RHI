using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class HdrLaunchOptionsTests
{
    [Theory]
    [InlineData("PROTON_ENABLE_HDR=1 %command%", true)]
    [InlineData("PROTON_ENABLE_HDR='1' gamemoderun %command%", true)]
    [InlineData("", false)]
    [InlineData("%command% PROTON_ENABLE_HDR=1", false)]
    [InlineData("PROTON_ENABLE_HDR=0 %command%", false)]
    [InlineData("env PROTON_ENABLE_HDR=1 %command%", false)]
    public void ActiveStateRequiresUnambiguousSavedHdr(string options, bool enabled)
        => Assert.Equal(enabled, HdrLaunchOptions.IsEnabled(options));

    [Fact]
    public void DisablePreservesOtherFeaturesAndSubsequentEdits()
    {
        const string original = "PROTON_ENABLE_WAYLAND=1  ENABLE_HDR_WSI=1 WINEDLLOVERRIDES='dxgi=n,b' %command% -dx12";
        var enabled = HdrLaunchOptions.Merge(original, false, false);
        Assert.Equal(original + " -nosplash", HdrLaunchOptions.Disable(enabled + " -nosplash"));
        Assert.Equal(original, HdrLaunchOptions.Disable(original));
        Assert.Throws<FormatException>(() => HdrLaunchOptions.Disable("%command% PROTON_ENABLE_HDR=1"));
    }

    [Fact]
    public void FullPresetPreservesOtherFeaturesAndFormatting()
    {
        const string original = "MANGOHUD=1  WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b;dinput8=n' PROTON_ENABLE_NVAPI=1 DXVK_NVAPI_DRS_SETTINGS=0x10E41DF7=0x5 gamescope -f -- %command% -dx12 --name \"My Game\"";
        var merged = HdrLaunchOptions.Merge(original, true, true);
        Assert.Equal("PROTON_ENABLE_WAYLAND=1 PROTON_ENABLE_HDR=1 ENABLE_HDR_WSI=1 " + original, merged);
        Assert.Equal(merged, HdrLaunchOptions.Merge(merged, true, true));
    }

    [Theory]
    [InlineData("PROTON_ENABLE_HDR=1")]
    [InlineData("PROTON_ENABLE_HDR='1'")]
    [InlineData("PROTON_ENABLE_HDR=\"1\"")]
    public void ExistingEnabledValueIsKeptExactly(string assignment)
    {
        var original = assignment + " %command% --foo";
        Assert.Equal(original, HdrLaunchOptions.Merge(original, false, false));
    }

    [Fact]
    public void UnselectedVariablesAreNotRemovedOrChanged()
    {
        const string original = "PROTON_ENABLE_WAYLAND=0 ENABLE_HDR_WSI=0 %command%";
        Assert.Equal("PROTON_ENABLE_HDR=1 " + original, HdrLaunchOptions.Merge(original, false, false));
    }

    [Theory]
    [InlineData("", "PROTON_ENABLE_HDR=1 %command%")]
    [InlineData("-dx12 -nosplash", "PROTON_ENABLE_HDR=1 %command% -dx12 -nosplash")]
    [InlineData("gamemoderun %command% -dx12", "PROTON_ENABLE_HDR=1 gamemoderun %command% -dx12")]
    public void SupportsEmptyOptionsArgumentsAndWrappers(string original, string expected)
        => Assert.Equal(expected, HdrLaunchOptions.Merge(original, false, false));

    [Theory]
    [InlineData("PROTON_ENABLE_HDR=0 %command%")]
    [InlineData("PROTON_ENABLE_WAYLAND=0 %command%")]
    [InlineData("ENABLE_HDR_WSI=0 %command%")]
    [InlineData("PROTON_ENABLE_HDR=1 PROTON_ENABLE_HDR=1 %command%")]
    [InlineData("%command% PROTON_ENABLE_HDR=1")]
    [InlineData("gamescope -- env PROTON_ENABLE_HDR=1 %command%")]
    [InlineData("env -i %command%")]
    [InlineData("/usr/bin/env --ignore-environment %command%")]
    [InlineData("FOO=\"text PROTON_ENABLE_HDR=1\" %command%")]
    [InlineData("PROTON_ENABLE_HDR=\"$HDR\" %command%")]
    [InlineData("FOO=$(something) %command%")]
    [InlineData("%command%; echo finished")]
    [InlineData("%command% | tee log")]
    [InlineData("%command% > log")]
    [InlineData("%command%\nother-command")]
    [InlineData("FOO='unclosed %command%")]
    [InlineData("%command% %command%")]
    [InlineData("%command% \"%command%\"")]
    [InlineData("\"%command%\"")]
    [InlineData("some-launcher game.exe")]
    public void ConflictsAndAmbiguousSyntaxAreRefused(string original)
        => Assert.Throws<FormatException>(() => HdrLaunchOptions.Merge(original, true, true));

    [Fact]
    public void ReshadeAndDlssMergesCanRunBeforeAndAfterHdr()
    {
        var extras = new LaunchExtras(["dinput8"],
            new Dictionary<string, string?> { ["PROTON_ENABLE_NVAPI"] = "1" },
            new Dictionary<uint, uint?> { [0x10E41DF7] = 5 });
        var managed = Proton.LaunchOptions("MANGOHUD=1 %command% -dx12", "dxgi.dll", extras);
        var hdr = HdrLaunchOptions.Merge(managed, true, true);
        Assert.Equal(hdr, Proton.LaunchOptions(hdr, "dxgi.dll", extras));
        var hdrFirst = HdrLaunchOptions.Merge("MANGOHUD=1 %command% -dx12", true, true);
        var managedAfter = Proton.LaunchOptions(hdrFirst, "dxgi.dll", extras);
        Assert.Equal(managedAfter, HdrLaunchOptions.Merge(managedAfter, true, true));
        Assert.True(InstallationStatus.HasLaunchOverrides(managedAfter, "dxgi.dll", extras));
        Assert.EndsWith("MANGOHUD=1 %command% -dx12", managedAfter);
    }
}
