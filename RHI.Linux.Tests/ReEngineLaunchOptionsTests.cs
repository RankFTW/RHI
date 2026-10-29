using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class ReEngineLaunchOptionsTests
{
    [Theory]
    [InlineData("", "%command%")]
    [InlineData("-dx12", "%command% -dx12")]
    [InlineData("PROTON_ENABLE_HDR=1 WINEDLLOVERRIDES='dxgi=n,b;dinput8=n,b' gamescope -- %command% --name \"My Game\"", "PROTON_ENABLE_HDR=1 WINEDLLOVERRIDES='dxgi=n,b;dinput8=n,b' gamescope -- %command% --name \"My Game\"")]
    public void EnablePreservesOptionsAndDoesNotDuplicate(string original, string normalized)
    {
        var enabled = ReEngineLaunchOptions.SetEnabled(original, true);
        Assert.Equal(normalized + " " + ReEngineLaunchOptions.Argument, enabled);
        Assert.True(ReEngineLaunchOptions.IsEnabled(enabled));
        Assert.Equal(enabled, ReEngineLaunchOptions.SetEnabled(enabled, true));
        Assert.Equal(normalized, ReEngineLaunchOptions.SetEnabled(enabled, false));
    }

    [Theory]
    [InlineData("/WineDetectionEnabled:False")]
    [InlineData("'/WineDetectionEnabled:False'")]
    [InlineData("\"/WineDetectionEnabled:False\"")]
    public void RemovalPreservesLaterEditsAndHdr(string argument)
    {
        var original = "PROTON_ENABLE_HDR=1 %command% " + argument + " -dx12";
        Assert.True(ReEngineLaunchOptions.IsEnabled(original));
        Assert.Equal("PROTON_ENABLE_HDR=1 %command% -dx12", ReEngineLaunchOptions.SetEnabled(original, false));
    }

    [Theory]
    [InlineData("%command% /WineDetectionEnabled:True")]
    [InlineData("/WineDetectionEnabled:False %command%")]
    [InlineData("%command% /WineDetectionEnabled:False /WineDetectionEnabled:False")]
    [InlineData("FOO='/WineDetectionEnabled:False' %command%")]
    [InlineData("%command% --name 'text /WineDetectionEnabled:False'")]
    [InlineData("%command%; echo done")]
    [InlineData("%command%\nother")]
    [InlineData("%command% %command%")]
    [InlineData("%command% \"%command%\"")]
    [InlineData("\"%command%\"")]
    [InlineData("/path/to/launcher")]
    public void AmbiguousOptionsAreNotChanged(string options)
    {
        Assert.Throws<FormatException>(() => ReEngineLaunchOptions.SetEnabled(options, true));
        Assert.Throws<FormatException>(() => ReEngineLaunchOptions.SetEnabled(options, false));
        Assert.False(ReEngineLaunchOptions.IsEnabled(options));
    }

    [Fact]
    public void OtherFeatureMergesPreserveBypass()
    {
        var options = ReEngineLaunchOptions.SetEnabled("%command% -dx12", true);
        options = HdrLaunchOptions.Merge(options, true, true);
        options = Proton.LaunchOptions(options, "dxgi.dll", new LaunchExtras(["dinput8"], new Dictionary<string, string?>(), new Dictionary<uint, uint?>()));
        Assert.True(ReEngineLaunchOptions.IsEnabled(options));
        var disabled = ReEngineLaunchOptions.SetEnabled(options, false);
        Assert.True(HdrLaunchOptions.IsEnabled(disabled));
        Assert.Equal("1", Proton.ReadVariable(disabled, "PROTON_ENABLE_WAYLAND"));
        Assert.Contains("dinput8", disabled);
        Assert.DoesNotContain(ReEngineLaunchOptions.Argument, disabled);
    }
}
