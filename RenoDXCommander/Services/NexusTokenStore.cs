using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Persists Nexus Mods API keys and OAuth tokens to disk using DPAPI encryption
/// (DataProtectionScope.CurrentUser). Tokens are stored at
/// <c>%LocalAppData%\RHI\nexus_tokens.dat</c> for API keys and
/// <c>%LocalAppData%\RHI\nexus_oauth_tokens.dat</c> for OAuth tokens.
/// </summary>
public class NexusTokenStore : INexusTokenStore
{
    private static readonly string DefaultTokenDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI");

    private readonly string _apiKeyPath;
    private readonly string _oauthPath;

    /// <summary>
    /// Initializes a new instance using the default token directory
    /// (<c>%LocalAppData%\RHI</c>).
    /// </summary>
    public NexusTokenStore()
        : this(DefaultTokenDir) { }

    /// <summary>
    /// Initializes a new instance with a custom storage directory.
    /// Used by tests to avoid polluting real app data.
    /// </summary>
    internal NexusTokenStore(string tokenDir)
    {
        _apiKeyPath = Path.Combine(tokenDir, "nexus_tokens.dat");
        _oauthPath = Path.Combine(tokenDir, "nexus_oauth_tokens.dat");
    }

    /// <inheritdoc />
    public void SaveApiKey(string apiKey)
    {
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(apiKey);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            EnsureDirectory(_apiKeyPath);
            File.WriteAllBytes(_apiKeyPath, encrypted);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusTokenStore.SaveApiKey] Failed to save API key — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string? LoadApiKey()
    {
        try
        {
            if (!File.Exists(_apiKeyPath))
                return null;

            var encrypted = File.ReadAllBytes(_apiKeyPath);
            var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusTokenStore.LoadApiKey] Decryption failed, deleting corrupted file — {ex.Message}");
            TryDelete(_apiKeyPath);
            return null;
        }
    }

    /// <inheritdoc />
    public void SaveOAuthTokens(string accessToken, string refreshToken, DateTimeOffset expiry)
    {
        try
        {
            var data = new OAuthTokenData(accessToken, refreshToken, expiry);
            var json = JsonSerializer.Serialize(data);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            EnsureDirectory(_oauthPath);
            File.WriteAllBytes(_oauthPath, encrypted);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusTokenStore.SaveOAuthTokens] Failed to save OAuth tokens — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OAuthTokenData? LoadOAuthTokens()
    {
        try
        {
            if (!File.Exists(_oauthPath))
                return null;

            var encrypted = File.ReadAllBytes(_oauthPath);
            var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<OAuthTokenData>(json);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusTokenStore.LoadOAuthTokens] Decryption failed, deleting corrupted file — {ex.Message}");
            TryDelete(_oauthPath);
            return null;
        }
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        TryDelete(_apiKeyPath);
        TryDelete(_oauthPath);
    }

    /// <inheritdoc />
    public bool HasStoredToken()
    {
        return File.Exists(_apiKeyPath) || File.Exists(_oauthPath);
    }

    /// <summary>
    /// Ensures the parent directory of the given file path exists.
    /// </summary>
    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Attempts to delete a file, logging any failure.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusTokenStore.TryDelete] Failed to delete {path} — {ex.Message}");
        }
    }
}
