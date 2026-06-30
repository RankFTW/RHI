using System.Text.Json;

var repos = new[]
{
    ("RHI", "https://api.github.com/repos/RankFTW/RHI/releases"),
    ("ReLimiter", "https://api.github.com/repos/RankFTW/ReLimiter/releases"),
    ("DOF Fix", "https://api.github.com/repos/RankFTW/rhi-repo/releases"),
};

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "RHI-Stats");

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║        RankFTW Download Statistics       ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

foreach (var (repoName, repoUrl) in repos)
{
    try
    {
        // Fetch all releases (paginated — GitHub returns max 100 per page)
        var allReleases = new List<JsonElement>();
        var pageUrl = repoUrl + "?per_page=100";
        while (!string.IsNullOrEmpty(pageUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            request.Headers.Add("User-Agent", "RHI-Stats");
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                allReleases.Add(el.Clone());

            // Check for next page via Link header
            pageUrl = null;
            if (response.Headers.TryGetValues("Link", out var linkValues))
            {
                var linkHeader = string.Join(",", linkValues);
                var nextMatch = System.Text.RegularExpressions.Regex.Match(linkHeader, @"<([^>]+)>;\s*rel=""next""");
                if (nextMatch.Success)
                    pageUrl = nextMatch.Groups[1].Value;
            }
        }

        int totalDownloads = 0;
        var versionStats = new List<(string Tag, int Downloads, string Date)>();

        foreach (var release in allReleases)
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "unknown";
            var published = release.GetProperty("published_at").GetString() ?? "";
            var date = DateTime.TryParse(published, out var dt) ? dt.ToString("yyyy-MM-dd") : "—";

            int releaseDownloads = 0;
            if (release.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("download_count", out var countEl))
                        releaseDownloads += countEl.GetInt32();
                }
            }

            totalDownloads += releaseDownloads;
            versionStats.Add((tag, releaseDownloads, date));
        }

        // Repo header
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ── {repoName} ──");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Total Downloads: {totalDownloads:N0}");
        Console.ResetColor();
        Console.WriteLine();

        // Table header
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"Version",-20} {"Downloads",10} {"Date",12}");
        Console.WriteLine($"  {"───────────────────",-20} {"──────────",10} {"────────────",12}");
        Console.ResetColor();

        // Per-release stats
        foreach (var (tag, downloads, date) in versionStats)
        {
            var color = downloads > 100 ? ConsoleColor.White
                      : downloads > 10 ? ConsoleColor.Gray
                      : ConsoleColor.DarkGray;
            Console.ForegroundColor = color;
            Console.WriteLine($"  {tag,-20} {downloads,10:N0} {date,12}");
        }

        Console.ResetColor();
        Console.WriteLine();
    }
    catch (HttpRequestException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {repoName}: Error — {ex.Message}");
        Console.ResetColor();
        Console.WriteLine();
    }
}

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Source: GitHub API");
Console.ResetColor();
Console.WriteLine();
Console.Write("Press any key to exit...");
Console.ReadKey(true);
