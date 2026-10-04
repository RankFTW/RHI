using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Localization;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _initializingLanguage;

    private void InitializeLanguageSelector()
    {
        _initializingLanguage = true;
        InterfaceLanguageCombo.SelectedIndex = ViewModel.Settings.InterfaceLanguage == Loc.SimplifiedChinese ? 1 : 0;
        _initializingLanguage = false;
    }

    private void InterfaceLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLanguage || InterfaceLanguageCombo.SelectedItem is not ComboBoxItem item
            || item.Tag is not string language) return;
        language = Loc.NormalizeLanguage(language);
        if (language == Loc.Language) return;
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        Loc.Initialize(language);
        ViewModel.Settings.InterfaceLanguage = language;
        ViewModel.SaveSettingsPublic();
        ViewModel.RefreshLocalizedText();
        ((XamlCatalog)Microsoft.UI.Xaml.Application.Current.Resources["LocalizedStrings"]).Refresh();
        LocalizedValue.RefreshAll();
        _settingsHandler.RefreshGlobalUpdateSummary();
        if (ViewModel.Settings.RecentGamesMenu)
            Services.TrayIconService.UpdateJumpList(ViewModel.Settings.RecentLaunches);
        _crashReporter.Log($"[Localization] Applied {language} immediately");
    }
}
