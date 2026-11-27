// MainViewModel.Activity.cs -- What RHI is doing in the background right now, for the status bar.

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private readonly object _activityLock = new();
    private readonly List<(int Id, string Text)> _activities = new();
    private int _nextActivityId;

    /// <summary>
    /// The most recently started background activity that is still running
    /// (e.g. "Checking for updates..."), or "" when idle. Safe to read from any thread.
    /// </summary>
    public string CurrentActivityText
    {
        get { lock (_activityLock) return _activities.Count > 0 ? _activities[^1].Text : ""; }
    }

    /// <summary>
    /// Marks a background activity as running until the returned object is disposed.
    /// Activities can overlap; the status bar shows the newest one still running.
    /// </summary>
    internal ActivityScope BeginActivity(string text)
    {
        int id;
        lock (_activityLock)
        {
            id = ++_nextActivityId;
            _activities.Add((id, text));
        }
        NotifyActivityChanged();
        return new ActivityScope(this, id);
    }

    private void EndActivity(int id)
    {
        lock (_activityLock) _activities.RemoveAll(a => a.Id == id);
        NotifyActivityChanged();
    }

    /// <summary>Changes a running activity's text (e.g. to show a slow download inside "Checking for updates...").</summary>
    private void SetActivityText(int id, string text)
    {
        lock (_activityLock)
        {
            int i = _activities.FindIndex(a => a.Id == id);
            if (i < 0 || _activities[i].Text == text) return;
            _activities[i] = (id, text);
        }
        NotifyActivityChanged();
    }

    private void NotifyActivityChanged()
        => DispatcherQueue?.TryEnqueue(() => OnPropertyChanged(nameof(CurrentActivityText)));

    internal sealed class ActivityScope : IDisposable
    {
        private MainViewModel? _owner;
        private readonly int _id;
        public ActivityScope(MainViewModel owner, int id) { _owner = owner; _id = id; }
        /// <summary>Changes the text shown while this activity runs (no-op once it ended).</summary>
        public void SetText(string text) => Volatile.Read(ref _owner)?.SetActivityText(_id, text);
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndActivity(_id);
        }
    }
}
