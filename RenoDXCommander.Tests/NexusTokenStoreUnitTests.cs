using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for NexusTokenStore covering corrupted file handling,
/// ClearAll behavior, and HasStoredToken state.
/// Requirements: 9.2, 9.4
/// </summary>
public class NexusTokenStoreUnitTests : IDisposable
{
    private readonly string _tempDir;

    public NexusTokenStoreUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NexusTokenStoreUnitTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    // ── Corrupted file handling ───────────────────────────────────────────────────
    // Validates: Requirement 9.4

    [Fact]
    public void LoadApiKey_WithCorruptedFile_ReturnsNull_AndDeletesFile()
    {
        // Arrange: write garbage bytes to the token file
        var tokenFilePath = Path.Combine(_tempDir, "nexus_tokens.dat");
        File.WriteAllBytes(tokenFilePath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 });

        var store = new NexusTokenStore(_tempDir);

        // Act
        var result = store.LoadApiKey();

        // Assert: returns null and the corrupted file is deleted
        Assert.Null(result);
        Assert.False(File.Exists(tokenFilePath));
    }

    // ── ClearAll removes the file ─────────────────────────────────────────────────
    // Validates: Requirement 9.4

    [Fact]
    public void ClearAll_AfterSavingKey_RemovesTokenFile()
    {
        // Arrange
        var store = new NexusTokenStore(_tempDir);
        store.SaveApiKey("test-api-key-12345");

        // Verify the file was created
        Assert.True(store.HasStoredToken());

        // Act
        store.ClearAll();

        // Assert
        Assert.False(store.HasStoredToken());
        Assert.False(File.Exists(Path.Combine(_tempDir, "nexus_tokens.dat")));
    }

    // ── HasStoredToken returns correct state ──────────────────────────────────────
    // Validates: Requirement 9.2, 9.4

    [Fact]
    public void HasStoredToken_ReturnsFalse_WhenNoTokenExists()
    {
        // Arrange: fresh store with no saved tokens
        var store = new NexusTokenStore(_tempDir);

        // Act & Assert
        Assert.False(store.HasStoredToken());
    }

    [Fact]
    public void HasStoredToken_ReturnsTrue_AfterSavingApiKey()
    {
        // Arrange
        var store = new NexusTokenStore(_tempDir);

        // Act
        store.SaveApiKey("my-nexus-api-key");

        // Assert
        Assert.True(store.HasStoredToken());
    }

    [Fact]
    public void HasStoredToken_ReturnsFalse_AfterClearing()
    {
        // Arrange
        var store = new NexusTokenStore(_tempDir);
        store.SaveApiKey("my-nexus-api-key");
        Assert.True(store.HasStoredToken());

        // Act
        store.ClearAll();

        // Assert
        Assert.False(store.HasStoredToken());
    }
}
