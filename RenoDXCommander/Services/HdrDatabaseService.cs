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

    private const string DiscussionUrl = @"https://github\.com/KoKlusz/HDR-Gaming-Database/discussions/\d+";

    /// <summary>
    /// Matches an entry in either README format:
    ///   HTML table (current):  &lt;a href="https://github.com/KoKlusz/HDR-Gaming-Database/discussions/123"&gt;Game Name&lt;/a&gt;
    ///   Markdown list (older): * [Game Name](https://github.com/KoKlusz/HDR-Gaming-Database/discussions/123)
    /// </summary>
    private static readonly Regex EntryPattern = new(
        @"<a\s+href=""(?<url>" + DiscussionUrl + @")""\s*>(?<name>[^<]+)</a>"
        + @"|^\s*\*\s*\[(?<name>[^\]]+)\]\((?<url>" + DiscussionUrl + @")\)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

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
            result = Parse(markdown);

            CrashReporter.Log(result.Count == 0
                ? $"[HdrDatabaseService.FetchAsync] Parsed 0 HDR database entries from {markdown.Length} chars — the README format may have changed"
                : $"[HdrDatabaseService.FetchAsync] Parsed {result.Count} HDR database entries");
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
    /// Game name → discussion URL from the README text. Names are HTML-decoded ("Ratchet &amp;amp; Clank"),
    /// with curly quotes straightened and ™ ® © removed so they match store names. First occurrence wins.
    /// </summary>
    internal static Dictionary<string, string> Parse(string readme)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EntryPattern.Matches(readme))
        {
            var gameName = StripTrademarks(NormalizeQuotes(System.Net.WebUtility.HtmlDecode(match.Groups["name"].Value).Trim()));
            if (gameName.Length == 0) continue;
            result.TryAdd(gameName, match.Groups["url"].Value.Trim());
        }
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
