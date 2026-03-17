using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
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

public class InvertBoolToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class StringToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        !string.IsNullOrWhiteSpace(v as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class StringToBool : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) => !string.IsNullOrWhiteSpace(v as string);
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class HexColorToBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, string l)
    {
        if (v is not string hex) return new SolidColorBrush(Colors.Gray);
        try
        {
            hex = hex.TrimStart('#');
            var a = hex.Length == 8 ? System.Convert.ToByte(hex[..2], 16) : (byte)255;
            var r = System.Convert.ToByte(hex.Length == 8 ? hex[2..4] : hex[..2], 16);
            var g = System.Convert.ToByte(hex.Length == 8 ? hex[4..6] : hex[2..4], 16);
            var b = System.Convert.ToByte(hex.Length == 8 ? hex[6..8] : hex[4..6], 16);
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
        catch { return new SolidColorBrush(Colors.Gray); }
    }
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class GameStatusToInstallVisible : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is GameStatus s && s is GameStatus.Available or GameStatus.NotInstalled or GameStatus.UpdateAvailable
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class GameStatusInstalledVisible : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is GameStatus s && s is GameStatus.Installed or GameStatus.UpdateAvailable
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

// ── Sidebar list item converters (bool IsSelected → theme brush) ────────────
public class BoolToSidebarItemBg : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is true
            ? (SolidColorBrush)Application.Current.Resources["SidebarItemSelectedBrush"]
            : new SolidColorBrush(Colors.Transparent);
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToSidebarItemBorder : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is true
            ? (SolidColorBrush)Application.Current.Resources["SidebarItemSelectedBorderBrush"]
            : new SolidColorBrush(Colors.Transparent);
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}

public class BoolToSidebarItemFg : IValueConverter
{
    public object Convert(object v, Type t, object p, string l) =>
        v is true
            ? (SolidColorBrush)Application.Current.Resources["SidebarItemSelectedTextBrush"]
            : (SolidColorBrush)Application.Current.Resources["SidebarItemTextBrush"];
    public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
}
