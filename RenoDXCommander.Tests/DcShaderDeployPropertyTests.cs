using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property tests for DC shader deploy on install.
/// These tests verify that <c>SyncGameFolder</c> is called during
/// <c>InstallDcAsync</c> only when <c>dcModeLevel &gt; 0</c> (DC Mode),
/// and <c>RemoveFromGameFolder</c> is NOT called (shaders are preserved).
///
/// EXPECTED OUTCOME: All tests PASS.
/// </summary>
[Collection("StaticShaderMode")]
public class DcShaderDeployPropertyTests : IDisposable
{
    private readonly string _tempRoot;

    public DcShaderDeployPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcDcProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Property 1: SyncGameFolder called iff dcModeLevel > 0 ──────────────────

    /// <summary>
    /// For any <c>dcModeLevel</c> in {0, 1, 2}, calling <c>InstallDcAsync</c> SHALL
    /// invoke <c>SyncGameFolder</c> if and only if <c>dcModeLevel &gt; 0</c>.
    /// When <c>dcModeLevel == 0</c>, <c>SyncGameFolder</c> SHALL NOT be called.
    /// <c>SyncDcFolder</c> SHALL NOT be called for any level.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 5.1**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property SyncGameFolder_CalledOnlyWhenDcModeLevel_GreaterThanZero()
    {
        var genLevel = Gen.Elements(0, 1, 2);

        return Prop.ForAll(
            Arb.From(genLevel),
            (int dcModeLevel) =>
            {
                var installPath = Path.Combine(_tempRoot, $"Game_Sync_{dcModeLevel}_{Guid.NewGuid():N}"[..40]);
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

                sut.InstallDcAsync(
                    gameName: "TestGame",
                    installPath: installPath,
                    dcModeLevel: dcModeLevel).GetAwaiter().GetResult();

                var expectSyncCalled = dcModeLevel > 0;
                var gameFolderCalled = tracker.SyncGameFolderCalled;
                var correctCallState = gameFolderCalled == expectSyncCalled;
                var correctDir = !gameFolderCalled || tracker.SyncGameFolderDir == installPath;
                var dcFolderNotCalled = !tracker.SyncDcFolderCalled;

                return (correctCallState && correctDir && dcFolderNotCalled)
                    .Label($"dcModeLevel={dcModeLevel}: SyncGameFolderCalled={gameFolderCalled} " +
                           $"(expected {expectSyncCalled}), " +
                           $"dir={tracker.SyncGameFolderDir ?? "null"} (expected {installPath}), " +
                           $"SyncDcFolderCalled={tracker.SyncDcFolderCalled} (expected false)");
            });
    }

    // ── Property 1 (cont.): RemoveFromGameFolder NOT called during InstallDcAsync ──

    /// <summary>
    /// For any <c>dcModeLevel</c> in {0, 1, 2}, calling <c>InstallDcAsync</c> SHALL NOT
    /// invoke <c>RemoveFromGameFolder</c> — game-local shaders are preserved during DC install.
    ///
    /// **Validates: Requirements 1.3, 5.1**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property RemoveFromGameFolder_NotCalledDuringDcInstall()
    {
        var genLevel = Gen.Elements(0, 1, 2);

        return Prop.ForAll(
            Arb.From(genLevel),
            (int dcModeLevel) =>
            {
                var installPath = Path.Combine(_tempRoot, $"Game_Rm_{dcModeLevel}_{Guid.NewGuid():N}"[..40]);
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

                sut.InstallDcAsync(
                    gameName: "TestGame",
                    installPath: installPath,
                    dcModeLevel: dcModeLevel).GetAwaiter().GetResult();

                var notCalled = !tracker.RemoveFromGameFolderCalled;

                return notCalled
                    .Label($"dcModeLevel={dcModeLevel}: RemoveFromGameFolderCalled={tracker.RemoveFromGameFolderCalled} " +
                           $"(expected false — shaders should be preserved)");
            });
    }

    // ── Tracking IShaderPackService ───────────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool SyncDcFolderCalled { get; private set; }
        public bool SyncGameFolderCalled { get; private set; }
        public string? SyncGameFolderDir { get; private set; }
        public bool RemoveFromGameFolderCalled { get; private set; }
        public string? RemoveFromGameFolderDir { get; private set; }
        public bool RestoreOriginalIfPresentCalled { get; private set; }

        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } =
            new List<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;

        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToDcFolder() { }
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }

        public void RemoveFromGameFolder(string gameDir)
        {
            RemoveFromGameFolderCalled = true;
            RemoveFromGameFolderDir = gameDir;
        }

        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir)
        {
            RestoreOriginalIfPresentCalled = true;
        }

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
            IEnumerable<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode,
                string? shaderModeOverride)> locations,
            IEnumerable<string>? selectedPackIds = null) { }
    }

    // ── Fake HttpMessageHandler ───────────────────────────────────────────────────

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
