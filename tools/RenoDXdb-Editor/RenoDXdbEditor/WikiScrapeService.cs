using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Web;

namespace RenoDXdbEditor;

/// <summary>
/// A mod entry as scraped from the RenoDX wiki — before any DB comparison.
/// </summary>
public record WikiMod(
    string  Name,
    string  Status,        // "Done" or "WIP"
    string  Author,
    string? SnapshotUrl,
    string? SnapshotUrl32,
    string? NexusUrl,
    string? DiscordUrl,
    string? DiscussionUrl,
    string? Notes);

/// <summary>
/// Fetches and parses the RenoDX wiki Mods page, returning all named mods
/// (those in the main table before any engine/deprecated heading).
/// Logic mirrors fetch-named-mods.ps1 + _wiki-parse-common.ps1.
/// </summary>
public static class WikiScrapeService
{
    private const string WikiUrl = "https://github.com/clshortfuse/renodx/wiki/Mods";

    private static readonly HttpClient _http = new();

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the wiki page and returns all named mods that have status "Done".
    /// Throws on HTTP failure.
    /// </summary>
    public static async Task<List<WikiMod>> FetchNamedDoneModsAsync(string? githubToken = null)
    {
        var html = await FetchHtmlAsync(githubToken);
        var all  = ParseNamedMods(html);
        return all.Where(m => m.Status == "Done").ToList();
    }

    // ── HTML fetch ────────────────────────────────────────────────────────────

