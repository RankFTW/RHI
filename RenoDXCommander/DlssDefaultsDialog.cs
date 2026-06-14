using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Dialog for configuring DLSS/Streamline default versions, presets, and render scales.
/// These defaults can be applied to any game in one click via the "Apply Defaults" button.
/// </summary>
public static class DlssDefaultsDialog
{
    public static async Task ShowAsync(MainViewModel viewModel, IDlssStreamlineService dlssService, DlssPresetService presetService, XamlRoot xamlRoot)
    {
        var settings = viewModel.Settings;

        var grid = new Grid { ColumnSpacing = 12, MinWidth = 700 };
        // 4 columns with dividers: SR | div | RR | div | FG | div | SL
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── SR Column ──
        var srCol = new StackPanel { Spacing = 4 };
        srCol.Children.Add(new TextBlock { Text = "DLSS", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        srCol.Children.Add(new TextBlock { Text = "Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var srVersionCombo = BuildCombo(dlssService.DlssVersions, settings.DefaultDlssVersion);
        srCol.Children.Add(srVersionCombo);

        srCol.Children.Add(new TextBlock { Text = "Preset", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var srPresetCombo = BuildPresetComboBox(DlssPresetService.SrPresets, settings.DefaultSrPreset);
        srCol.Children.Add(srPresetCombo);

        srCol.Children.Add(new TextBlock { Text = "Render Scale", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var srScaleCombo = BuildRenderScaleComboBox(settings.DefaultSrRenderScale);
        srCol.Children.Add(srScaleCombo);

        Grid.SetColumn(srCol, 0);
        grid.Children.Add(srCol);

        // Divider
        grid.Children.Add(MakeDivider(1));

        // ── RR Column ──
        var rrCol = new StackPanel { Spacing = 4 };
        rrCol.Children.Add(new TextBlock { Text = "Ray Reconstruction", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        rrCol.Children.Add(new TextBlock { Text = "Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var rrVersionCombo = BuildCombo(dlssService.DlssdVersions, settings.DefaultDlssdVersion);
        rrCol.Children.Add(rrVersionCombo);

        rrCol.Children.Add(new TextBlock { Text = "Preset", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var rrPresetCombo = BuildPresetComboBox(DlssPresetService.RrPresets, settings.DefaultRrPreset);
        rrCol.Children.Add(rrPresetCombo);

        rrCol.Children.Add(new TextBlock { Text = "Render Scale", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var rrScaleCombo = BuildRenderScaleComboBox(settings.DefaultRrRenderScale);
        rrCol.Children.Add(rrScaleCombo);

        Grid.SetColumn(rrCol, 2);
        grid.Children.Add(rrCol);

        // Divider
        grid.Children.Add(MakeDivider(3));

        // ── FG Column ──
        var fgCol = new StackPanel { Spacing = 4 };
        fgCol.Children.Add(new TextBlock { Text = "Frame Generation", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        fgCol.Children.Add(new TextBlock { Text = "Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var fgVersionCombo = BuildCombo(dlssService.DlssgVersions, settings.DefaultDlssgVersion);
        fgCol.Children.Add(fgVersionCombo);

        fgCol.Children.Add(new TextBlock { Text = "Preset", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var fgPresetCombo = BuildPresetComboBox(DlssPresetService.FgPresets, settings.DefaultFgPreset);
        fgCol.Children.Add(fgPresetCombo);

        Grid.SetColumn(fgCol, 4);
        grid.Children.Add(fgCol);

        // Divider
        grid.Children.Add(MakeDivider(5));

        // ── SL Column ──
        var slCol = new StackPanel { Spacing = 4 };
        slCol.Children.Add(new TextBlock { Text = "Streamline", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        slCol.Children.Add(new TextBlock { Text = "Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var slVersionCombo = BuildCombo(dlssService.StreamlineVersions, settings.DefaultStreamlineVersion);
        slCol.Children.Add(slVersionCombo);

        Grid.SetColumn(slCol, 6);
        grid.Children.Add(slCol);

        var dialog = new ContentDialog
        {
            Title = "DLSS & Streamline Defaults",
            Content = grid,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        // Save selections
        settings.DefaultDlssVersion = GetSelectedVersion(srVersionCombo);
        settings.DefaultDlssdVersion = GetSelectedVersion(rrVersionCombo);
        settings.DefaultDlssgVersion = GetSelectedVersion(fgVersionCombo);
        settings.DefaultStreamlineVersion = GetSelectedVersion(slVersionCombo);
        settings.DefaultSrPreset = GetSelectedPreset(srPresetCombo, DlssPresetService.SrPresets);
        settings.DefaultRrPreset = GetSelectedPreset(rrPresetCombo, DlssPresetService.RrPresets);
        settings.DefaultFgPreset = GetSelectedPreset(fgPresetCombo, DlssPresetService.FgPresets);
        settings.DefaultSrRenderScale = GetSelectedRenderScale(srScaleCombo);
        settings.DefaultRrRenderScale = GetSelectedRenderScale(rrScaleCombo);

        viewModel.SaveSettingsPublic();
        CrashReporter.Log("[DlssDefaultsDialog] Defaults saved");
    }

    private static Border MakeDivider(int column)
    {
        var divider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(divider, column);
        return divider;
    }

    private static ComboBox BuildCombo(IReadOnlyList<string> versions, string currentDefault)
    {
        var items = new List<string> { "Default" };
        items.AddRange(versions);

        int selectedIdx = 0;
        if (!string.IsNullOrEmpty(currentDefault))
        {
            var idx = items.IndexOf(currentDefault);
            if (idx > 0) selectedIdx = idx;
            else
            {
                for (int i = 1; i < items.Count; i++)
                {
                    if (items[i].StartsWith(currentDefault, StringComparison.OrdinalIgnoreCase))
                    { selectedIdx = i; break; }
                }
            }
        }

        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIdx,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
    }

    private static ComboBox BuildPresetComboBox((string Name, uint Value)[] presets, uint currentDefault)
    {
        var items = presets.Select(p => p.Name).ToList();

        int selectedIdx = 0;
        if (currentDefault != 0)
        {
            var idx = Array.FindIndex(presets, p => p.Value == currentDefault);
            if (idx >= 0) selectedIdx = idx;
        }

        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIdx,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
    }

    private static ComboBox BuildRenderScaleComboBox(uint currentDefault)
    {
        var options = DlssPresetService.RenderScaleOptions;
        var items = options.Select(o => o.Name).ToList();

        int selectedIdx = 0;
        if (currentDefault != 0)
        {
            var idx = Array.FindIndex(options, o => o.Value == currentDefault);
            if (idx >= 0) selectedIdx = idx;
        }

        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIdx,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
    }

    private static string GetSelectedVersion(ComboBox combo)
    {
        if (combo.SelectedIndex <= 0) return ""; // "Default" = empty = don't change
        return combo.SelectedItem as string ?? "";
    }

    private static uint GetSelectedPreset(ComboBox combo, (string Name, uint Value)[] presets)
    {
        var idx = combo.SelectedIndex;
        if (idx >= 0 && idx < presets.Length) return presets[idx].Value;
        return 0;
    }

    private static uint GetSelectedRenderScale(ComboBox combo)
    {
        var options = DlssPresetService.RenderScaleOptions;
        var idx = combo.SelectedIndex;
        if (idx >= 0 && idx < options.Length) return options[idx].Value;
        return 0;
    }
}
