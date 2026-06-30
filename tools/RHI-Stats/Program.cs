using System.Text.Json;
using System.Text.RegularExpressions;

var repos = new[]
{
    ("RHI", "https://api.github.com/repos/RankFTW/RHI/releases"),
    ("ReLimiter", "https://api.github.com/repos/RankFTW/ReLimiter/releases"),
    ("RHI Repo", "https://api.github.com/repos/RankFTW/rhi-repo/releases"),
};

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "RHI-Stats");

while (true)
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔══════════════════════════════════════════╗");
    Console.WriteLine("║        RankFTW Download Statistics       ║");
    Console.WriteLine("╚══════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();

    int grandTotal = 0;
    var summaries = new List<(string Name, int Total)>();

    foreach (var (repoName, repoUrl) in repos)
    {
        try
        {
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

                pageUrl = null;
                if (response.Headers.TryGetValues("Link", out var linkValues))
                {
                    var linkHeader = string.Join(",", linkValues);
                    var nextMatch = Regex.Match(linkHeader, @"<([^>]+)>;\s*rel=""next""");
                    if (nextMatch.Success)
                        pageUrl = nextMatch.Groups[1].Value;
                }
            }

            int repoTotal = 0;
            foreach (var release in allReleases)
            {
                if (release.TryGetProperty("assets", out var assets))
                    foreach (var asset in assets.EnumerateArray())
                        if (asset.TryGetProperty("download_count", out var countEl))
                            repoTotal += countEl.GetInt32();
            }

            grandTotal += repoTotal;
            summaries.Add((repoName, repoTotal));
        }
        catch (Exception ex)
        {
            summaries.Add((repoName + " (error)", 0));
        }
    }

    // Summary line
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("  ");
    for (int i = 0; i < summaries.Count; i++)
    {
        if (i > 0) Console.Write("  |  ");
        Console.Write($"{summaries[i].Name}: {summaries[i].Total:N0}");
    }
    Console.WriteLine($"  |  TOTAL: {grandTotal:N0}");
    Console.ResetColor();
    Console.WriteLine();

    // Show latest 10 for each repo
    foreach (var (repoName, repoUrl) in repos)
    {
        try
        {
            var json = await http.GetStringAsync(repoUrl + "?per_page=10");
            using var doc = JsonDocument.Parse(json);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ── {repoName} (latest 10) ──");
            Console.ResetColor();

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString() ?? "?";
                var published = release.GetProperty("published_at").GetString() ?? "";
                var date = DateTime.TryParse(published, out var dt) ? dt.ToString("yyyy-MM-dd") : "—";
                int dl = 0;
                if (release.TryGetProperty("assets", out var assets))
                    foreach (var asset in assets.EnumerateArray())
                        if (asset.TryGetProperty("download_count", out var c))
                            dl += c.GetInt32();

                var color = dl > 100 ? ConsoleColor.White : dl > 10 ? ConsoleColor.Gray : ConsoleColor.DarkGray;
                Console.ForegroundColor = color;
                Console.WriteLine($"    {tag,-20} {dl,8:N0}   {date}");
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        catch { }
    }

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  [R] Refresh   [Q] Quit");
    Console.ResetColor();

    while (true)
    {
        var key = Console.ReadKey(true).Key;
        if (key == ConsoleKey.R) break;
        if (key == ConsoleKey.Q) return;
    }
}
