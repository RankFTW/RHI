using Moq;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for NexusUpdateChecker covering update detection, cache bypass,
/// rate limit handling, and skipping mods with null/unparseable NexusUrl.
/// Requirements: 3.1, 3.2, 3.4, 3.5, 3.6
/// </summary>
public class NexusUpdateCheckerUnitTests
{
    private readonly Mock<INexusApiClient> _mockApiClient;

    public NexusUpdateCheckerUnitTests()
    {
        _mockApiClient = new Mock<INexusApiClient>();
        // Default: not rate limited
        _mockApiClient.Setup(c => c.IsRateLimited).Returns(false);
    }

    // ── Helper to create a GameCardViewModel with a NexusUrl and installed version ──

    private static GameCardViewModel CreateCard(
        string? nexusUrl,
        string? installedVersion = null)
    {
        var card = new GameCardViewModel
        {
            GameName = "TestGame",
            NexusUrl = nexusUrl,
            RdxInstalledVersion = installedVersion
        };
        return card;
    }

    // ── 1. Update detection with version mismatch ────────────────────────────────
    // Validates: Requirements 3.1, 3.2

    [Fact]
    public async Task CheckAllAsync_VersionMismatch_SetsUpdateAvailable()
    {
        // Arrange: card has local version "1.0.0", remote returns "2.0.0"
        var card = CreateCard(
            nexusUrl: "https://www.nexusmods.com/cyberpunk2077/mods/123",
            installedVersion: "1.0.0");

        var remoteFiles = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 1001,
                FileName: "mod-v2.zip",
                Version: "2.0.0",
                SizeKb: 5000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("cyberpunk2077", 123))
            .ReturnsAsync(remoteFiles);

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card });

        // Assert
        Assert.True(card.IsNexusUpdateAvailable);
        _mockApiClient.Verify(c => c.GetModFilesV1Async("cyberpunk2077", 123), Times.Once);
    }

    [Fact]
    public async Task CheckAllAsync_VersionMatch_DoesNotSetUpdateAvailable()
    {
        // Arrange: card has local version "1.0.0", remote also returns "1.0.0"
        var card = CreateCard(
            nexusUrl: "https://www.nexusmods.com/cyberpunk2077/mods/123",
            installedVersion: "1.0.0");

        var remoteFiles = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 1001,
                FileName: "mod-v1.zip",
                Version: "1.0.0",
                SizeKb: 5000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("cyberpunk2077", 123))
            .ReturnsAsync(remoteFiles);

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card });

        // Assert
        Assert.False(card.IsNexusUpdateAvailable);
    }

    // ── 2. Cache bypass on manual refresh ────────────────────────────────────────
    // Validates: Requirements 3.4, 3.5

    [Fact]
    public async Task CheckAllAsync_CacheBypass_CallsApiTwice()
    {
        // Arrange: use a short TTL so the cache would normally be valid
        var card = CreateCard(
            nexusUrl: "https://www.nexusmods.com/skyrimspecialedition/mods/42",
            installedVersion: "1.0.0");

        var remoteFiles = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 500,
                FileName: "mod.zip",
                Version: "1.1.0",
                SizeKb: 3000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("skyrimspecialedition", 42))
            .ReturnsAsync(remoteFiles);

        // Use a long TTL so the first call's cache would still be valid
        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act: first call populates cache
        await checker.CheckAllAsync(new[] { card }, bypassCache: false);

        // Second call with bypassCache=true should call the API again
        await checker.CheckAllAsync(new[] { card }, bypassCache: true);

        // Assert: API called twice (not served from cache on second call)
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async("skyrimspecialedition", 42),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CheckAllAsync_WithoutCacheBypass_UsesCache()
    {
        // Arrange
        var card = CreateCard(
            nexusUrl: "https://www.nexusmods.com/skyrimspecialedition/mods/42",
            installedVersion: "1.0.0");

        var remoteFiles = new List<NexusFileInfo>
        {
            new NexusFileInfo(
                FileId: 500,
                FileName: "mod.zip",
                Version: "1.1.0",
                SizeKb: 3000,
                Category: "MAIN",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("skyrimspecialedition", 42))
            .ReturnsAsync(remoteFiles);

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act: first call populates cache
        await checker.CheckAllAsync(new[] { card }, bypassCache: false);

        // Second call without bypass should use cache
        await checker.CheckAllAsync(new[] { card }, bypassCache: false);

        // Assert: API called only once (second call served from cache)
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async("skyrimspecialedition", 42),
            Times.Once);
    }

    // ── 3. Rate limit handling (stop on 429) ─────────────────────────────────────
    // Validates: Requirement 3.6

    [Fact]
    public async Task CheckAllAsync_RateLimited_StopsProcessingCards()
    {
        // Arrange: API client reports rate limited from the start
        _mockApiClient.Setup(c => c.IsRateLimited).Returns(true);

        var card1 = CreateCard(
            nexusUrl: "https://www.nexusmods.com/cyberpunk2077/mods/100",
            installedVersion: "1.0.0");

        var card2 = CreateCard(
            nexusUrl: "https://www.nexusmods.com/eldenring/mods/200",
            installedVersion: "1.0.0");

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card1, card2 });

        // Assert: API should never be called because rate limit was hit immediately
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAllAsync_RateLimitedMidway_StopsRemainingCards()
    {
        // Arrange: rate limit kicks in after the first card is processed
        var callCount = 0;
        _mockApiClient
            .Setup(c => c.IsRateLimited)
            .Returns(() => callCount >= 1);

        var card1 = CreateCard(
            nexusUrl: "https://www.nexusmods.com/cyberpunk2077/mods/100",
            installedVersion: "1.0.0");

        var card2 = CreateCard(
            nexusUrl: "https://www.nexusmods.com/eldenring/mods/200",
            installedVersion: "1.0.0");

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("cyberpunk2077", 100))
            .ReturnsAsync(new List<NexusFileInfo>
            {
                new NexusFileInfo(1, "mod.zip", "2.0.0", 1000, "MAIN", DateTimeOffset.UtcNow)
            })
            .Callback(() => callCount++);

        _mockApiClient
            .Setup(c => c.GetModFilesV1Async("eldenring", 200))
            .ReturnsAsync(new List<NexusFileInfo>
            {
                new NexusFileInfo(2, "mod2.zip", "3.0.0", 2000, "MAIN", DateTimeOffset.UtcNow)
            });

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card1, card2 });

        // Assert: first card was processed, second was skipped due to rate limit
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async("cyberpunk2077", 100), Times.Once);
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async("eldenring", 200), Times.Never);
    }

    // ── 4. Skipping mods with null/unparseable NexusUrl ──────────────────────────
    // Validates: Requirement 3.6 (skip logic)

    [Fact]
    public async Task CheckAllAsync_NullNexusUrl_SkipsCard()
    {
        // Arrange: card with null NexusUrl
        var card = CreateCard(nexusUrl: null, installedVersion: "1.0.0");

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card });

        // Assert: API should not be called
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAllAsync_UnparseableNexusUrl_SkipsCard()
    {
        // Arrange: card with a URL that can't be parsed
        var card = CreateCard(
            nexusUrl: "https://example.com/not-a-nexus-url",
            installedVersion: "1.0.0");

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card });

        // Assert: API should not be called
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAllAsync_EmptyNexusUrl_SkipsCard()
    {
        // Arrange: card with empty NexusUrl
        var card = CreateCard(nexusUrl: "", installedVersion: "1.0.0");

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card });

        // Assert: API should not be called
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAllAsync_CatalogueOnlyUrl_SkipsCard()
    {
        // Arrange: card with a catalogue-only URL (no mod ID)
        var card = CreateCard(
            nexusUrl: "https://www.nexusmods.com/games/cyberpunk2077",
            installedVersion: "1.0.0");

        var checker = new NexusUpdateChecker(_mockApiClient.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act
        await checker.CheckAllAsync(new[] { card });

        // Assert: API should not be called (no mod ID to query)
        _mockApiClient.Verify(
            c => c.GetModFilesV1Async(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }
}
