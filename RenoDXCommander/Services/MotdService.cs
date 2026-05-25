using System.Security.Cryptography;
using System.Text;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches and displays a Message of the Day from GitHub.
/// Shows a dialog once per unique message (tracked by content hash).
/// </summary>
public static class MotdService
{
    private const string MotdUrl = "https://raw.githubusercontent.com/RankFTW/RHI/main/motd.md";
    private static readonly string HashPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "motd_hash.txt");

    /// <summary>
    /// Fetches the MOTD and returns the content if it's new (not yet shown to this user).
    /// Returns null if no message, empty message, or already shown.
    /// </summary>
    public static async Task<string?> CheckAsync(HttpClient http)
    {
        try
        {
            var response = await http.GetAsync(MotdUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) return null;

            // Hash the content to detect changes
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

            // Check if we've already shown this message
            var lastHash = File.Exists(HashPath) ? File.ReadAllText(HashPath).Trim() : "";
            if (hash == lastHash) return null;

            // Save the new hash
            Directory.CreateDirectory(Path.GetDirectoryName(HashPath)!);
            File.WriteAllText(HashPath, hash);

            return content.Trim();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MotdService.CheckAsync] Failed — {ex.Message}");
            return null;
        }
    }
}
