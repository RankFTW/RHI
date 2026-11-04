// SettingsHandler.OsDefaultVariant.cs — Settings › Components › OptiScaler: default OptiScaler version.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private const string OsDefaultVariantRowTag = "OsDefaultVariantRow";

    // Display label ↔ internal value (same names as the OptiScaler ⚙'s version dropdown)
    private static readonly (string Label, string Value)[] OsDefaultVariants =
    {
        ("Stable", "Stable"),
        ("Nightly", "Nightly"),
        ("DLSS NR", "DlssNr"),
        ("F5 DLSSNR", "F5"),
    };

    private ComboBox? _osDefaultVariantCombo;

    /// <summary>
    /// Adds the "Default version" row above GPU type (once), built in C# because of the
    /// MainWindow.xaml constraint, and selects the saved value.
    /// </summary>
    private void EnsureOsDefaultVariantRow()
    {
        try
        {
            if (_osDefaultVariantCombo == null)
            {
                // Found by searching down from the Settings cards: FrameworkElement.Parent can be
                // null here before the page has been laid out
                if (FindRowOf(_window.SettingsCardsPanel, _window.OsGpuCombo) is not { } found)
                {
                    CrashReporter.Log("[SettingsHandler.EnsureOsDefaultVariantRow] GPU type row not found");
                    return;
                }
                var (list, gpuRow) = found;

                var row = new Grid { ColumnSpacing = 10, Tag = OsDefaultVariantRowTag };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
                var label = new TextBlock { Text = "Default version" };
                if (Application.Current.Resources.TryGetValue("SettingsLabelStyle", out var ls) && ls is Style labelStyle)
                    label.Style = labelStyle;
                row.Children.Add(label);

                var combo = new ComboBox();
                if (Application.Current.Resources.TryGetValue("SettingsComboStyle", out var cs) && cs is Style comboStyle)
                    combo.Style = comboStyle;
                foreach (var (l, _) in OsDefaultVariants) combo.Items.Add(l);
                ToolTipService.SetToolTip(combo,
                    "The OptiScaler version installed in games where you haven't picked one in the game's OptiScaler ⚙. " +
                    "Games that already have OptiScaler keep the version they have. " +
                    "Stable is the official release, Nightly the daily build, DLSS NR and F5 DLSSNR the Neural Rendering forks.");
                Grid.SetColumn(combo, 1);
                row.Children.Add(combo);
                list.Children.Insert(list.Children.IndexOf(gpuRow), row);
                _osDefaultVariantCombo = combo;
                SelectOsDefaultVariant();
                combo.SelectionChanged += OsDefaultVariantCombo_SelectionChanged;
                return;
            }
            SelectOsDefaultVariant();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureOsDefaultVariantRow] {ex.Message}");
        }
    }

    /// <summary>The panel holding the row that contains <paramref name="target"/>, and that row.</summary>
    private static (Panel List, FrameworkElement Row)? FindRowOf(DependencyObject root, UIElement target)
    {
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Panel row && row.Children.Contains(target) && child is FrameworkElement fe)
                    return (panel, fe);
                if (FindRowOf(child, target) is { } hit) return hit;
            }
        }
        else if (root is Border { Child: { } bc }) return FindRowOf(bc, target);
        else if (root is ScrollViewer { Content: DependencyObject sc }) return FindRowOf(sc, target);
        else if (root is ContentControl { Content: DependencyObject cc }) return FindRowOf(cc, target);
        return null;
    }

    private bool _osDefaultVariantSelecting;

    private void SelectOsDefaultVariant()
    {
        if (_osDefaultVariantCombo == null) return;
        _osDefaultVariantSelecting = true;
        int idx = Array.FindIndex(OsDefaultVariants, v => v.Value == ViewModel.Settings.OsDefaultVariant);
        _osDefaultVariantCombo.SelectedIndex = idx >= 0 ? idx : 0;
        _osDefaultVariantSelecting = false;
    }

    private void OsDefaultVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_osDefaultVariantSelecting || _osDefaultVariantCombo == null) return;
        try
        {
            int idx = _osDefaultVariantCombo.SelectedIndex;
            if (idx < 0 || idx >= OsDefaultVariants.Length) return;
            ViewModel.SetOsDefaultVariant(OsDefaultVariants[idx].Value);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.OsDefaultVariantCombo_SelectionChanged] {ex.Message}");
        }
    }
}
