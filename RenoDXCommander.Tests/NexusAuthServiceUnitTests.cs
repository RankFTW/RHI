using Moq;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for NexusAuthService covering key validation, 401 handling,
/// account unlinking, and startup re-validation.
/// Requirements: 1.1, 1.2, 1.3, 1.4, 1.6
/// </summary>
public class NexusAuthServiceUnitTests
{
    private readonly Mock<INexusApiClient> _mockApiClient;
    private readonly Mock<INexusTokenStore> _mockTokenStore;
    private readonly NexusAuthService _authService;

    public NexusAuthServiceUnitTests()
    {
        _mockApiClient = new Mock<INexusApiClient>();
        _mockTokenStore = new Mock<INexusTokenStore>();
        _authService = new NexusAuthService(_mockApiClient.Object, _mockTokenStore.Object);
    }

    // ── Successful validation stores key and sets IsPremium ───────────────────────
    // Validates: Requirements 1.1, 1.2

    [Fact]
    public async Task ValidateAndStoreApiKeyAsync_WithValidKey_StoresKeyAndSetsState()
    {
        // Arrange
        const string apiKey = "test-valid-api-key";
        var validResponse = new NexusValidationResponse(
            Name: "TestUser",
            Email: "test@example.com",
            ProfileUrl: "https://www.nexusmods.com/users/12345",
            IsPremium: true,
            IsSupporter: false,
            Key: apiKey);

        _mockApiClient
            .Setup(c => c.ValidateKeyAsync(apiKey))
            .ReturnsAsync(validResponse);

        // Act
        var result = await _authService.ValidateAndStoreApiKeyAsync(apiKey);

        // Assert
        Assert.True(result);
        _mockTokenStore.Verify(s => s.SaveApiKey(apiKey), Times.Once);
        Assert.True(_authService.IsAuthenticated);
        Assert.True(_authService.IsPremium);
        Assert.NotNull(_authService.CurrentUser);
        Assert.Equal("TestUser", _authService.CurrentUser!.Name);
        Assert.Equal("test@example.com", _authService.CurrentUser.Email);
        Assert.Equal("https://www.nexusmods.com/users/12345", _authService.CurrentUser.ProfileUrl);
        Assert.True(_authService.CurrentUser.IsPremium);
        Assert.False(_authService.CurrentUser.IsSupporter);
    }

    // ── 401 response does not store key ──────────────────────────────────────────
    // Validates: Requirement 1.3

    [Fact]
    public async Task ValidateAndStoreApiKeyAsync_WithInvalidKey_DoesNotStoreKey()
    {
        // Arrange: ValidateKeyAsync returns null (simulating 401)
        const string apiKey = "invalid-api-key";

        _mockApiClient
            .Setup(c => c.ValidateKeyAsync(apiKey))
            .ReturnsAsync((NexusValidationResponse?)null);

        // Act
        var result = await _authService.ValidateAndStoreApiKeyAsync(apiKey);

        // Assert
        Assert.False(result);
        _mockTokenStore.Verify(s => s.SaveApiKey(It.IsAny<string>()), Times.Never);
        Assert.False(_authService.IsAuthenticated);
    }

    // ── UnlinkAccount clears tokens and profile ──────────────────────────────────
    // Validates: Requirement 1.4

    [Fact]
    public async Task UnlinkAccount_ClearsTokensAndProfile()
    {
        // Arrange: first authenticate successfully
        const string apiKey = "test-api-key";
        var validResponse = new NexusValidationResponse(
            Name: "TestUser",
            Email: "test@example.com",
            ProfileUrl: "https://www.nexusmods.com/users/12345",
            IsPremium: true,
            IsSupporter: true,
            Key: apiKey);

        _mockApiClient
            .Setup(c => c.ValidateKeyAsync(apiKey))
            .ReturnsAsync(validResponse);

        await _authService.ValidateAndStoreApiKeyAsync(apiKey);

        // Verify authenticated state before unlinking
        Assert.True(_authService.IsAuthenticated);
        Assert.True(_authService.IsPremium);
        Assert.NotNull(_authService.CurrentUser);

        // Act
        _authService.UnlinkAccount();

        // Assert
        _mockTokenStore.Verify(s => s.ClearAll(), Times.Once);
        Assert.Null(_authService.CurrentUser);
        Assert.False(_authService.IsAuthenticated);
        Assert.False(_authService.IsPremium);
    }

    // ── RevalidateStoredKeyAsync re-validates on startup ──────────────────────────
    // Validates: Requirement 1.6

    [Fact]
    public async Task RevalidateStoredKeyAsync_WithStoredKey_RevalidatesSuccessfully()
    {
        // Arrange: token store has a stored key
        const string storedKey = "stored-api-key-from-previous-session";

        _mockTokenStore
            .Setup(s => s.LoadApiKey())
            .Returns(storedKey);

        var validResponse = new NexusValidationResponse(
            Name: "ReturningUser",
            Email: "returning@example.com",
            ProfileUrl: "https://www.nexusmods.com/users/67890",
            IsPremium: true,
            IsSupporter: false,
            Key: storedKey);

        _mockApiClient
            .Setup(c => c.ValidateKeyAsync(storedKey))
            .ReturnsAsync(validResponse);

        // Act
        var result = await _authService.RevalidateStoredKeyAsync();

        // Assert
        Assert.True(result);
        Assert.True(_authService.IsAuthenticated);
        Assert.True(_authService.IsPremium);
        Assert.NotNull(_authService.CurrentUser);
        Assert.Equal("ReturningUser", _authService.CurrentUser!.Name);
    }
}
