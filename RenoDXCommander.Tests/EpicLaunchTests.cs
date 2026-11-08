using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class EpicLaunchTests
{
    [Fact]
    public void FullId_MatchesEpicShortcut()
    {
        // Alan Wake 2, from Epic's own desktop shortcut
        var uri = GameDetectionService.EpicLaunchUri(
            "c4763f236d08423eb47b4c3008779c84", "93f2a8c3547846eda966cb3c152a026e", "dc9d2e595d0e4650b35d659f90d41059");
        Assert.Equal("com.epicgames.launcher://apps/c4763f236d08423eb47b4c3008779c84%3A93f2a8c3547846eda966cb3c152a026e%3Adc9d2e595d0e4650b35d659f90d41059?action=launch&silent=true", uri);
    }

    [Fact]
    public void MissingIds_FallBackToAppName()
    {
        Assert.Equal("com.epicgames.launcher://apps/Fortnite?action=launch&silent=true",
            GameDetectionService.EpicLaunchUri(null, null, "Fortnite"));
        Assert.Equal("com.epicgames.launcher://apps/Fortnite?action=launch&silent=true",
            GameDetectionService.EpicLaunchUri("fn", null, "Fortnite"));
    }

    [Theory]
    [InlineData("\"C:\\Launcher.exe\" com.epicgames.launcher://apps/ns:item:app?action=uninstall", "ns", "item")]
    [InlineData("com.epicgames.launcher://apps/ns%3Aitem%3Aapp?action=uninstall", "ns", "item")]
    [InlineData("com.epicgames.launcher://apps/ns:app?action=uninstall", "ns", null)]
    [InlineData("something else", null, null)]
    public void ParsesUninstallLink(string cmd, string? ns, string? item)
    {
        var (n, i) = GameDetectionService.ParseEpicUriIds(cmd);
        Assert.Equal(ns, n);
        Assert.Equal(item, i);
    }
}
