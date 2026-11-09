// LaunchSessionStore.cs -- remembers games started from RHI and the display changes made for them,
// so HDR / resolution are still put back when the game exits after RHI was refreshed or restarted.
using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>A game launched from RHI and what RHI changed for it.</summary>
public sealed class LaunchSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameName { get; set; } = "";
    public string Store { get; set; } = "";
    public string? InstallPath { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True once the game's process has been found (it may still be starting before that).</summary>
    public bool GameSeen { get; set; }

    public bool HdrToggled { get; set; }
    public List<uint>? HdrTargets { get; set; }

    public bool ResToggled { get; set; }
    /// <summary>Resolution key ("WxH@Hz") to go back to; null = Windows' saved default.</summary>
    public string? ResolutionToRestore { get; set; }

    public bool Matches(string gameName, string? store) =>
        string.Equals(GameName, gameName, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrEmpty(store) || string.IsNullOrEmpty(Store)
            || string.Equals(Store, store, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Launch sessions saved in <c>%LocalAppData%\RHI\launch_sessions.json</c>. Written on launch,
/// removed once the game has exited and the display changes were reverted. Thread-safe.
/// </summary>
public sealed class LaunchSessionStore
{
    public static LaunchSessionStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "launch_sessions.json"));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private List<LaunchSession>? _sessions;

    public LaunchSessionStore(string path) => _path = path;

    public List<LaunchSession> GetAll()
    {
        lock (_lock) return Load().Select(Clone).ToList();
    }

    public void Add(LaunchSession session)
    {
        lock (_lock) { Load().Add(Clone(session)); Save(); }
    }

    public void MarkSeen(string id)
    {
        lock (_lock)
        {
            var s = Load().FirstOrDefault(x => x.Id == id);
            if (s == null || s.GameSeen) return;
            s.GameSeen = true;
            Save();
        }
    }

    public void Remove(string id)
    {
        lock (_lock) { if (Load().RemoveAll(x => x.Id == id) > 0) Save(); }
    }

    /// <summary>True if a game launched from RHI with this name / store is running.</summary>
    public bool IsRunning(string gameName, string? store)
    {
        lock (_lock) return Load().Any(s => s.GameSeen && s.Matches(gameName, store));
    }

    /// <summary>
    /// Removes a finished session and says what to put back. HDR / resolution are left alone while
    /// another game launched from RHI is still running and needs them; the resolution from before the first of
    /// those launches is handed on to the session still running, so the last one to exit restores it.
    /// </summary>
    public (bool RevertHdr, bool RevertResolution) Finish(string id)
    {
        lock (_lock)
        {
            var list = Load();
            var s = list.FirstOrDefault(x => x.Id == id);
            if (s == null) return (false, false);
            list.Remove(s);

            // Only games actually seen running count: a launch whose game never appeared (still starting,
            // or a store launch RHI couldn't track) must not keep HDR on after this game closes.
            bool revertHdr = s.HdrToggled && !list.Any(x => x.HdrToggled && x.GameSeen);
            bool revertRes = false;
            if (s.ResToggled)
            {
                var other = list.Where(x => x.ResToggled && x.GameSeen).OrderBy(x => x.StartedUtc).FirstOrDefault();
                if (other == null) revertRes = true;
                else if (s.StartedUtc < other.StartedUtc) other.ResolutionToRestore = s.ResolutionToRestore;
            }
            Save();
            return (revertHdr, revertRes);
        }
    }

    private List<LaunchSession> Load()
    {
        if (_sessions != null) return _sessions;
        _sessions = new();
        try
        {
            if (File.Exists(_path))
                _sessions = JsonSerializer.Deserialize<List<LaunchSession>>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[LaunchSessionStore.Load] Couldn't read '{_path}' — {ex.Message}");
        }
        return _sessions;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (_sessions == null || _sessions.Count == 0) { File.Delete(_path); return; }
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_sessions, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[LaunchSessionStore.Save] Couldn't write '{_path}' — {ex.Message}");
        }
    }

    private static LaunchSession Clone(LaunchSession s) => new()
    {
        Id = s.Id, GameName = s.GameName, Store = s.Store, InstallPath = s.InstallPath,
        StartedUtc = s.StartedUtc, GameSeen = s.GameSeen,
        HdrToggled = s.HdrToggled, HdrTargets = s.HdrTargets?.ToList(),
        ResToggled = s.ResToggled, ResolutionToRestore = s.ResolutionToRestore,
    };
}
