using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>GITC Uplift settings RHI writes into the game's reshade.ini: StartEnabled (2.0.0+) and the EnableKey hotkey.</summary>
public class UpliftIniTests
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    [Fact]
    public void StartEnabled_And_EnableKey_GoUnderUpliftSection()
    {
        var game = Path.Combine(Path.GetTempPath(), "rhi-uplift-ini-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(game);
            var svc = new UpliftService(new HttpClient(), new NullReporter(), new GitHubETagCache());
            Assert.Null(UpliftService.ReadStartEnabled(game));          // no reshade.ini: nothing to show
            Assert.False(svc.SetStartEnabled(game, true));

            var ini = Path.Combine(game, "reshade.ini");
            File.WriteAllText(ini, "[GENERAL]\r\nEffectSearchPaths=.\\\r\n[Uplift]\r\nEnabled=1\r\n");
            Assert.False(UpliftService.ReadStartEnabled(game));

            Assert.True(svc.SetStartEnabled(game, true));
            Assert.True(UpliftService.ReadStartEnabled(game));
            Assert.Equal("1", Dlssg20_30Service.ReadIniValue(ini, "Uplift", "StartEnabled"));
            Assert.True(svc.SetStartEnabled(game, false));
            Assert.Equal("0", Dlssg20_30Service.ReadIniValue(ini, "Uplift", "StartEnabled"));

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
