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
            case Button btn: TabsTheme.RestyleButton(btn); return;
            case TextBlock tx: TabsTheme.RestyleText(tx); return;
            case ToggleSwitch or Slider or ProgressBar or ProgressRing: return;
        }

        if (root is Panel panel)
            foreach (var child in panel.Children) Restyle(child);
        else if (root is Border { Child: { } bc }) Restyle(bc);
        else if (root is ScrollViewer { Content: DependencyObject sc }) Restyle(sc);
        else if (root is ContentControl { Content: DependencyObject cc }) Restyle(cc);
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
