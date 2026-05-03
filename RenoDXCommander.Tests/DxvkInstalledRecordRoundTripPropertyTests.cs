using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DxvkInstalledRecord JSON round-trip.
/// Feature: dxvk-integration, Property 1: DxvkInstalledRecord serialization round-trip
/// </summary>
public class DxvkInstalledRecordRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenNonNullString =
        Gen.Elements(
            "Cyberpunk 2077", "Elden Ring", "Starfield",
            "Baldur's Gate 3", "Alan Wake 2", "The Witcher 3",
            "FINAL FANTASY XIV Online", "Dark Souls III",
            "some-random-value", "");

    private static readonly Gen<string> GenInstallPath =
        Gen.Elements(
            @"C:\Games\Cyberpunk 2077\bin\x64",
            @"D:\SteamLibrary\steamapps\common\Elden Ring\Game",
            @"C:\Program Files\Steam\steamapps\common\Starfield",
            @"E:\Games\BG3\bin",
            @"C:\Games\Test");

    private static readonly Gen<string> GenVersionTag =
        Gen.Elements("v2.7.1", "v2.6.0", "v2.5.3", "v1.10.3", "v2.8.0-rc1", "");

    private static readonly Gen<string> GenDllName =
        Gen.Elements(
            "d3d8.dll", "d3d9.dll", "d3d10core.dll",
            "d3d11.dll", "dxgi.dll");

    private static readonly Gen<List<string>> GenDllList =
        GenDllName.ListOf()
            .Select(items => items.ToList());

    private static readonly Gen<DateTime> GenDateTime =
        Gen.Choose(2020, 2030).SelectMany(year =>
        Gen.Choose(1, 12).SelectMany(month =>
        Gen.Choose(1, 28).SelectMany(day =>
        Gen.Choose(0, 23).SelectMany(hour =>
        Gen.Choose(0, 59).SelectMany(minute =>
        Gen.Choose(0, 59).Select(second =>
            new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc)))))));

    /// <summary>
    /// Generates a complete DxvkInstalledRecord with arbitrary field values.
    /// </summary>
    private static readonly Gen<DxvkInstalledRecord> GenDxvkRecord =
        GenNonNullString.SelectMany(gameName =>
        GenInstallPath.SelectMany(installPath =>
        GenVersionTag.SelectMany(version =>
        GenDllList.SelectMany(installedDlls =>
        GenDllList.SelectMany(pluginFolderDlls =>
        GenDllList.SelectMany(backedUpFiles =>
        Arb.Generate<bool>().SelectMany(deployedConf =>
        Arb.Generate<bool>().SelectMany(inOptiScalerPlugins =>
        GenDateTime.Select(installedAt =>
            new DxvkInstalledRecord
            {
                GameName = gameName,
                InstallPath = installPath,
                DxvkVersion = version,
                InstalledDlls = installedDlls,
                PluginFolderDlls = pluginFolderDlls,
                BackedUpFiles = backedUpFiles,
                DeployedConf = deployedConf,
                InOptiScalerPlugins = inOptiScalerPlugins,
                InstalledAt = installedAt,
            })))))))));

    // ── Property 1: DxvkInstalledRecord serialization round-trip ──────────────────
    // Feature: dxvk-integration, Property 1: DxvkInstalledRecord serialization round-trip
    // **Validates: Requirements 18.4**

    /// <summary>
    /// For any valid DxvkInstalledRecord with arbitrary strings, DLL name lists,
    /// boolean flags, and DateTime values, serializing to JSON then deserializing
    /// produces an equivalent record with all fields matching the original.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DxvkInstalledRecord_RoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            Arb.From(GenDxvkRecord),
            (DxvkInstalledRecord original) =>
            {
                var json = JsonSerializer.Serialize(original);
                var deserialized = JsonSerializer.Deserialize<DxvkInstalledRecord>(json)!;

                bool gameNameMatch = deserialized.GameName == original.GameName;
                bool installPathMatch = deserialized.InstallPath == original.InstallPath;
                bool versionMatch = deserialized.DxvkVersion == original.DxvkVersion;
                bool installedDllsMatch = deserialized.InstalledDlls.SequenceEqual(original.InstalledDlls);
                bool pluginFolderDllsMatch = deserialized.PluginFolderDlls.SequenceEqual(original.PluginFolderDlls);
                bool backedUpFilesMatch = deserialized.BackedUpFiles.SequenceEqual(original.BackedUpFiles);
                bool deployedConfMatch = deserialized.DeployedConf == original.DeployedConf;
                bool inOptiScalerPluginsMatch = deserialized.InOptiScalerPlugins == original.InOptiScalerPlugins;
                bool installedAtMatch = deserialized.InstalledAt == original.InstalledAt;

                return (gameNameMatch && installPathMatch && versionMatch &&
                        installedDllsMatch && pluginFolderDllsMatch && backedUpFilesMatch &&
                        deployedConfMatch && inOptiScalerPluginsMatch && installedAtMatch)
                    .Label($"gameNameMatch={gameNameMatch}, installPathMatch={installPathMatch}, " +
                           $"versionMatch={versionMatch}, installedDllsMatch={installedDllsMatch}, " +
                           $"pluginFolderDllsMatch={pluginFolderDllsMatch}, backedUpFilesMatch={backedUpFilesMatch}, " +
                           $"deployedConfMatch={deployedConfMatch}, inOptiScalerPluginsMatch={inOptiScalerPluginsMatch}, " +
                           $"installedAtMatch={installedAtMatch}");
            });
    }
}
