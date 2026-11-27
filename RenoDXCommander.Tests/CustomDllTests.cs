using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Custom DLLs (the user's own DLLs from Custom\DLLs\): install / remove with the .original sentinel, the update
/// pass only replacing RHI's own copies, never re-creating a deleted copy, one name per game, rename, and the name
/// counting as RHI's in the owners list. Everything runs in a temp folder.
/// </summary>
public class CustomDllTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-customdll-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly string _data;
    private readonly string _game;
    private readonly CustomDllService _svc;

    public CustomDllTests()
    {
        _folder = Path.Combine(_root, "DLLs");
        _data = Path.Combine(_root, "data");
        _game = Path.Combine(_root, "Game");
        Directory.CreateDirectory(_folder);
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_game);
        _svc = new CustomDllService(_folder, _data);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    /// <summary>A minimal PE DLL header (64-bit unless told otherwise) plus <paramref name="payload"/> bytes.</summary>
    private static byte[] FakeDll(int payload, bool is64 = true, bool isDll = true, byte fill = 0xAB)
    {
        var b = new byte[0x200 + payload];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(b, 0x3C);
        b[0x80] = (byte)'P'; b[0x81] = (byte)'E';
        BitConverter.GetBytes((ushort)(is64 ? 0x8664 : 0x014C)).CopyTo(b, 0x84);
        BitConverter.GetBytes((ushort)(isDll ? 0x2022 : 0x0022)).CopyTo(b, 0x80 + 22);
        for (int i = 0x200; i < b.Length; i++) b[i] = fill;
        return b;
    }

    private string PutSource(string name, byte[] bytes)
    {
        var p = Path.Combine(_folder, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private CustomDllGame Game(string name = "Test Game", string store = "Steam", string? path = null,
        bool running = false, bool hidden = false) => new(name, store, path ?? _game, false, running, hidden);

    // ── Sources ───────────────────────────────────────────────────────────────

    [Fact]
    public void ListSources_ReadsBitnessAndRejectsNonDlls()
    {
        PutSource("a64.dll", FakeDll(10));
        PutSource("b32.dll", FakeDll(10, is64: false));
        PutSource("exe.dll", FakeDll(10, isDll: false));
        File.WriteAllText(Path.Combine(_folder, "junk.dll"), "not a dll");

        var list = _svc.ListSources().ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.True(list["a64.dll"].Usable && list["a64.dll"].Is64Bit);
        Assert.True(list["b32.dll"].Usable && !list["b32.dll"].Is64Bit);
        Assert.False(list["exe.dll"].Usable);
        Assert.False(list["junk.dll"].Usable);
    }

    [Fact]
    public void Install_RefusesWrongBitnessAndRunningGame()
    {
        PutSource("b32.dll", FakeDll(10, is64: false));
        Assert.NotNull(_svc.Install(Game(), "b32.dll", "dxgi.dll"));
        PutSource("a64.dll", FakeDll(10));
        Assert.NotNull(_svc.Install(Game(running: true), "a64.dll", "dxgi.dll"));
        Assert.False(File.Exists(Path.Combine(_game, "dxgi.dll")));
    }

    // ── Install / remove ──────────────────────────────────────────────────────

    [Fact]
    public void InstallRemove_WithoutGameFile_UsesSentinel()
    {
        var src = PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));

        var dest = Path.Combine(_game, "dxgi.dll");
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dest));
        Assert.Equal(0, new FileInfo(dest + ".original").Length);
        Assert.Equal(new[] { "dxgi.dll" }, RhiInstallManifest.GetComponentFiles(_game, "CustomDll:SpecialK64.dll"));
        var rec = Assert.Single(_svc.GetRecords("Test Game", "Steam"));
        Assert.Equal("dxgi.dll", rec.InstalledAs);
        Assert.Equal(CustomDllService.Sha256Of(dest), rec.Sha256);

        // The mod's own settings file beside it must survive the removal
        File.WriteAllText(Path.Combine(_game, "dxgi.ini"), "[SpecialK]");

        Assert.Null(_svc.Remove(Game(), "SpecialK64.dll"));
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".original"));
        Assert.True(File.Exists(Path.Combine(_game, "dxgi.ini")));
        Assert.Empty(RhiInstallManifest.GetComponentFiles(_game, "CustomDll:SpecialK64.dll"));
        Assert.Empty(_svc.GetRecords("Test Game", "Steam"));
    }

    [Fact]
    public void InstallRemove_WithGameFile_RestoresIt()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        var dest = Path.Combine(_game, "version.dll");
        var gameBytes = FakeDll(7, fill: 0x11);
        File.WriteAllBytes(dest, gameBytes);

        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "version.dll"));
        Assert.Equal(gameBytes, File.ReadAllBytes(dest + ".original"));

        Assert.Null(_svc.Remove(Game(), "SpecialK64.dll"));
        Assert.Equal(gameBytes, File.ReadAllBytes(dest));
        Assert.False(File.Exists(dest + ".original"));
    }

    [Fact]
    public void Remove_LeavesHandReplacedFile()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        var dest = Path.Combine(_game, "dxgi.dll");
        var userBytes = FakeDll(333, fill: 0x22);
        File.WriteAllBytes(dest, userBytes);

        var msg = _svc.Remove(Game(), "SpecialK64.dll");
        Assert.NotNull(msg);
        Assert.StartsWith("Note:", msg);
        Assert.Equal(userBytes, File.ReadAllBytes(dest));
        Assert.Empty(_svc.GetRecords("Test Game", "Steam"));
    }

    [Fact]
    public void TwoDlls_CantShareTargetName()
    {
        PutSource("a.dll", FakeDll(10));
        PutSource("b.dll", FakeDll(20));
        Assert.Null(_svc.Install(Game(), "a.dll", "winmm.dll"));
        Assert.NotNull(_svc.Install(Game(), "b.dll", "winmm.dll"));
        // The same source only once per game
        Assert.NotNull(_svc.Install(Game(), "a.dll", "version.dll"));
        Assert.Null(_svc.Install(Game(), "b.dll", "version.dll"));
        Assert.Equal(2, _svc.GetRecords("Test Game", "Steam").Count);
    }

    [Fact]
    public void Install_RefusesNameHeldByAnotherComponent()
    {
        PutSource("a.dll", FakeDll(10));
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dxgi.dll"] = "ReShade" };
        Assert.NotNull(_svc.Install(Game(), "a.dll", "dxgi.dll", owners));
        Assert.False(File.Exists(Path.Combine(_game, "dxgi.dll")));
    }

    // ── Updates ───────────────────────────────────────────────────────────────

    [Fact]
    public void Redeploy_ReplacesOnlyRhisCopy()
    {
        var game2Dir = Path.Combine(_root, "Game2");
        Directory.CreateDirectory(game2Dir);
        PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        Assert.Null(_svc.Install(Game("Other", path: game2Dir), "SpecialK64.dll", "dxgi.dll"));

        // The user replaced the second game's copy by hand
        var handBytes = FakeDll(444, fill: 0x33);
        File.WriteAllBytes(Path.Combine(game2Dir, "dxgi.dll"), handBytes);

        // New version in the folder (same file name)
        var v2 = FakeDll(555, fill: 0x44);
        PutSource("SpecialK64.dll", v2);

        var result = _svc.CheckAndRedeploy(new[] { Game(), Game("Other", path: game2Dir) }, "test");
        Assert.Equal(1, result.Updated);
        Assert.Equal(v2, File.ReadAllBytes(Path.Combine(_game, "dxgi.dll")));
        Assert.Equal(handBytes, File.ReadAllBytes(Path.Combine(game2Dir, "dxgi.dll")));
        var rec = Assert.Single(_svc.GetRecords("Test Game", "Steam"));
        Assert.NotNull(rec.UpdatedUtc);
        var state = Assert.Single(_svc.GetStates("Other", "Steam"));
        Assert.Equal(CustomDllState.ChangedOutsideRhi, state.State);
    }

    [Fact]
    public void Redeploy_SkipsHiddenAndRunningGames()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        PutSource("SpecialK64.dll", FakeDll(200, fill: 0x55));

        Assert.Equal(0, _svc.CheckAndRedeploy(new[] { Game(hidden: true) }, "test").Updated);
        Assert.Equal(0, _svc.CheckAndRedeploy(new[] { Game(running: true) }, "test").Updated);
        Assert.Equal(1, _svc.CheckAndRedeploy(new[] { Game() }, "test").Updated);
    }

    [Fact]
    public void Redeploy_NeverRecreatesDeletedCopy()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        File.Delete(Path.Combine(_game, "dxgi.dll"));
        PutSource("SpecialK64.dll", FakeDll(200, fill: 0x66));

        var result = _svc.CheckAndRedeploy(new[] { Game() }, "test");
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Dropped);
        Assert.False(File.Exists(Path.Combine(_game, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_game, "dxgi.dll.original")));
        Assert.Empty(_svc.GetRecords("Test Game", "Steam"));
        Assert.Empty(RhiInstallManifest.GetComponentFiles(_game, "CustomDll:SpecialK64.dll"));
    }

    [Fact]
    public void State_SourceMissing()
    {
        var src = PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        File.Delete(src);
        Assert.Equal(CustomDllState.SourceMissing, Assert.Single(_svc.GetStates("Test Game", "Steam")).State);
        Assert.Equal(0, _svc.CheckAndRedeploy(new[] { Game() }, "test").Updated);
        Assert.True(File.Exists(Path.Combine(_game, "dxgi.dll")));
    }

    // ── Rename / folder move ──────────────────────────────────────────────────

    [Fact]
    public void Rename_MovesRecords()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game("Old Name"), "SpecialK64.dll", "dxgi.dll"));
        Assert.Equal(1, _svc.RenameRecords("Old Name", "New Name"));
        Assert.Empty(_svc.GetRecords("Old Name", "Steam"));
        Assert.Equal("dxgi.dll", Assert.Single(_svc.GetRecords("New Name", "Steam")).InstalledAs);
        // Removing under the new name works
        Assert.Null(_svc.Remove(Game("New Name"), "SpecialK64.dll"));
        Assert.False(File.Exists(Path.Combine(_game, "dxgi.dll")));
    }

    [Fact]
    public void Repoint_MovesInstallPath()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        var newDir = Path.Combine(_root, "Moved");
        Assert.Equal(1, _svc.RepointRecords("Test Game", _game, newDir));
        Assert.Equal(newDir, Assert.Single(_svc.GetRecords("Test Game", "Steam")).InstallPath);
    }

    // ── Other components see the name as RHI's ────────────────────────────────

    [Fact]
    public void InstalledName_IsInOwnersList()
    {
        PutSource("SpecialK64.dll", FakeDll(100));
        var card = new GameCardViewModel { GameName = "Test Game", InstallPath = _game };
        Assert.Null(UltimateAsiLoaderService.FixedRhiDllOwner(card, "dxgi.dll"));

        Assert.Null(_svc.Install(Game(), "SpecialK64.dll", "dxgi.dll"));
        Assert.Equal("Custom DLL (SpecialK64.dll)", UltimateAsiLoaderService.FixedRhiDllOwner(card, "DXGI.dll"));
        Assert.True(CustomDllService.IsCustomDllFile(_game, "dxgi.dll"));

        Assert.Null(_svc.Remove(Game(), "SpecialK64.dll"));
        Assert.Null(UltimateAsiLoaderService.FixedRhiDllOwner(card, "dxgi.dll"));
    }

    [Fact]
    public void TargetNames_LeaveOutBink()
    {
        var names = CustomDllService.TargetNames(is32Bit: false);
        Assert.Equal("dxgi.dll", names[0]);
        Assert.Contains("winhttp.dll", names);
        Assert.DoesNotContain(names, n => n.StartsWith("bink", StringComparison.OrdinalIgnoreCase));
    }
}
