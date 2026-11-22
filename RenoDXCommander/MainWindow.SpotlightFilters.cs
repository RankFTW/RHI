// MainWindow.SpotlightFilters.cs — Spotlight: the Filters button opens the engine / mod chips and saved
// searches in a flyout beside the button, instead of a row under the search that pushes the strip around.
// Classic and Tabs keep the row. The chips are the XAML row's content, moved into the flyout while it's
// open and put back when it closes, so every handler and RefreshFilterButtonStyles keep working.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private Flyout? _filtersFlyout;
    private Border? _filtersFlyoutHost;

    /// <summary>The Filters button: a flyout in Spotlight, the chips row elsewhere.</summary>
    private void ToggleFilters()
    {
        if (_spotlightOn)
        {
            if (_filtersFlyout?.IsOpen == true) _filtersFlyout.Hide();
            else ShowFiltersFlyout();
            return;
        }
        _filtersOpen = !_filtersOpen;
        RefreshTabsChrome();
    }

    private void ShowFiltersFlyout()
    {
        if (_filtersBtn == null || _chipsRow == null) return;
        try
        {
            // Built on each open so it picks up the current theme colours
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 620.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, TabsTheme.B(TabsTheme.PanelBg)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, TabsTheme.B(TabsTheme.CardBorder)));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));

            _filtersFlyoutHost = new Border { MaxWidth = 590 };
            var content = _chipsRow.Child;
            _chipsRow.Child = null; // an element can only have one parent
            _filtersFlyoutHost.Child = content;

            _filtersFlyout = new Flyout
            {
                Content = _filtersFlyoutHost,
                FlyoutPresenterStyle = style,
                // Opens towards the page: above the button when the strip is at the bottom
                Placement = ViewModel.Settings.SpotlightShelfAtBottom ? FlyoutPlacementMode.Top : FlyoutPlacementMode.Bottom,
            };
            _filtersFlyout.Closed += (_, _) => ReturnFilterChips();
            _filtersFlyout.ShowAt(_filtersBtn);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowFiltersFlyout] Failed — {ex.Message}");
            ReturnFilterChips();
        }
    }

    /// <summary>Puts the chips back in their XAML row (after the flyout closes).</summary>
    private void ReturnFilterChips()
    {
        if (_filtersFlyoutHost?.Child is not { } content || _chipsRow == null) return;
        _filtersFlyoutHost.Child = null;
        _chipsRow.Child = content;
    }

    /// <summary>Closes the filters flyout (layout switch).</summary>
    private void HideFiltersFlyout()
    {
        if (_filtersFlyout?.IsOpen == true) _filtersFlyout.Hide();
        ReturnFilterChips();
    }

    /// <summary>Controller Filters button: open / close, and focus the first chip when opened.</summary>
    private void PadToggleFilters(XamlRoot xamlRoot)
    {
        if (_spotlightOn)
        {
            if (_filtersFlyout?.IsOpen == true) { _filtersFlyout.Hide(); AfterLayout(() => FocusHome(xamlRoot)); return; }
            ShowFiltersFlyout();
            AfterLayout(() =>
            {
                if (_filtersFlyoutHost != null && FocusManager.FindFirstFocusableElement(_filtersFlyoutHost) is { } first)
                    PadFocus(first);
            });
            return;
        }
        _filtersOpen = !_filtersOpen;
        RefreshTabsChrome();
        AfterLayout(() =>
        {
            if (_filtersOpen && _chipsRow != null && FocusManager.FindFirstFocusableElement(_chipsRow) is { } chip)
                PadFocus(chip);
            else FocusHome(xamlRoot);
        });
    }
}
