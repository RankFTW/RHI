using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class SharedServicesTests
{
    [Fact]
    public async Task GitHubCacheRevalidatesAndReusesBodyOn304AndServerFailure()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            if (requests++ == 0)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("release-json") };
                response.Headers.ETag = new EntityTagHeaderValue("\"release-1\"");
                return response;
            }
            Assert.Equal("\"release-1\"", Assert.Single(request.Headers.IfNoneMatch).Tag);
            return new HttpResponseMessage(requests == 2 ? HttpStatusCode.NotModified : HttpStatusCode.ServiceUnavailable);
        }));
        var cache = new GitHubETagCache(getToken: () => "test-token");
        for (var i = 0; i < 3; i++)
            Assert.Equal("release-json", await cache.GetWithETagAsync(http, "https://api.github.com/releases"));
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task OrdinaryForbiddenDoesNotDisableOtherReleaseRequests()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(requests++ == 0 ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
        { Content = new StringContent("ok") }));
        var cache = new GitHubETagCache();
        Assert.Null(await cache.GetWithETagAsync(http, "https://api.github.com/forbidden"));
        Assert.False(cache.IsRateLimited);
        Assert.Equal("ok", await cache.GetWithETagAsync(http, "https://api.github.com/available"));
    }

    [Fact]
    public void SharedReFrameworkExtractionHandlesWindowsZipPathsAndPermissions()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "release.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("release\\DINPUT8.DLL");
                entry.ExternalAttributes = 0;
                using var output = new StreamWriter(entry.Open());
                output.Write("dll-payload");
            }
            var destination = Path.Combine(root, "dinput8.dll");
            REFrameworkArchive.ExtractDllFromZip(zip, destination);
            Assert.Equal("dll-payload", File.ReadAllText(destination));
            Assert.Throws<FileNotFoundException>(() => DllArchive.ExtractDll(zip, destination, "missing.dll"));
            Assert.Equal("dll-payload", File.ReadAllText(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SharedShaderCatalogHonorsOverridesAndDisabledPacks()
    {
        var catalog = new ShaderPackCatalog();
        Assert.EndsWith("/slim.zip", catalog.FindPack("CrosireMaster")!.PortableZipUrl);
        catalog.ApplyManifestOverrides(JsonSerializer.Deserialize<RemoteManifest>("""
            {"shaderPacks": {
                "CrosireMaster": {"url": "https://example.org/standard.zip", "kind": "DirectUrl"},
                "Lilium": {"disabled": true}
            }}
            """));
        Assert.Equal("https://example.org/standard.zip", catalog.FindPack("CrosireMaster")!.Url);
        Assert.Null(catalog.FindPack("CrosireMaster")!.PortableZipUrl);
        Assert.Null(catalog.FindPack("Lilium"));
        Assert.True(ShaderPackCatalog.IsExcludedShader("grainspread.FX"));
    }

    [Fact]
    public void SharedDiscoveryTreatsAnEmptyGamePathAsNoInstallation()
    {
        Assert.Empty(DlssFileDiscovery.Scan("").GameDlls);
        Assert.Empty(DlssScanner.Detect("").Paths);
    }

    [Fact]
    public void SharedDiscoveryKeepsNativeCopiesSeparateAndSkipsSymlinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "OPTISCALER.INI"), "");
            var opti = Path.Combine(root, "NVNGX_DLSS.DLL");
            File.WriteAllText(opti, "opti");
            var plugin = Path.Combine(root, "Engine", "Plugins");
            Directory.CreateDirectory(plugin);
            var native = Path.Combine(plugin, "nvngx_dlss.dll");
            File.WriteAllText(native, "native");
            File.WriteAllText(Path.Combine(plugin, "SL.COMMON.DLL"), "streamline");
            var helper = Path.Combine(root, "host64");
            Directory.CreateDirectory(helper);
            File.WriteAllText(Path.Combine(helper, "nvngx_dlssg.dll"), "helper");
            Directory.CreateSymbolicLink(Path.Combine(plugin, "loop"), root);
            var shared = DlssFileDiscovery.Scan(root, name => name == "host64");
            Assert.Equal(native, shared.GameDlls[DlssFileDiscovery.Dlss]);
            Assert.Equal(opti, shared.OptiScalerDlls[DlssFileDiscovery.Dlss]);
            Assert.False(shared.GameDlls.ContainsKey(DlssFileDiscovery.Dlssg));
            Assert.Equal(plugin, shared.StreamlineFolder);
            Assert.Equal(native, DlssScanner.Detect(root).Paths[DlssKind.SR]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SharedSwapPreservesOriginalAcrossRepeatedUpdatesAndFailedCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-swap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "nvngx_dlss.dll");
            var source = Path.Combine(root, "source.dll");
            File.WriteAllText(target, "original");
            File.WriteAllText(source, "first");
            SentinelFiles.Deploy(source, target);
            File.WriteAllText(source, "second");
            SentinelFiles.Deploy(source, target);
            Assert.Throws<FileNotFoundException>(() => SentinelFiles.Deploy(Path.Combine(root, "missing"), target));
            Assert.Equal("second", File.ReadAllText(target));
            Assert.Equal("original", File.ReadAllText(SentinelFiles.BackupOf(target)));
            Assert.Empty(Directory.EnumerateFiles(root, "*.rhi-*"));
            SentinelFiles.Restore(target);
            Assert.Equal("original", File.ReadAllText(target));
            Assert.False(SentinelFiles.Placed(target));
            File.Delete(target);
            SentinelFiles.Deploy(source, target);
            SentinelFiles.Restore(target);
            Assert.False(File.Exists(target));
            Assert.False(SentinelFiles.Placed(target));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
