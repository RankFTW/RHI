using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for NxmProtocolHandler.Parse robustness.
/// Feature: nexus-api-integration, Property 3: NXM URI parsing robustness
/// **Validates: Requirements 8.3**
/// </summary>
public class NxmUriParsingRobustnessPropertyTests
{
    // ── Property 3 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 3: NXM URI parsing robustness
    // **Validates: Requirements 8.3**

    /// <summary>
    /// For any arbitrary string input, calling NxmProtocolHandler.Parse SHALL never
    /// throw an exception. It SHALL return a valid NxmUri for well-formed input or
    /// null for malformed input.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Parse_NeverThrows_ForAnyArbitraryString()
    {
        var handler = new NxmProtocolHandler();

        return Prop.ForAll(
            Arb.From<string>(),
            (string input) =>
            {
                // Act: Parse should never throw, regardless of input
                NxmUri? result = null;
                Exception? caught = null;

                try
                {
                    result = handler.Parse(input);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                // Assert: no exception was thrown
                bool noException = caught is null;

                // Assert: result is either a valid NxmUri or null
                bool validResult = result is null || IsValidNxmUri(result);

                return (noException && validResult)
                    .Label($"noException={noException}, validResult={validResult}, " +
                           $"input='{Truncate(input, 80)}', " +
                           $"exception={caught?.GetType().Name}: {caught?.Message}");
            });
    }

    /// <summary>
    /// Validates that a parsed NxmUri has all expected field constraints:
    /// non-empty game domain, positive mod ID, positive file ID, non-empty key, positive expires.
    /// </summary>
    private static bool IsValidNxmUri(NxmUri uri)
    {
        return !string.IsNullOrEmpty(uri.GameDomain)
            && uri.ModId > 0
            && uri.FileId > 0
            && !string.IsNullOrEmpty(uri.Key)
            && uri.Expires > 0;
    }

    /// <summary>
    /// Truncates a string for readable label output.
    /// </summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (value is null) return "<null>";
        if (value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }
}
