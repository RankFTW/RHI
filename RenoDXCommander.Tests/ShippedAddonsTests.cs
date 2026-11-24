using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class ShippedAddonsTests
{
    private const string Template = "[ADDON]\r\nDisabledAddons=Generic Depth,Effect Runtime Sync\r\n\r\n[GENERAL]\r\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\r\n";

    [Fact]
    public void Enable_RemovesBothNames_KeepsEverythingElse()
    {
        var result = AuxInstallService.SetShippedAddonsInText(Template, enabled: true);
        Assert.Equal(Template.Replace("DisabledAddons=Generic Depth,Effect Runtime Sync", "DisabledAddons="), result);
    }

    [Fact]
    public void Enable_KeepsOtherDisabledAddons_AndMatchesFileSuffix()
    {
        var text = "[ADDON]\nDisabledAddons=Generic Depth@ReShade64.dll, My Addon ,Effect Runtime Sync\n";
        Assert.Equal("[ADDON]\nDisabledAddons=My Addon\n", AuxInstallService.SetShippedAddonsInText(text, enabled: true));
    }

    [Fact]
    public void Disable_RoundTripsToTemplate_AndIsIdempotent()
    {
        var enabled = AuxInstallService.SetShippedAddonsInText(Template, enabled: true);
        Assert.Equal(Template, AuxInstallService.SetShippedAddonsInText(enabled, enabled: false));
        Assert.Same(Template, AuxInstallService.SetShippedAddonsInText(Template, enabled: false));
    }

    [Fact]
    public void Disable_AddsLineOrSectionWhenMissing()
    {
        Assert.Equal("[ADDON]\nDisabledAddons=Generic Depth,Effect Runtime Sync\nAddonPath=.\\addons\n",
            AuxInstallService.SetShippedAddonsInText("[ADDON]\nAddonPath=.\\addons\n", enabled: false));
        Assert.Equal("[ADDON]\r\nDisabledAddons=Generic Depth,Effect Runtime Sync\r\n\r\n[GENERAL]\r\n",
            AuxInstallService.SetShippedAddonsInText("[GENERAL]\r\n", enabled: false));
    }

    [Fact]
    public void Enable_WithoutLine_ChangesNothing()
    {
        const string text = "[GENERAL]\nNoReloadOnInit=0\n";
        Assert.Same(text, AuxInstallService.SetShippedAddonsInText(text, enabled: true));
    }

    [Fact]
    public void OnlyTheAddonSectionIsRead()
    {
        const string text = "[OTHER]\nDisabledAddons=Generic Depth\n[ADDON]\nDisabledAddons=Generic Depth\n";
        Assert.Equal("[OTHER]\nDisabledAddons=Generic Depth\n[ADDON]\nDisabledAddons=\n",
            AuxInstallService.SetShippedAddonsInText(text, enabled: true));
    }

    [Fact]
    public void GenericDepthOnly_EnablesAndDisablesJustThatOne()
    {
        var on = AuxInstallService.SetShippedAddonsInText(Template, enabled: true, [AuxInstallService.GenericDepth]);
        Assert.Contains("DisabledAddons=Effect Runtime Sync\r\n", on);
        var off = AuxInstallService.SetShippedAddonsInText(on, enabled: false, [AuxInstallService.GenericDepth]);
        Assert.Contains("DisabledAddons=Effect Runtime Sync,Generic Depth\r\n", off);
    }}
