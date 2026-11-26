namespace RenoDXCommander.Services;

/// <summary>
/// One download of the reshade.me home page for the two ReShade update checks
/// (with and without add-on support), which start at the same moment.
/// </summary>
internal static class ReShadeHomePage
{
    private static readonly object _lock = new();
    private static Task<(string html, int status)>? _task;
    private static DateTime _atUtc;

    // Shared by the two ReShade update checks, which start together. Reused for 60 s.
    public static Task<(string html, int status)> GetAsync(HttpClient http, string url)
    {
        lock (_lock)
        {
            if (_task != null && !_task.IsFaulted && !_task.IsCanceled
                && (!_task.IsCompleted || DateTime.UtcNow - _atUtc < TimeSpan.FromSeconds(60)))
                return _task;
            _atUtc = DateTime.UtcNow;
            return _task = FetchAsync(http, url);
        }
    }

    // No EnsureSuccessStatusCode: reshade.me returns HTTP 500 with a valid page.
    private static async Task<(string, int)> FetchAsync(HttpClient http, string url)
    {
        using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
        return (await response.Content.ReadAsStringAsync(), (int)response.StatusCode);
    }
}
