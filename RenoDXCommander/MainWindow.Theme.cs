// MainWindow.Theme.cs — Colour theme (Settings › General › Colour theme): the parts of the window that aren't
// shared brushes and so don't recolour by themselves when ThemeService applies a theme.

using Microsoft.UI.Xaml;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>Title bar colours from App.xaml's TitleBar* colour resources (ThemeService recolours them).</summary>
    internal void ApplyTitleBarColors()
    {
        if (AppWindow.TitleBar is not { } titleBar) return;
        try
        {
            var res = Application.Current.Resources;
            titleBar.BackgroundColor               = (Windows.UI.Color)res["TitleBarBackground"];
            titleBar.ForegroundColor               = (Windows.UI.Color)res["TitleBarForeground"];
            titleBar.InactiveBackgroundColor       = (Windows.UI.Color)res["TitleBarInactiveBackground"];
            titleBar.InactiveForegroundColor       = (Windows.UI.Color)res["TitleBarInactiveForeground"];
            titleBar.ButtonBackgroundColor         = (Windows.UI.Color)res["TitleBarButtonBackground"];
            titleBar.ButtonForegroundColor         = (Windows.UI.Color)res["TitleBarButtonForeground"];
            titleBar.ButtonHoverBackgroundColor    = (Windows.UI.Color)res["TitleBarButtonHoverBackground"];
            titleBar.ButtonHoverForegroundColor    = (Windows.UI.Color)res["TitleBarButtonHoverForeground"];
            titleBar.ButtonPressedBackgroundColor  = (Windows.UI.Color)res["TitleBarButtonPressedBackground"];
            titleBar.ButtonPressedForegroundColor  = (Windows.UI.Color)res["TitleBarButtonPressedForeground"];
            titleBar.ButtonInactiveBackgroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveBackground"];
            titleBar.ButtonInactiveForegroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveForeground"];
        }
        catch (Exception ex) when (ex is InvalidCastException or System.Runtime.InteropServices.COMException or KeyNotFoundException)
        {
            _crashReporter.Log($"[MainWindow.ApplyTitleBarColors] {ex.Message}");
        }
    }

    /// <summary>After a theme change: title bar, and the game list's bound colour strings.</summary>
    private void OnColorThemeChanged()
    {
        ApplyTitleBarColors();
        foreach (var card in ViewModel.AllCards)
            card.NotifyThemeChanged();
    }
}
