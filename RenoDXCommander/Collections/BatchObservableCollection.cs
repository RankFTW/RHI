using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace RenoDXCommander.Collections;

/// <summary>
/// An ObservableCollection that supports batch updates with a single Reset notification.
/// Use <see cref="ReplaceAll"/> to clear and repopulate the collection while firing
/// only one <see cref="NotifyCollectionChangedAction.Reset"/> event instead of
/// individual Add/Remove notifications per item.
/// </summary>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    /// <summary>
    /// Replaces all items in the collection with the provided list,
    /// firing a single Reset notification instead of per-item Add notifications.
    /// </summary>
    public void ReplaceAll(IList<T> newItems)
    {
        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in newItems)
                Items.Add(item);
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnPropertyChanged(e);
    }
}
