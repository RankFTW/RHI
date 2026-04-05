using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Builds and shows the shader selection ContentDialog.
/// Supports both global (Deploy/Cancel) and per-game (Confirm/Cancel) contexts.
/// </summary>
public static class ShaderPopupHelper
{
    public enum PopupContext { Global, PerGame }

    /// <summary>
    /// Shows the shader selection popup.
    /// Returns the list of selected pack IDs, or null if cancelled.
    /// </summary>
    public static async Task<List<string>?> ShowAsync(
        XamlRoot xamlRoot,
        IShaderPackService shaderPackService,
        List<string>? currentSelection,
        PopupContext context)
    {
        var packs = shaderPackService.AvailablePacks;
        var primaryButtonText = "Deploy";

        // Handle empty packs state (Req 9.1, 9.2)
        if (packs.Count == 0)
        {
            var emptyDlg = new ContentDialog
            {
                Title             = "Select Shader Packs",
                Content           = new TextBlock
                {
                    Text       = "No shader packs available.",
                    FontSize   = 13,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                },
                PrimaryButtonText = primaryButtonText,
                IsPrimaryButtonEnabled = false,
                CloseButtonText   = "Cancel",
                XamlRoot          = xamlRoot,
                Background        = Brush(ResourceKeys.SurfaceOverlayBrush),
                MinWidth          = 750,
            };

            await emptyDlg.ShowAsync();
            return null;
        }

        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);

        var panel = new StackPanel { Spacing = 4 };
        var checkBoxes = new List<(string Id, CheckBox Box)>();

        // Group packs by category and render each group with a header
        var groups = packs
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key); // Essential=0, Recommended=1, Extra=2

        foreach (var group in groups)
        {
            var headerText = group.Key switch
            {
                ShaderPackService.PackCategory.Essential    => "Essential",
                ShaderPackService.PackCategory.Recommended  => "Recommended",
                _                                           => "Extra",
            };

            panel.Children.Add(new TextBlock
            {
                Text       = headerText,
                FontSize   = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                Margin     = new Thickness(0, checkBoxes.Count > 0 ? 10 : 0, 0, 4),
            });

            foreach (var (id, displayName, _) in group)
            {
                var description = shaderPackService.GetPackDescription(id);

                var contentPanel = new StackPanel { Spacing = 0 };
                contentPanel.Children.Add(new TextBlock
                {
                    Text       = displayName,
                    FontSize   = 13,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                });
                if (!string.IsNullOrEmpty(description))
                {
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text       = description,
                        FontSize   = 11,
                        Opacity    = 0.6,
                        Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                    });
                }

                var cb = new CheckBox
                {
                    Content   = contentPanel,
                    IsChecked = selected.Contains(id),
                };

                // Auto-select dependencies when this pack is checked
                var capturedId = id;
                cb.Checked += (s, ev) =>
                {
                    var required = shaderPackService.GetRequiredPacks(capturedId);
                    foreach (var reqId in required)
                    {
                        var depBox = checkBoxes.FirstOrDefault(c =>
                            c.Id.Equals(reqId, StringComparison.OrdinalIgnoreCase)).Box;
                        if (depBox != null && depBox.IsChecked != true)
                            depBox.IsChecked = true;
                    }
                };

                // Lilium is no longer locked — users can untick it in Global context
                checkBoxes.Add((id, cb));
                panel.Children.Add(cb);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            Content                    = panel,
            MaxHeight                  = 700,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dlg = new ContentDialog
        {
            Title             = "Select Shader Packs",
            Content           = scrollViewer,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText   = "Cancel",
            XamlRoot          = xamlRoot,
            Background        = Brush(ResourceKeys.SurfaceOverlayBrush),
            MinWidth          = 750,
        };

        var dialogResult = await dlg.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary)
            return null; // cancelled — caller should preserve existing state

        // Build the confirmed selection from ticked checkboxes
        var confirmed = new List<string>();
        foreach (var (id, box) in checkBoxes)
        {
            if (box.IsChecked == true)
                confirmed.Add(id);
        }

        return confirmed;
    }

    /// <summary>
    /// Pure logic: computes the checkbox model for the popup.
    /// Returns one entry per available pack with its pre-checked state.
    /// Visible to tests via InternalsVisibleTo.
    /// </summary>
    internal static List<(string Id, bool IsChecked)> ComputeCheckboxModel(
        IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> availablePacks,
        List<string>? currentSelection)
    {
        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);
        var model = new List<(string Id, bool IsChecked)>(availablePacks.Count);
        foreach (var (id, _, _) in availablePacks)
            model.Add((id, selected.Contains(id)));
        return model;
    }

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