    private static async Task<string> FetchHtmlAsync(string? githubToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, WikiUrl);
        req.Headers.UserAgent.ParseAdd("RenoDXdb-Editor/2.0");
        if (!string.IsNullOrEmpty(githubToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    // ── Heading position helper ───────────────────────────────────────────────

    private static int FindHeadingPos(string html, string pattern)
    {
        // Match <h1>–<h6> tags and check their text content
        var matches = Regex.Matches(html, @"<h[1-6][^>]*>.*?</h[1-6]>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match m in matches)
        {
            var text = StripTags(m.Value);
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                return m.Index;
        }
        return -1;
    }

    // ── Tag stripping ─────────────────────────────────────────────────────────

    private static string StripTags(string html)
    {
        var s = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = HttpUtility.HtmlDecode(s);
        s = s.Replace('\u2018', '\'').Replace('\u2019', '\'')
             .Replace('\u201C', '"').Replace('\u201D', '"');
        return s.Trim();
    }

    // ── Main parser ───────────────────────────────────────────────────────────

    private static List<WikiMod> ParseNamedMods(string html)
    {
        // Determine the slice we care about — named mods only, before engine / deprecated sections
        int uePos      = FindHeadingPos(html, @"UE\s+Extended");
        int unityPos   = FindHeadingPos(html, @"unity\s+engine");
        int depPos     = FindHeadingPos(html, @"deprecated");

        var candidates = new[] { uePos, unityPos, depPos }.Where(p => p >= 0).ToArray();
        int endPos     = candidates.Length > 0 ? candidates.Min() : -1;

        var mods = new List<WikiMod>();

        // Find all <table>…</table> blocks
        var tableMatches = Regex.Matches(html,
            @"(?s)<table[^>]*>(.*?)</table>", RegexOptions.IgnoreCase);

        foreach (Match tableMatch in tableMatches)
        {
            int tStart = tableMatch.Index;

            // Only tables within the named-mods slice
            if (endPos >= 0 && tStart >= endPos) continue;

            var tableHtml = tableMatch.Groups[1].Value;

            // Parse header row
            var headerRowMatch = Regex.Match(tableHtml,
                @"(?s)<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase);
            if (!headerRowMatch.Success) continue;

            var headerCells = Regex.Matches(headerRowMatch.Groups[1].Value,
                @"(?s)<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase);
            if (headerCells.Count < 2) continue;

            var headers = headerCells.Cast<Match>()
                .Select(m => StripTags(m.Groups[1].Value).ToLowerInvariant())
                .ToArray();

            // Named-mods table must have a Links column
            bool hasLinks  = headers.Any(h => Regex.IsMatch(h, @"link|download"));
            bool hasStatus = headers.Any(h => Regex.IsMatch(h, @"status"));
            if (!hasLinks) continue;

            // Determine column indices
            int nameCol = 0, authorCol = -1, linksCol = -1, statusCol = -1, notesCol = -1;
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i];
                if      (Regex.IsMatch(h, @"maintainer|author|developer")) authorCol  = i;
                else if (Regex.IsMatch(h, @"link|download"))               linksCol   = i;
                else if (Regex.IsMatch(h, @"status"))                      statusCol  = i;
                else if (Regex.IsMatch(h, @"note"))                        notesCol   = i;
            }

            // Parse data rows (skip the header row)
            var rowMatches = Regex.Matches(tableHtml,
                @"(?s)<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase);
            bool firstRow = true;
            foreach (Match rowMatch in rowMatches)
            {
                if (firstRow) { firstRow = false; continue; }

                var cells = Regex.Matches(rowMatch.Groups[1].Value,
                    @"(?s)<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase);
                if (cells.Count < 2) continue;

                var cellArr = cells.Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToArray();

                // Name
                string name = StripTags(cellArr[nameCol]);
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Author
                string author = authorCol >= 0 && authorCol < cellArr.Length
                    ? StripTags(cellArr[authorCol]) : "";

                // URLs — scan the links cell (or all cells if no dedicated links col)
                string? snapshotUrl = null, snapshotUrl32 = null;
                string? nexusUrl = null, discordUrl = null, discussionUrl = null;

                var scanCells = linksCol >= 0 ? new[] { cellArr[linksCol] } : cellArr;
                foreach (var c in scanCells)
                {
                    foreach (Match hm in Regex.Matches(c, @"href=""([^""]+)"""))
                    {
                        var href = hm.Groups[1].Value;
                        if      (href.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
                            snapshotUrl32 = href;
                        else if (Regex.IsMatch(href, @"\.addon64$|\.github\.io/renodx/|/releases/download/",
                                     RegexOptions.IgnoreCase))
                        {
                            snapshotUrl ??= href;
                        }
                        else if (Regex.IsMatch(href, @"nexusmods\.com", RegexOptions.IgnoreCase))
                            nexusUrl = href;
                        else if (Regex.IsMatch(href, @"discord\.com|discord\.gg", RegexOptions.IgnoreCase))
                            discordUrl = href;
                        else if (Regex.IsMatch(href, @"renodx/discussions/", RegexOptions.IgnoreCase))
                            discussionUrl = href;
                        else if (Regex.IsMatch(href, @"snapshot|download", RegexOptions.IgnoreCase)
                                 && snapshotUrl == null)
                            snapshotUrl = href;
                    }
                }

                // Status — WIP = 🚧, Done = ✅
                string status = "Done";
                if (statusCol >= 0 && statusCol < cellArr.Length)
                {
                    var statusText = StripTags(cellArr[statusCol]);
                    if (statusText.Contains("🚧")) status = "WIP";
                }

                // Notes — tooltip first, then status cell text, then dedicated notes col
                string? notes = null;
                if (statusCol >= 0 && statusCol < cellArr.Length)
                {
                    var tooltipMatch = Regex.Match(cellArr[statusCol], @"title=""([^""]+)""");
                    if (tooltipMatch.Success)
                        notes = tooltipMatch.Groups[1].Value.Trim();

                    if (notes == null)
                    {
                        var afterEmoji = StripTags(cellArr[statusCol])
                            .Replace("✅", "").Replace("🚧", "").Trim();
                        if (!string.IsNullOrWhiteSpace(afterEmoji))
                            notes = afterEmoji;
                    }
                }
                if (notes == null && notesCol >= 0 && notesCol < cellArr.Length)
                {
                    var n = StripTags(cellArr[notesCol]);
                    if (!string.IsNullOrWhiteSpace(n)) notes = n.Trim();
                }
                // Fall through to extra cells if still nothing
                if (notes == null)
                {
                    for (int i = 0; i < cellArr.Length; i++)
                    {
                        if (i == nameCol || i == authorCol || i == linksCol ||
                            i == statusCol || i == notesCol) continue;
                        var n = StripTags(cellArr[i]);
                        if (!string.IsNullOrWhiteSpace(n)) { notes = n.Trim(); break; }
                    }
                }
                if (string.IsNullOrWhiteSpace(notes)) notes = null;

                mods.Add(new WikiMod(name, status, author,
                    snapshotUrl, snapshotUrl32,
                    nexusUrl, discordUrl, discussionUrl, notes));
            }
        }

        return mods;
    }
}

/// <summary>
/// A UE-Extended game entry as scraped from the wiki engine table.
/// The table has Name / Status / Notes columns (no Links column).
/// </summary>
public record WikiUeExtEntry(
    string  Name,
    string  Status,   // "Done" or "WIP"
    string? Notes);

public static class WikiUeExtendedScrapeService
{
    private const string WikiUrl = "https://github.com/clshortfuse/renodx/wiki/Mods";

    private static readonly HttpClient _http = new();

    /// <summary>
    /// Downloads the wiki page and returns all UE-Extended entries with status "Done".
    /// </summary>
    public static async Task<List<WikiUeExtEntry>> FetchUeExtendedDoneAsync(string? githubToken = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, WikiUrl);
        req.Headers.UserAgent.ParseAdd("RenoDXdb-Editor/2.0");
        if (!string.IsNullOrEmpty(githubToken))
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var html = await resp.Content.ReadAsStringAsync();

        var all = ParseUeExtended(html);
        return all.Where(e => e.Status == "Done").ToList();
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    private static List<WikiUeExtEntry> ParseUeExtended(string html)
    {
        // Find the "Unreal Engine Extended" heading
        int ueExtPos = FindHeadingPos(html, @"Unreal\s+Engine\s+Extended");
        if (ueExtPos < 0) return new List<WikiUeExtEntry>();

        // End at the next section after ueExtPos ("Unreal Engine" but NOT "Unreal Engine Extended")
        int ueLegacyPos = FindHeadingPosAfter(html, ueExtPos + 1, @"Unreal\s+Engine(?!\s+Extended)");
        int unityPos    = FindHeadingPosAfter(html, ueExtPos + 1, @"Unity\s+Engine");
        int depPos      = FindHeadingPosAfter(html, ueExtPos + 1, @"deprecated");

        var candidates = new[] { ueLegacyPos, unityPos, depPos }.Where(p => p > ueExtPos).ToArray();
        int endPos = candidates.Length > 0 ? candidates.Min() : -1;

        var entries = new List<WikiUeExtEntry>();

        var tableMatches = Regex.Matches(html,
            @"(?s)<table[^>]*>(.*?)</table>", RegexOptions.IgnoreCase);

        foreach (Match tableMatch in tableMatches)
        {
            int tStart = tableMatch.Index;
            if (tStart < ueExtPos) continue;
            if (endPos >= 0 && tStart >= endPos) continue;

            var tableHtml = tableMatch.Groups[1].Value;

            // Parse header row
            var headerRowMatch = Regex.Match(tableHtml,
                @"(?s)<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase);
            if (!headerRowMatch.Success) continue;

            var headerCells = Regex.Matches(headerRowMatch.Groups[1].Value,
                @"(?s)<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase);
            if (headerCells.Count < 2) continue;

            var headers = headerCells.Cast<Match>()
                .Select(m => StripTagsUe(m.Groups[1].Value).ToLowerInvariant())
                .ToArray();

            // Engine table: has Status, does NOT have Links/Downloads column
            bool hasStatus = headers.Any(h => Regex.IsMatch(h, @"status"));
            bool hasLinks  = headers.Any(h => Regex.IsMatch(h, @"link|download"));
            if (!hasStatus || hasLinks) continue;

            // Column indices
            int nameCol = 0, statusCol = -1, notesCol = -1;
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i];
                if      (Regex.IsMatch(h, @"status")) statusCol = i;
                else if (Regex.IsMatch(h, @"note"))   notesCol  = i;
            }

            // Data rows
            var rowMatches = Regex.Matches(tableHtml,
                @"(?s)<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase);
            bool firstRow = true;
            foreach (Match rowMatch in rowMatches)
            {
                if (firstRow) { firstRow = false; continue; }

                var cells = Regex.Matches(rowMatch.Groups[1].Value,
                    @"(?s)<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase);
                if (cells.Count < 2) continue;

                var cellArr = cells.Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToArray();

                string name = StripTagsUe(cellArr[nameCol]);
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Status
                string status = "Done";
                if (statusCol >= 0 && statusCol < cellArr.Length)
                {
                    if (StripTagsUe(cellArr[statusCol]).Contains("🚧"))
                        status = "WIP";
                }

                // Notes — tooltip first, then plain status cell text after emoji, then notes col
                string? notes = null;
                if (statusCol >= 0 && statusCol < cellArr.Length)
                {
                    var tooltip = Regex.Match(cellArr[statusCol], @"title=""([^""]+)""");
                    if (tooltip.Success)
                        notes = tooltip.Groups[1].Value.Trim();

                    if (notes == null)
                    {
                        var plain = StripTagsUe(cellArr[statusCol])
                            .Replace("✅", "").Replace("🚧", "").Trim();
                        if (!string.IsNullOrWhiteSpace(plain))
                            notes = plain;
                    }
                }
                if (notes == null && notesCol >= 0 && notesCol < cellArr.Length)
                {
                    var n = StripTagsUe(cellArr[notesCol]);
                    if (!string.IsNullOrWhiteSpace(n)) notes = n.Trim();
                }
                // Any remaining non-name cell
                if (notes == null)
                {
                    for (int i = 1; i < cellArr.Length; i++)
                    {
                        if (i == statusCol || i == notesCol) continue;
                        var n = StripTagsUe(cellArr[i]);
                        if (!string.IsNullOrWhiteSpace(n)) { notes = n.Trim(); break; }
                    }
                }
                if (string.IsNullOrWhiteSpace(notes)) notes = null;

                entries.Add(new WikiUeExtEntry(name, status, notes));
            }
        }

        return entries;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int FindHeadingPos(string html, string pattern)
    {
        var matches = Regex.Matches(html, @"<h[1-6][^>]*>.*?</h[1-6]>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match m in matches)
        {
            if (Regex.IsMatch(StripTagsUe(m.Value), pattern, RegexOptions.IgnoreCase))
                return m.Index;
        }
        return -1;
    }

    private static int FindHeadingPosAfter(string html, int afterPos, string pattern)
    {
        var matches = Regex.Matches(html, @"<h[1-6][^>]*>.*?</h[1-6]>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match m in matches)
        {
            if (m.Index <= afterPos) continue;
            if (Regex.IsMatch(StripTagsUe(m.Value), pattern, RegexOptions.IgnoreCase))
                return m.Index;
        }
        return -1;
    }

    private static string StripTagsUe(string html)
    {
        var s = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = System.Web.HttpUtility.HtmlDecode(s);
        s = s.Replace('\u2018', '\'').Replace('\u2019', '\'')
             .Replace('\u201C', '"').Replace('\u201D', '"');
        return s.Trim();
    }
}
