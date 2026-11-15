// DlssStreamlineService.SpecificCache.cs -- caches one exact DLSS / DLSS-G / Streamline version from
// dlss_manifest.json (not the newest). Used by RTX Encore's runtimes for games without DLSS, which
// need exactly DLSS 310.9.16, DLSS-G 310.9.16 and Streamline 2.14.16.

namespace RenoDXCommander.Services;

public partial class DlssStreamlineService
{
    /// <inheritdoc />
    public async Task<string?> EnsureSpecificDlssCachedAsync(string version)
    {
        var entry = FindDlssEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificDlssCachedAsync] Version '{version}' not found in manifest");
            return null;
        }
        var cachedDir = FileHelper.SafeCombine(DlssCacheDir, entry.Version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificDlssCachedAsync] Unsafe version '{entry.Version}' — skipped");
            return null;
        }
        var cachedDll = Path.Combine(cachedDir, DlssDllName);
        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <inheritdoc />
    public async Task<string?> EnsureSpecificDlssgCachedAsync(string version)
    {
        var entry = FindDlssgEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificDlssgCachedAsync] Version '{version}' not found in manifest");
            return null;
        }
        var cachedDir = FileHelper.SafeCombine(DlssgCacheDir, entry.Version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificDlssgCachedAsync] Unsafe version '{entry.Version}' — skipped");
            return null;
        }
        var cachedDll = Path.Combine(cachedDir, DlssgDllName);
        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssgDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <inheritdoc />
    public async Task<string?> EnsureSpecificStreamlineCachedAsync(string version)
    {
        var entry = FindStreamlineEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificStreamlineCachedAsync] Version '{version}' not found in manifest");
            return null;
        }
        // Same folder name as EnsureNewestStreamlineCachedAsync (the manifest's version), so one download serves both
        var cachedDir = FileHelper.SafeCombine(StreamlineCacheDir, entry.Version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificStreamlineCachedAsync] Unsafe version '{entry.Version}' — skipped");
            return null;
        }
        var indicator = Path.Combine(cachedDir, StreamlineIndicator);
        if (!File.Exists(indicator))
            await DownloadAndCacheStreamlineAsync(entry.Url, cachedDir).ConfigureAwait(false);
        return File.Exists(indicator) ? cachedDir : null;
    }
}
