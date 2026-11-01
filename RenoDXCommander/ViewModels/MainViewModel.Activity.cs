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
    internal IDisposable BeginActivity(string text)
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

    private void NotifyActivityChanged()
        => DispatcherQueue?.TryEnqueue(() => OnPropertyChanged(nameof(CurrentActivityText)));

    private sealed class ActivityScope : IDisposable
    {
        private MainViewModel? _owner;
        private readonly int _id;
        public ActivityScope(MainViewModel owner, int id) { _owner = owner; _id = id; }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndActivity(_id);
        }
    }
}
