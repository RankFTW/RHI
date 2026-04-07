// Unit tests for DownloadsMigrationService
// Validates: Requirements 8.1, 8.2, 8.10, 9.1, 9.2, 9.3, 9.4
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for <see cref="DownloadsMigrationService"/> covering marker-based
/// skip/run logic, error resilience, and marker write behaviour.
/// Uses temporary directories for full filesystem isolation.
/// </summary>
public class DownloadsMigrationServiceTests : IDisposable
{
    private readonly string _root;

    public DownloadsMigrationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MigTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private string MarkerPath => Path.Combine(_root, ".reorganised");

    private void SeedFile(string fileName, string? content = null)
    {
        File.WriteAllText(Path.Combine(_root, fileName), content ?? fileName);
    }

    private bool FileExistsInSubfolder(string subfolder, string fileName)
    {
        return File.Exists(Path.Combine(_root, subfolder, fileName));
    }

    private bool FileExistsInRoot(string fileName)
    {
        return File.Exists(Path.Combine(_root, fileName));
    }

    // ── 1. Migration runs when marker absent (Req 8.1) ─────────────────

    [Fact]
    public void RunOnce_MarkerAbsent_MigratesFilesToSubdirectories()
    {
        // Arrange — seed one file per category
        SeedFile("renodx-test.addon64");
        SeedFile("relimiter.addon64");
        SeedFile("luma_hdr.zip");          // luma_ prefix takes priority over .zip
        SeedFile("shaderpack.7z");
        SeedFile("readme.txt");

        // Act
        DownloadsMigrationService.RunOnce(_root);

        // Assert — each file landed in its classified subfolder
        Assert.True(FileExistsInSubfolder("renodx", "renodx-test.addon64"));
        Assert.True(FileExistsInSubfolder("framelimiter", "relimiter.addon64"));
        Assert.True(FileExistsInSubfolder("luma", "luma_hdr.zip"));
        Assert.True(FileExistsInSubfolder("shaders", "shaderpack.7z"));
        Assert.True(FileExistsInSubfolder("misc", "readme.txt"));

        // Files should no longer be in root
        Assert.False(FileExistsInRoot("renodx-test.addon64"));
        Assert.False(FileExistsInRoot("relimiter.addon64"));
        Assert.False(FileExistsInRoot("luma_hdr.zip"));
        Assert.False(FileExistsInRoot("shaderpack.7z"));
        Assert.False(FileExistsInRoot("readme.txt"));
    }

    // ── 2. Migration skips when marker present (Req 8.2) ────────────────

    [Fact]
    public void RunOnce_MarkerPresent_DoesNotMoveFiles()
    {
        // Arrange — create marker and seed a file
        File.WriteAllBytes(MarkerPath, []);
        SeedFile("shaderpack.7z");

        // Act
        DownloadsMigrationService.RunOnce(_root);

        // Assert — file stays in root, not moved
        Assert.True(FileExistsInRoot("shaderpack.7z"));
        Assert.False(FileExistsInSubfolder("shaders", "shaderpack.7z"));
    }

    // ── 3. Marker written after completion (Req 8.10) ───────────────────

    [Fact]
    public void RunOnce_WritesMarkerAfterMigration()
    {
        // Arrange — seed a file so migration has work to do
        SeedFile("readme.txt");

        // Act
        DownloadsMigrationService.RunOnce(_root);

        // Assert
        Assert.True(File.Exists(MarkerPath));
    }

    // ── 4. Marker written even with partial failures (Req 9.3) ──────────

    [Fact]
    public void RunOnce_WritesMarker_EvenWhenSomeMoveFails()
    {
        // Arrange — seed two files; lock one to cause a move failure
        SeedFile("shaderpack.7z");
        SeedFile("readme.txt");

        var lockedPath = Path.Combine(_root, "shaderpack.7z");
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        DownloadsMigrationService.RunOnce(_root);

        // Assert — marker still written despite the locked file failing to move
        Assert.True(File.Exists(MarkerPath));
    }

    // ── 5. Continues after individual move failure (Req 9.1) ────────────

    [Fact]
    public void RunOnce_ContinuesMovingFiles_AfterIndividualMoveFailure()
    {
        // Arrange — seed two files; lock the first to cause a failure
        SeedFile("shaderpack.7z");
        SeedFile("readme.txt");

        var lockedPath = Path.Combine(_root, "shaderpack.7z");
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        DownloadsMigrationService.RunOnce(_root);

        // Assert — the unlocked file was still moved successfully
        Assert.True(FileExistsInSubfolder("misc", "readme.txt"));
        // The locked file stays in root
        Assert.True(FileExistsInRoot("shaderpack.7z"));
    }

    // ── 6. Handles subdirectory creation failure gracefully (Req 9.2) ───

    [Fact]
    public void RunOnce_SkipsFilesForSubdirectory_WhenCreationFails()
    {
        // Arrange — place a regular file where a subdirectory would be created
        // so that Directory.CreateDirectory fails for that path.
        // We block "shaders" by creating a file with that name.
        File.WriteAllText(Path.Combine(_root, "shaders"), "blocker");

        SeedFile("shaderpack.7z");   // would go to shaders/
        SeedFile("readme.txt");      // would go to misc/

        // Act — should not throw
        DownloadsMigrationService.RunOnce(_root);

        // Assert — readme.txt still moved to misc (unaffected category)
        Assert.True(FileExistsInSubfolder("misc", "readme.txt"));
        // shaderpack.7z stays in root because "shaders" dir couldn't be created
        Assert.True(FileExistsInRoot("shaderpack.7z"));
        // Marker still written
        Assert.True(File.Exists(MarkerPath));
    }

    // ── 7. Handles marker write failure gracefully (Req 9.4) ────────────

    [Fact]
    public void RunOnce_DoesNotThrow_WhenMarkerWriteFails()
    {
        // Arrange — seed a file and block the marker path with a read-only directory
        SeedFile("readme.txt");

        // Create a directory named ".reorganised" so File.WriteAllBytes fails
        Directory.CreateDirectory(MarkerPath);

        // Act — should not throw even though marker can't be written as a file
        var exception = Record.Exception(() => DownloadsMigrationService.RunOnce(_root));

        // Assert
        Assert.Null(exception);
        // The file should still have been migrated
        Assert.True(FileExistsInSubfolder("misc", "readme.txt"));
    }
}
