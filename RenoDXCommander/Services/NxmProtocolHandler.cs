using System.Web;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Implements nxm:// protocol handler registration and URI parsing.
/// Uses HKCU registry entries (per-user, no admin required) and
/// never throws from <see cref="Parse"/> — returns null for malformed input.
/// </summary>
public class NxmProtocolHandler : INxmProtocolHandler
{
    private const string RegistryKeyPath = @"Software\Classes\nxm";

    /// <inheritdoc />
    public bool Register()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                CrashReporter.Log("[NxmProtocolHandler.Register] Cannot determine executable path");
                return false;
            }

            // Create HKCU\Software\Classes\nxm
            using var nxmKey = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            if (nxmKey is null)
            {
                CrashReporter.Log("[NxmProtocolHandler.Register] Failed to create registry key");
                return false;
            }

            nxmKey.SetValue("", "URL:NXM Protocol");
            nxmKey.SetValue("URL Protocol", "");

            // Set shell\open\command to the RHI executable path with "%1" argument
            using var cmdKey = nxmKey.CreateSubKey(@"shell\open\command");
            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            CrashReporter.Log("[NxmProtocolHandler.Register] Registered nxm:// protocol handler");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.Register] Insufficient permissions — {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.Register] Failed — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public bool Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
            CrashReporter.Log("[NxmProtocolHandler.Unregister] Removed nxm:// protocol handler registration");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.Unregister] Insufficient permissions — {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.Unregister] Failed — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsRegistered()
    {
        try
        {
            using var nxmKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (nxmKey is null)
                return false;

            using var cmdKey = nxmKey.OpenSubKey(@"shell\open\command");
            if (cmdKey is null)
                return false;

            var command = cmdKey.GetValue("") as string;
            if (string.IsNullOrEmpty(command))
                return false;

            // Check if the registered command points to the current RHI executable
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return false;

            return command.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.IsRegistered] Error reading registry — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Parses URIs matching: nxm://{domain}/mods/{modId}/files/{fileId}?key={key}&amp;expires={expires}
    /// Never throws — returns null for any malformed input.
    /// </remarks>
    public NxmUri? Parse(string uri)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uri))
                return null;

            // Ensure it starts with nxm://
            if (!uri.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                return null;

            // Use Uri class for robust parsing
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Malformed URI: {uri}");
                return null;
            }

            // Host is the game domain
            var gameDomain = parsed.Host;
            if (string.IsNullOrEmpty(gameDomain))
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Missing game domain: {uri}");
                return null;
            }

            // Path segments: /mods/{modId}/files/{fileId}
            // parsed.Segments gives: ["/", "mods/", "{modId}/", "files/", "{fileId}"]
            var segments = parsed.Segments;
            if (segments.Length < 5)
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Insufficient path segments: {uri}");
                return null;
            }

            // Validate "mods" segment
            if (!segments[1].TrimEnd('/').Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Expected 'mods' segment: {uri}");
                return null;
            }

            // Parse mod ID
            if (!int.TryParse(segments[2].TrimEnd('/'), out var modId) || modId <= 0)
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Invalid mod ID: {uri}");
                return null;
            }

            // Validate "files" segment
            if (!segments[3].TrimEnd('/').Equals("files", StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Expected 'files' segment: {uri}");
                return null;
            }

            // Parse file ID
            if (!int.TryParse(segments[4].TrimEnd('/'), out var fileId) || fileId <= 0)
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Invalid file ID: {uri}");
                return null;
            }

            // Parse query parameters
            var queryParams = HttpUtility.ParseQueryString(parsed.Query);

            var key = queryParams["key"] ?? "";
            if (string.IsNullOrEmpty(key))
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Missing 'key' query parameter: {uri}");
                return null;
            }

            var expiresStr = queryParams["expires"];
            if (!long.TryParse(expiresStr, out var expires) || expires <= 0)
            {
                CrashReporter.Log($"[NxmProtocolHandler.Parse] Invalid 'expires' query parameter: {uri}");
                return null;
            }

            return new NxmUri(gameDomain, modId, fileId, key, expires);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.Parse] Unexpected error parsing URI '{uri}' — {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public string Format(NxmUri nxmUri)
    {
        return $"nxm://{nxmUri.GameDomain}/mods/{nxmUri.ModId}/files/{nxmUri.FileId}?key={nxmUri.Key}&expires={nxmUri.Expires}";
    }
}
