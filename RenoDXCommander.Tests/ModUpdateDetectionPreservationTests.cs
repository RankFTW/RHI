using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Random = System.Random;

namespace RenoDXCommander.Tests;

/// <summary>
/// Preservation property tests for mod update detection.
///
/// These tests capture the baseline behavior of non-clshortfuse URLs that must
/// remain unchanged after the bugfix. They are written and verified BEFORE the fix
/// is applied, following observation-first methodology.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
///
/// EXPECTED OUTCOME on UNFIXED code: All tests PASS (confirms baseline to preserve).
/// </summary>
public class ModUpdateDetectionPreservationTests : IDisposable
{
    private readonly string _tempRoot;

    public ModUpdateDetectionPreservationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcPreserve_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Property 2a ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Property 2a: For all marat569.github.io URL paths, CheckForUpdateAsync uses
    /// the download-based path (GET request, not HEAD).
    ///
    /// Same-size file → returns false; different-size file → returns true.
    /// Verified by checking that the fake handler receives a GET request.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 5)]
    public Property Marat569Urls_UseDownloadPath_SameSize_ReturnsFalse()
    {
        // The only marat569 URL currently in _downloadCheckUrls is the resolved one.
        // ResolveSnapshotUrl maps "renodx-ue-extended.addon64" → marat569 URL.
        // So we use the marat569 URL directly as SnapshotUrl.
        var url = "https://marat569.github.io/renodx/renodx-ue-extended.addon64";
        var addonFileName = "renodx-ue-extended.addon64";

        // Generate arbitrary file sizes (small range to keep tests fast)
        var genSize = Gen.Choose(100, 5000);

        return Prop.ForAll(Arb.From(genSize), (int fileSize) =>
        {
            var fileBytes = new byte[fileSize];
            new Random(fileSize).NextBytes(fileBytes);

            var gameDir = Path.Combine(_tempRoot, $"Game2a_same_{fileSize}");
            Directory.CreateDirectory(gameDir);
            var localFilePath = Path.Combine(gameDir, addonFileName);
            File.WriteAllBytes(localFilePath, fileBytes);

            var record = new InstalledModRecord
            {
                GameName = "TestGame",
                InstallPath = gameDir,
                AddonFileName = addonFileName,
                SnapshotUrl = url,
                RemoteFileSize = fileSize,
                InstalledAt = DateTime.UtcNow,
            };

            // GET returns same-size bytes → download path should return false
            var handler = new TrackingHttpMessageHandler(headContentLength: fileSize, getResponseBytes: fileBytes);
            var httpClient = new HttpClient(handler);
            var service = new ModInstallService(httpClient);

            var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

            // Must use GET (download path), not HEAD
            return (!hasUpdate && handler.GetRequestCount > 0 && handler.HeadRequestCount == 0)
                .Label($"Expected no update via GET path. hasUpdate={hasUpdate}, GET={handler.GetRequestCount}, HEAD={handler.HeadRequestCount}");
        });
    }

    [Property(MaxTest = 5)]
    public Property Marat569Urls_UseDownloadPath_DifferentSize_ReturnsTrue()
    {
        var url = "https://marat569.github.io/renodx/renodx-ue-extended.addon64";
        var addonFileName = "renodx-ue-extended.addon64";

        var genSize = Gen.Choose(100, 5000);

        return Prop.ForAll(Arb.From(genSize), (int localSize) =>
        {
            var localBytes = new byte[localSize];
            new Random(localSize).NextBytes(localBytes);

            // Remote file is a different size (local + 100 bytes)
            var remoteSize = localSize + 100;
            var remoteBytes = new byte[remoteSize];
            new Random(remoteSize).NextBytes(remoteBytes);

            var gameDir = Path.Combine(_tempRoot, $"Game2a_diff_{localSize}");
            Directory.CreateDirectory(gameDir);
            var localFilePath = Path.Combine(gameDir, addonFileName);
            File.WriteAllBytes(localFilePath, localBytes);

            var record = new InstalledModRecord
            {
                GameName = "TestGame",
                InstallPath = gameDir,
                AddonFileName = addonFileName,
                SnapshotUrl = url,
                RemoteFileSize = localSize,
                InstalledAt = DateTime.UtcNow,
            };

            // GET returns different-size bytes → download path should return true
            var handler = new TrackingHttpMessageHandler(headContentLength: remoteSize, getResponseBytes: remoteBytes);
            var httpClient = new HttpClient(handler);
            var service = new ModInstallService(httpClient);

            var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

            // Must use GET (download path), not HEAD
            return (hasUpdate && handler.GetRequestCount > 0 && handler.HeadRequestCount == 0)
                .Label($"Expected update via GET path. hasUpdate={hasUpdate}, GET={handler.GetRequestCount}, HEAD={handler.HeadRequestCount}");
        });
    }

