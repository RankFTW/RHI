using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class GenericUnityUrlTests
{
    private const string Old64 = "https://github.com/NotVoosh/renodx-unity/releases/download/snapshot/renodx-unityengine.addon64";

    [Fact]
    public void Defaults_WithoutManifestKeys()
    {
        try
        {
            WikiService.ApplyManifestUnityUrls(null);
            Assert.Equal(WikiService.DefaultGenericUnityUrl64, WikiService.GenericUnityUrl64);
            Assert.Equal(WikiService.DefaultGenericUnityUrl32, WikiService.GenericUnityUrl32);
            WikiService.ApplyManifestUnityUrls(new Dictionary<string, string> { ["dc"] = "https://example.com/x" });
            Assert.Equal(WikiService.DefaultGenericUnityUrl64, WikiService.GenericUnityUrl64);
        }
        finally { WikiService.ApplyManifestUnityUrls(null); }
    }

    [Fact]
    public void ManifestKeysOverride_InvalidOnesFallBack()
    {
        try
        {
            WikiService.ApplyManifestUnityUrls(new Dictionary<string, string>
            {
                ["unity64"] = " https://example.com/a/renodx-unityengine.addon64 ",
                ["unity32"] = "not a link",
            });
            Assert.Equal("https://example.com/a/renodx-unityengine.addon64", WikiService.GenericUnityUrl64);
            Assert.Equal(WikiService.DefaultGenericUnityUrl32, WikiService.GenericUnityUrl32);
        }
        finally { WikiService.ApplyManifestUnityUrls(null); }
    }

    [Fact]
    public void OldLinksAndSnapshotOverrides_ResolveToTheCurrentLink()
    {
        try
        {
            WikiService.ApplyManifestUnityUrls(null);
            Assert.Equal(WikiService.DefaultGenericUnityUrl64, ModInstallService.ResolveSnapshotUrlPublic(Old64));
            WikiService.ApplyManifestUnityUrls(new Dictionary<string, string> { ["unity64"] = "https://example.com/b/renodx-unityengine.addon64" });
            Assert.Equal("https://example.com/b/renodx-unityengine.addon64", ModInstallService.ResolveSnapshotUrlPublic(WikiService.DefaultGenericUnityUrl64));
            // Other addons are untouched
            const string other = "https://clshortfuse.github.io/renodx/renodx-hades.addon64";
            Assert.Equal(other, ModInstallService.ResolveSnapshotUrlPublic(other));
        }
        finally { WikiService.ApplyManifestUnityUrls(null); }
    }
}
