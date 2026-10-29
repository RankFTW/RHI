using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RHI.Linux.Core;

namespace RHI.Linux;

public sealed class HdrLaunchWindow : Window
{
    public HdrLaunchWindow(Game game)
    {
        Title = "HDR launch options — " + game.Name;
        Width = 720; Height = 650; MinWidth = 520; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        Content = new ScrollViewer { Content = body };
        void Explain(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        Explain("Preview HDR launch options for this game. Existing ReShade, DLSS, wrapper and game arguments are kept. Nothing is saved to Steam automatically.");
        var configs = Proton.LocalConfigs(game).ToList();
        var account = new ComboBox { ItemsSource = configs, SelectedIndex = configs.Count > 0 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (configs.Count > 1) body.Children.Add(account);
        Explain("Existing launch options — paste the complete current value if you changed it in Steam or use another launcher:");
        var original = new TextBox { TextWrapping = TextWrapping.Wrap, MinHeight = 75, Name = "HdrExistingOptions" };
        body.Children.Add(original);
        var wayland = new CheckBox { Content = "Enable Proton Wayland (PROTON_ENABLE_WAYLAND=1)", IsChecked = true };
        var wsi = new CheckBox { Content = "Also enable HDR WSI (ENABLE_HDR_WSI=1)", IsChecked = false };
        body.Children.Add(wayland); body.Children.Add(wsi);
        Explain("Adds PROTON_ENABLE_HDR=1. Use a Proton build that supports the selected options and enable HDR on your display. HDR WSI is optional for setups that require it. Unchecked options are left unchanged. This does not verify HDR output.");
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 85, Name = "HdrLaunchPreview" };
        body.Children.Add(preview);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var actions = new StackPanel { Spacing = 10 };
        var generate = new Button { Content = "Preview HDR options" };
        var copy = new Button { Content = "Copy preview", IsEnabled = false };
        var restore = new Button { Content = "Restore original preview" };
        actions.Children.Add(generate); actions.Children.Add(copy); actions.Children.Add(restore); body.Children.Add(actions); body.Children.Add(status);
        string loaded = "";
        void Invalidate() { preview.Text = ""; copy.IsEnabled = false; status.Text = "Generate a preview after making changes."; }
        void Load()
        {
            try
            {
                loaded = account.SelectedItem is string config && game.AppId != null ? Proton.ReadOptions(config, game.AppId) ?? "" : "";
                original.Text = loaded; Invalidate();
            }
            catch (Exception ex) { original.Text = ""; loaded = ""; Invalidate(); status.Text = ex.Message; }
        }
        original.TextChanged += (_, _) => Invalidate();
        wayland.IsCheckedChanged += (_, _) => Invalidate(); wsi.IsCheckedChanged += (_, _) => Invalidate();
        account.SelectionChanged += (_, _) => Load();
        generate.Click += (_, _) =>
        {
            Invalidate();
            try { preview.Text = HdrLaunchOptions.Merge(original.Text ?? "", wayland.IsChecked == true, wsi.IsChecked == true); copy.IsEnabled = true; status.Text = "Review, then copy into Steam → Properties → General → Launch Options. Reopen this tool after changing ReShade or DLSS launch settings."; }
            catch (FormatException ex) { status.Text = ex.Message; }
        };
        restore.Click += (_, _) => { preview.Text = original.Text ?? ""; copy.IsEnabled = true; status.Text = "Original options from the input above, without the preset additions. Copy to restore only if you have made no subsequent launcher changes."; };
        copy.Click += async (_, _) =>
        {
            try { if (Clipboard == null) throw new IOException("Clipboard unavailable."); await Clipboard.SetTextAsync(preview.Text ?? ""); status.Text = "Copied. Paste into your game's launch options."; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        Load();
    }
}
