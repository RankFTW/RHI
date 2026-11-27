using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>The "seen mods" list behind the new-mod badges (wiki, Ultra+, Luma).</summary>
public sealed class SeenModsStoreTests : IDisposable
{
    private sealed class TestStore(string dir) : SeenModsStore("seen_test_mods.json", dir);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-seen-tests-" + Guid.NewGuid().ToString("N"));

    public SeenModsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder: left for the OS to clean */ }
    }

    [Fact]
    public void FirstRun_SeedMarksEverythingSeen()
    {
        var store = new TestStore(_dir);
        store.SeedIfEmpty(new[] { "Game A", "Game B" });
        Assert.Empty(store.GetNewMods(new[] { "Game A", "Game B" }));
        Assert.Equal(new[] { "Game C" }, store.GetNewMods(new[] { "Game A", "Game C" }));
    }

    [Fact]
    public void Seed_DoesNothingOnceTheListHasNames()
    {
        var store = new TestStore(_dir);
        store.MarkAsSeen(new[] { "Game A" });
        store.SeedIfEmpty(new[] { "Game B" });
        Assert.Equal(new[] { "Game B" }, store.GetNewMods(new[] { "Game A", "Game B" }));
    }

    [Fact]
    public void Mark_SurvivesAReload()
    {
        new TestStore(_dir).MarkAsSeen(new[] { "Game A" });
        var reloaded = new TestStore(_dir);
        Assert.Contains("Game A", reloaded.GetSeenMods());
        Assert.Empty(reloaded.GetNewMods(new[] { "Game A" }));
    }

    [Fact]
    public void CorruptFile_LoadsAsEmptyWithoutThrowing()
    {
        File.WriteAllText(Path.Combine(_dir, "seen_test_mods.json"), "{ not json");
        var store = new TestStore(_dir);
        Assert.Empty(store.GetSeenMods());
        Assert.Equal(new[] { "Game A" }, store.GetNewMods(new[] { "Game A" }));
    }

    [Fact]
    public void Names_MatchWithoutRegardToCase()
    {
        var store = new TestStore(_dir);
        store.MarkAsSeen(new[] { "Lies of P" });
        Assert.Empty(store.GetNewMods(new[] { "LIES OF P" }));
    }
}
