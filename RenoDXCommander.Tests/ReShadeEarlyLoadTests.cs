using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>[ADDON] LoadFromDllMain list edits: RHI adds / removes only its own entry.</summary>
public class ReShadeEarlyLoadTests
{
    private const string DlssFix = "renodx-dlssfix.addon64";
    private const string Mfg     = "renodx-mfgunlock.addon64";

    private static bool IsDlssFix(string n) => n.Contains("dlssfix", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Add_ToEmpty_ReturnsName() => Assert.Equal(DlssFix, ReShadeEarlyLoad.Add(null, DlssFix));

    [Fact]
    public void Add_KeepsOtherAddons() =>
        Assert.Equal($"{Mfg},{DlssFix}", ReShadeEarlyLoad.Add(Mfg, DlssFix));

    [Fact]
    public void Add_AlreadyListed_NoDuplicate() =>
        Assert.Equal($"{Mfg},RENODX-DLSSFIX.addon64", ReShadeEarlyLoad.Add($"{Mfg}, RENODX-DLSSFIX.addon64", DlssFix));

    [Fact]
    public void Add_ListedWithFolder_NoDuplicate() =>
        Assert.Equal(@"addons\renodx-dlssfix.addon64", ReShadeEarlyLoad.Add(@"addons\renodx-dlssfix.addon64", DlssFix));

    [Fact]
    public void Remove_OnlyEntry_ReturnsNull() => Assert.Null(ReShadeEarlyLoad.Remove(DlssFix, IsDlssFix));

    [Fact]
    public void Remove_KeepsOtherAddons() =>
        Assert.Equal(Mfg, ReShadeEarlyLoad.Remove($"{DlssFix},{Mfg}", IsDlssFix));

    [Fact]
    public void Remove_NotListed_KeepsValue() =>
        Assert.Equal(Mfg, ReShadeEarlyLoad.Remove(Mfg, IsDlssFix));

    [Fact]
    public void Contains_IgnoresCaseAndSpaces() =>
        Assert.True(ReShadeEarlyLoad.Contains($"{DlssFix} , RenoDX-MFGUnlock.addon64", Mfg));
}
