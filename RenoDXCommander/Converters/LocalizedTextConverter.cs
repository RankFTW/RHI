using Microsoft.UI.Xaml.Data;
using RenoDXCommander.Localization;

namespace RenoDXCommander.Converters;

/// <summary>Translate option labels while leaving SelectedItem and saved values in English.</summary>
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new LocalizedValue(() => value is string text ? Loc.Option(text) : value);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Localized labels are display-only.");
}
