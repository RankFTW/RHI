using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests verifying that <c>InstallReShadeAsync</c> always deploys
/// shaders locally via <c>SyncGameFolder</c>.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
/// </summary>
[Collection("StaticShaderMode")]
public class InstallReShadeLocalDeployPropertyTests : IDisposable
{
    private readonly string _tempRoot;

    public InstallReShadeLocalDeployPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcRsDeploy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);

        // Ensure staged ReShade DLLs exist (InstallReShadeAsync copies from these)
        Directory.CreateDirectory(AuxInstallService.RsStagingDir);
        var fakeRsDll = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        if (!File.Exists(AuxInstallService.RsStagedPath64))
            File.WriteAllBytes(AuxInstallService.RsStagedPath64, fakeRsDll);
        if (!File.Exists(AuxInstallService.RsStagedPath32))
            File.WriteAllBytes(AuxInstallService.RsStagedPath32, fakeRsDll);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Property: InstallReShadeAsync always calls SyncGameFolder ─

    /// <summary>
    /// **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
    ///
    /// For any game install, <c>InstallReShadeAsync</c> SHALL call <c>SyncGameFolder</c>
    /// with the game's install path.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property InstallReShadeAsync_AlwaysDeploysLocally()
    {
        var gen = from suffix in Gen.Choose(1, 999999)
                  select suffix;

        return Prop.ForAll(gen.ToArbitrary(), suffix =>
        {
            var installPath = Path.Combine(_tempRoot, $"game_{suffix}");
            Directory.CreateDirectory(installPath);

            var tracker = new TrackingShaderPackService();
            var handler = new FakeHttpMessageHandler(new byte[] { 0xDE, 0xAD });
            using var http = new HttpClient(handler);
            var sut = new AuxInstallService(http, tracker);

            try
            {
                sut.InstallReShadeAsync(
                    gameName: "TestGame",
                    installPath: installPath).GetAwaiter().GetResult();

                if (!tracker.SyncGameFolderCalled)
                    return false.Label(
                        $"SyncGameFolder was NOT called");

                if (tracker.SyncGameFolderDir != installPath)
                    return false.Label(
                        $"SyncGameFolder called with wrong path: expected '{installPath}', " +
                        $"got '{tracker.SyncGameFolderDir}'");

                return true.Label("OK");
            }
            finally
            {
                try { Directory.Delete(installPath, recursive: true); } catch { }
            }
        });
    }

    // ── Tracking IShaderPackService ───────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool SyncGameFolderCalled { get; private set; }
        public string? SyncGameFolderDir { get; private set; }

        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } =
            new List<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;
        public string[] GetRequiredPacks(string packId) => Array.Empty<string>();
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }
        public void RemoveFromGameFolder(string gameDir) { }
        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) { }

        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
        {
            SyncGameFolderCalled = true;
            SyncGameFolderDir = gameDir;
        }

        public void SyncShadersToAllLocations(
            IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations,
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
