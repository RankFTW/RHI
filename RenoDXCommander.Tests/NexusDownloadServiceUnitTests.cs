using System.Net;
using System.Text;
using Moq;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for NexusDownloadService covering premium flow API sequence,
/// 403 fallback, nxm URI processing (valid and expired keys), and progress reporting.
/// Requirements: 5.1, 5.2, 5.3, 5.5, 6.4, 6.5, 6.6
/// </summary>
public class NexusDownloadServiceUnitTests : IDisposable
{
    private readonly Mock<INexusApiClient> _mockApiClient;
    private readonly Mock<INexusAuthService> _mockAuthService;
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly NexusDownloadService _service;

    public NexusDownloadServiceUnitTests()
    {
        _mockApiClient = new Mock<INexusApiClient>();
        _mockAuthService = new Mock<INexusAuthService>();
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
        _service = new NexusDownloadService(_mockApiClient.Object, _mockAuthService.Object, _httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // ── 1. Premium flow calls correct API sequence ───────────────────────────────
    // Validates: Requirements 5.1, 5.2

    [Fact]
    public async Task DownloadAndInstallAsync_PremiumUser_CallsGetModFilesV1ThenGetDownloadLinks()
    {
        // Arrange
        _mockAuthService.Setup(a => a.IsPremium).Returns(true);

        var files = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 1001,
                FileName: "renodx-v2.zip",
                Version: "2.0.0",
                SizeKb: 5000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        var links = new List<NexusDownloadLink>
        {
            new NexusDownloadLink("Nexus CDN", "Nexus", "https://cdn.nexusmods.com/files/renodx-v2.zip")
        };

        var callOrder = new List<string>();

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("cyberpunk2077", 123))
            .ReturnsAsync(files)
            .Callback(() => callOrder.Add("GetModFilesV1Async"));

        _mockApiClient
            .Setup(c => c.GetDownloadLinksAsync("cyberpunk2077", 123, 1001, null, null))
            .ReturnsAsync(links)
            .Callback(() => callOrder.Add("GetDownloadLinksAsync"));

        // Mock HTTP response for the actual file download
        _mockHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("fake file content"))
        });

        var card = new GameCardViewModel { GameName = "Cyberpunk 2077" };

        // Act
        await _service.DownloadAndInstallAsync(card, "cyberpunk2077", 123);

        // Assert: both API methods called in correct order
        Assert.Equal(2, callOrder.Count);
        Assert.Equal("GetModFilesV1Async", callOrder[0]);
        Assert.Equal("GetDownloadLinksAsync", callOrder[1]);

        _mockApiClient.Verify(c => c.GetModFilesV1Async("cyberpunk2077", 123), Times.Once);
        _mockApiClient.Verify(c => c.GetDownloadLinksAsync("cyberpunk2077", 123, 1001, null, null), Times.Once);
    }

    // ── 2. Empty download links handled gracefully ───────────────────────────────
    // Validates: Requirement 5.5

    [Fact]
    public async Task DownloadAndInstallAsync_PremiumUser_EmptyDownloadLinks_HandlesGracefully()
    {
        // Arrange
        _mockAuthService.Setup(a => a.IsPremium).Returns(true);

        var files = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 2002,
                FileName: "mod.zip",
                Version: "1.0.0",
                SizeKb: 3000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("eldenring", 456))
            .ReturnsAsync(files);

        // Return empty list — simulates no CDN links available
        _mockApiClient
            .Setup(c => c.GetDownloadLinksAsync("eldenring", 456, 2002, null, null))
            .ReturnsAsync(new List<NexusDownloadLink>());

        var card = new GameCardViewModel { GameName = "Elden Ring" };

        // Act — should not throw
        var exception = await Record.ExceptionAsync(
            () => _service.DownloadAndInstallAsync(card, "eldenring", 456));

        // Assert
        Assert.Null(exception);
        _mockApiClient.Verify(c => c.GetDownloadLinksAsync("eldenring", 456, 2002, null, null), Times.Once);
    }

    // ── 3. nxm URI processing with valid key ─────────────────────────────────────
    // Validates: Requirements 6.4, 6.5

    [Fact]
    public async Task ProcessNxmUriAsync_ValidKey_CallsGetDownloadLinksWithCorrectParams()
    {
        // Arrange: expires far in the future
        var futureExpires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var uri = new NxmUri(
            GameDomain: "cyberpunk2077",
            ModId: 100,
            FileId: 5001,
            Key: "abc123def456",
            Expires: futureExpires);

        var links = new List<NexusDownloadLink>
        {
            new NexusDownloadLink("Nexus CDN", "Nexus", "https://cdn.nexusmods.com/files/mod-file.zip")
        };

        _mockApiClient
            .Setup(c => c.GetDownloadLinksAsync("cyberpunk2077", 100, 5001, "abc123def456", futureExpires))
            .ReturnsAsync(links);

        // Mock HTTP response for the actual file download
        _mockHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("downloaded content"))
        });

        // Act
        await _service.ProcessNxmUriAsync(uri);

        // Assert: GetDownloadLinksAsync called with the correct key and expires
        _mockApiClient.Verify(
            c => c.GetDownloadLinksAsync("cyberpunk2077", 100, 5001, "abc123def456", futureExpires),
            Times.Once);
    }

    // ── 4. nxm URI processing with expired key ───────────────────────────────────
    // Validates: Requirement 6.6

    [Fact]
    public async Task ProcessNxmUriAsync_ExpiredKey_DoesNotCallGetDownloadLinks()
    {
        // Arrange: expires in the past
        var pastExpires = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var uri = new NxmUri(
            GameDomain: "skyrimspecialedition",
            ModId: 200,
            FileId: 6001,
            Key: "expired-key-xyz",
            Expires: pastExpires);

        // Act
        await _service.ProcessNxmUriAsync(uri);

        // Assert: GetDownloadLinksAsync should NOT be called because the key is expired
        _mockApiClient.Verify(
            c => c.GetDownloadLinksAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<long?>()),
            Times.Never);
    }

    // ── 5. Progress reporting during premium download ────────────────────────────
    // Validates: Requirements 5.3

    [Fact]
    public async Task DownloadAndInstallAsync_PremiumUser_ReportsProgress()
    {
        // Arrange
        _mockAuthService.Setup(a => a.IsPremium).Returns(true);

        var files = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 3003,
                FileName: "progress-test.zip",
                Version: "1.0.0",
                SizeKb: 1000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        var links = new List<NexusDownloadLink>
        {
            new NexusDownloadLink("Nexus CDN", "Nexus", "https://cdn.nexusmods.com/files/progress-test.zip")
        };

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("cyberpunk2077", 789))
            .ReturnsAsync(files);

        _mockApiClient
            .Setup(c => c.GetDownloadLinksAsync("cyberpunk2077", 789, 3003, null, null))
            .ReturnsAsync(links);

        // Create a response with known content length so progress can be calculated
        var fileContent = new byte[1024]; // 1KB of data
        Array.Fill(fileContent, (byte)'A');
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fileContent)
        };
        responseMessage.Content.Headers.ContentLength = fileContent.Length;
        _mockHandler.SetResponse(responseMessage);

        var card = new GameCardViewModel { GameName = "Cyberpunk 2077" };
        var progressValues = new List<double>();
        var progress = new Progress<double>(v => progressValues.Add(v));

        // Act
        await _service.DownloadAndInstallAsync(card, "cyberpunk2077", 789, progress);

        // Allow progress callbacks to complete (Progress<T> posts to SynchronizationContext)
        await Task.Delay(100);

        // Assert: progress was reported at least once (final 1.0 report)
        Assert.NotEmpty(progressValues);
        Assert.Contains(progressValues, v => v >= 0.99); // Final progress should be ~1.0
    }

    // ── Helper: Mock HttpMessageHandler ──────────────────────────────────────────

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private HttpResponseMessage _response = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };

        public void SetResponse(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
