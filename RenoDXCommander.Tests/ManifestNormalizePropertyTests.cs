using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ManifestService.Normalize null-safety.
/// </summary>
public class ManifestNormalizePropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static Gen<Dictionary<string, TValue>?> GenNullableDict<TValue>(Gen<TValue> valueGen)
    {
        var nonNull =
            from count in Gen.Choose(0, 3)
            from keys in Gen.SubListOf(new[] { "Game A", "Game B", "Game C", "Elden Ring", "Starfield" })
            from values in Gen.Sequence(Enumerable.Repeat(valueGen, keys.Count()))
            select (Dictionary<string, TValue>?)keys.Zip(values)
                .ToDictionary(kv => kv.First, kv => kv.Second);

        return Gen.OneOf(Gen.Constant<Dictionary<string, TValue>?>(null), nonNull);
    }

    private static readonly Gen<GameNoteEntry> GenGameNote =
        from notes in Gen.Elements("Note 1", "Note 2", null)
        from url in Gen.Elements("https://example.com", null)
        select new GameNoteEntry { Notes = notes, NotesUrl = url };

    private static readonly Gen<ForceExternalEntry> GenForceExternal =
        from url in Gen.Elements("https://example.com/mod", null)
        from label in Gen.Elements("Download", "External", null)
        select new ForceExternalEntry { Url = url, Label = label };

    private static readonly Gen<ManifestDllNames> GenDllNames =
        from reshade in Gen.Elements("d3d9.dll", "dxgi.dll", null)
        from dc in Gen.Elements("winmm.dll", null)
        select new ManifestDllNames { ReShade = reshade, Dc = dc };

    /// <summary>
    /// Generates a RemoteManifest with randomly null dictionary properties.
    /// </summary>
    private static readonly Gen<RemoteManifest> GenManifest =
        from version in Gen.Choose(1, 10)
        from wikiNames in GenNullableDict(Gen.Elements("Override A", "Override B"))
        from gameNotes in GenNullableDict(GenGameNote)
        from dcModes in GenNullableDict(Gen.Choose(0, 3))
        from forceExt in GenNullableDict(GenForceExternal)
        from installPaths in GenNullableDict(Gen.Elements(@"C:\Games", @"D:\Steam"))
        from wikiStatus in GenNullableDict(Gen.Elements("Working", "Broken", "Partial"))
        from snapshots in GenNullableDict(Gen.Elements("https://snap.com/a", "https://snap.com/b"))
        from lumaNotes in GenNullableDict(GenGameNote)
        from engineOvr in GenNullableDict(Gen.Elements("Unreal", "Unity", "Source 2"))
        from dllOvr in GenNullableDict(GenDllNames)
        select new RemoteManifest
        {
            Version = version,
            WikiNameOverrides = wikiNames,
            GameNotes = gameNotes,
            DcModeOverrides = dcModes,
            ForceExternalOnly = forceExt,
            InstallPathOverrides = installPaths,
            WikiStatusOverrides = wikiStatus,
            SnapshotOverrides = snapshots,
            LumaGameNotes = lumaNotes,
            EngineOverrides = engineOvr,
            DllNameOverrides = dllOvr,
        };

    // ── Property 10: ManifestService.Normalize handles null dictionaries ──────────
    // Feature: codebase-optimization, Property 10: ManifestService.Normalize handles null dictionaries
    // **Validates: Requirements 13.4**
    [Property(MaxTest = 100)]
    public Property Normalize_WithRandomlyNullDictionaries_DoesNotThrow()
    {
        return Prop.ForAll(
            Arb.From(GenManifest),
            (RemoteManifest manifest) =>
            {
                Exception? caught = null;
                try
                {
                    ManifestService.Normalize(manifest);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                var nullProps = new List<string>();
                if (manifest.WikiNameOverrides is null) nullProps.Add("WikiNameOverrides");
                if (manifest.GameNotes is null) nullProps.Add("GameNotes");
                if (manifest.DcModeOverrides is null) nullProps.Add("DcModeOverrides");
                if (manifest.ForceExternalOnly is null) nullProps.Add("ForceExternalOnly");
                if (manifest.InstallPathOverrides is null) nullProps.Add("InstallPathOverrides");
                if (manifest.WikiStatusOverrides is null) nullProps.Add("WikiStatusOverrides");
                if (manifest.SnapshotOverrides is null) nullProps.Add("SnapshotOverrides");
                if (manifest.LumaGameNotes is null) nullProps.Add("LumaGameNotes");
                if (manifest.EngineOverrides is null) nullProps.Add("EngineOverrides");
                if (manifest.DllNameOverrides is null) nullProps.Add("DllNameOverrides");

                return (caught is null)
                    .Label($"Threw {caught?.GetType().Name}: {caught?.Message} " +
                           $"(null props: [{string.Join(", ", nullProps)}])");
            });
    }
}
