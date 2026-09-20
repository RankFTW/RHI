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

        srCol.Children.Add(new TextBlock { Text = "Версия", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var srVersionCombo = BuildCombo(dlssService.DlssVersions, settings.DefaultDlssVersion, includeDriverOverride: true, currentDriverOverride: settings.DefaultSrDriverOverride);
        srCol.Children.Add(srVersionCombo);

        srCol.Children.Add(new TextBlock { Text = "Пресет", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var srPresetCombo = BuildPresetComboBox(DlssPresetService.SrPresets, settings.DefaultSrPreset);
        srCol.Children.Add(srPresetCombo);

        srCol.Children.Add(new TextBlock { Text = "Масштаб рендеринга", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var srScaleCombo = BuildRenderScaleComboBox(settings.DefaultSrRenderScale);
        srCol.Children.Add(srScaleCombo);

        Grid.SetColumn(srCol, 0);
        grid.Children.Add(srCol);

        // Divider
        grid.Children.Add(MakeDivider(1));

        // ── RR Column ──
        var rrCol = new StackPanel { Spacing = 4 };
        rrCol.Children.Add(new TextBlock { Text = "Ray Reconstruction", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        rrCol.Children.Add(new TextBlock { Text = "Версия", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var rrVersionCombo = BuildCombo(dlssService.DlssdVersions, settings.DefaultDlssdVersion, includeDriverOverride: true, currentDriverOverride: settings.DefaultRrDriverOverride);
        rrCol.Children.Add(rrVersionCombo);

        rrCol.Children.Add(new TextBlock { Text = "Пресет", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var rrPresetCombo = BuildPresetComboBox(DlssPresetService.RrPresets, settings.DefaultRrPreset);
        rrCol.Children.Add(rrPresetCombo);

        rrCol.Children.Add(new TextBlock { Text = "Масштаб рендеринга", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var rrScaleCombo = BuildRenderScaleComboBox(settings.DefaultRrRenderScale);
        rrCol.Children.Add(rrScaleCombo);

        Grid.SetColumn(rrCol, 2);
        grid.Children.Add(rrCol);

        // Divider
        grid.Children.Add(MakeDivider(3));

        // ── FG Column ──
        var fgCol = new StackPanel { Spacing = 4 };
        fgCol.Children.Add(new TextBlock { Text = "Генерация кадров", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        fgCol.Children.Add(new TextBlock { Text = "Версия", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var fgVersionCombo = BuildCombo(dlssService.DlssgVersions, settings.DefaultDlssgVersion, includeDriverOverride: true, currentDriverOverride: settings.DefaultFgDriverOverride);
        fgCol.Children.Add(fgVersionCombo);

        fgCol.Children.Add(new TextBlock { Text = "Пресет", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var fgPresetCombo = BuildPresetComboBox(DlssPresetService.FgPresets, settings.DefaultFgPreset);
        fgCol.Children.Add(fgPresetCombo);

        Grid.SetColumn(fgCol, 4);
        grid.Children.Add(fgCol);

        // Divider
        grid.Children.Add(MakeDivider(5));

        // ── NR Column (dev-only) ──
        ComboBox? nrVersionCombo = null;
        ComboBox? nrPresetCombo = null;
        if (FeatureFlags.DlssNr)
        {
            // Expand grid to 9 columns: SR, div, RR, div, FG, div, NR, div, SL
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nrCol = new StackPanel { Spacing = 4 };
            nrCol.Children.Add(new TextBlock { Text = "Нейронный рендеринг", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

            nrCol.Children.Add(new TextBlock { Text = "Версия", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
            nrVersionCombo = BuildCombo(dlssService.DlssnrVersions, settings.DefaultDlssnrVersion);
            nrCol.Children.Add(nrVersionCombo);

            nrCol.Children.Add(new TextBlock { Text = "Пресет", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
            nrPresetCombo = BuildPresetComboBox(DlssPresetService.NrPresets, settings.DefaultNrPreset);
            nrCol.Children.Add(nrPresetCombo);

            Grid.SetColumn(nrCol, 6);
            grid.Children.Add(nrCol);

            grid.Children.Add(MakeDivider(7));
        }

        int slGridCol = FeatureFlags.DlssNr ? 8 : 6;

        // ── SL Column ──
        var slCol = new StackPanel { Spacing = 4 };
        slCol.Children.Add(new TextBlock { Text = "Streamline", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });

        slCol.Children.Add(new TextBlock { Text = "Версия", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 0) });
        var slVersionCombo = BuildCombo(dlssService.StreamlineVersions, settings.DefaultStreamlineVersion);
        slCol.Children.Add(slVersionCombo);

        Grid.SetColumn(slCol, slGridCol);
        grid.Children.Add(slCol);

        var dialog = new ContentDialog
        {
            Title = "DLSS и Streamline по умолчанию",
            Content = grid,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        // Override default ContentDialog max width to fit 4 columns
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        // Save selections — "NVIDIA Override" stores the bool, not the version string
        var srVersionSelected = GetSelectedVersion(srVersionCombo);
        settings.DefaultSrDriverOverride = srVersionSelected == "Переопределение NVIDIA";
        settings.DefaultDlssVersion = settings.DefaultSrDriverOverride ? "" : srVersionSelected;

        var rrVersionSelected = GetSelectedVersion(rrVersionCombo);
        settings.DefaultRrDriverOverride = rrVersionSelected == "Переопределение NVIDIA";
        settings.DefaultDlssdVersion = settings.DefaultRrDriverOverride ? "" : rrVersionSelected;

        var fgVersionSelected = GetSelectedVersion(fgVersionCombo);
        settings.DefaultFgDriverOverride = fgVersionSelected == "Переопределение NVIDIA";
        settings.DefaultDlssgVersion = settings.DefaultFgDriverOverride ? "" : fgVersionSelected;

        settings.DefaultStreamlineVersion = GetSelectedVersion(slVersionCombo);
        settings.DefaultSrPreset = GetSelectedPreset(srPresetCombo, DlssPresetService.SrPresets);
        settings.DefaultRrPreset = GetSelectedPreset(rrPresetCombo, DlssPresetService.RrPresets);
        settings.DefaultFgPreset = GetSelectedPreset(fgPresetCombo, DlssPresetService.FgPresets);
        if (FeatureFlags.DlssNr)
        {
            settings.DefaultDlssnrVersion = nrVersionCombo != null ? GetSelectedVersion(nrVersionCombo) : "";
            settings.DefaultNrPreset = nrPresetCombo != null ? GetSelectedPreset(nrPresetCombo, DlssPresetService.NrPresets) : 0u;
        }
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

    private static ComboBox BuildCombo(IReadOnlyList<string> versions, string currentDefault, bool includeDriverOverride = false, bool currentDriverOverride = false)
    {
        var items = new List<string> { "Default" };
        items.AddRange(versions);
        items.Add("Custom");
        if (includeDriverOverride)
            items.Add("NVIDIA Override");

        int selectedIdx = 0;
        if (includeDriverOverride && currentDriverOverride)
        {
            selectedIdx = items.Count - 1; // "NVIDIA Override" is last
        }
        else if (!string.IsNullOrEmpty(currentDefault))
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

    private static StackPanel BuildRenderScaleComboBox(uint currentDefault)
    {
        var options = DlssPresetService.RenderScaleOptions;
        var items = options.Select(o => o.Name).ToList();

        int selectedIdx = 0;
        if (currentDefault > 0)
        {
            var idx = Array.FindIndex(options, o => o.Value == currentDefault);
            if (idx >= 0) selectedIdx = idx;
            else selectedIdx = items.Count - 1; // Custom
        }

        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIdx,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };

        // Inline TextBox shown when "Custom" is selected
        var customBox = new TextBox
        {
            PlaceholderText = "33–100",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxLength = 3,
            Visibility = (selectedIdx == items.Count - 1 && currentDefault > 0)
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed,
        };
        if (currentDefault > 0 && selectedIdx == items.Count - 1)
            customBox.Text = currentDefault.ToString();

        combo.SelectionChanged += (s, e) =>
        {
            customBox.Visibility = combo.SelectedItem as string == "Custom"
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        };

        var panel = new StackPanel { Spacing = 4, Tag = "RenderScalePanel" };
        panel.Children.Add(combo);
        panel.Children.Add(customBox);
        return panel;
    }

    private static uint GetSelectedRenderScale(StackPanel panel)
    {
        var combo   = panel.Children.OfType<ComboBox>().FirstOrDefault();
        var textBox = panel.Children.OfType<TextBox>().FirstOrDefault();
        if (combo == null) return 0;

        var options = DlssPresetService.RenderScaleOptions;
        var selected = combo.SelectedItem as string;

        if (selected == "Custom")
        {
            if (textBox != null && uint.TryParse(textBox.Text, out var val) && val >= 33 && val <= 100)
                return val;
            return 0;
        }

        var idx = combo.SelectedIndex;
        if (idx >= 0 && idx < options.Length) return options[idx].Value;
        return 0;
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
}
