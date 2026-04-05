using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for addon version tracking round-trip.
/// Feature: reshade-addons, Property 7: Addon version tracking round-trip
/// </summary>
public class AddonVersionTrackingRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenPackageName =
        from len in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray()))
        select new string(chars);

    private static readonly Gen<string> GenVersionString =
        Gen.Elements(
            "1.0.0", "2.3.1", "0.1.0", "10.20.30", "v1.2.3",
            "snapshot", "latest", "3.0.0-beta", "1.0.0-rc1", "unknown");

    private static readonly Gen<string?> GenOptionalString =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            GenPackageName.Select<string, string?>(s => s));

    private static readonly Gen<AddonVersionInfo> GenVersionInfo =
        from version in GenVersionString
        from lastChecked in Gen.Elements(
            "2024-01-15T10:30:00Z", "2025-06-01T00:00:00Z", "2023-12-31T23:59:59Z")
        from fileName32 in GenOptionalString
        from fileName64 in GenOptionalString
        select new AddonVersionInfo
        {
            Version = version,
            LastChecked = lastChecked,
            FileName32 = fileName32,
            FileName64 = fileName64
        };

    private static readonly Gen<Dictionary<string, AddonVersionInfo>> GenVersionsDictionary =
        from count in Gen.Choose(1, 5)
        from keys in Gen.ArrayOf(count, GenPackageName)
        from values in Gen.ArrayOf(count, GenVersionInfo)
        select keys.Zip(values)
            .GroupBy(kv => kv.First)
            .ToDictionary(g => g.Key, g => g.First().Second);

    // ── Property 7 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 7: Addon version tracking round-trip
    // **Validates: Requirements 3.7**

    /// <summary>
    /// For any addon package name and version string, writing the version to
    /// versions.json and then reading it back should produce the same version string.
    /// Tests the JSON serialization round-trip of Dictionary&lt;string, AddonVersionInfo&gt;
    /// using System.Text.Json, which is the same mechanism used by LoadVersions/SaveVersions.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property VersionTracking_JsonRoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            Arb.From(GenVersionsDictionary),
            (Dictionary<string, AddonVersionInfo> original) =>
            {
                // Serialize to JSON (same as SaveVersions)
                var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });

                // Deserialize back (same as LoadVersions)
                var restored = JsonSerializer.Deserialize<Dictionary<string, AddonVersionInfo>>(json);

                if (restored == null)
                    return false.Label("Deserialized dictionary was null");

                if (original.Count != restored.Count)
                    return false.Label($"Count mismatch: original={original.Count}, restored={restored.Count}");

                foreach (var kvp in original)
                {
                    if (!restored.TryGetValue(kvp.Key, out var restoredInfo))
                        return false.Label($"Missing key '{kvp.Key}' after round-trip");

                    if (kvp.Value.Version != restoredInfo.Version)
                        return false.Label($"Version mismatch for '{kvp.Key}': '{kvp.Value.Version}' vs '{restoredInfo.Version}'");

                    if (kvp.Value.LastChecked != restoredInfo.LastChecked)
                        return false.Label($"LastChecked mismatch for '{kvp.Key}'");

                    if (kvp.Value.FileName32 != restoredInfo.FileName32)
                        return false.Label($"FileName32 mismatch for '{kvp.Key}'");

                    if (kvp.Value.FileName64 != restoredInfo.FileName64)
                        return false.Label($"FileName64 mismatch for '{kvp.Key}'");
                }

                return true.Label("All fields preserved after JSON round-trip");
            });
    }
}
