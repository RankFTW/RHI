using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// SafeJsonFile protects settings.json and the install-record files: corrupt files are
/// restored from .bak, and an unreadable file is never overwritten with an empty copy.
/// </summary>
public class SafeJsonFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-safejson-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "data.json");

    public SafeJsonFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        Assert.True(SafeJsonFile.Write(FilePath, new List<string> { "a", "b" }, "test"));
        var list = SafeJsonFile.Read<List<string>>(FilePath, out var status);
        Assert.Equal(SafeJsonFile.LoadStatus.Ok, status);
        Assert.Equal(new[] { "a", "b" }, list);
    }

    [Fact]
    public void SecondWrite_KeepsPreviousAsBak()
    {
        SafeJsonFile.Write(FilePath, new List<string> { "first" }, "test");
        SafeJsonFile.Read<List<string>>(FilePath, out _);
        SafeJsonFile.Write(FilePath, new List<string> { "second" }, "test");

        Assert.True(File.Exists(FilePath + ".bak"));
        Assert.Contains("first", File.ReadAllText(FilePath + ".bak"));
        Assert.Contains("second", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"half\": ")]
    [InlineData("null")]
    public void CorruptFile_RestoredFromBak_AndKeptAside(string corrupt)
    {
        SafeJsonFile.Write(FilePath, new List<string> { "good" }, "test");
        SafeJsonFile.Read<List<string>>(FilePath, out _);
        SafeJsonFile.Write(FilePath, new List<string> { "good2" }, "test"); // .bak = ["good"]
        File.WriteAllText(FilePath, corrupt);

        var list = SafeJsonFile.Read<List<string>>(FilePath, out var status);

        Assert.Equal(SafeJsonFile.LoadStatus.Recovered, status);
        Assert.Equal(new[] { "good" }, list);
        Assert.Single(Directory.GetFiles(_dir, "data.json.corrupt-*"));
        Assert.Contains("good", File.ReadAllText(FilePath)); // restored on disk too
    }

    [Fact]
    public void CorruptFile_NoBak_ReportsCorrupt()
    {
        File.WriteAllText(FilePath, "not json");
        var list = SafeJsonFile.Read<List<string>>(FilePath, out var status);
        Assert.Null(list);
        Assert.Equal(SafeJsonFile.LoadStatus.Corrupt, status);
    }

    [Fact]
    public void MissingFile_WithBak_Recovered()
    {
        File.WriteAllText(FilePath + ".bak", "[\"saved\"]");
        var list = SafeJsonFile.Read<List<string>>(FilePath, out var status);
        Assert.Equal(SafeJsonFile.LoadStatus.Recovered, status);
        Assert.Equal(new[] { "saved" }, list);
    }

    [Fact]
    public void MissingFile_NoBak_ReportsMissing()
    {
        Assert.Null(SafeJsonFile.Read<List<string>>(FilePath, out var status));
        Assert.Equal(SafeJsonFile.LoadStatus.Missing, status);
    }

    [Fact]
    public void UnreadableFile_BlocksWrite_UntilReadSucceeds()
    {
        SafeJsonFile.Write(FilePath, new List<string> { "keep" }, "test");

        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            SafeJsonFile.Read<List<string>>(FilePath, out var status);
            Assert.Equal(SafeJsonFile.LoadStatus.Unreadable, status);
        }

        // The write was built from an empty list — it must not replace the real data.
        Assert.False(SafeJsonFile.Write(FilePath, new List<string>(), "test"));
        Assert.Contains("keep", File.ReadAllText(FilePath));

        // Once a read succeeds, writes work again.
        SafeJsonFile.Read<List<string>>(FilePath, out var after);
        Assert.Equal(SafeJsonFile.LoadStatus.Ok, after);
        Assert.True(SafeJsonFile.Write(FilePath, new List<string> { "new" }, "test"));
    }

    [Fact]
    public void Update_ConcurrentCallers_LoseNothing()
    {
        Parallel.For(0, 50, i =>
            SafeJsonFile.Update<List<int>>(FilePath, l => l.Add(i), "test"));

        var list = SafeJsonFile.Read<List<int>>(FilePath, out _)!;
        Assert.Equal(50, list.Count);
        Assert.Equal(Enumerable.Range(0, 50), list.OrderBy(x => x));
    }
}
