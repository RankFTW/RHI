using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>GitHub answers are asked for gzipped; the cache must read both kinds and keep weak ETags whole.</summary>
public class GitHubETagCacheGzipTests
{
    private const string Json = "[{\"tag_name\":\"v1.2.3\",\"name\":\"Ünïcode ✓\"}]";

    private static HttpResponseMessage Response(byte[] body, string? encoding)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        if (encoding != null) content.Headers.ContentEncoding.Add(encoding);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    [Fact]
    public async Task ReadBody_Gzipped_IsDecompressed()
    {
        using var resp = Response(Gzip(Json), "gzip");
        Assert.Equal(Json, await GitHubETagCache.ReadBodyAsync(resp, CancellationToken.None));
    }

    [Fact]
    public async Task ReadBody_Plain_IsReadAsIs()
    {
        using var resp = Response(Encoding.UTF8.GetBytes(Json), null);
        Assert.Equal(Json, await GitHubETagCache.ReadBodyAsync(resp, CancellationToken.None));
    }

    [Theory]
    [InlineData("W/\"abc123\"", true, "\"abc123\"")]   // what a gzipped answer's ETag is saved as now
    [InlineData("\"abc123\"", false, "\"abc123\"")]    // entries saved before (strong, quotes only)
    public void ParseETag_KeepsWeakTagsWeak(string saved, bool weak, string tag)
    {
        var parsed = GitHubETagCache.ParseETag(saved);
        Assert.Equal(weak, parsed.IsWeak);
        Assert.Equal(tag, parsed.Tag);
        Assert.Equal(saved, parsed.ToString());
    }
}
