using System.Security.Cryptography;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// RTX Encore's NVIDIA files for games without DLSS: the record (rtx-encore-runtimes\nvidia\rhi_installed.txt),
/// the install / remove decisions, DLSS detection skipping RHI's files, and removal touching only RHI's copies.
/// </summary>
public class RtxEncoreNoDlssTests : IDisposable
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-encore-nodlss-" + Guid.NewGuid().ToString("N"));

    public RtxEncoreNoDlssTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static readonly string H = new('a', 64);

    private RtxEncoreNvidiaMarker.Entry Put(string name, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(_dir, name), content);
        return new RtxEncoreNvidiaMarker.Entry(name, content.Length, Sha(content));
    }

    private void WriteMarker(params RtxEncoreNvidiaMarker.Entry[] entries)
    {
        var path = RtxEncoreNvidiaMarker.MarkerPath(_dir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RtxEncoreNvidiaMarker.Format(entries));
    }

    private static RtxEncoreService NewEncore() => new(new HttpClient(), new NullReporter(), RenoDXCommander.Tests.TestEnvironment.NewETagCache());

    // ── Record ───────────────────────────────────────────────────────────────

    [Fact]
    public void Marker_RoundTrips()
    {
        var entries = new[] { new RtxEncoreNvidiaMarker.Entry("sl.common.dll", 843904, H), new RtxEncoreNvidiaMarker.Entry("nvngx_dlss.dll", 1, new string('B', 64)) };
        var parsed = RtxEncoreNvidiaMarker.Parse(RtxEncoreNvidiaMarker.Format(entries));
        Assert.Equal(2, parsed.Count);
        Assert.Equal("sl.common.dll", parsed[0].Name);
        Assert.Equal(843904, parsed[0].Size);
        Assert.Equal(new string('b', 64), parsed[1].Sha256); // stored lower case
    }

    [Theory]
    [InlineData(@"..\evil.dll|1|" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(@"sub\x.dll|1|" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("x.dll|-1|" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("x.dll|1|nothex")]
    [InlineData("x.dll|1")]
    [InlineData("")]
    public void Marker_SkipsBadLines(string line) => Assert.Empty(RtxEncoreNvidiaMarker.Parse(line));

    [Fact]
    public void Marker_DuplicateNamesKeepFirst()
    {
        var parsed = RtxEncoreNvidiaMarker.Parse($"a.dll|1|{H}\nA.DLL|2|{H}\n");
        Assert.Single(parsed);
        Assert.Equal(1, parsed[0].Size);
    }

    [Fact]
    public void IsOwned_NeedsSameSize()
    {
        var entries = new List<RtxEncoreNvidiaMarker.Entry> { new("nvngx_dlssg.dll", 100, H) };
        Assert.True(RtxEncoreNvidiaMarker.IsOwned(entries, "NVNGX_DLSSG.dll", 100));
        Assert.False(RtxEncoreNvidiaMarker.IsOwned(entries, "nvngx_dlssg.dll", 101)); // replaced by the game
        Assert.False(RtxEncoreNvidiaMarker.IsOwned(entries, "nvngx_dlss.dll", 100));
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    [Fact]
    public void Skip_32BitAndOptiScalerBlockBoth()
    {
        Assert.Equal(RtxEncoreService.Reason32Bit, RtxEncoreService.NoDlssSkipReason(false, true, false, false, false, false, null));
        Assert.Equal(RtxEncoreService.Reason32Bit, RtxEncoreService.NoDlssSkipReason(true, true, false, false, false, false, null));
        Assert.Equal(RtxEncoreService.ReasonOptiScaler, RtxEncoreService.NoDlssSkipReason(false, false, true, false, false, false, null));
        Assert.Equal(RtxEncoreService.ReasonOptiScaler, RtxEncoreService.NoDlssSkipReason(true, false, true, false, false, false, null));
    }

    [Fact]
    public void Skip_HsfAndGameFgBlockOnlyFrameGeneration()
    {
        Assert.Equal(RtxEncoreService.ReasonHsf, RtxEncoreService.NoDlssSkipReason(true, false, false, true, false, false, null));
        Assert.Null(RtxEncoreService.NoDlssSkipReason(false, false, false, true, false, false, null));
        Assert.Equal(RtxEncoreService.ReasonGameHasFg, RtxEncoreService.NoDlssSkipReason(true, false, false, false, false, true, null));
        Assert.Null(RtxEncoreService.NoDlssSkipReason(false, false, false, false, false, true, null));
    }

    [Fact]
    public void Skip_GameDlssBlocksOnlyUpscaling()
    {
        Assert.Equal(RtxEncoreService.ReasonGameHasDlss, RtxEncoreService.NoDlssSkipReason(false, false, false, false, true, false, null));
        Assert.Null(RtxEncoreService.NoDlssSkipReason(true, false, false, false, true, false, null));
    }

    [Fact]
    public void Skip_TakenFileNameNeverOverwritten()
    {
        Assert.Equal(RtxEncoreService.ReasonTaken("sl.pcl.dll"),
            RtxEncoreService.NoDlssSkipReason(true, false, false, false, false, false, "sl.pcl.dll"));
        Assert.Null(RtxEncoreService.NoDlssSkipReason(true, false, false, false, false, false, null));
    }

    [Fact]
    public void Remove_WhenGameGainsItsOwnOrOptiScalerOrHsf()
    {
        Assert.Null(RtxEncoreService.NoDlssRemoveReason(false, false, false, false, false));
        Assert.Null(RtxEncoreService.NoDlssRemoveReason(true, false, false, false, false));
        Assert.Equal(RtxEncoreService.ReasonGameHasDlss, RtxEncoreService.NoDlssRemoveReason(false, false, false, true, false));
        Assert.Null(RtxEncoreService.NoDlssRemoveReason(false, false, true, false, true)); // HSF / game FG don't affect upscaling
        Assert.Equal(RtxEncoreService.ReasonHsf, RtxEncoreService.NoDlssRemoveReason(true, false, true, false, false));
        Assert.Equal(RtxEncoreService.ReasonGameHasFg, RtxEncoreService.NoDlssRemoveReason(true, false, false, false, true));
        Assert.Equal(RtxEncoreService.ReasonOptiScaler, RtxEncoreService.NoDlssRemoveReason(false, true, false, false, false));
    }

    [Fact]
    public void AcceptedBuilds_AreTheModsHashes()
    {
        Assert.True(RtxEncoreService.IsAcceptedFrameGenBuild("nvngx_dlssg.dll", "cadee1aa6db3907babdc3285da2aebc4553d92b89c38ba3eae8abd8bba1bea72"));
        Assert.True(RtxEncoreService.IsAcceptedFrameGenBuild("nvngx_dlssg.dll", "8D59D49B36410DD6B62FEF2503A5871D347415BD85D943FA0848563A95DD50C7"));
        Assert.True(RtxEncoreService.IsAcceptedFrameGenBuild("sl.interposer.dll", "b1d7395aebd7b9722841d64dcf7433680020b6a8e421f851c16d5ba0c2182edf"));
        Assert.False(RtxEncoreService.IsAcceptedFrameGenBuild("sl.common.dll", "b1d7395aebd7b9722841d64dcf7433680020b6a8e421f851c16d5ba0c2182edf"));
        Assert.Equal(7, RtxEncoreService.FrameGenFileNames.Count());
    }

    // ── Detection skips RHI's files ──────────────────────────────────────────

    [Fact]
    public void Detect_IgnoresListedFiles_CountsReplacedOnes()
    {
        var dlssg = Put("nvngx_dlssg.dll", new byte[] { 1, 2, 3 });
        var common = Put("sl.common.dll", new byte[] { 4, 5 });
        var up = Put("nvngx_dlss.dll", new byte[] { 6 });
        WriteMarker(dlssg, common, up);

        var dlss = new DlssStreamlineService(new HttpClient(), RenoDXCommander.Tests.TestEnvironment.NewETagCache());
        var r = dlss.Detect(_dir);
        Assert.Null(r.DlssPath);
        Assert.Null(r.DlssgPath);
        Assert.Null(r.StreamlineFolder);

        // A game update replaced nvngx_dlssg.dll (different size): it's the game's now
        File.WriteAllBytes(Path.Combine(_dir, "nvngx_dlssg.dll"), new byte[] { 9, 9, 9, 9 });
        r = dlss.Detect(_dir);
        Assert.NotNull(r.DlssgPath);
        Assert.Null(r.DlssPath);
    }

    [Fact]
    public void Detect_WithoutRecord_CountsFiles()
    {
        Put("nvngx_dlss.dll", new byte[] { 6 });
        var r = new DlssStreamlineService(new HttpClient(), RenoDXCommander.Tests.TestEnvironment.NewETagCache()).Detect(_dir);
        Assert.NotNull(r.DlssPath);
    }

    // ── Removal / deploy without downloads ───────────────────────────────────

    [Fact]
    public void Remove_DeletesOnlyMatchingFiles_AndTheRecord()
    {
        var ours = Put("sl.reflex.dll", new byte[] { 1, 2 });
        var replaced = Put("sl.pcl.dll", new byte[] { 3, 4 });
        WriteMarker(ours, replaced);
        File.WriteAllBytes(Path.Combine(_dir, "sl.pcl.dll"), new byte[] { 7, 7 }); // same size, different content

        NewEncore().RemoveNoDlssRuntimes(_dir);

        Assert.False(File.Exists(Path.Combine(_dir, "sl.reflex.dll")));
        Assert.True(File.Exists(Path.Combine(_dir, "sl.pcl.dll")));
        Assert.False(File.Exists(RtxEncoreNvidiaMarker.MarkerPath(_dir)));
        Assert.False(Directory.Exists(Path.Combine(_dir, RtxEncoreNvidiaMarker.RuntimesFolder)));
    }

    [Fact]
    public void Remove_KeepsIntelFolder()
    {
        var ours = Put("sl.reflex.dll", new byte[] { 1, 2 });
        WriteMarker(ours);
        var intel = Path.Combine(_dir, RtxEncoreNvidiaMarker.RuntimesFolder, "intel");
        Directory.CreateDirectory(intel);
        File.WriteAllText(Path.Combine(intel, "rhi_installed.txt"), "libxell.dll");

        NewEncore().RemoveNoDlssRuntimes(_dir);

        Assert.True(Directory.Exists(intel));
        Assert.False(Directory.Exists(Path.Combine(_dir, RtxEncoreNvidiaMarker.RuntimesFolder, RtxEncoreNvidiaMarker.NvidiaFolder)));
    }

    [Fact]
    public async Task Deploy_GameWithOwnDlssAndStreamline_WritesNothing()
    {
        var bin = Path.Combine(_dir, "Engine", "Plugins");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "nvngx_dlss.dll"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(bin, "sl.interposer.dll"), new byte[] { 2 });

        var r = await NewEncore().DeployNoDlssRuntimesAsync(_dir, false, false, new DlssStreamlineService(new HttpClient(), RenoDXCommander.Tests.TestEnvironment.NewETagCache()));

        Assert.False(r.Upscaling.Installed);
        Assert.Equal(RtxEncoreService.ReasonGameHasDlss, r.Upscaling.Reason);
        Assert.False(r.FrameGeneration.Installed);
        Assert.Equal(RtxEncoreService.ReasonGameHasFg, r.FrameGeneration.Reason);
        Assert.False(File.Exists(Path.Combine(_dir, "nvngx_dlss.dll")));
        Assert.False(File.Exists(RtxEncoreNvidiaMarker.MarkerPath(_dir)));
    }

    [Fact]
    public async Task Deploy_32Bit_WritesNothing()
    {
        var r = await NewEncore().DeployNoDlssRuntimesAsync(_dir, true, false, new DlssStreamlineService(new HttpClient(), RenoDXCommander.Tests.TestEnvironment.NewETagCache()));
        Assert.Equal(RtxEncoreService.Reason32Bit, r.Upscaling.Reason);
        Assert.Equal(RtxEncoreService.Reason32Bit, r.FrameGeneration.Reason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    [Fact]
    public async Task Deploy_ListedButDeletedByUser_IsNotPutBack()
    {
        var up = new RtxEncoreNvidiaMarker.Entry("nvngx_dlss.dll", 1, H);
        var fg = RtxEncoreService.FrameGenFileNames.Select(n => new RtxEncoreNvidiaMarker.Entry(n, 1, H)).ToArray();
        WriteMarker(fg.Prepend(up).ToArray()); // all listed, none on disk

        var r = await NewEncore().DeployNoDlssRuntimesAsync(_dir, false, false, new DlssStreamlineService(new HttpClient(), RenoDXCommander.Tests.TestEnvironment.NewETagCache()));

        Assert.Equal(RtxEncoreService.ReasonUserRemoved, r.Upscaling.Reason);
        Assert.Equal(RtxEncoreService.ReasonUserRemoved, r.FrameGeneration.Reason);
        Assert.False(File.Exists(Path.Combine(_dir, "nvngx_dlss.dll")));
    }
}
