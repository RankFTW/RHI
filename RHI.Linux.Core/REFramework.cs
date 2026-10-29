using System.Text.Json;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

// RE Framework (praydog) for RE Engine games, as installed by Windows RHI: the monolithic
// nightly REFramework.zip provides dinput8.dll beside the game executable. Under Proton the
// game also needs a native dinput8 DLL override, added to the Steam launch options.
public sealed class REFramework(HttpClient http, Downloads downloads)
{
    public const string Component = "RE Framework", Dll = REFrameworkArchive.DllFileName;
    public const string Description = "RE Framework is a modding framework for RE Engine games. It enables ReShade injection and other mods by hooking into the game's rendering pipeline.";
    private const string ReleasesApi = REFrameworkArchive.ReleasesApiUrl;
    private readonly GitHubETagCache _etagCache = new(CrashReporter.Log);
    private static string CacheFile => Path.Combine(LinuxPaths.Cache, "reframework", "latest.json");
    public NrRelease? Latest { get; private set; } = Load();

    private static NrRelease? Load()
    {
        try { return File.Exists(CacheFile) ? JsonSerializer.Deserialize<NrRelease>(File.ReadAllText(CacheFile), LinuxPaths.Json) : null; }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    // RE Engine games ship re_chunk_000.pak (the Windows app's detection signature).
    public static bool IsREEngine(string root)
    {
        if (!Directory.Exists(root)) return false;
        return Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Any(f => Path.GetFileName(f).Equals("re_chunk_000.pak", StringComparison.OrdinalIgnoreCase));
    }

    // "nightly-01424-d1461375…" → "01424", the number Windows RHI displays.
    public static string VersionNumber(string tag) => REFrameworkArchive.ExtractVersionNumber(tag)!;

    public async Task Refresh(bool force = false)
    {
        if (!force && Latest != null && File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromHours(1)) return;
        if (force) _etagCache.Invalidate(ReleasesApi);
        var json = await _etagCache.GetWithETagAsync(http, ReleasesApi)
            ?? throw new IOException("Could not retrieve the RE Framework releases.");
        using var document = JsonDocument.Parse(json);
        var release = AddonReleases.Parse(document.RootElement, "", REFrameworkArchive.ResolveZipName("")).FirstOrDefault()
            ?? throw new IOException("No RE Framework nightly release was found.");
        Latest = release with { Version = VersionNumber(release.Version) };
        LinuxPaths.WriteJson(CacheFile, Latest);
    }

    // Installed by Windows RHI on a shared library: dinput8.dll with its ".original" marker.
    public static bool FromWindows(Game game)
    {
        if (game.Executable == null) return false;
        var dll = LinuxPaths.ResolveCase(game.InstallDirectory, Dll);
        return File.Exists(dll) && Sentinel.Placed(dll) && !Managed(game);
    }
    private static bool Managed(Game game)
    {
        try { return new Installation(game.InstallDirectory).ReadState().Components.ContainsKey(Component); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }
    public static bool Installed(Game game) => game.Executable != null && (Managed(game) || FromWindows(game));

    public bool UpdateAvailable(ComponentStatus status) =>
        status.Installed && status.Version is { } version && version != "Local" && Latest != null && version != Latest.Version;

    public async Task Install(Game game, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        try { await Refresh(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException) { CrashReporter.Log("RE Framework releases: " + ex.Message); }
        // Versioned asset URLs never change, so a cached download is reused; "latest" is always fetched.
        var url = Latest?.Url ?? REFrameworkArchive.DownloadBaseUrl + REFrameworkArchive.ResolveZipName(game.Name);
        var version = Latest?.Version ?? "Nightly " + DateTime.UtcNow.ToString("yyyy-MM-dd");
        var zip = await downloads.Fetch(url, progress, refresh: Latest == null);
        var dll = Path.Combine(LinuxPaths.Cache, "reframework-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            Directory.CreateDirectory(LinuxPaths.Cache);
            REFrameworkArchive.ExtractDllFromZip(zip, dll);
            Downloads.ValidatePe(dll, game.Architecture);
            var payload = await File.ReadAllBytesAsync(dll);
            GameSetup.RequireClosed(game);
            progress?.Report("Installing RE Framework " + version + "…");
            await Task.Run(() =>
            {
                // Hand over a copy placed by Windows RHI first, so removal restores the game's own state.
                var target = LinuxPaths.ResolveCase(game.InstallDirectory, Dll);
                var installation = new Installation(game.InstallDirectory);
                if (!installation.ReadState().Components.ContainsKey(Component) && Sentinel.Placed(target)) Sentinel.Restore(target);
                installation.Install(Component, version, [new(Dll, payload)], replaceForeign: true);
            });
        }
        finally { if (File.Exists(dll)) File.Delete(dll); }
    }

    public static async Task Remove(Game game)
    {
        GameSetup.RequireClosed(game);
        await Task.Run(() =>
        {
            var installation = new Installation(game.InstallDirectory);
            if (installation.ReadState().Components.ContainsKey(Component)) installation.Remove(Component);
            else Sentinel.Restore(LinuxPaths.ResolveCase(game.InstallDirectory, Dll));
        });
    }

    // Proton loads Wine's builtin dinput8 unless the game's copy is marked native.
    public static IEnumerable<string> LaunchDlls(Game game) => Installed(game) ? ["dinput8"] : [];
}

// All RHI-managed launch settings for a game: Neural Rendering/DLSS, RE Framework and OptiScaler.
public static class GameLaunch
{
    public static LaunchExtras Extras(Game game, GamePreferences prefs)
    {
        var extras = NeuralRenderingSetup.Extras(game, prefs);
        var dlls = extras.Dlls.Concat(REFramework.LaunchDlls(game)).Concat(OptiScaler.LaunchDlls(game)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return extras with { Dlls = dlls };
    }
}
