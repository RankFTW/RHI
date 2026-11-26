using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Standalone DLSS Enabler removal only touches a file that is DLSS Enabler (version info), and
/// follows the sentinel pattern for what it replaced.
/// </summary>
public class DlssEnablerSafetyTests : IDisposable
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-de-" + Guid.NewGuid().ToString("N"));

    public DlssEnablerSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private static DlssEnablerService NewService()
        => new(new HttpClient(), new NullReporter(), null!, null!, RenoDXCommander.Tests.TestEnvironment.NewETagCache());

    [Fact]
    public void IsDlssEnablerDll_OtherFiles_False()
    {
        var plain = Path.Combine(_dir, "version.dll");
        File.WriteAllBytes(plain, new byte[] { 1, 2, 3 });
        Assert.False(DlssEnablerService.IsDlssEnablerDll(plain));
        Assert.False(DlssEnablerService.IsDlssEnablerDll(Path.Combine(_dir, "missing.dll")));
        var system = Path.Combine(Environment.SystemDirectory, "version.dll");
        if (File.Exists(system)) Assert.False(DlssEnablerService.IsDlssEnablerDll(system));
    }

    [Fact]
    public void Remove_FileThatIsNotDlssEnabler_Kept()
    {
        // A game update or another mod replaced the file since: neither it nor the backup is touched
        var dll = Path.Combine(_dir, "winhttp.dll");
        File.WriteAllBytes(dll, new byte[] { 9, 9 });
        File.WriteAllBytes(dll + ".original", new byte[] { 8 });

        NewService().RemoveStandaloneFile(_dir, "winhttp.dll", "Test");
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(dll));
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(dll + ".original"));
    }

    [Fact]
    public void Remove_FileGone_SentinelCleanedUp()
    {
        var dll = Path.Combine(_dir, "version.dll");
        File.WriteAllBytes(dll + ".original", Array.Empty<byte>());
        NewService().RemoveStandaloneFile(_dir, "version.dll", "Test");
        Assert.False(File.Exists(dll + ".original"));
    }

    [Fact]
    public void Remove_ForeignFile_ReportsKept()
    {
        var dll = Path.Combine(_dir, "winmm.dll");
        File.WriteAllBytes(dll, new byte[] { 9, 9 });
        Assert.False(NewService().RemoveStandaloneFile(_dir, "winmm.dll", "Test"));
        Assert.False(NewService().UninstallStandalone(_dir, "winmm.dll"));
        Assert.True(File.Exists(dll));
    }

    [Fact]
    public void Remove_NothingThere_ReportsDone()
    {
        Assert.True(NewService().RemoveStandaloneFile(_dir, "version.dll", "Test"));
    }

    [Fact]
    public void MatchesFile_SameBuildOnly()
    {
        var a = Path.Combine(_dir, "a.dll");
        var b = Path.Combine(_dir, "b.dll");
        var c = Path.Combine(_dir, "c.dll");
        File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(b, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(c, new byte[] { 1, 2, 4 });
        Assert.True(DlssEnablerService.MatchesFile(a, b));   // no version info: bytes compared
        Assert.False(DlssEnablerService.MatchesFile(a, c));
        Assert.False(DlssEnablerService.MatchesFile(a, Path.Combine(_dir, "missing.dll")));
    }
}
