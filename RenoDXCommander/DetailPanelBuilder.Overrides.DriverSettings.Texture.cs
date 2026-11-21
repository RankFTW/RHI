// DetailPanelBuilder.Overrides.DriverSettings.Texture.cs — Driver Settings › Texture Filtering group
// (texture LOD bias, anisotropic filtering, texture filtering quality).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private StackPanel BuildTextureFilteringColumn(DriverProfileData d, DlssPresetService svc,
        string capturedName, string installPath)
    {
        var col = new StackPanel { Spacing = 4 };
        var title = new TextBlock { Text = "Texture Filtering", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
        ToolTipService.SetToolTip(title, "Texture sharpness settings from NVIDIA Profile Inspector: LOD bias, anisotropic filtering and texture filtering quality.");
        col.Children.Add(title);

        ComboBox MakeCombo(IList<string> items, int index) => new()
        {
            ItemsSource = items,
            SelectedIndex = index,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            MaxDropDownHeight = 300,
        };
        void AddLabel(string text) => col.Children.Add(new TextBlock
        {
            Text = text, FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0),
        });

        // LOD Bias
        {
            AddLabel("LOD Bias");
            var options = DlssPresetService.LodBiasOptions;
            var items = options.Select(o => o.Name).ToList();
            int idx = Array.FindIndex(options, o => o.Eighths == d.LodBias);
            if (idx < 0 && d.LodBias is int own)
            {
                // A value set elsewhere (Profile Inspector) that isn't in the list: show it as it is
                items.Add($"{DlssPresetService.FormatLodBias(own)}  (Custom)");
                idx = items.Count - 1;
            }
            var combo = MakeCombo(items, Math.Max(0, idx));
            ToolTipService.SetToolTip(combo,
                "Texture LOD bias for this game. Negative values make textures sharper. Use it for games whose DLSS "
                + "leaves textures blurry because they don't lower their own bias.\n\n"
                + "A good start is the value marked for your DLSS mode. Too far negative causes shimmering.\n\n"
                + "RHI also turns on Transparency Supersampling (all), which DX10, DX11 and DX12 games need for the bias "
                + "to apply, and allows negative bias. Default removes these.");
            var init = true;
            combo.SelectionChanged += (s, e) =>
            {
                if (init) return;
                int i = combo.SelectedIndex;
                if (i < 0 || i >= options.Length) return; // the Custom entry: nothing to change
                int? value = options[i].Eighths;
                _ = Task.Run(() => svc.SetLodBias(capturedName, installPath, value));
                if (_driverSummaryData != null)
                {
                    _driverSummaryData = _driverSummaryData with { LodBias = value };
                    UpdateDriverSummary();
                }
            };
            col.Children.Add(combo);
            init = false;
        }

        // Anisotropic Filtering
        {
            AddLabel("Anisotropic Filtering");
            var options = DlssPresetService.AnisoOptions;
            var items = options.Select(o => o.Name).ToList();
            int idx = Array.FindIndex(options, o => o.Level == d.AnisoLevel);
            if (idx < 0)
            {
                items.Add($"{d.AnisoLevel}x (Custom)");
                idx = items.Count - 1;
            }
            var combo = MakeCombo(items, idx);
            ToolTipService.SetToolTip(combo,
                "Forces anisotropic filtering, which keeps textures sharp at angles (floors, roads). App Controlled lets the game decide. "
                + "Works in most DX9 to DX11 games; many DX12 and Vulkan games ignore it.");
            var init = true;
            combo.SelectionChanged += (s, e) =>
            {
                if (init) return;
                int i = combo.SelectedIndex;
                if (i < 0 || i >= options.Length) return;
                uint level = options[i].Level;
                _ = Task.Run(() => svc.SetAnisoLevel(capturedName, installPath, level));
                if (_driverSummaryData != null)
                {
                    _driverSummaryData = _driverSummaryData with { AnisoLevel = level };
                    UpdateDriverSummary();
                }
            };
            col.Children.Add(combo);
            init = false;
        }

        // Texture Filtering Quality
        {
            AddLabel("Quality");
            var options = DlssPresetService.TextureQualityOptions;
            var items = options.Select(o => o.Name).ToList();
            int idx = Array.FindIndex(options, o => o.Value == d.TextureQuality);
            if (idx < 0)
            {
                items.Add($"0x{d.TextureQuality:X8} (Custom)");
                idx = items.Count - 1;
            }
            var combo = MakeCombo(items, idx);
            ToolTipService.SetToolTip(combo,
                "Texture filtering quality. High Quality turns off the driver's filtering shortcuts (and trilinear optimization) "
                + "for the cleanest textures at a very small cost. Default lets the driver use Quality.");
            var init = true;
            combo.SelectionChanged += (s, e) =>
            {
                if (init) return;
                int i = combo.SelectedIndex;
                if (i < 0 || i >= options.Length) return;
                uint? value = options[i].Value;
                _ = Task.Run(() => svc.SetTextureQuality(capturedName, installPath, value));
            };
            col.Children.Add(combo);
            init = false;
        }

        return col;
    }
}
