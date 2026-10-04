using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace RenoDXCommander.Localization;

/// <summary>Live, display-only bindings for programmatically created text.</summary>
public static class Live
{
    private static readonly Dictionary<(Type, string), DependencyProperty> Properties = new();
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<string, LocalizedValue>> ManualValues = new();

    public static T Localize<T>(this T element, string property, object? displayText) where T : DependencyObject
    {
        var key = (element.GetType(), property);
        var value = new LocalizedValue(() => displayText is string text ? Loc.Relocalize(text) : displayText);
        if (!Properties.TryGetValue(key, out var dependencyProperty))
        {
            dependencyProperty = property == "ToolTip" ? ToolTipService.ToolTipProperty
                : (DependencyProperty?)(key.Item1.GetProperty(property + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null) ?? key.Item1.GetField(property + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null));
            // Run.Text and some other WinUI properties do not expose a public DP identifier.
            if (dependencyProperty == null)
            {
                var propertyInfo = key.Item1.GetProperty(property)
                    ?? throw new InvalidOperationException($"Unknown localized property: {key.Item1.Name}.{property}");
                var target = new WeakReference<DependencyObject>(element);
                value.PropertyChanged += (_, _) =>
                {
                    if (target.TryGetTarget(out var liveElement)) propertyInfo.SetValue(liveElement, value.Value);
                };
                ManualValues.GetOrCreateValue(element)[property] = value;
                propertyInfo.SetValue(element, value.Value);
                return element;
            }
            Properties[key] = dependencyProperty;
        }
        BindingOperations.SetBinding(element, dependencyProperty, new Binding
        {
            Source = value,
            Path = new PropertyPath(nameof(LocalizedValue.Value)),
            Mode = BindingMode.OneWay,
        });
        return element;
    }
}
