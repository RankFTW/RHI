using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

// Game-specific dialogs: foreign DLL confirmation, game notes, and UE-Extended warning.
public partial class DialogService
{
    // ── Foreign DLL Confirmation Dialogs ────────────────────────────────────────

    public async Task<bool> ShowForeignDxgiConfirmDialogAsync(GameCardViewModel card, string dxgiPath)
    {
        var fileSize = new System.IO.FileInfo(dxgiPath).Length;
        var sizeKB   = fileSize / 1024.0;

        var dlg = new ContentDialog
        {
            Title               = "⚠ Unknown dxgi.dll Detected",
            Content             = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground   = Brush(ResourceKeys.AccentAmberBrush),
                FontSize     = 13,
                Text         = $"A dxgi.dll file was found in:\n{card.InstallPath}\n\n" +
                               $"File size: {sizeKB:N0} KB\n\n" +
                               "RHI cannot identify this file as ReShade or Display Commander. " +
                               "It may belong to another mod (e.g. DXVK, Special K, ENB).\n\n" +
                               "Overwriting it may break the existing mod. Do you want to proceed?",
            },
            PrimaryButtonText   = "Overwrite",
            CloseButtonText     = "Cancel",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
            RequestedTheme      = ElementTheme.Dark,
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task<bool> ShowForeignWinmmConfirmDialogAsync(GameCardViewModel card, string winmmPath)
    {
        var fileSize = new System.IO.FileInfo(winmmPath).Length;
        var sizeKB   = fileSize / 1024.0;

        var dlg = new ContentDialog
        {
            Title               = "⚠ Unknown winmm.dll Detected",
            Content             = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground   = Brush(ResourceKeys.AccentAmberBrush),
                FontSize     = 13,
                Text         = $"A winmm.dll file was found in:\n{card.InstallPath}\n\n" +
                               $"File size: {sizeKB:N0} KB\n\n" +
                               "RHI cannot identify this file as Display Commander. " +
                               "It may belong to another mod or DLL injector.\n\n" +
                               "Overwriting it may break the existing mod. Do you want to proceed?",
            },
            PrimaryButtonText   = "Overwrite",
            CloseButtonText     = "Cancel",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
            RequestedTheme      = ElementTheme.Dark,
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    // ── Game Notes Dialog ────────────────────────────────────────────────────────

    public async void NotesButton_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;

        var textColour = Brush(ResourceKeys.TextSecondaryBrush);
        var linkColour = Brush(ResourceKeys.AccentBlueBrush);
        var dimColour  = Brush(ResourceKeys.TextTertiaryBrush);

        var outerPanel = new StackPanel { Spacing = 10 };

        // ── Wiki status badge at top-left ─────────────────────────────────────────
        var statusBg     = card.WikiStatusBadgeBackground;
        var statusBorder = card.WikiStatusBadgeBorderBrush;
        var statusFg     = card.WikiStatusBadgeForeground;
        var statusBadge = new Border
        {
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background      = new SolidColorBrush(ParseColor(statusBg)),
            BorderBrush     = new SolidColorBrush(ParseColor(statusBorder)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text       = card.WikiStatusLabel,
                FontSize   = 12,
                Foreground = new SolidColorBrush(ParseColor(statusFg)),
            }
        };
        outerPanel.Children.Add(statusBadge);

        // ── Luma info (when in Luma mode) ───────────────────────────────────────────
        if (card.IsLumaMode && (card.LumaMod != null || !string.IsNullOrWhiteSpace(card.LumaNotes)))
        {
            var lumaLabel = card.LumaMod != null
                ? $"Luma — {card.LumaMod.Status} {card.LumaMod.Author}"
                : "Luma mode";
            var lumaBadge = new Border
            {
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background      = Brush(ResourceKeys.AccentGreenBgBrush),
                BorderBrush     = Brush(ResourceKeys.AccentGreenBorderBrush),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = lumaLabel,
                    FontSize   = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                }
            };
            outerPanel.Children.Add(lumaBadge);

            var lumaNotesText = "";
            if (card.LumaMod != null)
            {
                if (!string.IsNullOrWhiteSpace(card.LumaMod.SpecialNotes))
                    lumaNotesText += card.LumaMod.SpecialNotes;
                if (!string.IsNullOrWhiteSpace(card.LumaMod.FeatureNotes))
                {
                    if (lumaNotesText.Length > 0) lumaNotesText += "\n\n";
                    lumaNotesText += card.LumaMod.FeatureNotes;
                }
            }

            if (!string.IsNullOrWhiteSpace(lumaNotesText))
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text         = lumaNotesText,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }

            // ── Manifest Luma notes (supplement wiki notes) ──────────────────────
            if (!string.IsNullOrWhiteSpace(card.LumaNotes))
            {
                if (!string.IsNullOrEmpty(card.LumaNotesUrl))
                {
                    var para = new Microsoft.UI.Xaml.Documents.Paragraph();
                    para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text       = card.LumaNotes,
                        Foreground = textColour,
                        FontSize   = 13,
                    });
                    para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                    var link = new Microsoft.UI.Xaml.Documents.Hyperlink
                    {
                        NavigateUri = new Uri(card.LumaNotesUrl),
                        Foreground  = linkColour,
                    };
                    link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text     = card.LumaNotesUrlLabel ?? card.LumaNotesUrl,
                        FontSize = 13,
                    });
                    para.Inlines.Add(link);
                    var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
                    rtb.Blocks.Add(para);
                    outerPanel.Children.Add(rtb);
                }
                else
                {
                    outerPanel.Children.Add(new TextBlock
                    {
                        Text         = card.LumaNotes,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground   = textColour,
                        FontSize     = 13,
                        LineHeight   = 22,
                    });
                }
            }

            // Fallback if neither wiki nor manifest provided notes
            if (string.IsNullOrWhiteSpace(lumaNotesText) && string.IsNullOrWhiteSpace(card.LumaNotes))
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text       = "No additional Luma notes for this game.",
                    Foreground = dimColour,
                    FontSize   = 13,
                });
            }
        }
        // ── Standard RenoDX notes ───────────────────────────────────────────────────
        else if (!string.IsNullOrWhiteSpace(card.Notes))
        {
            if (!string.IsNullOrEmpty(card.NotesUrl))
            {
                var para = new Microsoft.UI.Xaml.Documents.Paragraph();
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text       = card.Notes,
                    Foreground = textColour,
                    FontSize   = 13,
                });
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                var link = new Microsoft.UI.Xaml.Documents.Hyperlink
                {
                    NavigateUri = new Uri(card.NotesUrl),
                    Foreground  = linkColour,
                };
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text     = card.NotesUrlLabel ?? card.NotesUrl,
                    FontSize = 13,
                });
                para.Inlines.Add(link);

                var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
                rtb.Blocks.Add(para);
                outerPanel.Children.Add(rtb);
            }
            else
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text         = card.Notes,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }
        }
        else
        {
            outerPanel.Children.Add(new TextBlock
            {
                Text       = "No additional notes for this game.",
                Foreground = dimColour,
                FontSize   = 13,
            });
        }

        var scrollContent = new ScrollViewer
        {
            Content   = outerPanel,
            MaxHeight = 440,
            Padding   = new Thickness(0, 4, 12, 0),
        };

        var dialog = new ContentDialog
        {
            Title           = $"ℹ  {card.GameName}",
            Content         = scrollContent,
            CloseButtonText = "Close",
            XamlRoot        = _window.Content.XamlRoot,
            Background      = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme  = ElementTheme.Dark,
        };
        await dialog.ShowAsync();
    }

    // ── UE Extended Warning Dialog ──────────────────────────────────────────────

    public async Task ShowUeExtendedWarningAsync(GameCardViewModel card)
    {
        try
        {
            while (_window.Content.XamlRoot == null)
                await Task.Delay(100);

            var hasNotes = !string.IsNullOrWhiteSpace(card.Notes);
            var notesHint = hasNotes
                ? "\n\nCheck the Notes section for any additional compatibility information for this game."
                : "\n\nNo specific notes are available for this game — check the RDXC Discord for community reports.";

            var dlg = new ContentDialog
            {
                Title               = "⚠ UE-Extended Compatibility Warning",
                Content             = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize     = 13,
                    Text         = "Not all Unreal Engine games are compatible with UE-Extended.\n\n" +
                                   "UE-Extended uses a different injection method that works better " +
                                   "with some games but may cause crashes or issues with others." +
                                   notesHint,
                },
                PrimaryButtonText   = "OK, I understand",
                XamlRoot            = _window.Content.XamlRoot,
                Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
                RequestedTheme      = ElementTheme.Dark,
            };

            await dlg.ShowAsync();
        }
        catch (Exception ex) { CrashReporter.Log($"[DialogService.ShowUeExtendedWarningAsync] Failed to show UE warning for '{card.GameName}' — {ex.Message}"); }
    }
}
