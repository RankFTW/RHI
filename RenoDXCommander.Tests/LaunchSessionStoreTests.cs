using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Saved launches: survive a restart of RHI, and HDR / resolution are only put back by the last game out.</summary>
public class LaunchSessionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-launch-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "launch_sessions.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private static LaunchSession Session(string game, DateTime started, bool hdr = false, string? res = null) => new()
    {
        GameName = game, Store = "Steam", InstallPath = @"C:\Games\" + game, StartedUtc = started,
        HdrToggled = hdr, ResToggled = res != null, ResolutionToRestore = res,
    };

    [Fact]
    public void Session_IsReadBack_ByANewStore()
    {
        var s = Session("Portal 2", DateTime.UtcNow, hdr: true, res: "2560x1440@144");
        s.HdrTargets = new List<uint> { 4 };
        new LaunchSessionStore(FilePath).Add(s);

        var loaded = Assert.Single(new LaunchSessionStore(FilePath).GetAll());
        Assert.Equal(s.Id, loaded.Id);
        Assert.True(loaded.HdrToggled);
        Assert.Equal("2560x1440@144", loaded.ResolutionToRestore);
        Assert.Equal(new List<uint> { 4 }, loaded.HdrTargets);
    }

    [Fact]
    public void IsRunning_OnlyOnceTheGameWasSeen()
    {
        var store = new LaunchSessionStore(FilePath);
        var s = Session("Portal 2", DateTime.UtcNow);
        store.Add(s);
        Assert.False(store.IsRunning("portal 2", "Steam"));
        store.MarkSeen(s.Id);
        Assert.True(store.IsRunning("portal 2", "Steam"));
        Assert.False(store.IsRunning("Portal 2", "Epic"));
        Assert.True(new LaunchSessionStore(FilePath).IsRunning("Portal 2", "Steam"));
    }

    [Fact]
    public void Finish_LastGame_RevertsAndDeletesFile()
    {
        var store = new LaunchSessionStore(FilePath);
        var s = Session("Portal 2", DateTime.UtcNow, hdr: true, res: "2560x1440@144");
        store.Add(s);

        Assert.Equal((true, true), store.Finish(s.Id));
        Assert.Empty(store.GetAll());
        Assert.False(File.Exists(FilePath));
        Assert.Equal((false, false), store.Finish(s.Id));
    }

    [Fact]
    public void Finish_WhileAnotherGameStillNeedsThem_KeepsHdrAndResolution()
    {
        var store = new LaunchSessionStore(FilePath);
        var t = DateTime.UtcNow;
        var first = Session("A", t, hdr: true, res: "2560x1440@144");   // desktop resolution before any launch
        var second = Session("B", t.AddMinutes(1), hdr: true, res: "1920x1080@120"); // A's game resolution
        store.Add(first);
        store.Add(second);
        store.MarkSeen(first.Id);
        store.MarkSeen(second.Id);

        Assert.Equal((false, false), store.Finish(first.Id));
        // B now puts back the desktop resolution from before A started, not A's game resolution
        Assert.Equal("2560x1440@144", Assert.Single(store.GetAll()).ResolutionToRestore);
        Assert.Equal((true, true), store.Finish(second.Id));
    }

    [Fact]
    public void Finish_LaunchNeverSeenRunning_DoesNotKeepHdrOn()
    {
        // Valor Mortis (Game Pass) launched but its process was never found; AI Limit then closed
        var store = new LaunchSessionStore(FilePath);
        var t = DateTime.UtcNow;
        var pending = Session("Valor Mortis", t, hdr: true, res: "2560x1440@144");
        var played = Session("AI Limit", t.AddMinutes(2), hdr: true, res: "2560x1440@144");
        store.Add(pending);
        store.Add(played);
        store.MarkSeen(played.Id);

        Assert.Equal((true, true), store.Finish(played.Id));
    }

    [Fact]
    public void Finish_GameWithoutChanges_RevertsNothing()
    {
        var store = new LaunchSessionStore(FilePath);
        var s = Session("Portal 2", DateTime.UtcNow);
        store.Add(s);
        Assert.Equal((false, false), store.Finish(s.Id));
    }
}
