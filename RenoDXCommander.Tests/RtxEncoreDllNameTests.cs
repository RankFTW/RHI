using RenoDXCommander.Models;
using RenoDXCommander.Services;
using System.Text.Json;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>The manifest's rtxEncoreDllNames: reading, case-insensitive keys and name validation.</summary>
public class RtxEncoreDllNameTests
{
    [Theory]
    [InlineData("winmm.dll", "winmm.dll")]
    [InlineData("WinMM.DLL", "winmm.dll")]
    [InlineData(" winmm.dll ", "winmm.dll")]
    [InlineData("rtx-encore.asi", "rtx-encore.asi")]
    public void ToKnownProxyName_KnownName_ReturnsListSpelling(string value, string expected)
        => Assert.Equal(expected, RtxEncoreService.ToKnownProxyName(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("winmm")]
    [InlineData(@"..\winmm.dll")]
    [InlineData(@"C:\Windows\winmm.dll")]
    [InlineData("notarealproxy.dll")]
    public void ToKnownProxyName_UnknownOrUnsafe_ReturnsNull(string? value)
        => Assert.Null(RtxEncoreService.ToKnownProxyName(value));

    [Fact]
    public void Manifest_RtxEncoreDllNames_DeserializesAndIsCaseInsensitive()
    {
        var m = JsonSerializer.Deserialize<RemoteManifest>(
            "{\"rtxEncoreDllNames\":{\"Euro Truck Simulator 2\":\"winmm.dll\"}}")!;
        ManifestService.Normalize(m);
        Assert.Equal("winmm.dll", m.RtxEncoreDllNames!["euro truck simulator 2"]);
    }

    [Fact]
    public void Manifest_Ets2Entry_IsAKnownName()
    {
        // The shipped manifest must give ETS2 a name RHI accepts (RTX Encore rc.3 needs winmm.dll there)
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "manifest.json"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var m = JsonSerializer.Deserialize<RemoteManifest>(File.ReadAllText(Path.Combine(dir!, "manifest.json")))!;
        ManifestService.Normalize(m);
        Assert.Equal("winmm.dll", RtxEncoreService.ToKnownProxyName(m.RtxEncoreDllNames?["Euro Truck Simulator 2"]));
    }
}
