using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for NexusTokenStore SaveApiKey→LoadApiKey round-trip.
/// Feature: nexus-api-integration, Property 5: Token store encryption round-trip
/// </summary>
public class NexusTokenStoreRoundTripPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public NexusTokenStoreRoundTripPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NexusTokenStoreTests_" + Guid.NewGuid().ToString("N"));
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

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty printable ASCII string (chars 32–126), 1–200 chars long.
    /// </summary>
    private static readonly Gen<string> GenPrintableAsciiToken =
        from length in Gen.Choose(1, 200)
        from chars in Gen.ArrayOf(length, Gen.Choose(32, 126).Select(i => (char)i))
        select new string(chars);

    // ── Property 5 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 5: Token store encryption round-trip
    // **Validates: Requirements 9.1, 9.3**

    /// <summary>
    /// For any non-empty printable ASCII token string (1–200 chars),
    /// storing it via SaveApiKey and then loading it via LoadApiKey
    /// returns the original token string unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TokenStore_SaveThenLoad_RoundTrip_PreservesToken()
    {
        return Prop.ForAll(
            Arb.From(GenPrintableAsciiToken),
            (string token) =>
            {
                // Arrange: create a fresh store pointing at the temp directory
                var store = new NexusTokenStore(_tempDir);

                // Act: save then load
                store.SaveApiKey(token);
                var loaded = store.LoadApiKey();

                // Assert: loaded value matches original
                bool notNull = loaded is not null;
                bool matches = loaded == token;

                return (notNull && matches)
                    .Label($"notNull={notNull}, matches={matches} " +
                           $"(tokenLength={token.Length}, loaded={(loaded is null ? "null" : $"length={loaded.Length}")})");
            });
    }
}
