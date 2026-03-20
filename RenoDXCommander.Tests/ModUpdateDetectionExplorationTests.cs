using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Bug condition exploration tests for mod update detection on clshortfuse.github.io URLs.
///
/// The bug: clshortfuse.github.io URLs are NOT in _downloadCheckUrls, so they use the
/// HEAD Content-Length path. GitHub Pages CDN may return compressed Content-Length that
/// differs from the stored RemoteFileSize, causing false positives (update reported when
/// the file hasn't changed).
///
/// **Validates: Requirements 1.1, 1.2, 1.3**
///
/// EXPECTED OUTCOME on UNFIXED code: Test FAILS (confirms the bug exists).
/// After the fix is applied, these same tests should PASS.
/// </summary>
public class ModUpdateDetectionExplorationTests : IDisposable
{
    /// <summary>
    /// Realistic addon filenames that could be hosted on clshortfuse.github.io.
    /// </summary>
    private static readonly string[] AddonFileNames =
    {
        "renodx-unityengine.addon64",
        "renodx-unrealengine.addon64",
        "renodx-unityengine.addon32",
    };

    private readonly string _tempRoot;

    public ModUpdateDetectionExplorationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcModTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// Property 1: Bug Condition — clshortfuse.github.io URLs Use HEAD Instead of Download Path
    ///
    /// For any clshortfuse.github.io URL with a local file that matches the real remote file,
    /// CheckForUpdateAsync SHALL return false (no update). On UNFIXED code, the HEAD path
    /// sees a compressed Content-Length that differs from stored RemoteFileSize and incorrectly
    /// returns true (false positive), causing this property to FAIL.
    ///
    /// **Validates: Requirements 1.1, 1.2**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property BugCondition_ClshortfuseUrls_ShouldNotFalsePositive()
    {
        // Generate random addon filenames from the realistic set
        var genFileName = Gen.Elements(AddonFileNames);

        return Prop.ForAll(
            Arb.From(genFileName),
            (string addonFileName) =>
            {
                return BugConditionTestCore(addonFileName);
            });
    }

    private bool BugConditionTestCore(string addonFileName)
    {
        // The real (uncompressed) file content — 50,000 bytes
        var realFileSize = 50_000L;
        var realFileBytes = new byte[realFileSize];
        new System.Random(42).NextBytes(realFileBytes);

        // Compressed Content-Length that HEAD will return — differs from real size
        // This simulates GitHub Pages CDN returning gzip-compressed Content-Length
        var compressedSize = 38_000L;

        var snapshotUrl = $"https://clshortfuse.github.io/renodx/{addonFileName}";

        // Create a temp "game" directory with the local addon file
        var gameDir = Path.Combine(_tempRoot, "Game_" + addonFileName.Replace(".", "_"));
        Directory.CreateDirectory(gameDir);
        var localFilePath = Path.Combine(gameDir, addonFileName);
        File.WriteAllBytes(localFilePath, realFileBytes);

        // Create the installed mod record — RemoteFileSize was stored at install time
        // from the actual download (uncompressed), so it equals realFileSize
        var record = new InstalledModRecord
        {
            GameName = "TestGame",
            InstallPath = gameDir,
            AddonFileName = addonFileName,
            SnapshotUrl = snapshotUrl,
            RemoteFileSize = realFileSize,  // stored at install time from real download
            InstalledAt = DateTime.UtcNow,
        };

        // FakeHttpMessageHandler:
        // - HEAD returns compressed Content-Length (differs from stored RemoteFileSize)
        // - GET returns the real file bytes (same size as local file)
        var fakeHandler = new FakeHttpMessageHandler(compressedSize, realFileBytes);
        var httpClient = new HttpClient(fakeHandler);
        var service = new ModInstallService(httpClient);

        // Act: check for update
        var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

        // Assert: the file hasn't changed, so there should be NO update.
        // On UNFIXED code: HEAD returns compressedSize (38000) != RemoteFileSize (50000)
        //   → returns true (false positive!) → this assertion FAILS → proves the bug
        // On FIXED code: download-based path compares real sizes → returns false → PASSES
        return !hasUpdate;
    }

    /// <summary>
    /// Property 1b: Same-size update detection — clshortfuse.github.io URLs detect
    /// updates even when the remote file has the same byte count but different content.
    ///
    /// **Validates: Requirements 2.1**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property BugCondition_ClshortfuseUrls_SameSizeDifferentContent_DetectsUpdate()
    {
        var genFileName = Gen.Elements(AddonFileNames);

        return Prop.ForAll(
            Arb.From(genFileName),
            (string addonFileName) =>
            {
                var fileSize = 50_000;

                // Local file (old version)
                var localBytes = new byte[fileSize];
                new System.Random(42).NextBytes(localBytes);

                // Remote file (new version) — same size, different content
                var remoteBytes = new byte[fileSize];
                new System.Random(99).NextBytes(remoteBytes);

                var snapshotUrl = $"https://clshortfuse.github.io/renodx/{addonFileName}";

                var gameDir = Path.Combine(_tempRoot, "GameSameSize_" + addonFileName.Replace(".", "_"));
                Directory.CreateDirectory(gameDir);
                File.WriteAllBytes(Path.Combine(gameDir, addonFileName), localBytes);

                var record = new InstalledModRecord
                {
                    GameName = "TestGame",
                    InstallPath = gameDir,
                    AddonFileName = addonFileName,
                    SnapshotUrl = snapshotUrl,
                    RemoteFileSize = fileSize,
                    InstalledAt = DateTime.UtcNow,
                };

                var fakeHandler = new FakeHttpMessageHandler(fileSize, remoteBytes);
                var httpClient = new HttpClient(fakeHandler);
                var service = new ModInstallService(httpClient);

                var hasUpdate = service.CheckForUpdateAsync(record).GetAwaiter().GetResult();

                // Remote content differs → should detect update
                return hasUpdate;
            });
    }

    /// <summary>
    /// Fake HttpMessageHandler that simulates GitHub Pages CDN behavior:
    /// - HEAD requests return a Content-Length reflecting compressed transfer size
    /// - GET requests return the real (uncompressed) file bytes
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly long _headContentLength;
        private readonly byte[] _getResponseBytes;

        public FakeHttpMessageHandler(long headContentLength, byte[] getResponseBytes)
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
                // Simulate compressed Content-Length from CDN
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = _headContentLength;
            }
            else if (request.Method == HttpMethod.Get)
            {
                // Return real file bytes (uncompressed)
                response.Content = new ByteArrayContent(_getResponseBytes);
                response.Content.Headers.ContentLength = _getResponseBytes.Length;
            }

            return Task.FromResult(response);
        }
    }
}
