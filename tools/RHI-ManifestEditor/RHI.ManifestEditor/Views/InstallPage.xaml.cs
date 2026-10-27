using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RHI.ManifestEditor.ViewModels;

namespace RHI.ManifestEditor.Views;

public sealed partial class InstallPage : Page
{
    private InstallSectionViewModel? _vm;
    public InstallPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not MainViewModel main || main.Install == null) return;
        _vm = main.Install;
        InstallWarningsView.ItemsSource = _vm.InstallWarnings;
        SnapshotView.ItemsSource = _vm.SnapshotOverrides;
        DllOverrideView.ItemsSource = _vm.DllNameOverrides;
        LaunchExeView.ItemsSource = _vm.LaunchExeOverrides;
        LegacyVersionsView.ItemsSource = _vm.LegacyReShadeVersions;
        LegacyAvailableView.ItemsSource = _vm.LegacyReShadeAvailable;
        ForceExternalView.ItemsSource = _vm.ForceExternalOnly;
        GacSymlinkView.ItemsSource = _vm.GacSymlinkGames;
        RenodxIniView.ItemsSource = _vm.RenodxIniOverrides;
        RenodxExtraSettingsView.ItemsSource = _vm.RenodxExtraSettings;
    }

    private void AddInstallWarning_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddInstallWarningCommand.Execute(null);
    private void RemoveInstallWarning_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is InstallWarningItem i) _vm?.RemoveInstallWarningCommand.Execute(i); }

    private void AddSnapshot_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddSnapshotOverrideCommand.Execute(null);
    private void RemoveSnapshot_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is KeyValueItem i) _vm?.RemoveSnapshotOverrideCommand.Execute(i); }

    private void AddDllOverride_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddDllOverrideCommand.Execute(null);
    private void RemoveDllOverride_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is DllOverrideItem i) _vm?.RemoveDllOverrideCommand.Execute(i); }

    private void AddLaunchExe_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddLaunchExeOverrideCommand.Execute(null);
    private void RemoveLaunchExe_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is KeyValueItem i) _vm?.RemoveLaunchExeOverrideCommand.Execute(i); }

    private void AddLegacyVersion_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddLegacyVersionCommand.Execute(null);
    private void RemoveLegacyVersion_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is KeyValueItem i) _vm?.RemoveLegacyVersionCommand.Execute(i); }

    private void AddLegacyAvailable_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddLegacyAvailableCommand.Execute(null);
    private void RemoveLegacyAvailable_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is StringItem i) _vm?.RemoveLegacyAvailableCommand.Execute(i); }

    private void AddForceExternal_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddForceExternalCommand.Execute(null);
    private void RemoveForceExternal_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is ForceExternalItem i) _vm?.RemoveForceExternalCommand.Execute(i); }

    private void AddGacSymlink_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddGacSymlinkCommand.Execute(null);
    private void RemoveGacSymlink_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is KeyValueItem i) _vm?.RemoveGacSymlinkCommand.Execute(i); }


    private void AddRenodxIniGame_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddRenodxIniGameCommand.Execute(null);
    private void RemoveRenodxIniGame_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is RenodxIniGameItem i) _vm?.RemoveRenodxIniGameCommand.Execute(i); }

    private void AddRenodxIniEntry_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is RenodxIniGameItem i) i.AddEntryCommand.Execute(null); }
    private void RemoveRenodxIniEntry_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (((Button)s).Tag is not KeyValueItem entry || _vm == null) return;
        foreach (var game in _vm.RenodxIniOverrides)
            if (game.Entries.Contains(entry)) { game.RemoveEntryCommand.Execute(entry); break; }
    }

    private void AddRenodxExtra_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm?.AddRenodxExtraCommand.Execute(null);
    private void RemoveRenodxExtra_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
    { if (((Button)s).Tag is RenodxExtraSettingItem i) _vm?.RemoveRenodxExtraCommand.Execute(i); }
}
