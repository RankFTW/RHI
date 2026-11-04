// DialogLook.cs — gives every ContentDialog the new navy look when it's shown.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Applied by <see cref="DialogService.ShowSafeAsync"/> and <see cref="DialogService.ShowProgressAsync"/>,
/// so dialogs built the old way (string title, default colours) match the ⚙ settings dialogs:
/// navy frame, the dialog title style, Tabs buttons and inputs, blue ticks. Dialogs that already
/// called <see cref="CogDialog.ApplyDialogLook"/> are left as they are.
/// Lighter than <see cref="TabsTheme.Restyle"/>: inputs keep their sizes, only their colours change.
/// </summary>
internal static class DialogLook
{
    internal const string MarkerKey = "RhiDialogLook";

    /// <summary>The default ContentDialog max width, kept unless the dialog set its own.</summary>
    private const double DefaultMaxWidth = 548;

    internal static void Apply(ContentDialog dlg)
    {
        try
        {
            if (dlg.Resources.ContainsKey(MarkerKey)) return;
            double maxWidth = dlg.Resources.TryGetValue("ContentDialogMaxWidth", out var mw) && mw is double d ? d : DefaultMaxWidth;
            CogDialog.ApplyDialogLook(dlg, maxWidth);
            if (dlg.Title is string title && !string.IsNullOrWhiteSpace(title))
                dlg.Title = CogDialog.MakeDialogTitle(title);
            if (dlg.Content is string text)
                dlg.Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = TabsTheme.B(TabsTheme.TextControl),
                };
            else if (dlg.Content is DependencyObject content)
                Restyle(content);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogLook.Apply] {ex.Message}");
        }
    }

    private static void Restyle(DependencyObject root)
    {
        switch (root)
        {
            case ComboBox cb: StyleInput(cb); return; // never walk into dropdown items
            case TextBox tb: StyleInput(tb); return;
            case PasswordBox pb: StyleInput(pb); return;
            case Button btn: StyleButton(btn); return;
            case TextBlock tx: TabsTheme.RestyleText(tx); return;
            case ToggleSwitch or Slider or ProgressBar or ProgressRing: return;
        }

        if (root is Panel panel)
            foreach (var child in panel.Children) Restyle(child);
        else if (root is Border { Child: { } bc }) Restyle(bc);
        else if (root is ScrollViewer { Content: DependencyObject sc }) Restyle(sc);
        else if (root is ContentControl { Content: DependencyObject cc }) Restyle(cc);
    }

    /// <summary>
    /// Colours and corners only: buttons keep the size, padding and font the dialog gave them
    /// (TabsTheme.RestyleButton's fixed 34 px height made small buttons chunky). Flat buttons
    /// (transparent, no border) stay flat and only get the text and hover colours.
    /// </summary>
    private static void StyleButton(Button btn)
    {
        var tone = TabsTheme.ToneOf(btn.Foreground);
        var look = tone == TabsTheme.Tone.Grey ? TabsTheme.IconButton : TabsTheme.ButtonFor(tone);
        bool flat = btn.BorderThickness == new Thickness(0)
            && btn.Background is SolidColorBrush { Color.A: 0 };
        var r = btn.Resources;
        btn.Foreground = TabsTheme.B(look.Fg);
        if (btn.Content is TextBlock ct) ct.Foreground = TabsTheme.B(look.Fg);
        TabsTheme.SetResource(r, "ButtonForegroundPointerOver", TabsTheme.B(look.Fg));
        TabsTheme.SetResource(r, "ButtonForegroundPressed", TabsTheme.B(look.Fg));
        TabsTheme.SetResource(r, "ButtonForegroundDisabled", TabsTheme.B(TabsTheme.TextMuted));
        TabsTheme.SetResource(r, "ButtonBackgroundPointerOver", TabsTheme.B(look.HoverBg));
        TabsTheme.SetResource(r, "ButtonBackgroundPressed", TabsTheme.B(flat ? look.HoverBg : look.Bg));
        if (flat) return;

        btn.CornerRadius = new CornerRadius(6);
        btn.BorderThickness = new Thickness(1);
        btn.Background = TabsTheme.B(look.Bg);
        btn.BorderBrush = TabsTheme.B(look.Border);
        if (look.Bold) btn.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        TabsTheme.SetResource(r, "ButtonBorderBrushPointerOver", TabsTheme.B(look.Border));
        TabsTheme.SetResource(r, "ButtonBorderBrushPressed", TabsTheme.B(look.Border));
        TabsTheme.SetResource(r, "ButtonBackgroundDisabled", TabsTheme.B(look.Bg));
        TabsTheme.SetResource(r, "ButtonBorderBrushDisabled", TabsTheme.B(look.Border));
    }

    /// <summary>Colours and corners only: dialogs size their inputs themselves.</summary>
    private static void StyleInput(Control c)
    {
        c.CornerRadius = new CornerRadius(8);
        c.Background = TabsTheme.B(TabsTheme.PanelBg);
        c.BorderBrush = TabsTheme.B("#263042");
        c.Foreground = TabsTheme.B(TabsTheme.TextPrimary);
        if (c.MinHeight < 32) c.MinHeight = 32;
    }
}
