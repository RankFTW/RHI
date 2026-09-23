using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>Result of the custom-folder review dialog.</summary>
public sealed record CustomFolderReviewOutcome(
    List<CustomGameCandidate> Selected, List<CustomGameCandidate> Declined);

/// <summary>
/// Lets the user confirm which discovered games to add. Strong candidates start checked,
/// questionable ones start unchecked. Nothing is imported until "Add Selected" is pressed.
/// </summary>
public static class CustomFolderReviewDialog
{
    /// <returns>The user's selection, or null if the dialog was cancelled.</returns>
    public static async Task<CustomFolderReviewOutcome?> ShowAsync(
        IReadOnlyList<CustomGameCandidate> candidates, XamlRoot xamlRoot)
    {
        var boxes = new List<(CheckBox box, CustomGameCandidate candidate)>();
        var list = new StackPanel { Spacing = 10 };

        foreach (var c in candidates)
        {
            var box = new CheckBox
            {
                IsChecked = c.Confidence == CustomCandidateConfidence.High,
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Top,
            };
            boxes.Add((box, c));

            var details = new StackPanel { Spacing = 2 };
            details.Children.Add(new TextBlock
            {
                Text = c.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                TextWrapping = TextWrapping.Wrap,
            });
            details.Children.Add(new TextBlock
            {
                Text = c.InstallPath,
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
            details.Children.Add(new TextBlock
            {
                Text = Describe(c),
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                TextWrapping = TextWrapping.Wrap,
            });
            if (c.Confidence == CustomCandidateConfidence.Low)
            {
                details.Children.Add(new TextBlock
                {
                    Text = $"Not sure this is a game: {c.Reason}",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(box, 0);
            Grid.SetColumn(details, 1);
            row.Children.Add(box);
            row.Children.Add(details);
            list.Children.Add(row);
        }

        var selectAll = new HyperlinkButton { Content = "Select all", Padding = new Thickness(0), FontSize = 12 };
        var selectNone = new HyperlinkButton { Content = "Select none", Padding = new Thickness(0), FontSize = 12 };
        selectAll.Click += (_, _) => { foreach (var (b, _) in boxes) b.IsChecked = true; };
        selectNone.Click += (_, _) => { foreach (var (b, _) in boxes) b.IsChecked = false; };

        var strong = candidates.Count(c => c.Confidence == CustomCandidateConfidence.High);
        var content = new StackPanel { Spacing = 10, MinWidth = 560 };
        content.Children.Add(new TextBlock
        {
            Text = $"{candidates.Count} new game{(candidates.Count == 1 ? "" : "s")} found in your custom folders" +
                   (strong > 0 ? $" — {strong} strong match{(strong == 1 ? "" : "es")} pre-selected." : "."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { selectAll, selectNone } });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = list,
        });

        var dialog = new ContentDialog
        {
            Title = "Custom Game Folders",
            Content = content,
            PrimaryButtonText = "Add Selected",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            Background = UIFactory.Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return null;

        return new CustomFolderReviewOutcome(
            boxes.Where(b => b.box.IsChecked == true).Select(b => b.candidate).ToList(),
            boxes.Where(b => b.box.IsChecked != true).Select(b => b.candidate).ToList());
    }

    /// <summary>One line: executable · engine · graphics API · bitness.</summary>
    internal static string Describe(CustomGameCandidate c)
    {
        var parts = new List<string>
        {
            c.ExePath != null ? $"Exe: {Path.GetFileName(c.ExePath)}" : "Exe: (none)",
            $"Engine: {EngineLabel(c.Engine)}",
            $"API: {ApiLabel(c.GraphicsApi)}",
            BitnessLabel(c.Bitness),
        };
        return string.Join("  ·  ", parts);
    }

    private static string EngineLabel(EngineType e) => e switch
    {
        EngineType.Unreal => "Unreal Engine",
        EngineType.UnrealLegacy => "Unreal (legacy)",
        EngineType.Unity => "Unity",
        EngineType.REEngine => "RE Engine",
        _ => "unknown",
    };

    private static string ApiLabel(GraphicsApiType a) => a switch
    {
        GraphicsApiType.DirectX8 => "DirectX 8",
        GraphicsApiType.DirectX9 => "DirectX 9",
        GraphicsApiType.DirectX10 => "DirectX 10",
        GraphicsApiType.DirectX11 => "DirectX 11",
        GraphicsApiType.DirectX12 => "DirectX 12",
        GraphicsApiType.Vulkan => "Vulkan",
        GraphicsApiType.OpenGL => "OpenGL",
        _ => "unknown",
    };

    private static string BitnessLabel(MachineType m) => m switch
    {
        MachineType.x64 => "64-bit",
        MachineType.I386 => "32-bit",
        _ => "bitness unknown",
    };
}
