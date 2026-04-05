using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that InstallReShadeAsync always produces a standard
/// filename (dxgi.dll or the DLL override) and never ReShade64.dll or ReShade32.dll.
///
/// Feature: dc-removal, Property 7: ReShade installs as standard filename
/// **Validates: Requirements 12.2**
/// </summary>
[Collection("StaticShaderMode")]
public class ReShadeStandardFilenamePropertyTests : IDisposable
{
    private readonly string _tempRoot;

    public ReShadeStandardFilenamePropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcRsFilename_" + Guid.NewGuid().ToString("N")[..8]);
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

    /// <summary>
    /// Known DLL override filenames that games may use instead of dxgi.dll.
    /// </summary>
    private static readonly string[] OverrideNames =
    [
        "d3d11.dll",
        "dinput8.dll",
        "version.dll",
        "winmm.dll",
    ];

    /// <summary>
    /// Generator that produces (use32Bit, filenameOverride) combinations.
    /// filenameOverride is null (standard dxgi.dll) or one of the known override names.
    /// </summary>
    private static readonly Gen<(bool Use32Bit, string? FilenameOverride)> GenConfig =
        from use32Bit in Arb.Generate<bool>()
        from hasOverride in Arb.Generate<bool>()
        from overrideIdx in Gen.Choose(0, OverrideNames.Length - 1)
        let filenameOverride = hasOverride ? OverrideNames[overrideIdx] : null
        select (use32Bit, filenameOverride);

    // ── Property 7: ReShade installs as standard filename ─────────────────────
    // Feature: dc-removal, Property 7: ReShade installs as standard filename
    // **Validates: Requirements 12.2**
    [Property(MaxTest = 100)]
    public Property InstallReShadeAsync_NeverUsesReShade64OrReShade32()
    {
        return Prop.ForAll(GenConfig.ToArbitrary(), config =>
        {
            var installPath = Path.Combine(_tempRoot, $"game_{Guid.NewGuid():N}");
            Directory.CreateDirectory(installPath);

            var tracker = new TrackingShaderPackService();
            var handler = new FakeHttpMessageHandler(new byte[] { 0xDE, 0xAD });
            using var http = new HttpClient(handler);
            var sut = new AuxInstallService(http, tracker);

            try
            {
                var record = sut.InstallReShadeAsync(
                    gameName: "TestGame",
                    installPath: installPath,
                    use32Bit: config.Use32Bit,
                    filenameOverride: config.FilenameOverride).GetAwaiter().GetResult();

                var installedAs = record.InstalledAs;

                var isReShade64 = string.Equals(installedAs, "ReShade64.dll", StringComparison.OrdinalIgnoreCase);
                var isReShade32 = string.Equals(installedAs, "ReShade32.dll", StringComparison.OrdinalIgnoreCase);

                var expectedName = !string.IsNullOrWhiteSpace(config.FilenameOverride)
                    ? config.FilenameOverride
                    : AuxInstallService.RsNormalName;

                return (!isReShade64 && !isReShade32)
                    .Label($"InstalledAs was '{installedAs}' (use32Bit={config.Use32Bit}, override={config.FilenameOverride ?? "null"}) — expected '{expectedName}', must never be ReShade64.dll or ReShade32.dll");
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
        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } =
            new List<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;
        public string[] GetRequiredPacks(string packId) => Array.Empty<string>();
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }
        public void RemoveFromGameFolder(string gameDir) { }
        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) { }

        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null) { }

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
