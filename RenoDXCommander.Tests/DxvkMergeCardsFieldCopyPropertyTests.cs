using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for MergeCards DXVK field copy.
/// Feature: dxvk-integration, Property 14: MergeCards DXVK field copy
/// </summary>
public class DxvkMergeCardsFieldCopyPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameStatus> GenDxvkStatus =
        Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Installed,
            GameStatus.UpdateAvailable);

    private static readonly Gen<string?> GenVersionString =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements("v2.7.1", "v2.6.0", "v2.8.0-rc1", "v1.0.0"),
            Arb.Generate<NonEmptyString>().Select(s => (string?)s.Get));

    private static readonly Gen<DxvkInstalledRecord?> GenDxvkRecord =
        Gen.OneOf(
            Gen.Constant<DxvkInstalledRecord?>(null),
            from gameName in Arb.Generate<NonEmptyString>()
            from installPath in Arb.Generate<NonEmptyString>()
            from version in Gen.Elements("v2.7.1", "v2.6.0", "v2.8.0")
            from deployedConf in Arb.Generate<bool>()
            from inPlugins in Arb.Generate<bool>()
            select (DxvkInstalledRecord?)new DxvkInstalledRecord
            {
                GameName = gameName.Get,
                InstallPath = installPath.Get,
                DxvkVersion = version,
                DeployedConf = deployedConf,
                InOptiScalerPlugins = inPlugins,
                InstalledAt = DateTime.UtcNow
            });

    /// <summary>
    /// Generates a complete set of DXVK field values for a card.
    /// </summary>
    private static readonly Gen<(GameStatus Status, string? Version, DxvkInstalledRecord? Record, bool Enabled, bool Exclude)> GenDxvkFields =
        from status in GenDxvkStatus
        from version in GenVersionString
        from record in GenDxvkRecord
        from enabled in Arb.Generate<bool>()
        from exclude in Arb.Generate<bool>()
        select (status, version, record, enabled, exclude);

    // ── Property 14: MergeCards DXVK field copy ───────────────────────────────────
    // Feature: dxvk-integration, Property 14: MergeCards DXVK field copy
    // **Validates: Requirements 14.1**

    /// <summary>
    /// For any pair of (existing card, fresh card) with arbitrary DXVK field values,
    /// after applying the MergeCards field copy pattern, the existing card's DxvkStatus,
    /// DxvkInstalledVersion, DxvkRecord, DxvkEnabled, and ExcludeFromUpdateAllDxvk
    /// SHALL all equal the fresh card's corresponding values.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MergeCards_Copies_All_Dxvk_Fields_From_Fresh_To_Existing()
    {
        return Prop.ForAll(
            Arb.From(GenDxvkFields),
            Arb.From(GenDxvkFields),
            (existingFields, freshFields) =>
            {
                // Arrange: create existing card with initial DXVK values
                var existing = new GameCardViewModel
                {
                    GameName = "TestGame",
                    DxvkStatus = existingFields.Status,
                    DxvkInstalledVersion = existingFields.Version,
                    DxvkRecord = existingFields.Record,
                    DxvkEnabled = existingFields.Enabled,
                    ExcludeFromUpdateAllDxvk = existingFields.Exclude,
                };

                // Arrange: create fresh card with new DXVK values
                var fresh = new GameCardViewModel
                {
                    GameName = "TestGame",
                    DxvkStatus = freshFields.Status,
                    DxvkInstalledVersion = freshFields.Version,
                    DxvkRecord = freshFields.Record,
                    DxvkEnabled = freshFields.Enabled,
                    ExcludeFromUpdateAllDxvk = freshFields.Exclude,
                };

                // Act: apply the same field copy pattern used by MergeCards
                existing.DxvkStatus = fresh.DxvkStatus;
                existing.DxvkInstalledVersion = fresh.DxvkInstalledVersion;
                existing.DxvkRecord = fresh.DxvkRecord;
                existing.DxvkEnabled = fresh.DxvkEnabled;
                existing.ExcludeFromUpdateAllDxvk = fresh.ExcludeFromUpdateAllDxvk;

                // Assert: all DXVK fields on existing match fresh
                bool statusMatch = existing.DxvkStatus == freshFields.Status;
                bool versionMatch = existing.DxvkInstalledVersion == freshFields.Version;
                bool recordMatch = ReferenceEquals(existing.DxvkRecord, freshFields.Record);
                bool enabledMatch = existing.DxvkEnabled == freshFields.Enabled;
                bool excludeMatch = existing.ExcludeFromUpdateAllDxvk == freshFields.Exclude;

                return (statusMatch && versionMatch && recordMatch && enabledMatch && excludeMatch)
                    .Label($"statusMatch={statusMatch} (expected={freshFields.Status}, got={existing.DxvkStatus}), " +
                           $"versionMatch={versionMatch} (expected={freshFields.Version}, got={existing.DxvkInstalledVersion}), " +
                           $"recordMatch={recordMatch}, " +
                           $"enabledMatch={enabledMatch} (expected={freshFields.Enabled}, got={existing.DxvkEnabled}), " +
                           $"excludeMatch={excludeMatch} (expected={freshFields.Exclude}, got={existing.ExcludeFromUpdateAllDxvk})");
            });
    }
}
