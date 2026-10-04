using System.ComponentModel;

namespace RenoDXCommander.Localization;

/// <summary>A live, display-only binding. Weak registration never keeps a closed UI alive.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class LocalizedValue : INotifyPropertyChanged
{
    private static readonly List<WeakReference<LocalizedValue>> Values = new();
    private readonly Func<object?> _render;
    public object? Value => _render();
    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizedValue(Func<object?> render)
    {
        _render = render;
        if (Values.Count % 256 == 0) Values.RemoveAll(value => !value.TryGetTarget(out _));
        Values.Add(new(this));
    }

    public static void RefreshAll()
    {
        foreach (var reference in Values.ToArray())
            if (reference.TryGetTarget(out var value))
                value.PropertyChanged?.Invoke(value, new(nameof(Value)));
        Values.RemoveAll(value => !value.TryGetTarget(out _));
    }
}
