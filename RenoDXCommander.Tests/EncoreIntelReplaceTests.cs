using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// RTX Encore replaces the game's own XeSS files in place: a failed replacement must leave the game's file
/// (and an earlier backup) where they were. Universal MFG's update over an old install with no sentinel
/// must not keep the mod as the game's "original".
/// </summary>
public class EncoreIntelReplaceTests : IDisposable
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-encoreintel-" + Guid.NewGuid().ToString("N"));
    private readonly string _staging;
    private readonly string _game;
    private readonly Rtx40MfgService _svc;

    public EncoreIntelReplaceTests()
    {
        _staging = Path.Combine(_root, "staging");
        _game    = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(_staging, "xess"));
        Directory.CreateDirectory(_game);
        File.WriteAllBytes(Path.Combine(_staging, Rtx40MfgService.StagedDllName), new byte[] { 5, 5, 5 });
        File.WriteAllText(Path.Combine(_staging, "staging_format.txt"), "2");
        _svc = new Rtx40MfgService(new HttpClient(), new NullReporter(), RenoDXCommander.Tests.TestEnvironment.NewETagCache(), _staging);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private static readonly byte[] GameXess = { 1, 1 };
    private static readonly byte[] OldBackup = { 2, 2 };
    private static readonly byte[] NewXess = { 3, 3, 3 };

    [Fact]
    public void Replace_Success_GameFileBecomesBackup()
    {
        var p = Path.Combine(_game, "libxess.dll");
        File.WriteAllBytes(p, GameXess);
        File.WriteAllBytes(p + ".encore-original", OldBackup);
        var staged = Path.Combine(_root, "staged.dll");
        File.WriteAllBytes(staged, NewXess);

        Assert.True(_svc.ReplaceGameIntelFile(p, staged));
        Assert.Equal(NewXess, File.ReadAllBytes(p));
        Assert.Equal(GameXess, File.ReadAllBytes(p + ".encore-original"));
        Assert.Equal(2, Directory.GetFiles(_game).Length); // no temp or old backup left
    }

    [Fact]
    public void Replace_Failure_LeavesGameFileAndBackup()
    {
        var p = Path.Combine(_game, "libxess.dll");
        File.WriteAllBytes(p, GameXess);
        File.WriteAllBytes(p + ".encore-original", OldBackup);
        var missingStaged = Path.Combine(_root, "missing.dll");

        Assert.False(_svc.ReplaceGameIntelFile(p, missingStaged));
        Assert.Equal(GameXess, File.ReadAllBytes(p));
        Assert.Equal(OldBackup, File.ReadAllBytes(p + ".encore-original"));
        Assert.Equal(2, Directory.GetFiles(_game).Length);
    }

    [Fact]
    public void MfgUpdate_OldInstallWithoutSentinel_UninstallRemovesMod()
    {
        // An install from before sentinels: RHI's older mod copy as winmm.dll, no winmm.dll.original.
        // Same product as the staged DLL (version info), so it's RHI's: no backup of it is kept.
        // Two system DLLs with the same ProductName stand in for an old and a new mod build.
        var sysA = Path.Combine(Environment.SystemDirectory, "version.dll");
        var sysB = Path.Combine(Environment.SystemDirectory, "winmm.dll");
        if (!File.Exists(sysA) || !File.Exists(sysB)) return;
        File.Copy(sysA, Path.Combine(_staging, Rtx40MfgService.StagedDllName), overwrite: true);
        var dll = Path.Combine(_game, "winmm.dll");
        File.Copy(sysB, dll);
        Assert.True(AuxInstallService.SameProductName(dll, Path.Combine(_staging, Rtx40MfgService.StagedDllName)));

        Assert.Equal(1, _svc.UpdateInstalledGames(new[] { (_game, "winmm.dll") }));
        Assert.Empty(File.ReadAllBytes(dll + ".original"));

        _svc.Uninstall(_game, "winmm.dll");
        Assert.False(File.Exists(dll));
        Assert.False(File.Exists(dll + ".original"));
    }

    [Fact]
    public void MfgUpdate_OtherFileUnderName_KeptAsOriginal()
    {
        // No sentinel and the file there isn't the mod (another mod's file took the name): it is backed up,
        // not claimed as RHI's, and comes back on uninstall
        var dll = Path.Combine(_game, "winmm.dll");
        File.WriteAllBytes(dll, new byte[] { 4, 4 });

        Assert.Equal(1, _svc.UpdateInstalledGames(new[] { (_game, "winmm.dll") }));
        Assert.Equal(new byte[] { 4, 4 }, File.ReadAllBytes(dll + ".original"));

        _svc.Uninstall(_game, "winmm.dll");
        Assert.Equal(new byte[] { 4, 4 }, File.ReadAllBytes(dll));
        Assert.False(File.Exists(dll + ".original"));
    }
    // ── Keeping the game's own Intel files (issue #184: launchers that check file hashes) ──

    [Fact]
    public void Restore_PutsGameFileBack()
    {
        var p = Path.Combine(_game, "libxell.dll");
        var staged = Path.Combine(_root, "staged.dll");
        File.WriteAllBytes(staged, NewXess);
        File.WriteAllBytes(p, NewXess);                       // RHI's replacement
        File.WriteAllBytes(p + ".encore-original", GameXess); // the game's file

        Assert.True(_svc.RestoreGameIntelFile(p, staged));
        Assert.Equal(GameXess, File.ReadAllBytes(p));
        Assert.False(File.Exists(p + ".encore-original"));
    }

    [Fact]
    public void Restore_GameUpdatedFile_KeptAndBackupDropped()
    {
        var p = Path.Combine(_game, "libxell.dll");
        var staged = Path.Combine(_root, "staged.dll");
        File.WriteAllBytes(staged, NewXess);
        File.WriteAllBytes(p, new byte[] { 9, 9 });           // a game update put its new file there
        File.WriteAllBytes(p + ".encore-original", GameXess);

        Assert.True(_svc.RestoreGameIntelFile(p, staged));
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(p));
        Assert.False(File.Exists(p + ".encore-original"));
    }

    private string WriteMarker(params string[] lines)
    {
        var dir = Path.Combine(_game, Rtx40MfgService.EncoreRuntimeRelDir);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "rhi_installed.txt"), lines.Prepend("license.txt"));
        return _game;
    }

    [Fact]
    public void Needs_Keep_OnlyWhenAReplacedFileStillHasItsBackup()
    {
        var p = Path.Combine(_game, "libxell.dll");
        File.WriteAllBytes(p, GameXess);
        WriteMarker("native:libxell.dll");
        Assert.False(Rtx40MfgService.NeedsEncoreIntelFiles(_game, _game, keepGameFiles: true));

        File.WriteAllBytes(p + ".encore-original", OldBackup);
        Assert.True(Rtx40MfgService.NeedsEncoreIntelFiles(_game, _game, keepGameFiles: true));
    }

    [Fact]
    public void Needs_KeptFiles_ReplacedOnlyWhenTheChoiceIsReplace()
    {
        File.WriteAllBytes(Path.Combine(_game, "libxell.dll"), GameXess); // the game's older copy, wrong size for 3.0.2
        WriteMarker("kept:libxell.dll");
        Assert.False(Rtx40MfgService.NeedsEncoreIntelFiles(_game, _game, keepGameFiles: true));
        Assert.True(Rtx40MfgService.NeedsEncoreIntelFiles(_game, _game, keepGameFiles: false));
    }

    [Fact]
    public void State_KeptFilesCountAsSetUp()
    {
        WriteMarker(@"kept:Engine\libxell.dll", "native:libxess.dll", "libxess_fg.dll");
        File.WriteAllBytes(Path.Combine(_game, Rtx40MfgService.EncoreRuntimeRelDir, "libxess_fg.dll"), NewXess);
        var (complete, inGame, kept) = Rtx40MfgService.GetEncoreIntelState(_game);
        Assert.True(complete);
        Assert.Equal(1, inGame);
        Assert.Equal(new[] { "libxell.dll" }, kept);
    }
}
