using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// The saved ETag cache is loaded in the background and only rewritten when it changed. The guard that
/// matters: a save must never happen before the load, or the file is replaced by an empty cache.
/// </summary>
public class GitHubETagCachePersistTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-etag-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public GitHubETagCachePersistTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "github_etag_cache.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; left for the OS to clean */ }
    }

    private const string UrlA = "https://api.github.com/repos/a/a/releases/latest";
    private const string UrlB = "https://api.github.com/repos/b/b/releases/latest";

    private void Seed(params (string url, string etag, string body)[] entries)
    {
        var d = entries.ToDictionary(e => e.url, e => new GitHubETagCache.SavedEntry
        {
            ETag = e.etag, Body = e.body, SavedUtc = DateTime.UtcNow.AddDays(-1),
        });
        File.WriteAllText(_file, JsonSerializer.Serialize(d));
    }

    private Dictionary<string, GitHubETagCache.SavedEntry> ReadFile() =>
        JsonSerializer.Deserialize<Dictionary<string, GitHubETagCache.SavedEntry>>(File.ReadAllText(_file))!;

    /// <summary>Answers every GET with 200, the given ETag and body (304 when If-None-Match matches).</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public string ETag = "\"v1\"";
        public string Body = "{\"tag_name\":\"1\"}";
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (request.Headers.IfNoneMatch.Any(t => t.Tag == ETag))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
            resp.Headers.ETag = new EntityTagHeaderValue(ETag);
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public void SaveRightAfterConstruction_KeepsWhatWasOnDisk()
    {
        Seed((UrlA, "\"a1\"", "body-a"));
        var cache = new GitHubETagCache(_file);

        cache.WriteNow(); // an early save must wait for the load, not write an empty cache

        var onDisk = ReadFile();
        Assert.Single(onDisk);
        Assert.Equal("body-a", onDisk[UrlA].Body);
    }

    [Fact]
    public async Task NewEntry_IsSavedAlongsideTheLoadedOnes()
    {
        Seed((UrlA, "\"a1\"", "body-a"));
        var cache = new GitHubETagCache(_file);
        using var http = new HttpClient(new FakeGitHub());

        var body = await cache.GetWithETagAsync(http, UrlB);
        cache.WriteNow();

        Assert.Equal("{\"tag_name\":\"1\"}", body);
        var onDisk = ReadFile();
        Assert.Equal(2, onDisk.Count);
        Assert.Equal("body-a", onDisk[UrlA].Body);
        Assert.Equal("\"v1\"", onDisk[UrlB].ETag);
    }

    [Fact]
    public async Task LoadedEntry_IsUsedForTheFirstRequest()
    {
        Seed((UrlA, "\"v1\"", "saved-body"));
        var cache = new GitHubETagCache(_file);
        var gitHub = new FakeGitHub();
        using var http = new HttpClient(gitHub);

        var body = await cache.GetWithETagAsync(http, UrlA); // If-None-Match "v1" → 304 → the saved body

        Assert.Equal("saved-body", body);
        Assert.Equal(1, gitHub.Requests);
    }

    [Fact]
    public async Task UnchangedAfterInvalidateAndRefetch_FileIsNotRewritten()
    {
        var gitHub = new FakeGitHub();
        Seed((UrlA, gitHub.ETag, gitHub.Body));
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_file, stamp);
        var cache = new GitHubETagCache(_file);
        using var http = new HttpClient(gitHub);

        cache.Invalidate(UrlA);                    // what Refresh does
        await cache.GetWithETagAsync(http, UrlA);  // comes back with the same ETag and body
        cache.WriteNow();

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_file));
    }

    [Fact]
    public async Task ChangedEntry_IsWritten()
    {
        var gitHub = new FakeGitHub();
        Seed((UrlA, gitHub.ETag, gitHub.Body));
        var cache = new GitHubETagCache(_file);
        using var http = new HttpClient(gitHub);

        gitHub.ETag = "\"v2\"";
        gitHub.Body = "{\"tag_name\":\"2\"}";
        await cache.GetWithETagAsync(http, UrlA);
        cache.WriteNow();

        Assert.Equal("\"v2\"", ReadFile()[UrlA].ETag);
        Assert.Equal("{\"tag_name\":\"2\"}", ReadFile()[UrlA].Body);
    }

    [Fact]
    public void InvalidatedEntry_IsRemovedFromTheFile()
    {
        Seed((UrlA, "\"a1\"", "body-a"), (UrlB, "\"b1\"", "body-b"));
        var cache = new GitHubETagCache(_file);

        cache.Invalidate(UrlA);
        cache.WriteNow();

        var onDisk = ReadFile();
        Assert.Single(onDisk);
        Assert.True(onDisk.ContainsKey(UrlB));
    }
}
