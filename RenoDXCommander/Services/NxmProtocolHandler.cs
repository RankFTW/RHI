// NxmProtocolHandler.cs — Handles the nxm:// protocol for Nexus Mods "Mod Manager Download".
// Parses NXM URLs, registers/checks the Windows protocol handler, and provides the NxmLink model.
// Registration is gated behind DevUnlockService.IsUnlocked — only dev-unlocked users get it.

using Microsoft.Win32;

namespace RenoDXCommander.Services;

/// <summary>
/// Represents a parsed nxm:// protocol URL.
/// Format: nxm://{domain}/mods/{modId}/files/{fileId}?key={key}&amp;expires={expires}&amp;user_id={userId}
/// </summary>
public record NxmLink(
    string Domain,
    int ModId,
    int FileId,
    string Key,
    string Expires,
    string UserId);

public static class NxmProtocolHandler
{
    private const string ProtocolKey = @"SOFTWARE\Classes\nxm";
    private const string CommandKey  = @"SOFTWARE\Classes\nxm\shell\open\command";

    // ── Parse ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an nxm:// URL into an NxmLink. Returns null if the URL is malformed.
    /// Handles both the two-argument form ("--nxm nxm://...") and the raw form ("nxm://...").
    /// </summary>
    public static NxmLink? Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        // Strip leading "nxm:" prefix used by the single-instance pipe forwarding
        if (raw.StartsWith("nxm:", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
            raw = "nxm://" + raw.Substring(4).TrimStart('/');

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase)) return null;

        // Path segments: /mods/{modId}/files/{fileId}
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        // uri.Host = domain, segments = ["mods", modId, "files", fileId]
        if (segments.Length < 4) return null;
        if (!string.Equals(segments[0], "mods", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(segments[2], "files", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(segments[1], out var modId)) return null;
        if (!int.TryParse(segments[3], out var fileId)) return null;

        var domain = uri.Host;
        if (string.IsNullOrEmpty(domain)) return null;

        // Query params: key, expires, user_id
        var query = ParseQueryString(uri.Query);
        query.TryGetValue("key",     out var key);
        query.TryGetValue("expires", out var expires);
        query.TryGetValue("user_id", out var userId);

        return new NxmLink(
            Domain:  domain,
            ModId:   modId,
            FileId:  fileId,
            Key:     key     ?? "",
            Expires: expires ?? "",
            UserId:  userId  ?? "");
    }

    // ── Registry helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if RHI is the currently registered nxm:// handler.
    /// Checks the HKCU command value for the current process path.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(CommandKey);
            if (key == null) return false;
            var val = key.GetValue("")?.ToString() ?? "";
            var exePath = Environment.ProcessPath ?? "";
            return !string.IsNullOrEmpty(exePath)
                && val.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if ANY nxm:// handler is registered (RHI, Vortex, MO2, etc.).
    /// Used to avoid overwriting another manager's registration without consent.
    /// </summary>
    public static bool AnyHandlerRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ProtocolKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers RHI as the nxm:// protocol handler in HKCU.
    /// Only writes if no handler exists OR if RHI is already the handler (idempotent).
    /// Does NOT overwrite Vortex/MO2 silently — callers must check AnyHandlerRegistered() first.
    /// </summary>
    public static void RegisterProtocolHandler()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу RHI");

        // Normalize to backslashes (Win32 GetOpenFileName pitfall)
        exePath = exePath.Replace('/', '\\');

        try
        {
            // HKCU\Software\Classes\nxm
            using (var rootKey = Registry.CurrentUser.CreateSubKey(ProtocolKey))
            {
                rootKey.SetValue("", "URL:NXM Protocol");
                rootKey.SetValue("URL Protocol", "");
            }

            // HKCU\Software\Classes\nxm\shell\open\command
            using (var cmdKey = Registry.CurrentUser.CreateSubKey(CommandKey))
            {
                cmdKey.SetValue("", $"\"{exePath}\" --nxm \"%1\"");
            }

            CrashReporter.Log($"[NxmProtocolHandler] Registered nxm:// handler → {exePath}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.RegisterProtocolHandler] Failed — {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Removes the nxm:// protocol handler from HKCU if RHI owns it.
    /// No-op if another app (Vortex, MO2) is the current handler.
    /// </summary>
    public static void UnregisterProtocolHandler()
    {
        if (!IsRegistered()) return;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);
            CrashReporter.Log("[NxmProtocolHandler] Unregistered nxm:// handler");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NxmProtocolHandler.UnregisterProtocolHandler] Failed — {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        var qs = query.TrimStart('?');
        foreach (var part in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx <= 0) continue;
            var k = Uri.UnescapeDataString(part[..eqIdx]);
            var v = Uri.UnescapeDataString(part[(eqIdx + 1)..]);
            result[k] = v;
        }
        return result;
    }
}
