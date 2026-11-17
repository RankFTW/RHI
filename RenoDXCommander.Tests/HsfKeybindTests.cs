using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>HSF's in-game keys in HSF.ini: defaults when absent, one key per job, and the global keys at install.</summary>
public class HsfKeybindTests : IDisposable
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    private readonly string _game = Path.Combine(Path.GetTempPath(), "rhi-hsfkeys-" + Guid.NewGuid().ToString("N"));
    private readonly HsfService _svc = new(new HttpClient(), new NullReporter(), new GitHubETagCache());

    public HsfKeybindTests() => Directory.CreateDirectory(_game);

    public void Dispose()
    {
        try { Directory.Delete(_game, true); } catch (IOException) { }
    }

    private string Ini(string text)
    {
        var ini = Path.Combine(_game, HsfService.IniName);
        File.WriteAllText(ini, text);
        return ini;
    }

    private static HsfService.Keybind Bind(string key) => HsfService.Keybinds.Single(b => b.IniKey == key);

    [Fact]
    public void ReadKeybind_AbsentKeysUseHsfDefaults()
    {
        Ini("[HSF]\r\nOsdKey=46\r\n");
        Assert.Equal(0x76, HsfService.ReadKeybind(_game, Bind("MfgKey")));   // F7
        Assert.Equal(0, HsfService.ReadKeybind(_game, Bind("FrameGenKey")));
        Assert.Equal(46, HsfService.ReadKeybind(_game, Bind("OsdKey")));
    }

    [Fact]
    public void WriteKeybind_TakesTheKeyFromAnotherHsfHotkey()
    {
        var ini = Ini("[HSF]\r\nViewKey=119\r\n");
        // F7 is MfgKey's default even though the line is absent
        Assert.True(_svc.WriteKeybind(_game, Bind("FrameGenKey"), 0x76));
        Assert.Equal("118", Dlssg20_30Service.ReadIniValue(ini, "HSF", "FrameGenKey"));
        Assert.Equal("0", Dlssg20_30Service.ReadIniValue(ini, "HSF", "MfgKey"));
        Assert.True(_svc.WriteKeybind(_game, Bind("OsdKey"), 119));
        Assert.Equal("0", Dlssg20_30Service.ReadIniValue(ini, "HSF", "ViewKey"));
    }

    [Fact]
    public void ApplyKeybinds_WritesOnlyKeysThatAreSet()
    {
        var ini = Ini("[HSF]\r\nMfgKey=117\r\n");
        int n = _svc.ApplyKeybinds(_game, new Dictionary<string, int> { ["FrameGenKey"] = 45, ["MfgKey"] = 0, ["OsdKey"] = 36 });
        Assert.Equal(2, n);
        Assert.Equal("45", Dlssg20_30Service.ReadIniValue(ini, "HSF", "FrameGenKey"));
        Assert.Equal("117", Dlssg20_30Service.ReadIniValue(ini, "HSF", "MfgKey")); // None: the game's key stays
        Assert.Equal("36", Dlssg20_30Service.ReadIniValue(ini, "HSF", "OsdKey"));
    }

    [Fact]
    public void WriteKeybind_NoHsfIni_False() =>
        Assert.False(_svc.WriteKeybind(_game, Bind("MfgKey"), 0x75));
}
