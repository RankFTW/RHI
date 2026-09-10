using System.IO.Compression;
using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads and deploys DLSS5 ReShade AIO. The upstream release has different
/// layouts for native 64-bit games and the 32-bit host-wrapper path, so it cannot
/// use the generic addon pack extractor (which intentionally extracts addons only).
/// </summary>
public sealed class Dlss5AioService
{
    internal const string RepositoryUrl = "https://github.com/kibblerz/DLSS5-Reshade-AIO";
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/kibblerz/DLSS5-Reshade-AIO/releases/latest";

    private const string MarkerFileName = ".rhi-dlss5-aio.json";
    private const string BackupSuffix = ".rhi-dlss5-aio.original";
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "DLSS5-AIO");

    private readonly HttpClient _http;
    private readonly IDlssStreamlineService _dlssService;
    private readonly GitHubETagCache _etagCache;

    public Dlss5AioService(
        HttpClient http,
        IDlssStreamlineService dlssService,
        GitHubETagCache etagCache)
    {
        _http = http;
        _dlssService = dlssService;
        _etagCache = etagCache;
    }

    public static bool IsInstalled(string installPath, bool is32Bit)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return false;
        var addon = Path.Combine(installPath, is32Bit
            ? "standalone-dlssnr.addon32"
            : "standalone-dlssnr.addon64");
        var bridge = Path.Combine(installPath, is32Bit ? "host64" : "", "nvngx.dll");
        return File.Exists(addon) && File.Exists(bridge);
    }

    public async Task<string> InstallAsync(
        string installPath,
        bool is32Bit,
        IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            throw new DirectoryNotFoundException($"Game folder not found: {installPath}");

        progress?.Report("Finding latest DLSS5 AIO release...");
        var release = await GetLatestReleaseAsync().ConfigureAwait(false);
        var asset = SelectArchitectureAsset(release.Assets, is32Bit)
            ?? throw new InvalidOperationException(
                $"The latest DLSS5 AIO release does not contain a {(is32Bit ? "32" : "64")}-bit ZIP.");

        var stagedRoot = await EnsurePackageCachedAsync(release.TagName, asset, is32Bit, progress)
            .ConfigureAwait(false);

        ValidatePackage(stagedRoot, is32Bit);

        // Resolve every external dependency before touching the game folder.
        if (is32Bit && (!File.Exists(AuxInstallService.RsStagedPath64) ||
            new FileInfo(AuxInstallService.RsStagedPath64).Length <= AuxInstallService.MinReShadeSize))
        {
            throw new InvalidOperationException(
                "64-bit addon-enabled ReShade is not staged. Update/install ReShade in RHI, then retry.");
        }

        progress?.Report("Downloading NVIDIA DLSS runtimes...");
        await _dlssService.FetchManifestAsync().ConfigureAwait(false);
        var cachedSr = await _dlssService.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
        var cachedFg = await _dlssService.EnsureNewestDlssgCachedAsync().ConfigureAwait(false);
        var cachedNr = await _dlssService.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
        ValidateRuntime(cachedSr, "nvngx_dlss.dll");
        ValidateRuntime(cachedFg, "nvngx_dlssg.dll");
        ValidateRuntime(cachedNr, "nvngx_dlssnr.dll");

        // A reinstall first restores files removed from a newer/older package manifest,
        // then creates a fresh deployment record.
        if (File.Exists(Path.Combine(installPath, MarkerFileName)))
            Uninstall(installPath);

        var deployed = new List<string>();
        try
        {
            progress?.Report("Deploying DLSS5 AIO files...");
            foreach (var source in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stagedRoot, source);
                if (relative.Equals("host64\\ADD_REQUIRED_64BIT_FILES_HERE.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                deployed.Add(relative);
                DeployWithBackup(source, Path.Combine(installPath, relative));
            }

            // The x86 wrapper is a native x64 process and therefore needs its own
            // addon-enabled 64-bit ReShade proxy inside host64.
            if (is32Bit)
            {
                progress?.Report("Creating the 64-bit host environment...");
                const string hostReShade = "host64\\dxgi.dll";
                deployed.Add(hostReShade);
                DeployWithBackup(AuxInstallService.RsStagedPath64, Path.Combine(installPath, hostReShade));
            }

            var runtimeRoot = is32Bit ? Path.Combine(installPath, "host64") : installPath;

            DeployRuntime(cachedSr, runtimeRoot, "nvngx_dlss.dll", deployed, installPath);
            DeployRuntime(cachedFg, runtimeRoot, "nvngx_dlssg.dll", deployed, installPath);
            DeployRuntime(cachedNr, runtimeRoot, "nvngx_dlssnr.dll", deployed, installPath);

            var marker = new InstallMarker
            {
                Version = release.TagName,
                Is32Bit = is32Bit,
                Files = deployed.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
            File.WriteAllText(Path.Combine(installPath, MarkerFileName),
                JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            foreach (var relative in deployed.AsEnumerable().Reverse())
                if (TryResolveUnderRoot(installPath, relative, out var target)) RestoreBackup(target);
            throw;
        }

        CrashReporter.Log($"[Dlss5AioService] Installed {release.TagName} ({(is32Bit ? "x86 host" : "x64")}) to '{installPath}'");
        progress?.Report($"DLSS5 AIO {release.TagName} installed");
        return release.TagName;
    }

    public void Uninstall(string installPath)
    {
        var markerPath = Path.Combine(installPath, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            CrashReporter.Log($"[Dlss5AioService] No RHI deployment marker in '{installPath}'; leaving files untouched");
            return;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(markerPath));
            foreach (var relative in marker?.Files ?? Enumerable.Empty<string>())
            {
                if (!TryResolveUnderRoot(installPath, relative, out var target))
                {
                    CrashReporter.Log($"[Dlss5AioService] Ignored unsafe marker path '{relative}'");
                    continue;
                }
                RestoreBackup(target);
            }
            File.Delete(markerPath);
            RemoveEmptyOwnedDirectories(installPath);
            CrashReporter.Log($"[Dlss5AioService] Removed RHI-managed AIO files from '{installPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[Dlss5AioService] Uninstall failed for '{installPath}' — {ex.Message}");
            throw;
        }
    }

    internal static ReleaseAsset? SelectArchitectureAsset(IEnumerable<ReleaseAsset> assets, bool is32Bit)
    {
        var suffix = is32Bit ? "-32-bit.zip" : "-64-bit.zip";
        return assets.FirstOrDefault(a =>
            a.Name.StartsWith("DLSS5-ReShade-AIO-", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryResolveUnderRoot(string root, string relativePath, out string fullPath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ReleaseInfo> GetLatestReleaseAsync()
    {
        var body = await _etagCache.GetWithETagAsync(_http, LatestReleaseApiUrl).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not query the latest DLSS5 AIO release from GitHub.");
        using var json = JsonDocument.Parse(body);

        var tag = json.RootElement.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tag))
            throw new InvalidOperationException("Latest DLSS5 AIO release has no tag name.");

        var assets = new List<ReleaseAsset>();
        foreach (var item in json.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            var url = item.GetProperty("browser_download_url").GetString();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                assets.Add(new ReleaseAsset(name, url));
        }
        return new ReleaseInfo(tag, assets);
    }

    private async Task<string> EnsurePackageCachedAsync(
        string version,
        ReleaseAsset asset,
        bool is32Bit,
        IProgress<string>? progress)
    {
        var safeVersion = string.Concat(version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var destination = Path.Combine(CacheRoot, safeVersion, is32Bit ? "x86" : "x64");
        if (PackageLooksComplete(destination, is32Bit)) return destination;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempDir = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        var tempZip = Path.Combine(Path.GetTempPath(), $"rhi-dlss5-aio-{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report($"Downloading {asset.Name}...");
            using (var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var output = File.Create(tempZip);
                await input.CopyToAsync(output).ConfigureAwait(false);
            }

            Directory.CreateDirectory(tempDir);
            using (var archive = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    if (!TryResolveUnderRoot(tempDir, entry.FullName, out var target))
                        throw new InvalidDataException($"Unsafe path in DLSS5 AIO package: {entry.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }

            ValidatePackage(tempDir, is32Bit);
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Move(tempDir, destination);
            return destination;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static bool PackageLooksComplete(string root, bool is32Bit)
    {
        try { ValidatePackage(root, is32Bit); return true; }
        catch { return false; }
    }

    private static void ValidatePackage(string root, bool is32Bit)
    {
        var required = is32Bit
            ? new[]
            {
                "standalone-dlssnr.addon32",
                "dlss5-aio-x86.cfg",
                "reshade-shaders\\Shaders\\DLSS5_Feed.fx",
                "reshade-shaders\\Shaders\\DLSS5_Feed_impl.fxh",
                "reshade-shaders\\Shaders\\DLSS5_Feed_D3D9.fx",
                "host64\\AIO DLSS5 32-bit Wrapper.exe",
                "host64\\standalone-dlssnr.addon64",
                "host64\\nvngx.dll",
                "host64\\reshade-shaders\\Shaders\\DLSS5_AIO_Feed.fx",
            }
            : new[]
            {
                "standalone-dlssnr.addon64",
                "nvngx.dll",
                "reshade-shaders\\Shaders\\DLSS5_AIO_Feed.fx",
                "reshade-shaders\\Shaders\\StandaloneBoundary.fx",
            };

        var missing = required.Where(p => !File.Exists(Path.Combine(root, p))).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("DLSS5 AIO package is incomplete. Missing: " + string.Join(", ", missing));
    }

    private static void DeployRuntime(
        string? source,
        string runtimeRoot,
        string fileName,
        List<string> deployed,
        string installPath)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            throw new InvalidOperationException($"RHI could not download required runtime {fileName}.");
        var target = Path.Combine(runtimeRoot, fileName);
        deployed.Add(Path.GetRelativePath(installPath, target));
        DeployWithBackup(source, target);
    }

    private static void ValidateRuntime(string? source, string fileName)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source) || new FileInfo(source).Length == 0)
            throw new InvalidOperationException($"RHI could not download required runtime {fileName}.");
    }

    private static void DeployWithBackup(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var backup = target + BackupSuffix;
        if (!File.Exists(backup))
        {
            if (File.Exists(target)) File.Copy(target, backup);
            else File.WriteAllBytes(backup, Array.Empty<byte>());
        }
        File.Copy(source, target, overwrite: true);
    }

    private static void RestoreBackup(string target)
    {
        var backup = target + BackupSuffix;
        if (!File.Exists(backup)) return;
        if (new FileInfo(backup).Length == 0)
        {
            if (File.Exists(target)) File.Delete(target);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, overwrite: true);
        }
        File.Delete(backup);
    }

    private static void RemoveEmptyOwnedDirectories(string installPath)
    {
        foreach (var relative in new[]
        {
            "host64\\reshade-shaders\\Shaders", "host64\\reshade-shaders", "host64",
            "reshade-shaders\\Shaders", "reshade-shaders", "licenses",
        })
        {
            var path = Path.Combine(installPath, relative);
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                    Directory.Delete(path);
            }
            catch { }
        }
    }

    internal sealed record ReleaseAsset(string Name, string DownloadUrl);
    private sealed record ReleaseInfo(string TagName, List<ReleaseAsset> Assets);

    private sealed class InstallMarker
    {
        public string Version { get; set; } = "";
        public bool Is32Bit { get; set; }
        public List<string> Files { get; set; } = new();
    }
}
