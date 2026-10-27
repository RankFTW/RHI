namespace RenoDXCommander.Services;
internal static class UiDispatch
{
    internal static async Task<T> InvokeAsync<T>(Func<Action, bool> enqueue, Func<T> action,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (!enqueue(() =>
        {
            if (completion.Task.IsCompleted) return;
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }))
            completion.TrySetException(new InvalidOperationException("The UI dispatcher is shutting down."));
        return await completion.Task.ConfigureAwait(false);
    }
}