    // ── Property 2b ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Property 2b: For all non-github.io host URLs, CheckForUpdateAsync uses HEAD
    /// Content-Length comparison. Verified by checking only HEAD requests are made.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property NonGithubIoUrls_UseHeadPath()
    {
        // Generate random host names that are NOT *.github.io
        var genHost = Gen.Elements(
            "github.com", "example.com", "cdn.example.org",
            "files.nexusmods.com", "releases.example.net", "storage.googleapis.com");
        var genPath = Gen.Elements(
            "/renodx/mod.addon64", "/downloads/file.addon64", "/v1/release.addon64");
        var genSize = Gen.Choose(1000, 100_000);

        var genInput = from host in genHost
                       from path in genPath
                       from size in genSize
                       select (host, path, size);

        return Prop.ForAll(Arb.From(genInput), (ValueTuple<string, string, int> input) =>
        {
            var (host, path, fileSize) = input;
            var url = $"https://{host}{path}";
            var addonFileName = Path.GetFileName(path);

            var gameDir = Path.Combine(_tempRoot, $"Game2b_{host}_{fileSize}");
            Directory.CreateDirectory(gameDir);
            var localFilePath = Path.Combine(gameDir, addonFileName);
            var localBytes = new byte[fileSize];
            new Random(fileSize).NextBytes(localBytes);
            File.WriteAllBytes(localFilePath, localBytes);

            var record = new InstalledModRecord
            {
                GameName = "TestGame",
                InstallPath = gameDir,
                AddonFileName = addonFileName,
                SnapshotUrl = url,
                RemoteFileSize = fileSize,
                InstalledAt = DateTime.UtcNow,
            };

            // HEAD returns same Content-Length → should return false (no update)
            var handler = new TrackingHttpMessageHandler(headContentLength: fileSize, getResponseBytes: Array.Empty<byte>());
            var httpClient = new HttpClient(handler);
            var service = new ModInstallService(httpClient);

            var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

            // Must use HEAD only, no GET
            return (!hasUpdate && handler.HeadRequestCount > 0 && handler.GetRequestCount == 0)
                .Label($"Expected HEAD-only path. hasUpdate={hasUpdate}, HEAD={handler.HeadRequestCount}, GET={handler.GetRequestCount}");
        });
    }

    // ── Property 2c ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Property 2c: For any InstalledModRecord with SnapshotUrl = null,
    /// CheckForUpdateAsync returns false.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property NullSnapshotUrl_ReturnsFalse()
    {
        var genGameName = Gen.Elements("GameA", "GameB", "GameC", "SomeRPG");
        var genAddon = Gen.Elements("mod.addon64", "renodx.addon32", "custom.addon64");

        var genInput = from gameName in genGameName
                       from addon in genAddon
                       select (gameName, addon);

        return Prop.ForAll(Arb.From(genInput), (ValueTuple<string, string> input) =>
        {
            var (gameName, addon) = input;

            var record = new InstalledModRecord
            {
                GameName = gameName,
                InstallPath = _tempRoot,
                AddonFileName = addon,
                SnapshotUrl = null,  // null → should return false
                RemoteFileSize = 12345,
                InstalledAt = DateTime.UtcNow,
            };

            // No HTTP calls should be made at all
            var handler = new TrackingHttpMessageHandler(headContentLength: 0, getResponseBytes: Array.Empty<byte>());
            var httpClient = new HttpClient(handler);
            var service = new ModInstallService(httpClient);

            var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

            return (!hasUpdate && handler.HeadRequestCount == 0 && handler.GetRequestCount == 0)
                .Label($"Expected false with no HTTP calls. hasUpdate={hasUpdate}, HEAD={handler.HeadRequestCount}, GET={handler.GetRequestCount}");
        });
    }

    // ── Property 2d ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Property 2d: For any InstalledModRecord where the local file does not exist,
    /// CheckForUpdateAsync returns true.
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property MissingLocalFile_ReturnsTrue()
    {
        var genAddon = Gen.Elements("mod.addon64", "renodx.addon32", "custom.addon64");
        var genHost = Gen.Elements("github.com", "example.com", "cdn.example.org");

        var genInput = from addon in genAddon
                       from host in genHost
                       select (addon, host);

        return Prop.ForAll(Arb.From(genInput), (ValueTuple<string, string> input) =>
        {
            var (addon, host) = input;
            var url = $"https://{host}/downloads/{addon}";

            // Use a directory that exists but does NOT contain the addon file
            var gameDir = Path.Combine(_tempRoot, $"Game2d_{host}_{addon}");
            Directory.CreateDirectory(gameDir);
            // Do NOT create the local file — it should be missing

            var record = new InstalledModRecord
            {
                GameName = "TestGame",
                InstallPath = gameDir,
                AddonFileName = addon,
                SnapshotUrl = url,
                RemoteFileSize = 50000,
                InstalledAt = DateTime.UtcNow,
            };

            // No HTTP calls should be made — early return before network
            var handler = new TrackingHttpMessageHandler(headContentLength: 0, getResponseBytes: Array.Empty<byte>());
            var httpClient = new HttpClient(handler);
            var service = new ModInstallService(httpClient);

            var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

            return (hasUpdate && handler.HeadRequestCount == 0 && handler.GetRequestCount == 0)
                .Label($"Expected true with no HTTP calls. hasUpdate={hasUpdate}, HEAD={handler.HeadRequestCount}, GET={handler.GetRequestCount}");
        });
    }

    // ── Tracking HTTP Handler ────────────────────────────────────────────────────
    /// <summary>
    /// Fake HttpMessageHandler that tracks which HTTP methods were used (HEAD vs GET)
    /// and returns configurable responses for each.
    /// </summary>
    private class TrackingHttpMessageHandler : HttpMessageHandler
    {
        private readonly long _headContentLength;
        private readonly byte[] _getResponseBytes;

        public int HeadRequestCount { get; private set; }
        public int GetRequestCount { get; private set; }

        public TrackingHttpMessageHandler(long headContentLength, byte[] getResponseBytes)
        {
            _headContentLength = headContentLength;
            _getResponseBytes = getResponseBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

            if (request.Method == HttpMethod.Head)
            {
                HeadRequestCount++;
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = _headContentLength;
            }
            else if (request.Method == HttpMethod.Get)
            {
                GetRequestCount++;
                response.Content = new ByteArrayContent(_getResponseBytes);
                response.Content.Headers.ContentLength = _getResponseBytes.Length;
            }

            return Task.FromResult(response);
        }
    }
}
