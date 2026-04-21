using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches and parses the HDR Gaming Database README.md to extract game names
/// and their discussion URLs. Caches results for the session.
/// </summary>
public interface IHdrDatabaseService
{
    Task<Dictionary<string, string>> FetchAsync(IProgress<string>? progress = null);
    Dictionary<string, string>? CachedData { get; }
}

public class HdrDatabaseService : IHdrDatabaseService
{
    private readonly HttpClient _http;

    /// <summary>Session-cached database. Populated after the first successful fetch.</summary>
    public Dictionary<string, string>? CachedData { get; private set; }

    internal const string ReadmeUrl =
        "https://raw.githubusercontent.com/KoKlusz/HDR-Gaming-Database/main/README.md";

    /// <summary>
    /// Matches lines like:
    ///   * [Game Name](https://github.com/KoKlusz/HDR-Gaming-Database/discussions/123)
    ///   * [Game Name](https://github.com/KoKlusz/HDR-Gaming-Database/discussions/123) ⭐
    /// </summary>
    private static readonly Regex EntryPattern = new(
        @"^\s*\*\s*\[([^\]]+)\]\((https://github\.com/KoKlusz/HDR-Gaming-Database/discussions/\d+)\)",
        RegexOptions.Compiled);

    public HdrDatabaseService(HttpClient http)
    {
        _http = http;
    }

    public async Task<Dictionary<string, string>> FetchAsync(IProgress<string>? progress = null)
    {
        // Return cached data if available
        if (CachedData != null)
            return CachedData;

        progress?.Report("Fetching HDR Gaming Database...");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var markdown = await _http.GetStringAsync(ReadmeUrl).ConfigureAwait(false);
            var lines = markdown.Split('\n');

            foreach (var line in lines)
            {
                var match = EntryPattern.Match(line);
                if (!match.Success) continue;

                var gameName = NormalizeQuotes(match.Groups[1].Value.Trim());
                gameName = StripTrademarks(gameName);
                var url = match.Groups[2].Value.Trim();

                // First occurrence wins (case-insensitive)
                result.TryAdd(gameName, url);
            }

            CrashReporter.Log($"[HdrDatabaseService.FetchAsync] Parsed {result.Count} HDR database entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[HdrDatabaseService.FetchAsync] Failed to fetch HDR database — {ex.Message}");
        }

        CachedData = result;
        progress?.Report($"HDR database: {result.Count} entries");
        return result;
    }

    /// <summary>
    /// Strips ™ ® © symbols from a game name so Steam names match database names.
    /// </summary>
    private static string StripTrademarks(string name) =>
        name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

    /// <summary>
    /// Replaces curly/smart quotes and apostrophes with their straight ASCII equivalents
    /// so database game names match Steam names which use standard characters.
    /// </summary>
    private static string NormalizeQuotes(string s) =>
        s.Replace('\u2018', '\'')  // left single quote  → '
         .Replace('\u2019', '\'')  // right single quote → '
         .Replace('\u201C', '"')   // left double quote  → "
         .Replace('\u201D', '"');  // right double quote → "
}
