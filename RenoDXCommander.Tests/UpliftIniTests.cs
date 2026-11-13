using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>GITC Uplift settings RHI writes into the game's reshade.ini: the EnableKey hotkey.</summary>
public class UpliftIniTests
{
    [Fact]
    public void EnableKey_GoesUnderUpliftSection()
    {
        var game = Path.Combine(Path.GetTempPath(), "rhi-uplift-ini-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(game);
            var ini = Path.Combine(game, "reshade.ini");
            Assert.False(UpliftService.ApplyEnableKey(ini, 121));       // no reshade.ini: nothing written

            File.WriteAllText(ini, "[GENERAL]\r\nEffectSearchPaths=.\\\r\n[Uplift]\r\nEnabled=1\r\n");
            Assert.True(UpliftService.ApplyEnableKey(ini, 121));        // F10
            Assert.Equal("121", Dlssg20_30Service.ReadIniValue(ini, "Uplift", "EnableKey"));
            Assert.False(UpliftService.ApplyEnableKey(ini, 0));         // none: the game's own key stays
            Assert.Equal("121", Dlssg20_30Service.ReadIniValue(ini, "Uplift", "EnableKey"));
            Assert.Equal("1", Dlssg20_30Service.ReadIniValue(ini, "Uplift", "Enabled"));
            Assert.Equal(".\\", Dlssg20_30Service.ReadIniValue(ini, "GENERAL", "EffectSearchPaths"));
        }
        finally { Directory.Delete(game, recursive: true); }
    }
}
