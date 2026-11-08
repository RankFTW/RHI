// PageFetch.cs -- fetches a web page (GitHub wiki pages) with a short time limit.
// The shared HttpClient waits up to 10 minutes (sized for big downloads). When github.com hangs
// (Nov 2026: its UK servers timed out for hours) a page fetch could hold up the startup scan,
// and with it Refresh / Check for updates. With a limit it fails like a 504 and the scan carries on.

namespace RenoDXCommander.Services;

public static class PageFetch
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>GET a page as text; throws TimeoutException if it takes longer than <paramref name="timeout"/>.</summary>
    public static async Task<string> GetStringAsync(HttpClient http, string url, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var limit = timeout ?? DefaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limit);
        try
        {
            return await http.GetStringAsync(url, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No response from {new Uri(url).Host} after {limit.TotalSeconds:0} s");
        }
    }
}
