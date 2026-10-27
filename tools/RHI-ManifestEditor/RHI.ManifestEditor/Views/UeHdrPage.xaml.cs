using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RHI.ManifestEditor.ViewModels;

namespace RHI.ManifestEditor.Views;

public sealed partial class UeHdrPage : Page
{
    private UeHdrSectionViewModel? _vm;
    public UeHdrPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not MainViewModel main || main.UeHdr == null) return;
        _vm = main.UeHdr;
        NativeHdrView.ItemsSource = _vm.NativeHdrGames;
        UeCompatView.ItemsSource = _vm.UeExtendedCompatibility;
        NoUeView.ItemsSource = _vm.NoUeExtendedGames;
        LumaCompatView.ItemsSource = _vm.LumaRenodxCompat;
    }

    private void AddNativeHdr_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddNativeHdrCommand.Execute(null);
    private void RemoveNativeHdr_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is StringItem i) _vm?.RemoveNativeHdrCommand.Execute(i); }
    private void AddUeCompat_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddUeCompatCommand.Execute(null);
    private void RemoveUeCompat_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is UeExtendedCompatItem i) _vm?.RemoveUeCompatCommand.Execute(i); }
    private void AddNoUe_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddNoUeExtendedCommand.Execute(null);
    private void RemoveNoUe_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is StringItem i) _vm?.RemoveNoUeExtendedCommand.Execute(i); }
    private void AddLumaCompat_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddLumaCompatCommand.Execute(null);
    private void RemoveLumaCompat_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is StringItem i) _vm?.RemoveLumaCompatCommand.Execute(i); }
}
