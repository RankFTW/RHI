using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// A picked install folder ("picked|original" in FolderOverrides) and where Reset install folder goes back to.
/// A store scan corrects the saved original to the folder the store reports: pick, reset, pick again used to
/// copy an earlier picked folder forward as the "original" (Phantom Blade Zero, Nov 2026).
/// </summary>
public class FolderOverrideTests
{
    // ApplyFolderOverrides uses none of the constructor's services
    private static GameNameService NewService() => new(null!, null!, null!, null!);

    private const string Root = @"E:\SteamLibrary\steamapps\common\PBZ";
    private const string Binaries = @"E:\SteamLibrary\steamapps\common\PBZ\ProjectPBZ\Binaries\Win64";
    private const string Platform = @"E:\SteamLibrary\steamapps\common\PBZ\platform\steam";

    [Fact]
    public void StoreScan_CorrectsWrongOriginal()
    {
        var svc = NewService();
        svc.FolderOverrides["Phantom Blade Zero|Steam"] = $"{Binaries}|{Platform}";
        var games = new List<DetectedGame> { new() { Name = "Phantom Blade Zero", InstallPath = Root, Source = "Steam" } };

        Assert.Equal(1, svc.ApplyFolderOverrides(games, fromStoreScan: true));
        Assert.Equal(Binaries, games[0].InstallPath);                                   // the picked folder still wins
        Assert.Equal($"{Binaries}|{Root}", svc.FolderOverrides["Phantom Blade Zero|Steam"]);
    }

    [Fact]
    public void CachedList_LeavesOriginalAlone()
    {
        var svc = NewService();
        svc.FolderOverrides["Phantom Blade Zero|Steam"] = $"{Binaries}|{Platform}";
        // Saved library entries can hold any old path: never treat them as the store's folder
        var games = new List<DetectedGame> { new() { Name = "Phantom Blade Zero", InstallPath = Root, Source = "Steam" } };

        Assert.Equal(0, svc.ApplyFolderOverrides(games));
        Assert.Equal(Binaries, games[0].InstallPath);
        Assert.Equal($"{Binaries}|{Platform}", svc.FolderOverrides["Phantom Blade Zero|Steam"]);
    }

    [Theory]
    [InlineData(Binaries)]               // the scan already returned the picked folder: nothing to learn
    [InlineData(Root)]                   // original already right
    [InlineData(Root + @"\")]            // same folder, trailing slash
    public void StoreScan_NoChangeWhenNothingToCorrect(string scanned)
    {
        var svc = NewService();
        svc.FolderOverrides["Phantom Blade Zero|Steam"] = $"{Binaries}|{Root}";
        var games = new List<DetectedGame> { new() { Name = "Phantom Blade Zero", InstallPath = scanned, Source = "Steam" } };

        Assert.Equal(0, svc.ApplyFolderOverrides(games, fromStoreScan: true));
        Assert.Equal(Binaries, games[0].InstallPath);
        Assert.Equal($"{Binaries}|{Root}", svc.FolderOverrides["Phantom Blade Zero|Steam"]);
    }

    [Fact]
    public void LegacyNameOnlyKey_CorrectedInPlace()
    {
        var svc = NewService();
        svc.FolderOverrides["Phantom Blade Zero"] = Binaries;                          // old entry, no original
        var games = new List<DetectedGame> { new() { Name = "Phantom Blade Zero", InstallPath = Root, Source = "Steam" } };

        Assert.Equal(1, svc.ApplyFolderOverrides(games, fromStoreScan: true));
        Assert.Equal($"{Binaries}|{Root}", svc.FolderOverrides["Phantom Blade Zero"]);
        Assert.False(svc.FolderOverrides.ContainsKey("Phantom Blade Zero|Steam"));
    }
}
