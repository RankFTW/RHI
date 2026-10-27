using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Windows.UI;

namespace RenoDXCommander.Converters;

public class BoolToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l) =>
        v is Visibility vis && vis == Visibility.Visible;
}
