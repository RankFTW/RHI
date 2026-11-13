using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// ANR FG (Apex Neural Rendering frame generation, github.com/apex-compute/ANR): downloads the
/// release's add-on package and installs it into OptiScaler's custom folder as a custom frame
/// generation provider (OptiScaler\custom\Apex Neural Rendering FG\dlssg\nvngx_dlssg.dll).
/// OptiScaler loads it with FGInput=nvngxfg and FGNvngxReplacement=<see cref="ProviderName"/>;
/// the OptiScaler preset sets those. Needs an OptiScaler build with custom DLSSG providers (Nightly).
/// </summary>
public class AnrFgService
{
    /// <summary>The provider's folder name, which is also its FGNvngxReplacement value.</summary>
    public const string ProviderName = "Apex Neural Rendering FG";

    private const string GitHubApiUrl = "https://api.github.com/repos/apex-compute/ANR/releases/latest";
    private const string AddonAssetSuffix = "-addon.zip";
    private const string ChecksumAssetName = "SHA256SUMS.txt";
    private const string ProviderDllRelPath = @"dlssg\nvngx_dlssg.dll";
    private const string UserIniRelPath = @"dlssg\nr_fg.ini";

    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;
    private readonly string _stagingDir;
    private readonly string _versionFile;
    private readonly SemaphoreSlim _stagingLock = new(1, 1);

