using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander.Controls;

/// <summary>
/// Turns off the background fade in WinUI's Button template, so hover and press colours switch
/// instantly. With Windows animation effects on, the fade from a solid background to WinUI's
/// default near-transparent white hover colour passed through a bright grey (flash on hover).
/// The fade is a BrushTransition on the template's root ContentPresenter, which a Style setter
/// can't reach, so this clears it when the button loads. Set app-wide by the implicit Button
/// style in App.xaml, and by explicit Button styles (SettingsButtonStyle, CogDialog).
/// </summary>
public static class ButtonFade
{
    public static readonly DependencyProperty DisabledProperty =
        DependencyProperty.RegisterAttached("Disabled", typeof(bool), typeof(ButtonFade),
            new PropertyMetadata(false, OnDisabledChanged));

    public static bool GetDisabled(DependencyObject obj) => (bool)obj.GetValue(DisabledProperty);
    public static void SetDisabled(DependencyObject obj, bool value) => obj.SetValue(DisabledProperty, value);

    private static void OnDisabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;
        button.Loaded -= Button_Loaded;
        if (e.NewValue is true)
        {
            button.Loaded += Button_Loaded;
            if (button.IsLoaded) ClearFade(button);
        }
    }

    // Runs on every load: a re-applied template brings the fade back.
    private static void Button_Loaded(object sender, RoutedEventArgs e) => ClearFade((Button)sender);

    private static void ClearFade(Button button)
    {
        if (VisualTreeHelper.GetChildrenCount(button) > 0
            && VisualTreeHelper.GetChild(button, 0) is ContentPresenter presenter
            && presenter.BackgroundTransition != null)
            presenter.BackgroundTransition = null;
    }
}
