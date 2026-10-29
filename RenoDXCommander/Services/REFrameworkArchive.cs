namespace RenoDXCommander.Services;

// Shared release identity and archive policy. Platforms own installation and backups.
public static class REFrameworkArchive
{
    public const string DllFileName = "dinput8.dll";
    public const string DownloadBaseUrl = RHI.Core.Sources.REFrameworkDownloadBase;
    public const string ReleasesApiUrl = RHI.Core.Sources.REFrameworkReleases;

    /// <summary>
    /// Maps game names to their RE Framework nightly ZIP filename.
    /// Since the monolithic REFramework.zip build, all RE Engine games use the
    /// same zip. The map is retained for any future per-game overrides.
    /// </summary>
    private static readonly Dictionary<string, string> GameZipMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // All RE Engine games now use the monolithic REFramework.zip.
        // Per-game zips (DMC5.zip, RE2.zip, etc.) are no longer published.
    };

    /// <summary>The monolithic zip that works for all supported RE Engine games.</summary>
    public const string MonolithicZipName = "REFramework.zip";

    public static string ResolveZipName(string gameName)
    {
        if (GameZipMap.TryGetValue(gameName, out var zip))
            return zip;

        // Strip ™®© and retry exact match
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName && GameZipMap.TryGetValue(stripped, out zip))
            return zip;

        return MonolithicZipName;
    }

    public static string? ExtractVersionNumber(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return tag;
        // Tags look like "nightly-01302-abcdef1" — grab the first numeric segment
        foreach (var part in tag.Split('-'))
        {
            if (part.Length > 0 && part.All(char.IsDigit))
                return part;
        }
        return tag;
    }

    public static void ExtractDllFromZip(string zipPath, string destination) =>
        DllArchive.ExtractDll(zipPath, destination, DllFileName);
}