    public AnrFgService(HttpClient http, GitHubETagCache etagCache)
        : this(http, etagCache,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "anr-fg"))
    { }

    /// <summary>Tests: use a throwaway staging folder. (DI only uses the public constructor.)</summary>
    internal AnrFgService(HttpClient http, GitHubETagCache etagCache, string stagingDir)
    {
        _http = http;
        _etagCache = etagCache;
        _stagingDir = stagingDir;
        _versionFile = Path.Combine(_stagingDir, "version.txt");
    }

    private string StagedProviderDir => Path.Combine(_stagingDir, ProviderName);

    public string? StagedVersion
    {
        get
        {
            try { return File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    public bool IsStagingReady => File.Exists(Path.Combine(StagedProviderDir, ProviderDllRelPath));

    /// <summary>The provider's folder in a game: OptiScaler's custom folder + <see cref="ProviderName"/>.</summary>
    public static string GetInstallDir(string gameInstallPath) =>
        Path.Combine(OptiScalerService.GetCustomProvidersDir(gameInstallPath), ProviderName);

    public static bool IsInstalledIn(string? gameInstallPath) =>
        !string.IsNullOrEmpty(gameInstallPath)
        && File.Exists(Path.Combine(GetInstallDir(gameInstallPath), ProviderDllRelPath));

    /// <summary>
    /// Makes sure the newest release is staged. Returns true when a staged copy is available
    /// (the newest, or an older one when the check or download failed).
    /// </summary>
    public async Task<bool> EnsureStagingAsync()
    {
        await _stagingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var release = await FetchLatestReleaseAsync().ConfigureAwait(false);
            if (release == null)
            {
                CrashReporter.Log($"[AnrFgService.EnsureStaging] Could not read the latest release — using staged {StagedVersion ?? "(none)"}");
                return IsStagingReady;
            }
            var (tag, zipUrl, zipName, sumsUrl) = release.Value;
            if (IsStagingReady && string.Equals(StagedVersion, tag, StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[AnrFgService.EnsureStaging] {tag} already staged");
                return true;
            }

            var previousVersion = IsStagingReady ? StagedVersion : null;
            Directory.CreateDirectory(_stagingDir);
            var tempDir = Path.Combine(_stagingDir, "_extract");
            try
            {
                var bytes = await _http.GetByteArrayAsync(zipUrl).ConfigureAwait(false);
                if (!await ChecksumMatchesAsync(bytes, zipName, sumsUrl).ConfigureAwait(false))
                    return IsStagingReady;

                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                Directory.CreateDirectory(tempDir);
                using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                    zip.ExtractToDirectory(tempDir);

                var extracted = Directory.GetDirectories(tempDir, ProviderName, SearchOption.AllDirectories)
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, ProviderDllRelPath)));
                if (extracted == null)
                {
                    CrashReporter.Log($"[AnrFgService.EnsureStaging] {zipName} has no '{ProviderName}\\{ProviderDllRelPath}' — keeping staged {StagedVersion ?? "(none)"}");
                    return IsStagingReady;
                }

                if (Directory.Exists(StagedProviderDir)) Directory.Delete(StagedProviderDir, recursive: true);
                Directory.Move(extracted, StagedProviderDir);
                File.WriteAllText(_versionFile, tag);
                CrashReporter.Log($"[AnrFgService.EnsureStaging] Staged {tag} from {zipName}");
                UpdateHistory.Record("Frame Generation", "ANR FG", previousVersion, tag);

                await AntivirusGuard.VerifyStagedAsync("ANR FG", tag,
                    new[] { Path.Combine(StagedProviderDir, ProviderDllRelPath) }).ConfigureAwait(false);
                return IsStagingReady;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                CrashReporter.Log($"[AnrFgService.EnsureStaging] Download of {tag} failed — {ex.Message}");
                if (AntivirusGuard.IsVirusError(ex))
                    AntivirusGuard.Report("ANR FG", tag, Path.Combine(StagedProviderDir, ProviderDllRelPath));
                return IsStagingReady;
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[AnrFgService.EnsureStaging] Temp folder cleanup failed — {ex.Message}"); }
            }
        }
        finally
        {
            _stagingLock.Release();
        }
    }

    /// <summary>
    /// Copies the staged provider into the game's OptiScaler custom folder. Keeps the user's nr_fg.ini
    /// (ANR's own settings) when there is one. Returns an error message, or null on success.
    /// </summary>
    public string? Install(string gameInstallPath)
    {
        if (!IsStagingReady) return "ANR FG isn't downloaded yet.";
        var dest = GetInstallDir(gameInstallPath);
        try
        {
            bool keepIni = File.Exists(Path.Combine(dest, UserIniRelPath));
            int copied = 0;
            foreach (var src in Directory.EnumerateFiles(StagedProviderDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(StagedProviderDir, src);
                if (keepIni && rel.Equals(UserIniRelPath, StringComparison.OrdinalIgnoreCase)) continue;
                var target = FileHelper.SafeCombine(dest, rel);
                if (target == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(src, target, overwrite: true);
                copied++;
            }
            CrashReporter.Log($"[AnrFgService.Install] {StagedVersion} → '{dest}' ({copied} files, kept nr_fg.ini: {keepIni})");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[AnrFgService.Install] '{dest}' — {ex.Message}");
            return $"Couldn't copy ANR FG into the game: {ex.Message}";
        }
    }

    /// <summary>Tag, add-on zip URL and name, and the checksum file's URL (null when the release has none).</summary>
    private async Task<(string tag, string zipUrl, string zipName, string? sumsUrl)?> FetchLatestReleaseAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag) || !root.TryGetProperty("assets", out var assets)) return null;

            string? zipUrl = null, zipName = null, sumsUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name == null || url == null) continue;
                if (name.EndsWith(AddonAssetSuffix, StringComparison.OrdinalIgnoreCase)) { zipUrl = url; zipName = name; }
                else if (name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
            }
            if (zipUrl == null || zipName == null)
            {
                CrashReporter.Log($"[AnrFgService.FetchLatestRelease] {tag} has no *{AddonAssetSuffix} asset");
                return null;
            }
            return (tag, zipUrl, zipName, sumsUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            CrashReporter.Log($"[AnrFgService.FetchLatestRelease] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks the download against the release's SHA256SUMS.txt. A release without the file, or
    /// without a line for this zip, passes (logged); a mismatch fails.
    /// </summary>
    private async Task<bool> ChecksumMatchesAsync(byte[] bytes, string zipName, string? sumsUrl)
    {
        if (sumsUrl == null)
        {
            CrashReporter.Log($"[AnrFgService.Checksum] Release has no {ChecksumAssetName}; {zipName} not verified");
            return true;
        }
        var sums = await _http.GetStringAsync(sumsUrl).ConfigureAwait(false);
        var expected = sums.Split('\n')
            .Select(l => l.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].TrimStart('*').Trim().Equals(zipName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .FirstOrDefault();
        if (expected == null)
        {
            CrashReporter.Log($"[AnrFgService.Checksum] {ChecksumAssetName} has no line for {zipName}; not verified");
            return true;
        }
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        CrashReporter.Log($"[AnrFgService.Checksum] {zipName} SHA256 mismatch (expected {expected}, got {actual}) — not staged");
        return false;
    }
}
