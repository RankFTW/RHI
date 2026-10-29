using System.Text.Json;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class ManifestPresetTests
{
    [Fact]
    public void ConsecutiveSnapshotsReplaceOverridesAndRestoreBuiltInsWithoutChangingSavedSelections()
    {
        var saved = (DlssProfile.SrPresets, DlssProfile.RrPresets, DlssProfile.FgPresets, DlssProfile.NrPresets);
        try
        {
            var prefs = new GamePreferences();
            DlssProfile.SetPreset(prefs, DlssKind.SR, 1234);
            Apply("""{"sr":[{"name":"Fixture Custom","value":1234},{"name":"J - TF1","value":99},{"name":"K - TF1","disabled":true}]}""");
            Assert.Contains(DlssProfile.SrPresets, p => p.Name == "Fixture Custom" && p.Value == 1234);
            Assert.Contains(DlssProfile.SrPresets, p => p.Name == "J - TF1" && p.Value == 99);
            Assert.DoesNotContain(DlssProfile.SrPresets, p => p.Name == "K - TF1");

            Apply("""{"sr":[{"name":"Fixture Custom","value":5678}]}""");
            Assert.Single(DlssProfile.SrPresets, p => p.Name == "Fixture Custom" && p.Value == 5678);
            Assert.Contains(DlssProfile.SrPresets, p => p.Name == "J - TF1" && p.Value == 10);
            Assert.Contains(DlssProfile.SrPresets, p => p.Name == "K - TF1" && p.Value == 11);
            Assert.Equal(1234u, DlssProfile.Preset(prefs, DlssKind.SR));

            Apply("""{"sr":[{"name":"Fixture Custom","disabled":true},{"name":"default","disabled":true}]}""");
            Assert.DoesNotContain(DlssProfile.SrPresets, p => p.Name == "Fixture Custom");
            Assert.Equal(("Default", 0u), DlssProfile.SrPresets[0]);
            Apply("""{"sr":[{"name":"Fixture Custom","value":2,"disabled":false}]}""");
            Assert.Contains(DlssProfile.SrPresets, p => p.Name == "Fixture Custom" && p.Value == 2);
            DlssProfile.ApplyManifestPresets(null);
            Assert.DoesNotContain(DlssProfile.SrPresets, p => p.Name == "Fixture Custom");
            Assert.Equal("NVIDIA Recommended", DlssProfile.SrPresets[^1].Name);
        }
        finally
        {
            (DlssProfile.SrPresets, DlssProfile.RrPresets, DlssProfile.FgPresets, DlssProfile.NrPresets) = saved;
        }
    }

    private static void Apply(string json)
    {
        using var document = JsonDocument.Parse(json);
        DlssProfile.ApplyManifestPresets(document.RootElement);
    }
}
