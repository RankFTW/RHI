using HtmlAgilityPack;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches and parses the OptiScaler wiki Compatibility List and FSR4 Compatibility List pages.
/// Caches results for the session.
/// </summary>
public interface IOptiScalerWikiService
{
    Task<OptiScalerWikiData> FetchAsync(IProgress<string>? progress = null);
    OptiScalerWikiData? CachedData { get; }
}

public class OptiScalerWikiService : IOptiScalerWikiService
{
    private readonly HttpClient _http;

    /// <summary>Session-cached wiki data. Populated after the first successful fetch.</summary>
    public OptiScalerWikiData? CachedData { get; private set; }

    // ── Wiki URLs ─────────────────────────────────────────────────────────────────
    internal const string CompatibilityListUrl =
        "https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List";

    internal const string Fsr4CompatibilityListUrl =
        "https://github.com/optiscaler/OptiScaler/wiki/FSR4-Compatibility-List";

    /// <summary>Base URL for constructing detail page links from relative hrefs.</summary>
    private const string WikiBaseUrl = "https://github.com/optiscaler/OptiScaler/wiki/";

    public OptiScalerWikiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<OptiScalerWikiData> FetchAsync(IProgress<string>? progress = null)
    {
        // Return cached data if available
        if (CachedData != null)
            return CachedData;

        progress?.Report("Fetching OptiScaler wiki...");

        var standardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase);
        var fsr4Compat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase);

        // Fetch both pages in parallel
        var standardTask = FetchPageSafeAsync(CompatibilityListUrl);
        var fsr4Task = FetchPageSafeAsync(Fsr4CompatibilityListUrl);

        await Task.WhenAll(standardTask, fsr4Task).ConfigureAwait(false);

        var standardHtml = await standardTask;
        var fsr4Html = await fsr4Task;

        // Parse standard compatibility list
        if (!string.IsNullOrEmpty(standardHtml))
        {
            try
            {
                ParseCompatibilityPage(standardHtml, standardCompat, isFsr4: false);
                CrashReporter.Log($"[OptiScalerWikiService.FetchAsync] Parsed {standardCompat.Count} standard compat entries");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerWikiService.FetchAsync] Failed to parse standard compat list — {ex.Message}");
            }
        }

        // Parse FSR4 compatibility list
        if (!string.IsNullOrEmpty(fsr4Html))
        {
            try
            {
                ParseCompatibilityPage(fsr4Html, fsr4Compat, isFsr4: true);
                CrashReporter.Log($"[OptiScalerWikiService.FetchAsync] Parsed {fsr4Compat.Count} FSR4 compat entries");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerWikiService.FetchAsync] Failed to parse FSR4 compat list — {ex.Message}");
            }
        }

        var data = new OptiScalerWikiData
        {
            StandardCompat = standardCompat,
            Fsr4Compat = fsr4Compat
        };

        CachedData = data;
        progress?.Report($"OptiScaler wiki: {standardCompat.Count} standard + {fsr4Compat.Count} FSR4 entries");
        return data;
    }

    /// <summary>
    /// Fetches a wiki page HTML, returning null on failure.
    /// </summary>
    private async Task<string?> FetchPageSafeAsync(string url)
    {
        try
        {
            return await _http.GetStringAsync(url).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerWikiService.FetchPageSafeAsync] Failed to fetch '{url}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses an OptiScaler wiki compatibility page HTML and populates the dictionary.
    /// The standard compatibility list table has columns:
    ///   Game Name | Working (✔️/❌) | FSR4 (✅) | Upscalers | Notes | Screenshots
    /// Defensive: null checks on every SelectSingleNode/SelectNodes call.
    /// </summary>
    internal static void ParseCompatibilityPage(
        string html,
        Dictionary<string, OptiScalerCompatEntry> entries,
        bool isFsr4)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return;

        foreach (var table in tables)
        {
            ParseTable(table, entries);
        }
    }

    /// <summary>
    /// Parses a single HTML table from the OptiScaler wiki.
    /// </summary>
    internal static void ParseTable(
        HtmlNode table,
        Dictionary<string, OptiScalerCompatEntry> entries)
    {
        var rows = table.SelectNodes(".//tr");
        if (rows == null || rows.Count < 2) return;

        // Skip header rows (first 1-2 rows are typically headers)
        // Detect header rows by checking for <th> elements
        var dataRows = rows.Where(r =>
        {
            var ths = r.SelectNodes("th");
            return ths == null || ths.Count == 0;
        }).ToList();

        foreach (var row in dataRows)
        {
            try
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count < 2) continue;

                // Column 0: Game name (may contain hyperlink to detail page)
                var nameCell = cells[0];
                var gameName = Clean(nameCell.InnerText);
                if (string.IsNullOrWhiteSpace(gameName)) continue;

                // Extract detail page URL from game name hyperlink
                string? detailPageUrl = null;
                var nameLink = nameCell.SelectSingleNode(".//a");
                if (nameLink != null)
                {
                    var href = nameLink.GetAttributeValue("href", "").Trim();
                    if (!string.IsNullOrEmpty(href))
                    {
                        detailPageUrl = ResolveUrl(href);
                    }
                }

                // Column 1: Working status (✔️ = Working, ❌ = Not Working)
                string status = "Unknown";
                if (cells.Count > 1)
                {
                    var statusText = cells[1].InnerText;
                    if (statusText.Contains("✔") || statusText.Contains("✔️"))
                        status = "Working";
                    else if (statusText.Contains("❌"))
                        status = "Not Working";
                }

                // Column 2: FSR4 status (✅ = confirmed working with FSR4)
                // This is optional — some entries have it, some don't
                // Column 3: Upscalers (DLSS, FSR, XeSS, etc.)
                // Column 4: Notes
                // The exact column indices depend on whether the FSR4 column is present

                var upscalers = new List<string>();
                string? notes = null;

                // The table structure has 6 columns:
                // 0: Game Name, 1: Working, 2: FSR4, 3: Upscalers, 4: Notes, 5: Screenshots
                // But some rows may have fewer cells
                if (cells.Count > 3)
                {
                    // Column 3: Upscalers
                    var upscalerText = Clean(cells[3].InnerText);
                    if (!string.IsNullOrWhiteSpace(upscalerText))
                    {
                        upscalers = upscalerText
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(u => !string.IsNullOrWhiteSpace(u))
                            .ToList();
                    }
                }

                if (cells.Count > 4)
                {
                    // Column 4: Notes
                    notes = Clean(cells[4].InnerText);
                    if (string.IsNullOrWhiteSpace(notes))
                        notes = null;
                }

                // Check FSR4 column (column 2) for partial status
                if (cells.Count > 2)
                {
                    var fsr4Text = cells[2].InnerText;
                    if (fsr4Text.Contains("✅"))
                    {
                        // FSR4 confirmed working — could enhance status
                    }
                }

                // Don't overwrite existing entries (first occurrence wins)
                if (!entries.ContainsKey(gameName))
                {
                    entries[gameName] = new OptiScalerCompatEntry
                    {
                        GameName = gameName,
                        Status = status,
                        Upscalers = upscalers,
                        Notes = notes,
                        DetailPageUrl = detailPageUrl
                    };
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerWikiService.ParseTable] Failed to parse row — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolves a potentially relative URL to an absolute URL.
    /// </summary>
    private static string? ResolveUrl(string href)
    {
        if (string.IsNullOrEmpty(href)) return null;

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;

        if (href.StartsWith("/"))
            return "https://github.com" + href;

        // Relative wiki link
        return WikiBaseUrl + href.TrimStart('.', '/');
    }

    private static string Clean(string s) =>
        NormalizeQuotes(HtmlEntity.DeEntitize(s ?? "").Trim());

    /// <summary>
    /// Replaces curly/smart quotes and apostrophes with their straight ASCII equivalents
    /// so wiki game names match Steam names which use standard characters.
    /// </summary>
    private static string NormalizeQuotes(string s) =>
        s.Replace('\u2018', '\'')  // left single quote  → '
         .Replace('\u2019', '\'')  // right single quote → '
         .Replace('\u201C', '"')   // left double quote  → "
         .Replace('\u201D', '"');  // right double quote → "
}
