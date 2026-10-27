namespace RenoDXCommander.Services;
/// <summary>
/// Serializes modal lifetimes, not just calls to Show/Hide. Call from the UI thread.
/// Kept independent of WinUI so delayed closing and shutdown can be regression tested.
/// </summary>
internal sealed class DialogCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private DialogSession? _active;
    public bool IsOpen => _gate.CurrentCount == 0;
    public async Task<DialogSession?> OpenAsync(Func<Task> show, Action hide, TimeSpan timeout)
    {
        try
        {
            if (!await _gate.WaitAsync(timeout, _shutdown.Token)) return null;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return null;
        }
        if (_shutdown.IsCancellationRequested)
        {
            _gate.Release();
            return null;
        }
        Task completion;
        try { completion = show(); }
        catch
        {
            _gate.Release();
            throw;
        }
        var session = new DialogSession(hide);
        _active = session;
        session.Completion = ObserveAsync(completion, session);
        // Show may pump UI events, including a shutdown request.
        if (_shutdown.IsCancellationRequested) session.Hide();
        return session;
    }
    private async Task ObserveAsync(Task completion, DialogSession session)
    {
        try { await completion; }
        finally
        {
            if (ReferenceEquals(_active, session)) _active = null;
            _gate.Release();
        }
    }
    public void Stop()
    {
        if (_shutdown.IsCancellationRequested) return;
        _shutdown.Cancel();
        _active?.Hide();
    }
}
internal sealed class DialogSession : IAsyncDisposable
{
    private readonly Action _hide;
    private bool _hideRequested;
    internal DialogSession(Action hide) => _hide = hide;
    public Task Completion { get; internal set; } = Task.CompletedTask;
    internal void Hide()
    {
        if (_hideRequested || Completion.IsCompleted) return;
        _hideRequested = true;
        _hide();
    }
    public async ValueTask DisposeAsync()
    {
        Hide();
        // Hide starts a closing animation. Ownership ends only when ShowAsync completes.
        await Completion;
    }
}
