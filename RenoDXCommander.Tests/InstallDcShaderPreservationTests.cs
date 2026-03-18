using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests verifying that <c>InstallDcAsync</c> deploys shaders locally
/// to the game folder via <c>SyncGameFolder</c> and never calls <c>SyncDcFolder</c>.
///
/// **Validates: Requirements 4.1, 4.2, 4.3**
/// </summary>
[Collection("StaticShaderMode")]
public class InstallDcShaderPreservationTests : IDisposable
{
    private readonly string _tempRoot;

    public InstallDcShaderPreservationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcDcPres_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// After <c>InstallDcAsync</c>, <c>SyncGameFolder</c> SHALL be called with
    /// the game's install path only when dcModeLevel &gt; 0.
    /// When dcModeLevel == 0, SyncGameFolder SHALL NOT be called.
    ///
    /// **Validates: Requirements 1.1, 1.2, 4.1, 4.3**
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InstallDcAsync_CallsSyncGameFolder_ForNonZeroDcModeLevels(int dcModeLevel)
    {
        var installPath = Path.Combine(_tempRoot, $"Game_SyncLocal_{dcModeLevel}");
        Directory.CreateDirectory(installPath);

        // Pre-seed the DC cache file so the download path is skipped
        Directory.CreateDirectory(AuxInstallService.DownloadCacheDir);
        var cachePath = Path.Combine(AuxInstallService.DownloadCacheDir, AuxInstallService.DcCacheFile);
        var fakeBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(cachePath, fakeBytes);

        var tracker = new TrackingShaderPackService();
        var handler = new FakeHttpMessageHandler(fakeBytes);
        using var http = new HttpClient(handler);
        var sut = new AuxInstallService(http, tracker);

        await sut.InstallDcAsync(
            gameName: "TestGame",
            installPath: installPath,
            dcModeLevel: dcModeLevel);

        Assert.True(tracker.SyncGameFolderCalled,
            $"SyncGameFolder should be called for dcModeLevel={dcModeLevel}");
        Assert.Equal(installPath, tracker.SyncGameFolderDir);
    }

    /// <summary>
    /// After <c>InstallDcAsync</c> with dcModeLevel == 0, <c>SyncGameFolder</c>
    /// SHALL NOT be called — shaders are only deployed in DC Mode.
    ///
    /// **Validates: Requirements 1.2, 1.3, 5.1**
    /// </summary>
    [Fact]
    public async Task InstallDcAsync_DoesNotCallSyncGameFolder_ForDcModeLevel0()
    {
        var installPath = Path.Combine(_tempRoot, "Game_SyncLocal_0");
        Directory.CreateDirectory(installPath);

        Directory.CreateDirectory(AuxInstallService.DownloadCacheDir);
        var cachePath = Path.Combine(AuxInstallService.DownloadCacheDir, AuxInstallService.DcCacheFile);
        var fakeBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(cachePath, fakeBytes);

        var tracker = new TrackingShaderPackService();
        var handler = new FakeHttpMessageHandler(fakeBytes);
        using var http = new HttpClient(handler);
        var sut = new AuxInstallService(http, tracker);

        await sut.InstallDcAsync(
            gameName: "TestGame",
            installPath: installPath,
            dcModeLevel: 0);

        Assert.False(tracker.SyncGameFolderCalled,
            "SyncGameFolder should NOT be called for dcModeLevel=0");
    }

    /// <summary>
    /// After <c>InstallDcAsync</c>, <c>SyncDcFolder</c> SHALL NOT be called —
    /// shaders are no longer routed to the DC AppData folder.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InstallDcAsync_DoesNotCallSyncDcFolder_ForAllDcModeLevels(int dcModeLevel)
    {
        var installPath = Path.Combine(_tempRoot, $"Game_NoDcSync_{dcModeLevel}");
        Directory.CreateDirectory(installPath);

        // Pre-seed the DC cache file so the download path is skipped
        Directory.CreateDirectory(AuxInstallService.DownloadCacheDir);
        var cachePath = Path.Combine(AuxInstallService.DownloadCacheDir, AuxInstallService.DcCacheFile);
        var fakeBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(cachePath, fakeBytes);

        var tracker = new TrackingShaderPackService();
        var handler = new FakeHttpMessageHandler(fakeBytes);
        using var http = new HttpClient(handler);
        var sut = new AuxInstallService(http, tracker);

        await sut.InstallDcAsync(
            gameName: "TestGame",
            installPath: installPath,
            dcModeLevel: dcModeLevel);

        Assert.False(tracker.SyncDcFolderCalled,
            $"SyncDcFolder should NOT be called for dcModeLevel={dcModeLevel}");
    }

    /// <summary>
    /// When a <c>shaderModeOverride</c> is provided and dcModeLevel &gt; 0,
    /// <c>SyncGameFolder</c> SHALL be called.
    ///
    /// **Validates: Requirements 4.3, 7.3**
    /// </summary>
    [Fact]
    public async Task InstallDcAsync_UsesShaderModeOverride_WhenProvided()
    {
        var installPath = Path.Combine(_tempRoot, "Game_Override");
        Directory.CreateDirectory(installPath);

        Directory.CreateDirectory(AuxInstallService.DownloadCacheDir);
        var cachePath = Path.Combine(AuxInstallService.DownloadCacheDir, AuxInstallService.DcCacheFile);
        var fakeBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(cachePath, fakeBytes);

        var tracker = new TrackingShaderPackService();
        var handler = new FakeHttpMessageHandler(fakeBytes);
        using var http = new HttpClient(handler);
        var sut = new AuxInstallService(http, tracker);

        await sut.InstallDcAsync(
            gameName: "TestGame",
            installPath: installPath,
            dcModeLevel: 1,
            shaderModeOverride: "All");

        Assert.True(tracker.SyncGameFolderCalled);
    }

    // ── Tracking IShaderPackService ───────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool SyncGameFolderCalled { get; private set; }
        public string? SyncGameFolderDir { get; private set; }

        public bool SyncDcFolderCalled { get; private set; }

        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } =
            new List<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToDcFolder() { }
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }
        public void RemoveFromGameFolder(string gameDir) { }
        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) { }

        public void SyncDcFolder(IEnumerable<string>? selectedPackIds = null)
        {
            SyncDcFolderCalled = true;
        }

        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
        {
            SyncGameFolderCalled = true;
            SyncGameFolderDir = gameDir;
        }

        public void SyncShadersToAllLocations(
            IEnumerable<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode, string? shaderModeOverride)> locations,
            IEnumerable<string>? selectedPackIds = null) { }
    }

    // ── Fake HttpMessageHandler ───────────────────────────────────────────────

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
