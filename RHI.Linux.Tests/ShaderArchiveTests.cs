using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class ShaderArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-shader-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    public ShaderArchiveTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_root, "cache"));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _cache);
        Directory.Delete(_root, true);
    }

    private byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        return memory.ToArray();
    }

    [Fact]
    public async Task ManifestReleaseOverrideSelectsMatchingAssetAndFiltersPackFiles()
    {
        LinuxPaths.WriteJson(Path.Combine(LinuxPaths.Cache, "manifest.json"), new
        {
            shaderPacks = new { Lilium = new { kind = "GhRelease", url = "https://example.org/release", assetExt = ".zip" } }
        });
        var zip = Zip(("release\\Shaders\\HDR.fx", "shader"), ("release/Textures/lut.png", "texture"),
            ("release/Shaders/GrainSpread.fx", "excluded"), ("release/readme.txt", "ignored"));
        var urls = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            urls.Add(url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = url == "https://example.org/release" ? new StringContent("""
                    {"assets":[
                        {"name":"shaders.7z","browser_download_url":"https://example.org/wrong"},
                        {"name":"shaders.ZIP","browser_download_url":"https://example.org/chosen"}
                    ]}
                    """) : new ByteArrayContent(zip)
            };
        }));
        var payloads = await new Downloads(http).Shaders("Lilium HDR");
        Assert.Equal(new[] { "https://example.org/release", "https://example.org/chosen" }, urls);
        Assert.Equal(2, payloads.Count);
        Assert.Contains(payloads, p => p.RelativePath == "reshade-shaders/Shaders/Lilium-HDR/HDR.fx");
        Assert.Contains(payloads, p => p.RelativePath == "reshade-shaders/Textures/Lilium-HDR/lut.png");
    }

    [Theory]
    [InlineData("../Shaders/escape.fx")]
    [InlineData("/Shaders/absolute.fx")]
    [InlineData("C:/Shaders/drive.fx")]
    public async Task ArchivePathsCannotEscapeThePayloadRoot(string entry)
    {
        var archive = Path.Combine(_root, "bad.zip");
        await File.WriteAllBytesAsync(archive, Zip((entry, "bad")));
        await Assert.ThrowsAsync<IOException>(() => ShaderArchives.ReadPayloads(archive, "bad.zip", "Standard"));
    }

    [Fact]
    public async Task ShaderZipRejectsSymlinks()
    {
        var path = Path.Combine(_root, "links.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Shaders/link.fx");
            entry.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
            using var writer = new StreamWriter(entry.Open());
            writer.Write("/outside/file");
        }
        await Assert.ThrowsAsync<IOException>(() => ShaderArchives.ReadPayloads(path, "links.zip", "Standard"));
    }

    [SevenZipFact]
    public async Task SevenZipReleaseCanBeReadWithoutExtractingArchiveControlledPaths()
    {
        // Source-only test runners need no extractor installed. Packaged releases test the bundled binary separately.
        var tool = ArchiveTools.SevenZip;
        var shaders = Path.Combine(_root, "Shaders");
        Directory.CreateDirectory(shaders);
        await File.WriteAllTextAsync(Path.Combine(shaders, "HDR.fx"), "shader");
        var archive = Path.Combine(_root, "pack.7z");
        await CreateSevenZip(tool, archive, "Shaders");
        var payloads = await ShaderArchives.ReadPayloads(archive, "pack.7z", "Lilium HDR");
        Assert.Single(payloads);
        Assert.Equal("reshade-shaders/Shaders/Lilium-HDR/HDR.fx", payloads[0].RelativePath);

        File.CreateSymbolicLink(Path.Combine(shaders, "link.fx"), "HDR.fx");
        var unsafeArchive = Path.Combine(_root, "links.7z");
        await CreateSevenZip(tool, unsafeArchive, "Shaders");
        await Assert.ThrowsAsync<IOException>(() => ShaderArchives.ReadPayloads(unsafeArchive, "links.7z", "Standard"));
    }

    private async Task CreateSevenZip(string tool, string archive, string entry)
    {
        var start = new ProcessStartInfo(tool) { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "a", "-snl", "-bd", "-y", "--", archive, entry }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        Assert.True(process.ExitCode == 0, await error);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}

public sealed class SevenZipFactAttribute : FactAttribute
{
    public SevenZipFactAttribute()
    {
        try { _ = ArchiveTools.SevenZip; }
        catch (IOException) { Skip = "No source-build 7-Zip available; packaged extractor is checked by check-linux-package.sh."; }
    }
}
