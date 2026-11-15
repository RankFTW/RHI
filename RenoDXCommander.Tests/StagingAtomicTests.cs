using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Staged downloads are written through a temp file and moved into place only when complete,
/// and an OptiScaler staging folder is replaced only after the new build is fully copied.
/// </summary>
public class StagingAtomicTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-staging-" + Guid.NewGuid().ToString("N"));

    public StagingAtomicTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    /// <summary>A stream that fails part-way, like a broken archive entry.</summary>
    private sealed class FailingStream : MemoryStream
    {
        public FailingStream() : base(new byte[] { 1, 2, 3, 4 }) { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("broken entry");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => throw new IOException("broken entry");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw new IOException("broken entry");
    }

    [Fact]
    public async Task WriteStreamAtomic_Success_ReplacesFile()
    {
        var path = Path.Combine(_dir, "a.dll");
        File.WriteAllBytes(path, new byte[] { 9 });
        await FileHelper.WriteBytesAtomicAsync(path, new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task WriteStreamAtomic_Failure_KeepsOldFileAndNoTemp()
    {
        var path = Path.Combine(_dir, "a.dll");
        File.WriteAllBytes(path, new byte[] { 9 });
        await Assert.ThrowsAsync<IOException>(() => FileHelper.WriteStreamAtomicAsync(() => new FailingStream(), path));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task WriteStreamAtomic_WrongLength_NotAccepted()
    {
        var path = Path.Combine(_dir, "new.dll");
        await Assert.ThrowsAsync<IOException>(() =>
            FileHelper.WriteStreamAtomicAsync(() => new MemoryStream(new byte[] { 1, 2 }), path, expectedLength: 10));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    private (string Staging, string Version, string Source) MakeStaging()
    {
        var staging = Path.Combine(_dir, "optiscaler");
        Directory.CreateDirectory(Path.Combine(staging, "saved-inis"));
        File.WriteAllText(Path.Combine(staging, "saved-inis", "mine.ini"), "user");
        File.WriteAllText(Path.Combine(staging, "OptiScaler.dll"), "old");
        File.WriteAllText(Path.Combine(staging, "old-only.dll"), "old");
        var version = Path.Combine(staging, "version.txt");
        File.WriteAllText(version, "v1");

        var source = Path.Combine(_dir, "extract");
        Directory.CreateDirectory(Path.Combine(source, "D3D12_Optiscaler"));
        File.WriteAllText(Path.Combine(source, "OptiScaler.dll"), "new");
        File.WriteAllText(Path.Combine(source, "D3D12_Optiscaler", "x.dll"), "new");
        return (staging, version, source);
    }

    [Fact]
    public void ReplaceStagedBuild_KeepsRhiSubfolders_ReplacesBuild()
    {
        var (staging, version, source) = MakeStaging();
        OptiScalerService.ReplaceStagedBuild(staging, source, version, deleteSubdirs: false, "Test");

        Assert.Equal("new", File.ReadAllText(Path.Combine(staging, "OptiScaler.dll")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(staging, "D3D12_Optiscaler", "x.dll")));
        Assert.False(File.Exists(Path.Combine(staging, "old-only.dll")));
        Assert.Equal("user", File.ReadAllText(Path.Combine(staging, "saved-inis", "mine.ini")));
        Assert.False(File.Exists(version)); // the caller writes the new version last
        Assert.False(Directory.Exists(staging + ".new"));
    }

    [Fact]
    public void ReplaceStagedBuild_CopyFails_CurrentBuildUntouched()
    {
        var (staging, version, source) = MakeStaging();
        var locked = Path.Combine(source, "OptiScaler.dll");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) // can't be copied
        {
            Assert.ThrowsAny<IOException>(() =>
                OptiScalerService.ReplaceStagedBuild(staging, source, version, deleteSubdirs: true, "Test"));
        }
        Assert.Equal("old", File.ReadAllText(Path.Combine(staging, "OptiScaler.dll")));
        Assert.Equal("v1", File.ReadAllText(version));
        Assert.True(File.Exists(Path.Combine(staging, "saved-inis", "mine.ini")));
    }

    [Fact]
    public void ReplaceStagedBuild_DeleteSubdirs_KeepsOnlyRhiData()
    {
        var (staging, version, source) = MakeStaging();
        Directory.CreateDirectory(Path.Combine(staging, "ini-reference"));
        File.WriteAllText(Path.Combine(staging, "ini-reference", "game.ini"), "ref");
        Directory.CreateDirectory(Path.Combine(staging, "OldBuildFolder"));
        File.WriteAllText(Path.Combine(staging, "OldBuildFolder", "stale.dll"), "old");

        OptiScalerService.ReplaceStagedBuild(staging, source, version, deleteSubdirs: true, "Test");

        Assert.False(Directory.Exists(Path.Combine(staging, "OldBuildFolder")));
        Assert.Equal("user", File.ReadAllText(Path.Combine(staging, "saved-inis", "mine.ini")));
        Assert.Equal("ref", File.ReadAllText(Path.Combine(staging, "ini-reference", "game.ini")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(staging, "D3D12_Optiscaler", "x.dll")));
    }
}
