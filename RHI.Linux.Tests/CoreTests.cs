using System.Text;
using System.Text.Json;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RHI.Linux.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-test-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    private readonly string? _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    public CoreTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_root, "cache"));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldData);
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _oldCache);
        Directory.Delete(_root, true);
    }
    private string FileAt(string path, string content)
    {
        var full = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, content); return full;
    }
    private static Payload Payload(string path, string content, bool seed = false) => new(path, Encoding.UTF8.GetBytes(content), seed);
    private static byte[] Pe(MachineType architecture)
    {
        var bytes = new byte[512]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(128).CopyTo(bytes, 0x3c);
        bytes[128] = (byte)'P'; bytes[129] = (byte)'E';
        BitConverter.GetBytes((ushort)architecture).CopyTo(bytes, 132); return bytes;
    }

    [Fact] public void VdfHandlesEscapesCommentsAndBracesInStrings()
    {
        var root = Vdf.Parse("//comment\n\"root\" { \"name\" \"a \\\"quote\\\" { }\" \"path\" \"C:\\\\games\" nested { key value } }");
        Assert.Equal("a \"quote\" { }", root.Child("root")!.Text("name"));
        Assert.Equal("C:\\games", root.Child("root")!.Text("path"));
        Assert.Equal("value", root.At("root", "nested")!.Text("key"));
    }
    [Theory]
    [InlineData("root {")]
    [InlineData("root { key }")]
    [InlineData("root { key \"unterminated }")]
    public void VdfRejectsTruncatedFiles(string text) => Assert.Throws<FormatException>(() => Vdf.Parse(text));

    [Fact] public void DiscoveryFindsFlatpakExternalLibrariesAndNestedShippingExecutable()
    {
        var steam = Path.Combine(_root, "home/.var/app/com.valvesoftware.Steam/.local/share/Steam");
        var library = Path.Combine(_root, "External Games");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps/libraryfolders.vdf"), $"\"libraryfolders\" {{ \"0\" {{ \"path\" {Vdf.Quote(library)} }} }}");
        FileAt("External Games/steamapps/appmanifest_42.acf", "\"AppState\" { \"appid\" \"42\" \"name\" \"Mortal Shell II\" \"installdir\" \"sparta\" }");
        var launcher = FileAt("External Games/steamapps/common/Sparta/Game.exe", new string('x', 9000));
        var shipping = FileAt("External Games/steamapps/common/Sparta/Game/Binaries/Win64/Game-Win64-Shipping.EXE", "");
        File.WriteAllBytes(shipping, Pe(MachineType.x64));
        var prefix = Path.Combine(steam, "steamapps/compatdata/42/pfx"); Directory.CreateDirectory(prefix);
        var alias = Path.Combine(_root, "steam-link"); Directory.CreateSymbolicLink(alias, steam);
        var discovery = new GameDiscovery();
        var games = discovery.Scan([steam, alias]);
        var game = Assert.Single(games);
        Assert.Equal("Mortal Shell II", game.Name);
        Assert.Equal("Steam Flatpak", game.Source);
        Assert.Equal(shipping, game.Executable);
        Assert.Equal(prefix, game.Prefix);
        Assert.Equal(MachineType.x64, game.Architecture);
        Assert.Empty(discovery.Warnings);
    }

    [Fact] public void DiscoveryHandlesOldLibraryFormatAndMissingPrefix()
    {
        var library = Path.Combine(_root, "library");
        FileAt("steam/steamapps/libraryfolders.vdf", $"libraryfolders {{ 1 {Vdf.Quote(library)} }}");
        FileAt("library/steamapps/appmanifest_123.acf", "AppState { appid 123 name Game installdir Game }");
        FileAt("library/steamapps/common/Game/Game.EXE", "exe");
        var game = Assert.Single(new GameDiscovery().Scan([Path.Combine(_root, "steam")]));
        Assert.Null(game.Prefix); Assert.NotNull(game.Executable);
    }

    [Fact] public void DiscoverySkipsRuntimeAndReportsMalformedManifestWithoutAborting()
    {
        FileAt("steam/steamapps/appmanifest_1.acf", "AppState { appid 1 name \"Proton Experimental\" installdir Proton }");
        FileAt("steam/steamapps/appmanifest_2.acf", "AppState {");
        var discovery = new GameDiscovery(); Assert.Empty(discovery.Scan([Path.Combine(_root, "steam")])); Assert.Single(discovery.Warnings);
    }

    [Fact] public void GameSettingsRoundtripDoesNotReadComputedInstallPaths()
    {
        var settings = new Settings { ManualGames = [new Game { Name = "Uninstalled", Root = "/missing" }] };
        settings.Save(); Assert.Equal("Uninstalled", Assert.Single(Settings.Load().ManualGames).Name);
    }

    [Fact] public void InstallationIsIdempotentAndRestoresOriginalCasingAndBytes()
    {
        var original = FileAt("DXGI.DLL", "other proxy");
        var install = new Installation(_root);
        install.Install("ReShade", "1", [Payload("dxgi.dll", "first"), Installation.DefaultIni()], true, "dxgi.dll");
        install.Install("ReShade", "2", [Payload("dxgi.dll", "second"), Installation.DefaultIni()], false, "dxgi.dll");
        Assert.Equal("second", File.ReadAllText(original));
        Assert.False(File.Exists(Path.Combine(_root, "dxgi.dll")));
        Assert.Equal("2", install.ReadState().Components["ReShade"]);
        install.Remove(); Assert.Equal("other proxy", File.ReadAllText(original));
        Assert.False(File.Exists(Path.Combine(_root, "ReShade.ini"))); Assert.Empty(install.ReadState().Files);
    }

    [Fact] public void ForeignConflictIsDetectedBeforeAnyWrites()
    {
        FileAt("dxgi.dll", "foreign"); var install = new Installation(_root);
        Assert.Throws<IOException>(() => install.Install("ReShade", "1", [Payload("new.txt", "new"), Payload("dxgi.dll", "replacement")]));
        Assert.False(File.Exists(Path.Combine(_root, "new.txt"))); Assert.Equal("foreign", File.ReadAllText(Path.Combine(_root, "dxgi.dll")));
    }

    [Fact] public void EditedBinaryIsNeverOverwrittenOrDeleted()
    {
        var install = new Installation(_root); install.Install("ReShade", "1", [Payload("dxgi.dll", "rhi")]);
        FileAt("dxgi.dll", "user replacement");
        Assert.Throws<IOException>(() => install.Install("ReShade", "2", [Payload("dxgi.dll", "new")], true));
        Assert.Throws<IOException>(() => install.Remove()); Assert.Equal("user replacement", File.ReadAllText(Path.Combine(_root, "dxgi.dll")));
    }

    [Fact] public void ExistingAndEditedUserConfigurationsArePreserved()
    {
        FileAt("ReShade.ini", "user's original"); var install = new Installation(_root);
        install.Install("ReShade", "1", [Payload("dxgi.dll", "rhi"), Installation.DefaultIni()]);
        Assert.Equal("user's original", File.ReadAllText(Path.Combine(_root, "ReShade.ini")));
        install.Remove(); Assert.True(File.Exists(Path.Combine(_root, "ReShade.ini")));
        File.Delete(Path.Combine(_root, "ReShade.ini"));
        install.Install("ReShade", "1", [Payload("dxgi.dll", "rhi"), Installation.DefaultIni()]);
        FileAt("ReShade.ini", "user edited the seed"); install.Remove();
        Assert.Equal("user edited the seed", File.ReadAllText(Path.Combine(_root, "ReShade.ini")));
    }

    [Fact] public void CompilerPreservesGameCopyButUpdatesManagedCopies()
    {
        var install = new Installation(_root);
        FileAt("d3dcompiler_47.dll", "game compiler");
        install.Install("ReShade", "1", [new Payload("d3dcompiler_47.dll", Encoding.UTF8.GetBytes("rhi"), PreserveExisting: true)]);
        Assert.Equal("game compiler", File.ReadAllText(Path.Combine(_root, "d3dcompiler_47.dll")));
        Assert.Empty(install.ReadState().Files);
        File.Delete(Path.Combine(_root, "d3dcompiler_47.dll"));
        install.Install("ReShade", "1", [new Payload("d3dcompiler_47.dll", Encoding.UTF8.GetBytes("rhi"), PreserveExisting: true)]);
        install.Install("ReShade", "2", [new Payload("d3dcompiler_47.dll", Encoding.UTF8.GetBytes("new rhi"), PreserveExisting: true)]);
        Assert.Equal("new rhi", File.ReadAllText(Path.Combine(_root, "d3dcompiler_47.dll")));
        install.Remove(); Assert.False(File.Exists(Path.Combine(_root, "d3dcompiler_47.dll")));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/tmp/escape.dll")]
    [InlineData("C:\\escape.dll")]
    [InlineData(".rhi-linux/manifest.json")]
    public void InstallationRejectsEscapingAndReservedPaths(string path)
    {
        Assert.Throws<IOException>(() => new Installation(_root).Install("Malicious", "1", [Payload(path, "x")]));
    }

    [Fact] public void SymlinkEscapeAndAmbiguousCaseAreRejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "rhi-outside-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "Shaders"), outside);
            Assert.Throws<IOException>(() => new Installation(_root).Install("Shaders", "1", [Payload("Shaders/a.fx", "x")]));
        }
        finally { Directory.Delete(outside); }
        FileAt("dxgi.dll", "a"); FileAt("DXGI.DLL", "b");
        Assert.Throws<IOException>(() => new Installation(_root).Install("ReShade", "1", [Payload("dxgi.dll", "x")], true));
    }

    [Fact] public void ComponentOwnershipAndDependenciesAreEnforced()
    {
        var install = new Installation(_root); install.Install("ReShade", "1", [Payload("dxgi.dll", "rhi")], false, "dxgi.dll");
        Assert.Throws<IOException>(() => install.Install("Other", "1", [Payload("dxgi.dll", "other")], true));
        Assert.Throws<IOException>(() => install.Install("ReShade", "2", [Payload("d3d9.dll", "rhi")], false, "d3d9.dll"));
        install.Install("RenoDX", "1", [Payload("renodx.addon64", "addon")]);
        Assert.Throws<IOException>(() => install.Remove("ReShade")); install.Remove(); Assert.Empty(install.ReadState().Files);
    }

    [Fact] public void UpdateRetiresOldAddonFileAndRestoresItsBackup()
    {
        FileAt("old.addon64", "original"); var install = new Installation(_root);
        install.Install("RenoDX", "1", [Payload("old.addon64", "first")], true);
        install.Install("RenoDX", "2", [Payload("new.addon64", "second")]);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_root, "old.addon64")));
        Assert.Single(install.ReadState().Files); install.Remove(); Assert.False(File.Exists(Path.Combine(_root, "new.addon64")));
    }

    [Fact] public void InterruptedTransactionRecoversBeforeNextOperation()
    {
        var install = new Installation(_root); install.Install("ReShade", "1", [Payload("dxgi.dll", "first")]);
        var state = install.ReadState();
        FileAt(".rhi-linux/transaction/undo", "first"); FileAt("dxgi.dll", "half-written-update");
        LinuxPaths.WriteJson(Path.Combine(_root, ".rhi-linux/transaction.json"), new Journal { State = state, Files = [new("dxgi.dll", "transaction/undo")] });
        install.Install("Addon", "1", [Payload("addon.addon64", "a")]);
        Assert.Equal("first", File.ReadAllText(Path.Combine(_root, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_root, ".rhi-linux/transaction.json")));
    }

    [Fact] public void OverridesPreserveUnrelatedModulesAndSplitGroupedOverrides()
    {
        var result = Proton.LaunchOptions("MANGOHUD=1 WINEDLLOVERRIDES=\"dxgi,d3d11=b;dinput8=n\" gamescope -f -- %command% -dx12", "dxgi.dll");
        Assert.Contains("MANGOHUD=1", result); Assert.Contains("dxgi=n,b", result); Assert.Contains("d3d11=b", result);
        Assert.Contains("dinput8=n", result); Assert.EndsWith("gamescope -f -- %command% -dx12", result);
        Assert.Equal(result, Proton.LaunchOptions(result, "dxgi.dll"));
    }

    [Theory]
    [InlineData("WINEDLLOVERRIDES=\"$OVERRIDES\" %command%")]
    [InlineData("WINEDLLOVERRIDES=x=n WINEDLLOVERRIDES=y=b %command%")]
    [InlineData("gamescope -f")]
    public void AmbiguousLaunchOptionsAreNotRewritten(string options) => Assert.Throws<FormatException>(() => Proton.LaunchOptions(options, "dxgi.dll"));

    [Theory]
    [InlineData("Vulkan")]
    [InlineData("Unknown")]
    [InlineData("DirectX8")]
    public void UnsupportedApiCannotSilentlyInstallWrongProxy(string api) => Assert.Throws<IOException>(() => Installation.ProxyFor(Enum.Parse<GraphicsApiType>(api)));

    [Fact] public void SteamConfigEditPreservesEverythingOutsideRequestedValue()
    {
        var content = "// Keep comments\nUserLocalConfigStore { Software { Valve { Steam { apps { 42 { LaunchOptions \"MANGOHUD=1 %command%\" playtime 100 } 99 { LaunchOptions untouched } } } } } }";
        var expected = content.Replace("\"MANGOHUD=1 %command%\"", Vdf.Quote("WINEDLLOVERRIDES='dxgi=n,b' MANGOHUD=1 %command%"));
        Assert.Equal(expected, Proton.EditOptions(content, "42", "WINEDLLOVERRIDES='dxgi=n,b' MANGOHUD=1 %command%"));
        var added = Proton.EditOptions(content, "101", "a \"quoted\" argument %command%");
        Assert.Equal("a \"quoted\" argument %command%", Vdf.Parse(added).At("UserLocalConfigStore", "Software", "Valve", "Steam", "apps", "101")!.Text("LaunchOptions"));
    }

    [Fact] public void PrefixSettingsResolveWindowsCasing()
    {
        var path = FileAt("pfx/drive_c/users/steamuser/appdata/local/Game/Saved/Config/Windows/Engine.ini", "");
        Assert.Equal(Path.Combine(_root, "pfx/drive_c/users/steamuser/appdata/local"), Proton.LocalAppData(Path.Combine(_root, "pfx")));
        Assert.Equal(path, Assert.Single(IniSettings.FindEngineInis(new Game { Prefix = Path.Combine(_root, "pfx") })));
    }

    [Fact] public void HdrIniEditsRetainExistingTweaksAndRestoreExactOriginal()
    {
        const string original = ";METADATA\r\n[ConsoleVariables]\r\nr.RayTracing.Enable=1\r\n[SystemSettings]\r\nr.AllowHDR=0\r\n";
        var path = FileAt("Engine.ini", original);
        IniSettings.Apply(path, IniSettings.UnrealHdr); IniSettings.Apply(path, IniSettings.UnrealHdr);
        var text = File.ReadAllText(path);
        Assert.Contains("r.RayTracing.Enable=1", text); Assert.Equal("1", IniSettings.Get(text, "SystemSettings", "r.AllowHDR"));
        IniSettings.Restore(path); Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact] public void HdrRestorePreservesSubsequentUserChanges()
    {
        var path = FileAt("Engine.ini", "[SystemSettings]\nr.AllowHDR=0\n"); IniSettings.Apply(path, IniSettings.UnrealHdr);
        File.WriteAllText(path, IniSettings.Set(File.ReadAllText(path), "SystemSettings", "r.HDR.Display.OutputDevice", "7") + "\n; user note");
        IniSettings.Restore(path); var restored = File.ReadAllText(path);
        Assert.Equal("0", IniSettings.Get(restored, "SystemSettings", "r.AllowHDR"));
        Assert.Equal("7", IniSettings.Get(restored, "SystemSettings", "r.HDR.Display.OutputDevice")); Assert.Contains("; user note", restored);
    }

    [Fact] public void UnrealHdrMakesEngineIniReadOnlyAndRestoresOriginalPermissions()
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = FileAt("Engine.ini", "[ConsoleVariables]\nr.RayTracing.Enable=1\n");
        var original = File.GetUnixFileMode(path);
        IniSettings.Apply(path, IniSettings.UnrealHdr, readOnly: true);
        Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(path) & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        IniSettings.Apply(path, IniSettings.UnrealHdr, readOnly: true);
        IniSettings.Restore(path);
        Assert.Equal(original, File.GetUnixFileMode(path));
        Assert.Equal("[ConsoleVariables]\nr.RayTracing.Enable=1\n", File.ReadAllText(path));
    }

    [Fact] public async Task RenoDxInstallAppliesRequiredHdrSettingsAndRestoresOriginals()
    {
        var exe = FileAt("fixture/Game.exe", ""); File.WriteAllBytes(exe, Pe(MachineType.x64));
        var engine = FileAt("prefix/drive_c/users/steamuser/AppData/Local/Fixture/Saved/Config/Windows/Engine.ini", "[ConsoleVariables]\nr.RayTracing.Enable=1\n");
        var game = new Game { Name = "Fixture HDR", Root = Path.GetDirectoryName(exe)!, Executable = exe, Prefix = Path.Combine(_root, "prefix") };
        var installation = new Installation(game.Root);
        installation.Install("ReShade", "Nightly fixture", [Payload("dxgi.dll", "installed"), Installation.DefaultIni()], proxy: "dxgi.dll");
        using var http = new HttpClient(new FixtureAddonHandler(Pe(MachineType.x64)));
        var catalog = new Catalog(http);
        catalog.Mods.Add(new RenoDXCommander.Models.GameMod { Name = game.Name, SnapshotUrl = "https://example.invalid/renodx-ue-extended.addon64", Notes = "Engine.ini" });
        await new GameSetup(new Downloads(http), catalog).InstallRenoDx(game, new GamePreferences(), null);
        var state = InstallationStatus.Read(game);
        Assert.True(state.Get("RenoDX").Installed); Assert.True(state.HdrConfigured);
        Assert.Equal("installed", File.ReadAllText(Path.Combine(game.Root, "dxgi.dll")));
        Assert.Contains("r.RayTracing.Enable=1", File.ReadAllText(engine));
        GameSetup.RestoreHdr(game);
        Assert.Equal("[ConsoleVariables]\nr.RayTracing.Enable=1\n", File.ReadAllText(engine));
    }
    private sealed class FixtureAddonHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://example.invalid/renodx-ue-extended.addon64", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        }
    }

    [Fact] public void SharedWikiModsUseTheirOwnSectionDownload()
    {
        const string html = "<h3>UE Extended <a href='https://example.org/ue.addon64'>download</a></h3><h2>Description</h2><table><tr><th>Name</th><th>Status</th><th>Notes</th></tr><tr><td>Mortal Shell II</td><td>✅</td><td>Engine.ini</td></tr></table><h3>Unity Engine</h3><p><a href='https://example.org/unity.addon64'>64</a><a href='https://example.org/unity.addon32'>32</a></p><table><tr><th>Name</th><th>Status</th><th>Notes</th></tr><tr><td>Unity Game</td><td>✅</td><td></td></tr></table>";
        using var http = Downloads.CreateClient();
        var (mods, _) = new WikiService(http, GameDiscovery.NormalizeName).ParseHtml(html); Catalog.ApplySharedDownloads(html, mods);
        Assert.Equal("https://example.org/ue.addon64", mods.Single(m => m.Name == "Mortal Shell II").SnapshotUrl);
        Assert.Equal("https://example.org/unity.addon64", mods.Single(m => m.Name == "Unity Game").SnapshotUrl);
        Assert.Equal("https://example.org/unity.addon32", mods.Single(m => m.Name == "Unity Game").SnapshotUrl32);
    }

    [Fact] public void DownloadsRejectWrongArchitectureAndNonPeData()
    {
        var path = FileAt("addon.dll", "<html>not a DLL</html>");
        Assert.Throws<IOException>(() => Downloads.ValidatePe(path, MachineType.x64));
        File.WriteAllBytes(path, Pe(MachineType.I386));
        Assert.Throws<IOException>(() => Downloads.ValidatePe(path, MachineType.x64));
        Downloads.ValidatePe(path, MachineType.I386);
    }
}
