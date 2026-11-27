using System.Security.Cryptography;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class ZeroHookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi_zerohook_" + Guid.NewGuid().ToString("N"));

    public ZeroHookTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("v1.1.0", "1.1.0")]
    [InlineData("v.1.0.6", "1.0.6")]
    [InlineData("1.2", "1.2")]
    [InlineData(" V2.0 ", "2.0")]
    public void NormaliseTag(string tag, string expected) => Assert.Equal(expected, ZeroHookService.NormaliseTag(tag));

    [Theory]
    [InlineData("v1.1.0", "1.0.6", true)]
    [InlineData("v.1.0.6", "1.1.0", false)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("2", "1.9", true)]
    [InlineData("1.1.0", null, true)]
    [InlineData(null, "1.1.0", false)]
    [InlineData("beta", "alpha", true)]
    [InlineData("beta", "BETA", false)]
    public void IsNewer(string? latest, string? installed, bool expected) => Assert.Equal(expected, ZeroHookService.IsNewer(latest, installed));

    [Fact]
    public void ParseSha256Digest()
    {
        var hex = new string('A', 64);
        Assert.Equal(new string('a', 64), ZeroHookService.ParseSha256Digest("sha256:" + hex));
        Assert.Null(ZeroHookService.ParseSha256Digest(null));
        Assert.Null(ZeroHookService.ParseSha256Digest("md5:abcd"));
        Assert.Null(ZeroHookService.ParseSha256Digest("sha256:xyz"));
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public void Verify_AcceptsAMatch_AndWritesVersion()
    {
        var svc = new ZeroHookService(_dir, isRunning: () => false);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var tmp = svc.ExePath + ".tmp";
        File.WriteAllBytes(tmp, data);
        Assert.True(svc.TryInstallVerified(tmp, data.Length, Sha(data), "1.1.0"));
        Assert.Equal(data, File.ReadAllBytes(svc.ExePath));
        Assert.False(File.Exists(tmp));
        Assert.Equal("1.1.0", svc.InstalledVersion);
    }

    [Fact]
    public void Verify_RejectsSizeMismatch_KeepsOldExe()
    {
        var svc = new ZeroHookService(_dir, isRunning: () => false);
        var old = new byte[] { 9, 9, 9 };
        File.WriteAllBytes(svc.ExePath, old);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var tmp = svc.ExePath + ".tmp";
        File.WriteAllBytes(tmp, data);
        Assert.False(svc.TryInstallVerified(tmp, data.Length + 1, Sha(data), "1.1.0"));
        Assert.Equal(old, File.ReadAllBytes(svc.ExePath));
        Assert.False(File.Exists(tmp));
    }

    [Fact]
    public void Verify_RejectsHashMismatch_KeepsOldExe()
    {
        var svc = new ZeroHookService(_dir, isRunning: () => false);
        var old = new byte[] { 9, 9, 9 };
        File.WriteAllBytes(svc.ExePath, old);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var tmp = svc.ExePath + ".tmp";
        File.WriteAllBytes(tmp, data);
        Assert.False(svc.TryInstallVerified(tmp, data.Length, new string('0', 64), "1.1.0"));
        Assert.Equal(old, File.ReadAllBytes(svc.ExePath));
        Assert.False(File.Exists(tmp));
    }

    [Fact]
    public void Verify_NoDigest_SizeOnly()
    {
        var svc = new ZeroHookService(_dir, isRunning: () => false);
        var data = new byte[] { 1, 2, 3 };
        var tmp = svc.ExePath + ".tmp";
        File.WriteAllBytes(tmp, data);
        Assert.True(svc.TryInstallVerified(tmp, data.Length, null, "1.0.6"));
        Assert.True(svc.IsDownloaded);
    }

    private static string TaskXml(string command) =>
        "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\r\n"
        + "  <Actions Context=\"Author\">\r\r\n    <Exec>\r\r\n      <Command>" + command + "</Command>\r\r\n    </Exec>\r\r\n  </Actions>\r\r\n</Task>";

    [Fact]
    public void ParseTaskCommand_StripsQuotesAndDecodes()
    {
        Assert.Equal(@"C:\A & B\ZeroHook.exe", ZeroHookService.ParseTaskCommand(TaskXml("\"C:\\A &amp; B\\ZeroHook.exe\"")));
        Assert.Null(ZeroHookService.ParseTaskCommand("<Task></Task>"));
        Assert.Null(ZeroHookService.ParseTaskCommand(""));
    }

    [Fact]
    public void PathsEqual_IgnoresCaseAndSlashes()
    {
        Assert.True(ZeroHookService.PathsEqual(@"C:\Users\X\AppData\Local\RHI\zerohook\ZeroHook.exe", "c:/users/x/appdata/local/rhi/ZEROHOOK/zerohook.exe"));
        Assert.False(ZeroHookService.PathsEqual(@"C:\Tools\ZeroHook.exe", @"C:\Users\X\AppData\Local\RHI\zerohook\ZeroHook.exe"));
        Assert.False(ZeroHookService.PathsEqual(null, @"C:\x.exe"));
    }

    [Fact]
    public void AutoRunState_FromQuery()
    {
        var exe = Path.Combine(_dir, "ZeroHook.exe");
        Assert.Equal(ZeroHookAutoRunState.OurExe,
            ZeroHookService.AutoRunStateFrom(new ZeroHookTaskQuery(0, TaskXml("\"" + exe.Replace('\\', '/').ToUpperInvariant() + "\""), ""), exe));
        Assert.Equal(ZeroHookAutoRunState.OtherExe,
            ZeroHookService.AutoRunStateFrom(new ZeroHookTaskQuery(0, TaskXml(@"C:\Tools\ZeroHook.exe"), ""), exe));
        Assert.Equal(ZeroHookAutoRunState.NotRegistered,
            ZeroHookService.AutoRunStateFrom(new ZeroHookTaskQuery(1, "", "ERROR: The system cannot find the file specified."), exe));
        Assert.Equal(ZeroHookAutoRunState.Unknown,
            ZeroHookService.AutoRunStateFrom(new ZeroHookTaskQuery(1, "", "ERROR: Access is denied."), exe));
        Assert.Equal(ZeroHookAutoRunState.Unknown,
            ZeroHookService.AutoRunStateFrom(new ZeroHookTaskQuery(-1, "", "", TimedOut: true), exe));
    }

    [Fact]
    public void AutoRunState_FailedQuery_DecidedByTaskFile_NotErrorText()
    {
        // Non-English Windows: the error text has no "denied"; the task file in System32\Tasks decides
        var exe = Path.Combine(_dir, "ZeroHook.exe");
        var q = new ZeroHookTaskQuery(1, "", "FEHLER: Zugriff verweigert.");
        Assert.Equal(ZeroHookAutoRunState.Unknown, ZeroHookService.AutoRunStateFrom(q, exe, taskFileExists: true));
        Assert.Equal(ZeroHookAutoRunState.NotRegistered, ZeroHookService.AutoRunStateFrom(q, exe, taskFileExists: false));
    }

    [Fact]
    public void Remove_RefusesWhenAutoRunPointsAtOurExe()
    {
        var exe = Path.Combine(_dir, "ZeroHook.exe");
        var svc = new ZeroHookService(_dir, () => new ZeroHookTaskQuery(0, TaskXml(exe), ""), () => false);
        File.WriteAllBytes(svc.ExePath, new byte[] { 1 });
        Assert.Equal(ZeroHookService.RefuseAutoRunText, svc.Remove());
        Assert.True(File.Exists(svc.ExePath));
    }

    [Fact]
    public void Remove_RefusesWhileRunning()
    {
        var svc = new ZeroHookService(_dir, () => new ZeroHookTaskQuery(1, "", "not found"), () => true);
        File.WriteAllBytes(svc.ExePath, new byte[] { 1 });
        Assert.Equal(ZeroHookService.RefuseRunningText, svc.Remove());
        Assert.True(File.Exists(svc.ExePath));
    }

    [Fact]
    public void Remove_DeletesOnlyRhiFiles_WhenNoTask()
    {
        var svc = new ZeroHookService(_dir, () => new ZeroHookTaskQuery(1, "", "not found"), () => false);
        File.WriteAllBytes(svc.ExePath, new byte[] { 1 });
        File.WriteAllText(Path.Combine(_dir, "version.txt"), "1.1.0");
        File.WriteAllText(Path.Combine(_dir, "zerohook_backup.json"), "{}");
        Assert.Null(svc.Remove());
        Assert.False(File.Exists(svc.ExePath));
        Assert.False(File.Exists(Path.Combine(_dir, "version.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "zerohook_backup.json")));
    }
}
