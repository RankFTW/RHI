using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for URL classification logic.
/// Feature: reshade-addons, Property 5: URL classification — direct save vs zip extract
/// **Validates: Requirements 3.2, 3.3**
/// </summary>
public class AddonUrlClassificationPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenHost =
        Gen.Elements(
            "https://example.com",
            "https://github.com/user/repo/releases/download/v1",
            "https://cdn.example.org/addons",
            "https://files.host.net/path/to");

    private static readonly Gen<string> GenFileName =
        Gen.Elements("addon", "MyAddon", "generic-depth", "effect_runtime", "RenoDX_DevKit");

    private static readonly Gen<string> GenAddon32Url =
        from host in GenHost
        from name in GenFileName
        select $"{host}/{name}.addon32";

    private static readonly Gen<string> GenAddon64Url =
        from host in GenHost
        from name in GenFileName
        select $"{host}/{name}.addon64";

    private static readonly Gen<string> GenZipUrl =
        from host in GenHost
        from name in GenFileName
        select $"{host}/{name}.zip";

    private static readonly Gen<string> GenAnyClassifiedUrl =
        Gen.OneOf(GenAddon32Url, GenAddon64Url, GenZipUrl);

    // ── Property 5 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 5: URL classification — direct save vs zip extract
    // **Validates: Requirements 3.2, 3.3**

    [Property(MaxTest = 20)]
    public Property IsZipUrl_ReturnsTrueIff_UrlEndsWithZip()
    {
        return Prop.ForAll(
            Arb.From(GenAnyClassifiedUrl),
            (string url) =>
            {
                bool result = AddonPackService.IsZipUrl(url);
                bool endsWithZip = url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

                return (result == endsWithZip)
                    .Label($"url={url}, IsZipUrl={result}, endsWithZip={endsWithZip}");
            });
    }

    [Property(MaxTest = 20)]
    public Property ClassifyUrlExtension_ReturnsAddon32_ForAddon32Urls()
    {
        return Prop.ForAll(
            Arb.From(GenAddon32Url),
            (string url) =>
            {
                string ext = AddonPackService.ClassifyUrlExtension(url);
                return (ext == ".addon32")
                    .Label($"url={url}, classified={ext}");
            });
    }

    [Property(MaxTest = 20)]
    public Property ClassifyUrlExtension_ReturnsAddon64_ForAddon64Urls()
    {
        return Prop.ForAll(
            Arb.From(GenAddon64Url),
            (string url) =>
            {
                string ext = AddonPackService.ClassifyUrlExtension(url);
                return (ext == ".addon64")
                    .Label($"url={url}, classified={ext}");
            });
    }

    [Property(MaxTest = 20)]
    public Property ClassifyUrlExtension_ReturnsZip_ForZipUrls()
    {
        return Prop.ForAll(
            Arb.From(GenZipUrl),
            (string url) =>
            {
                string ext = AddonPackService.ClassifyUrlExtension(url);
                return (ext == ".zip")
                    .Label($"url={url}, classified={ext}");
            });
    }

    [Property(MaxTest = 20)]
    public Property IsZipUrl_IsFalse_ForDirectAddonUrls()
    {
        var genDirectUrl = Gen.OneOf(GenAddon32Url, GenAddon64Url);

        return Prop.ForAll(
            Arb.From(genDirectUrl),
            (string url) =>
            {
                bool result = AddonPackService.IsZipUrl(url);
                return (!result)
                    .Label($"url={url}, IsZipUrl should be false but was {result}");
            });
    }
}
