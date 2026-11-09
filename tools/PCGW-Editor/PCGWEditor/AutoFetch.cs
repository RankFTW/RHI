// AutoFetch.cs -- automatic "Fetch Updates" + push: settings and the run log, both stored next to the exe
// (like github_token.txt and pcgw_credentials.txt).
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCGWEditor;

/// <summary>Auto fetch settings (auto_fetch_settings.json).</summary>
public sealed class AutoFetchSettings
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 180;
    public bool PushWhenChanged { get; set; } = true;
    /// <summary>Closing or minimising the window hides it to the system tray; Exit is in the tray menu.</summary>
    public bool KeepInTray { get; set; }
    public DateTime? LastRunUtc { get; set; }

    /// <summary>Choices in the settings window.</summary>
    public static readonly (int Minutes, string Label)[] Intervals =
    {
        (15, "Every 15 minutes"), (30, "Every 30 minutes"), (60, "Every hour"), (120, "Every 2 hours"),
        (180, "Every 3 hours"), (240, "Every 4 hours"), (360, "Every 6 hours"), (720, "Every 12 hours"),
        (1440, "Once a day"),
    };

    private static string FilePath => Path.Combine(AppDir, "auto_fetch_settings.json");
    internal static string AppDir => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static AutoFetchSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AutoFetchSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoFetchSettings.Load] {ex.Message}");
        }
        return new();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoFetchSettings.Save] {ex.Message}");
        }
    }
}

/// <summary>One line in the fetch log.</summary>
public sealed class FetchLogEntry
{
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    /// <summary>"Automatic", "Run now", "Manual" (toolbar Fetch Updates) or "Manual push".</summary>
    public string Trigger { get; set; } = "";
    /// <summary>"Updated", "NoChanges", "Skipped", "Failed" or "Pushed" (a manual push on its own).</summary>
    public string Outcome { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Added { get; set; } = new();
    public List<string> Updated { get; set; } = new();
    /// <summary>Null = no push attempted.</summary>
    public bool? Pushed { get; set; }
    public string? PushError { get; set; }

    // ── For the log window ──
    [JsonIgnore] public string When => TimeUtc.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm:ss");
    [JsonIgnore] public string Headline => Outcome switch
    {
        "Updated"   => $"{Added.Count} new, {Updated.Count} updated",
        "NoChanges" => "Nothing new on PCGW",
        "Skipped"   => "Skipped",
        "Pushed"    => "Pushed to GitHub",
        _           => "Failed",
    };
    [JsonIgnore] public string PushText => Pushed switch
    {
        true  => "Pushed to GitHub",
        false => "Push failed: " + (PushError ?? "unknown error"),
        null  => "",
    };
    [JsonIgnore] public string Games
    {
        get
        {
            var parts = new List<string>();
            if (Added.Count > 0) parts.Add("New: " + string.Join(", ", Added));
            if (Updated.Count > 0) parts.Add("Updated: " + string.Join(", ", Updated));
            return string.Join("\n", parts);
        }
    }
    /// <summary>"good", "neutral", "bad": colour of the headline.</summary>
    [JsonIgnore] public string Tone => Outcome is "Failed" || Pushed == false ? "bad"
                                     : Outcome is "Updated" or "Pushed" ? "good" : "neutral";
}

/// <summary>The fetch log (auto_fetch_log.jsonl, one JSON object per line, newest last; last 1000 kept).</summary>
public static class FetchLog
{
    private const int MaxEntries = 1000;
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(AutoFetchSettings.AppDir, "auto_fetch_log.jsonl");

    /// <summary>Raised (on the caller's thread) after an entry is added.</summary>
    public static event Action<FetchLogEntry>? Added;

    public static void Add(FetchLogEntry entry)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
                var lines = File.ReadAllLines(FilePath);
                if (lines.Length > MaxEntries + 100) File.WriteAllLines(FilePath, lines[^MaxEntries..]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[FetchLog.Add] {ex.Message}");
            }
        }
        Added?.Invoke(entry);
    }

    /// <summary>All entries, newest first.</summary>
    public static List<FetchLogEntry> LoadNewestFirst()
    {
        var list = new List<FetchLogEntry>();
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try { if (JsonSerializer.Deserialize<FetchLogEntry>(line) is { } e) list.Add(e); }
                    catch (JsonException) { /* a damaged line: skip it, keep the rest */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[FetchLog.Load] {ex.Message}");
            }
        }
        list.Reverse();
        return list;
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[FetchLog.Clear] {ex.Message}");
            }
        }
    }
}
