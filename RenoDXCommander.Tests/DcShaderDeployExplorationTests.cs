using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Bug condition exploration test for DC shader deploy on install.
/// When <c>dcModeLevel == 0</c>, <c>InstallDcAsync</c> removes the game-local
/// <c>reshade-shaders</c> folder but skips <c>SyncDcFolder</c>, leaving the user
/// with no shaders anywhere.
///
/// **Validates: Requirements 1.1, 2.1**
///
/// EXPECTED OUTCOME on UNFIXED code: Test FAILS (confirms the bug exists).
/// After the fix is applied, this test should PASS.
/// </summary>
public class DcShaderDeployExplorationTests : IDisposable
{
    private readonly string _tempRoot;

    public DcShaderDeployExplorationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcDcTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// Calls <c>InstallDcAsync</c> with <c>dcModeLevel=0</c> and asserts that
    /// <c>SyncDcFolder</c> was called. On unfixed code the <c>if (dcModeLevel > 0)</c>
    /// guard skips the call, so this test MUST FAIL — confirming the bug.
    ///
    /// **Validates: Requirements 1.1, 2.1**
    /// </summary>
    [Fact]
    public async Task BugCondition_DcModeLevel0_ShouldCallSyncDcFolder()
    {
        // Arrange
        var installPath = Path.Combine(_tempRoot, "GameFolder");
        Directory.CreateDirectory(installPath);

        // Ensure the download cache directory exists
        Directory.CreateDirectory(AuxInstallService.DownloadCacheDir);

        // Pre-seed the DC cache file so the download path is skipped
        var cachePath = Path.Combine(AuxInstallService.DownloadCacheDir, AuxInstallService.DcCacheFile);
        var fakeBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(cachePath, fakeBytes);

        // Set a known global shader mode
        ShaderPackService.CurrentMode = ShaderPackService.DeployMode.Minimum;

        // Create a tracking shader pack service to record calls
        var tracker = new TrackingShaderPackService();

        // Use a mock HttpMessageHandler that returns a fake HEAD response
        // so the HEAD/Range requests don't fail with network errors.
        // The cache file is pre-seeded, so the actual GET download is skipped.
        var handler = new FakeHttpMessageHandler(fakeBytes);
        using var http = new HttpClient(handler);

        var sut = new AuxInstallService(http, tracker);

        // Act
        await sut.InstallDcAsync(
            gameName: "TestGame",
            installPath: installPath,
            dcModeLevel: 0);

        // Assert — on unfixed code, SyncDcFolder is NOT called when dcModeLevel == 0
        Assert.True(
            tracker.SyncDcFolderCalled,
            "SyncDcFolder should be called during DC install even when dcModeLevel == 0, " +
            "but it was NOT called — confirming the bug.");
        Assert.Equal(ShaderPackService.DeployMode.Minimum, tracker.SyncDcFolderMode);
    }

    // ── Tracking IShaderPackService ───────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool SyncDcFolderCalled { get; private set; }
        public ShaderPackService.DeployMode? SyncDcFolderMode { get; private set; }
        public bool RemoveFromGameFolderCalled { get; private set; }
        public string? RemoveFromGameFolderDir { get; private set; }

        public IReadOnlyList<(string Id, string DisplayName)> AvailablePacks { get; } =
            new List<(string, string)>();

        public string? GetPackDescription(string packId) => null;

        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToDcFolder(ShaderPackService.DeployMode? mode = null) { }
        public void DeployToGameFolder(string gameDir, ShaderPackService.DeployMode? mode = null) { }

        public void RemoveFromGameFolder(string gameDir)
        {
            RemoveFromGameFolderCalled = true;
            RemoveFromGameFolderDir = gameDir;
        }

        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) { }

        public void SyncDcFolder(ShaderPackService.DeployMode m, IEnumerable<string>? selectedPackIds = null)
        {
            SyncDcFolderCalled = true;
            SyncDcFolderMode = m;
        }

        public void SyncGameFolder(string gameDir, ShaderPackService.DeployMode m,
            IEnumerable<string>? selectedPackIds = null) { }

        public void SyncShadersToAllLocations(
            IEnumerable<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode,
                string? shaderModeOverride)> locations,
            ShaderPackService.DeployMode? mode = null,
            IEnumerable<string>? selectedPackIds = null) { }
    }

    // ── Fake HttpMessageHandler ───────────────────────────────────────────────

    /// <summary>
    /// Returns a successful response with the given content for any request.
    /// HEAD requests return Content-Length matching the content size.
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        public FakeHttpMessageHandler(byte[] content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

            if (request.Method == HttpMethod.Head)
            {
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = _content.Length;
            }
            else
            {
                response.Content = new ByteArrayContent(_content);
                response.Content.Headers.ContentLength = _content.Length;
            }

            return Task.FromResult(response);
        }
    }
}
