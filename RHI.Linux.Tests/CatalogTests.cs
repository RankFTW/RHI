using RHI.Core;
using System.Net;
using System.Text.Json;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    public CatalogTests() => Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _root);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _oldCache);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private static HttpClient Client(Func<Uri, string> fetch) => new(new Handler(fetch));
    private sealed class Handler(Func<Uri, string> fetch) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fetch(request.RequestUri!)) });
    }
    private const string Live = """
        {"wikiNameOverrides":{"Fixture":"Wiki Name"},"snapshotOverrides":{"Fixture":"https://example.org/new.addon64"},
         "installWarnings":{"Fixture":{"reshade":"New warning"}},"gameNotes":{"Fixture":{"notes":"New instructions"}}}
        """;

    [Fact] public async Task LiveManifestAppliesOverridesAndPersistsForOfflineStartup()
    {
        using var http = Client(uri => { Assert.Equal(Sources.Manifest, uri.AbsoluteUri); return Live; });
        var catalog = new Catalog(http);
        await catalog.RefreshManifest();
        var mod = new GameMod { Name = "Wiki Name", SnapshotUrl = "https://example.org/old.addon64" };
        catalog.Mods.Add(mod);
        var game = new Game { Name = "Fixture" };
        Assert.Same(mod, catalog.Match(game));
        Assert.Equal("https://example.org/new.addon64", catalog.AddonUrl(game, mod));
        Assert.Contains("New warning", catalog.GameNotes(game));
        Assert.Contains("New instructions", catalog.GameNotes(game));
        using var offline = Client(_ => throw new HttpRequestException("offline"));
        var reloaded = new Catalog(offline);
        await reloaded.RefreshManifest();
        Assert.Equal("Wiki Name", reloaded.ManifestString("wikiNameOverrides", "Fixture"));
        Assert.Contains("retaining", reloaded.ManifestStatus);
    }

    [Theory]
    [InlineData("<html>unavailable</html>")]
    [InlineData("[]")]
    [InlineData("{\"wikiUnlinks\":{}}")]
    [InlineData("{\"installWarnings\":{\"Fixture\":42}}")]
    [InlineData("{\"dgVoodooVersions\":{\"latest\":42}}")]
    [InlineData("{\"gameNotes\":{\"Fixture\":{\"notes\":42}}}")]
    [InlineData("{\"installWarnings\":{\"Fixture\":{\"reshade\":42}}}")]
    [InlineData("{\"forceExternalOnly\":{\"Fixture\":{\"url\":42}}}")]
    [InlineData("{\"dlssPresets\":{\"sr\":[null]}}")]
    [InlineData("{\"shaderPacks\":{\"Lilium\":null}}")]
    public async Task InvalidResponseDoesNotReplaceGoodCache(string invalid)
    {
        Directory.CreateDirectory(LinuxPaths.Cache);
        var cache = Path.Combine(LinuxPaths.Cache, "manifest.json");
        File.WriteAllText(cache, Live);
        using var http = Client(_ => invalid);
        var catalog = new Catalog(http);
        await catalog.RefreshManifest();
        Assert.Equal("Wiki Name", catalog.ManifestString("wikiNameOverrides", "Fixture"));
        Assert.Equal(Live, File.ReadAllText(cache));
    }

    [Fact] public async Task CorruptCacheFallsBackToBundledManifestOffline()
    {
        Directory.CreateDirectory(LinuxPaths.Cache);
        File.WriteAllText(Path.Combine(LinuxPaths.Cache, "manifest.json"), "broken");
        using var http = Client(_ => throw new TaskCanceledException("timeout"));
        var catalog = new Catalog(http);
        await catalog.RefreshManifest();
        using var bundled = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.json")));
        var first = bundled.RootElement.GetProperty("wikiNameOverrides").EnumerateObject().First();
        Assert.Equal(first.Value.GetString(), catalog.ManifestString("wikiNameOverrides", first.Name));
    }

    [Fact] public async Task RefreshFetchesWikiOnceAndRetainsSharedHeadingDownloads()
    {
        var requests = new List<string>();
        using var http = Client(uri =>
        {
            requests.Add(uri.AbsoluteUri);
            if (uri.AbsoluteUri == Sources.Manifest) return Live;
            Assert.Equal(Sources.Wiki, uri.AbsoluteUri);
            return "<h3>UE <a href='https://example.org/shared.addon64'>download</a></h3><table><tr><th>Name</th><th>Status</th><th>Notes</th></tr><tr><td>Wiki Name</td><td>✅</td><td>Engine.ini</td></tr></table>";
        });
        var catalog = new Catalog(http);
        await catalog.Refresh();
        Assert.Equal(new[] { Sources.Manifest, Sources.Wiki }, requests);
        Assert.Equal("https://example.org/shared.addon64", catalog.Mods.Single(m => m.Name == "Wiki Name").SnapshotUrl);
    }

    [Fact] public async Task ManifestStillUpdatesWhenWikiFails()
    {
        using var http = Client(uri => uri.AbsoluteUri == Sources.Manifest ? Live : throw new HttpRequestException("wiki offline"));
        var catalog = new Catalog(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => catalog.Refresh());
        Assert.Equal("Wiki Name", catalog.ManifestString("wikiNameOverrides", "Fixture"));
    }
}
