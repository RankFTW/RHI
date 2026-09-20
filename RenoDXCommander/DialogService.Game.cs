using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
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
                Text         = $"В папке найден файл dxgi.dll:\n{card.InstallPath}\n\n" +
                               $"Размер файла: {sizeKB:N0} КБ\n\n" +
                               "RHI не смог опознать этот файл как ReShade или Display Commander. " +
                               "Он может принадлежать другому моду (например, DXVK, Special K, ENB).\n\n" +
                               "Перезапись может сломать существующий мод. Продолжить?",
            },
            PrimaryButtonText   = "Перезаписать",
            CloseButtonText     = "Отмена",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
            RequestedTheme      = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dlg);
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
                Text         = $"В папке найден файл winmm.dll:\n{card.InstallPath}\n\n" +
                               $"Размер файла: {sizeKB:N0} КБ\n\n" +
                               "RHI не смог опознать этот файл как Display Commander. " +
                               "Он может принадлежать другому моду или инъектору DLL.\n\n" +
                               "Перезапись может сломать существующий мод. Продолжить?",
            },
            PrimaryButtonText   = "Перезаписать",
            CloseButtonText     = "Отмена",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
            RequestedTheme      = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dlg);
        return result == ContentDialogResult.Primary;
    }

    // ── UE Extended Warning Dialog ──────────────────────────────────────────────

    /// <summary>
    /// Determines the AddonType from the Info button sender based on its x:Name or DataContext.
    /// Detail view buttons use x:Name; Card flyout buttons store AddonType in DataContext.
    /// </summary>
    private static AddonType? GetAddonTypeFromInfoButton(object sender)
    {
        if (sender is not Button btn) return null;

        // Card flyout Info buttons store AddonType directly in DataContext
        if (btn.DataContext is AddonType addonType)
            return addonType;

        // Detail view Info buttons use x:Name
        return btn.Name switch
        {
            "DetailRefInfoBtn"  => AddonType.REFramework,
            "DetailRsInfoBtn"   => AddonType.ReShade,
            "DetailRdxInfoBtn"  => AddonType.RenoDX,
            "DetailLumaInfoBtn" => AddonType.Luma,
            "DetailUlInfoBtn"   => AddonType.ReLimiter,
            "DetailDcInfoBtn"   => AddonType.DisplayCommander,
            "DetailOsInfoBtn"   => AddonType.OptiScaler,
            _ => null
        };
    }

    /// <summary>
    /// Shows the per-addon Info dialog for the given button sender.
    /// Resolves content via AddonInfoResolver and displays it in a styled ContentDialog.
    /// Handles RenoDX, Luma, and OptiScaler-specific content rendering.
    /// </summary>
    public async Task ShowAddonInfoDialogAsync(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;

        var addonType = GetAddonTypeFromInfoButton(sender);
        if (addonType == null) return;

        try
        {
            var manifest = _window.ViewModel.Manifest;
            var osWikiData = _optiScalerWikiService.CachedData;
            var hdrDatabase = _hdrDatabaseService.CachedData;
            var nexusService = App.Services.GetRequiredService<INexusUpdateService>() as NexusUpdateService;
            var nexusSummaries = nexusService?.ModSummaries;

            // On-demand fetch: if this is a RenoDX game with a Nexus URL and no cached summary, fetch it now
            if (addonType == AddonType.RenoDX && nexusService != null
                && (nexusSummaries == null || !nexusSummaries.ContainsKey(card.GameName)))
            {
                var nexusUrl = card.ExternalUrl;
                if (string.IsNullOrEmpty(nexusUrl) || !nexusUrl.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
                    nexusUrl = card.NexusUrl;
                if (!string.IsNullOrEmpty(nexusUrl) && nexusUrl.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
                {
                    await nexusService.FetchSummaryAsync(card.GameName, nexusUrl);
                    nexusSummaries = nexusService.ModSummaries;
                }
            }
            var resolver = new AddonInfoResolver();
            var result = resolver.Resolve(card, addonType.Value, manifest, osWikiData, hdrDatabase, nexusSummaries);

            var textColour = Brush(ResourceKeys.TextSecondaryBrush);
            var linkColour = Brush(ResourceKeys.AccentBlueBrush);
            var dimColour  = Brush(ResourceKeys.TextTertiaryBrush);
            var outerPanel = new StackPanel { Spacing = 10 };

            // ── Addon-specific content rendering ──────────────────────────────
            switch (addonType.Value)
            {
                case AddonType.RenoDX:
                    BuildRenoDXContent(outerPanel, card, result, manifest, textColour, linkColour, dimColour);
                    break;

                case AddonType.Luma:
                    BuildLumaContent(outerPanel, card, result, manifest, textColour, linkColour, dimColour);
                    break;

                case AddonType.OptiScaler:
                    BuildOptiScalerContent(outerPanel, result, textColour, linkColour, dimColour);
                    break;

                case AddonType.ReLimiter:
                    await _window.ViewModel.EnsureUlReleaseBodyAsync(card.UlInstalledVersion);
                    BuildReleaseNotesContent(outerPanel, result, card.UlInstalledVersion,
                        _window.ViewModel.LatestUlReleaseBody,
                        "https://github.com/RankFTW/ReLimiter/releases",
                        textColour, linkColour, dimColour);
                    break;

                case AddonType.DisplayCommander:
                    await _window.ViewModel.EnsureDcReleaseBodyAsync(card.DcInstalledVersion);
                    BuildReleaseNotesContent(outerPanel, result, card.DcInstalledVersion,
                        _window.ViewModel.LatestDcReleaseBody,
                        "https://github.com/pmnoxx/display-commander/releases",
                        textColour, linkColour, dimColour);
                    break;

                default:
                    BuildGenericContent(outerPanel, result, textColour, linkColour);
                    break;
            }

            var scrollContent = new ScrollViewer
            {
                Content   = outerPanel,
                MaxHeight = 440,
                Padding   = new Thickness(0, 4, 12, 0),
            };

            var addonName = GetAddonDisplayName(addonType.Value);

            var dialog = new ContentDialog
            {
                Title           = $"{addonName} — {card.GameName}",
                Content         = scrollContent,
                CloseButtonText = "Закрыть",
                XamlRoot        = _window.Content.XamlRoot,
                Background      = Brush(ResourceKeys.SurfaceToolbarBrush),
                RequestedTheme  = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowAddonInfoDialogAsync] Failed for '{card.GameName}' — {ex.Message}");
        }
    }

    /// <summary>Returns the human-readable display name for an addon type.</summary>
    private static string GetAddonDisplayName(AddonType addon) => addon switch
    {
        AddonType.REFramework     => "RE Framework",
        AddonType.ReShade         => "ReShade",
        AddonType.RenoDX          => "RenoDX",
        AddonType.ReLimiter       => "ReLimiter",
        AddonType.DisplayCommander => "Display Commander",
        AddonType.OptiScaler      => "OptiScaler",
        AddonType.Luma            => "Luma",
        _                         => "Info"
    };

    // ── RenoDX-specific dialog content (8.2) ─────────────────────────────────────

    /// <summary>
    /// Builds RenoDX-specific dialog content: wiki status badge, wiki notes,
    /// manifest gameNotes, and wiki page link as clickable hyperlink.
    /// Reuses the badge rendering pattern from the old NotesButton_Click.
    /// </summary>
    private void BuildRenoDXContent(
        StackPanel panel,
        GameCardViewModel card,
        AddonInfoResult result,
        RemoteManifest? manifest,
        SolidColorBrush textColour,
        SolidColorBrush linkColour,
        SolidColorBrush dimColour)
    {
        // ── RTX HDR mode — show dedicated content instead of RenoDX wiki ──────
        if (card.IsRtxHdrEnabled)
        {
            // RTX HDR badge (green, same style as Luma badge)
            var rtxHdrBadge = new Border
            {
                CornerRadius        = new CornerRadius(6),
                Padding             = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background          = Brush(ResourceKeys.AccentGreenBgBrush),
                BorderBrush         = Brush(ResourceKeys.AccentGreenBorderBrush),
                BorderThickness     = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = "RTX HDR включён",
                    FontSize   = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                }
            };
            panel.Children.Add(rtxHdrBadge);

            panel.Children.Add(new TextBlock
            {
                Text = "RTX HDR использует внедрение HDR на уровне драйвера NVIDIA, превращая SDR-игры в HDR. " +
                       "Работает на уровне GPU без внедрения DLL в игру, поэтому совместим с античит-системами.\n\n" +
                       "Требуется: установленный NVIDIA App, включённый Game Filter/Freestyle, GPU RTX, драйвер 550+.",
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
            });

            AddHyperlinkBlock(panel, "Руководство по калибровке RTX HDR", manifest?.RtxHdrInfoUrl ?? "https://www.reddit.com/r/nvidia/comments/1b03yfg/rtx_hdr_paper_white_gamma_reference_settings/", linkColour);

            // Still show HDR Gaming Database link if available
            if (!string.IsNullOrEmpty(result.HdrAnalysisUrl))
            {
                AddHyperlinkBlock(panel, "HDR-анализ — HDR Gaming Database", result.HdrAnalysisUrl, linkColour);
            }

            return; // Skip regular RenoDX content
        }

        // ── Wiki status badge (reuse existing badge rendering) ────────────────
        if (result.Source == InfoSourceType.Wiki && !string.IsNullOrEmpty(result.WikiStatusLabel))
        {
            var statusBg     = result.WikiStatusBadgeBg ?? card.WikiStatusBadgeBackground;
            var statusBorder = result.WikiStatusBadgeBorder ?? card.WikiStatusBadgeBorderBrush;
            var statusFg     = result.WikiStatusBadgeFg ?? card.WikiStatusBadgeForeground;

            var statusBadge = new Border
            {
                CornerRadius        = new CornerRadius(6),
                Padding             = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background          = new SolidColorBrush(ParseColor(statusBg)),
                BorderBrush         = new SolidColorBrush(ParseColor(statusBorder)),
                BorderThickness     = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = result.WikiStatusLabel,
                    FontSize   = 12,
                    Foreground = new SolidColorBrush(ParseColor(statusFg)),
                }
            };
            panel.Children.Add(statusBadge);
        }

        // ── Wiki notes text ──────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(result.Content))
        {
            panel.Children.Add(new TextBlock
            {
                Text         = result.Content,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
            });
        }

        // ── Manifest gameNotes supplement (when wiki is the source, manifest
        //    gameNotes may still have additional per-game notes) ───────────────
        if (result.Source == InfoSourceType.Wiki && manifest?.GameNotes != null
            && manifest.GameNotes.TryGetValue(card.GameName, out var gameNote)
            && !string.IsNullOrWhiteSpace(gameNote.Notes))
        {
            AddTextOrHyperlink(panel, gameNote.Notes, gameNote.NotesUrl,
                gameNote.NotesUrlLabel, textColour, linkColour);
        }

        // ── Wiki page link (NameUrl) as clickable hyperlink ──────────────────
        if (!string.IsNullOrEmpty(result.Url))
        {
            AddHyperlinkBlock(panel, result.UrlLabel ?? "Открыть страницу вики", result.Url, linkColour);
        }

        // ── "Also available on Nexus" link (Snapshot+Nexus games only) ───────
        if (!card.IsExternalOnly && card.Mod?.SnapshotUrl != null && !string.IsNullOrEmpty(card.NexusUrl))
        {
            AddHyperlinkBlock(panel, "Также доступно на Nexus Mods", card.NexusUrl, linkColour);
        }

        // ── HDR Gaming Database link (supplementary) ─────────────────────────
        if (!string.IsNullOrEmpty(result.HdrAnalysisUrl))
        {
            AddHyperlinkBlock(panel, "HDR-анализ — HDR Gaming Database", result.HdrAnalysisUrl, linkColour);
        }

        // ── Fallback if no content at all ────────────────────────────────────
        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text       = "Дополнительных заметок RenoDX для этой игры нет.",
                Foreground = dimColour,
                FontSize   = 13,
            });
        }
    }

    // ── Luma-specific dialog content (8.3) ───────────────────────────────────────

    /// <summary>
    /// Builds Luma-specific dialog content: Luma badge, LumaMod wiki notes
    /// (SpecialNotes + FeatureNotes), and lumaGameNotes manifest content.
    /// Reuses the Luma notes rendering pattern from the old NotesButton_Click.
    /// </summary>
    private void BuildLumaContent(
        StackPanel panel,
        GameCardViewModel card,
        AddonInfoResult result,
        RemoteManifest? manifest,
        SolidColorBrush textColour,
        SolidColorBrush linkColour,
        SolidColorBrush dimColour)
    {
        // ── Luma badge — only show for named mods (generic mods use DLSS/HDR ticks instead) ──
        if (card.LumaMod?.IsGenericLuma != true)
        {
            var lumaLabel = card.LumaMod != null
                ? $"Luma — {card.LumaMod.Status} {card.LumaMod.Author}".TrimEnd()
                : "Luma";
            var lumaBadge = new Border
            {
                CornerRadius        = new CornerRadius(6),
                Padding             = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background          = Brush(ResourceKeys.AccentGreenBgBrush),
                BorderBrush         = Brush(ResourceKeys.AccentGreenBorderBrush),
                BorderThickness     = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = lumaLabel,
                    FontSize   = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                }
            };
            panel.Children.Add(lumaBadge);
        }

        // ── Feature flags (HDR / DLSS+FSR) for generic Luma games ─────────────
        if (card.LumaMod?.IsGenericLuma == true && (card.LumaHdrSupported || card.LumaDlssFsrSupported))
        {
            var flagPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };

            if (card.LumaDlssFsrSupported)
                flagPanel.Children.Add(new TextBlock
                {
                    Text = "✅ DLSS / FSR",
                    FontSize = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                });

            if (card.LumaHdrSupported)
                flagPanel.Children.Add(new TextBlock
                {
                    Text = "✅ HDR",
                    FontSize = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                });

            panel.Children.Add(flagPanel);
        }

        // ── LumaMod wiki notes (SpecialNotes + FeatureNotes) ─────────────────
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
            panel.Children.Add(new TextBlock
            {
                Text         = lumaNotesText,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
            });
        }

        // ── Manifest lumaGameNotes content ───────────────────────────────────
        if (!string.IsNullOrWhiteSpace(card.LumaNotes))
        {
            AddTextOrHyperlink(panel, card.LumaNotes, card.LumaNotesUrl,
                card.LumaNotesUrlLabel, textColour, linkColour);
        }
        else if (manifest?.LumaGameNotes != null
            && manifest.LumaGameNotes.TryGetValue(card.GameName, out var lumaNote)
            && !string.IsNullOrWhiteSpace(lumaNote.Notes))
        {
            AddTextOrHyperlink(panel, lumaNote.Notes, lumaNote.NotesUrl,
                lumaNote.NotesUrlLabel, textColour, linkColour);
        }

        // ── Manifest-source content (Tier 1 override) ───────────────────────
        if (result.Source == InfoSourceType.Manifest && !string.IsNullOrWhiteSpace(result.Content))
        {
            // When manifest is the source, the result.Content is the manifest entry.
            // Only add if not already shown via LumaNotes above.
            if (result.Content != card.LumaNotes)
            {
                panel.Children.Add(new TextBlock
                {
                    Text         = result.Content,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }
            if (!string.IsNullOrEmpty(result.Url))
                AddHyperlinkBlock(panel, result.UrlLabel ?? result.Url, result.Url, linkColour);
        }

        // ── HDR Gaming Database link (supplementary) ─────────────────────────
        if (!string.IsNullOrEmpty(result.HdrAnalysisUrl))
        {
            AddHyperlinkBlock(panel, "HDR-анализ — HDR Gaming Database", result.HdrAnalysisUrl, linkColour);
        }

        // ── Fallback if no content at all ────────────────────────────────────
        if (panel.Children.Count == 0) // no content at all
        {
            if (result.Source == InfoSourceType.Fallback && !string.IsNullOrWhiteSpace(result.Content))
            {
                panel.Children.Add(new TextBlock
                {
                    Text         = result.Content,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }
            else if (panel.Children.Count <= 1)
            {
                panel.Children.Add(new TextBlock
                {
                    Text       = "Дополнительных заметок Luma для этой игры нет.",
                    Foreground = dimColour,
                    FontSize   = 13,
                });
            }
        }
    }

    // ── OptiScaler-specific dialog content (8.4) ─────────────────────────────────

    /// <summary>
    /// Builds OptiScaler-specific dialog content: compatibility status, supported
    /// upscalers, notes, and separate "OptiScaler Compatibility" / "FSR4 Compatibility"
    /// sections when both exist. Detail page URLs rendered as clickable hyperlinks.
    /// </summary>
    private void BuildOptiScalerContent(
        StackPanel panel,
        AddonInfoResult result,
        SolidColorBrush textColour,
        SolidColorBrush linkColour,
        SolidColorBrush dimColour)
    {
        if (result.Source == InfoSourceType.Wiki)
        {
            // ── Standard OptiScaler Compatibility section ─────────────────────
            if (result.OptiScalerCompat != null)
            {
                BuildOptiScalerSection(panel, "Совместимость OptiScaler",
                    result.OptiScalerCompat, textColour, linkColour);
            }

            // ── FSR4 Compatibility section ────────────────────────────────────
            if (result.OptiScalerFsr4Compat != null)
            {
                // Add spacing between sections
                if (result.OptiScalerCompat != null)
                    panel.Children.Add(new Border { Height = 4 });

                BuildOptiScalerSection(panel, "Совместимость FSR4",
                    result.OptiScalerFsr4Compat, textColour, linkColour);
            }

            // ── General notes from formatted content (if not already shown) ──
            // The result.Content contains the full formatted text; individual
            // sections above handle structured display. Add any remaining notes.
            if (result.OptiScalerCompat == null && result.OptiScalerFsr4Compat == null
                && !string.IsNullOrWhiteSpace(result.Content))
            {
                panel.Children.Add(new TextBlock
                {
                    Text         = result.Content,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
                // Show URL only for the unstructured fallback path (no per-section links)
                if (!string.IsNullOrEmpty(result.Url))
                    AddHyperlinkBlock(panel, result.UrlLabel ?? "Открыть страницу вики", result.Url, linkColour);
            }
        }
        else
        {
            // Manifest or fallback content
            BuildGenericContent(panel, result, textColour, linkColour);
        }

        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text       = "Для этой игры нет данных о совместимости с OptiScaler.",
                Foreground = dimColour,
                FontSize   = 13,
            });
        }
    }

    /// <summary>
    /// Builds a single OptiScaler compatibility section (standard or FSR4).
    /// Shows status, upscaler list, and notes.
    /// </summary>
    private static void BuildOptiScalerSection(
        StackPanel panel,
        string sectionTitle,
        OptiScalerCompatEntry entry,
        SolidColorBrush textColour,
        SolidColorBrush linkColour)
    {
        var upscalers = entry.Upscalers.Count > 0
            ? string.Join(", ", entry.Upscalers)
            : "Не указано";

        // Section header with status
        panel.Children.Add(new TextBlock
        {
            Text         = $"{sectionTitle}: {entry.Status}",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = textColour,
            FontSize     = 13,
            FontWeight   = Microsoft.UI.Text.FontWeights.SemiBold,
            LineHeight   = 22,
        });

        // Upscaler list
        panel.Children.Add(new TextBlock
        {
            Text         = $"Upscalers: {upscalers}",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = textColour,
            FontSize     = 13,
            LineHeight   = 22,
        });

        // Notes
        if (!string.IsNullOrWhiteSpace(entry.Notes))
        {
            panel.Children.Add(new TextBlock
            {
                Text         = entry.Notes,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
            });
        }

        // Detail page URL
        if (!string.IsNullOrEmpty(entry.DetailPageUrl))
        {
            var link = new Microsoft.UI.Xaml.Documents.Hyperlink
            {
                NavigateUri = new Uri(entry.DetailPageUrl),
                Foreground  = linkColour,
            };
            link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text     = "Подробнее",
                FontSize = 13,
            });
            var para = new Microsoft.UI.Xaml.Documents.Paragraph();
            para.Inlines.Add(link);
            var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
            rtb.Blocks.Add(para);
            panel.Children.Add(rtb);
        }
    }

    // ── Generic content rendering ────────────────────────────────────────────────

    /// <summary>
    /// Builds generic dialog content for addons without special rendering needs.
    /// Shows text content and optional URL hyperlink.
    /// </summary>
    private static void BuildGenericContent(
        StackPanel panel,
        AddonInfoResult result,
        SolidColorBrush textColour,
        SolidColorBrush linkColour)
    {
        if (!string.IsNullOrWhiteSpace(result.Content))
        {
            panel.Children.Add(new TextBlock
            {
                Text         = result.Content,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
            });
        }

        if (!string.IsNullOrEmpty(result.Url))
            AddHyperlinkBlock(panel, result.UrlLabel ?? result.Url, result.Url, linkColour);

        // ── HDR Gaming Database link (supplementary) ─────────────────────────
        if (!string.IsNullOrEmpty(result.HdrAnalysisUrl))
            AddHyperlinkBlock(panel, "HDR-анализ — HDR Gaming Database", result.HdrAnalysisUrl, linkColour);
    }

    // ── Release notes content rendering (ReLimiter, Display Commander) ───────────

    /// <summary>
    /// Builds dialog content showing the GitHub release notes for the installed version.
    /// Falls back to the generic addon description if release notes are not available.
    /// </summary>
    private static void BuildReleaseNotesContent(
        StackPanel panel,
        AddonInfoResult result,
        string? installedVersion,
        string? releaseBody,
        string releasesPageUrl,
        SolidColorBrush textColour,
        SolidColorBrush linkColour,
        SolidColorBrush dimColour)
    {
        // Show installed version header if available
        if (!string.IsNullOrWhiteSpace(installedVersion))
        {
            panel.Children.Add(new TextBlock
            {
                Text       = $"Installed: {installedVersion}",
                Foreground = dimColour,
                FontSize   = 12,
                Margin     = new Thickness(0, 0, 0, 4),
            });
        }

        // Show manifest game-specific note first (above release notes) if present
        if (result.Source == InfoSourceType.Manifest && !string.IsNullOrWhiteSpace(result.Content))
        {
            panel.Children.Add(new TextBlock
            {
                Text         = result.Content,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
                Margin       = new Thickness(0, 0, 0, 12),
            });
            // Add a separator before release notes
            panel.Children.Add(new Border
            {
                Height      = 1,
                Background  = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
                Margin      = new Thickness(0, 0, 0, 12),
            });
        }

        // Show release notes as markdown if available
        if (!string.IsNullOrWhiteSpace(releaseBody))
        {
            var markdown = new CommunityToolkit.WinUI.Controls.MarkdownTextBlock
            {
                Text              = releaseBody,
                Background        = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground        = textColour,
                FontSize          = 12,
                UseEmphasisExtras = true,
                UseListExtras     = true,
                UseTaskLists      = true,
            };

            var markdownContainer = new Grid { RequestedTheme = ElementTheme.Dark };
            markdownContainer.Children.Add(markdown);
            panel.Children.Add(markdownContainer);
        }
        else if (result.Source != InfoSourceType.Manifest)
        {
            // Fall back to generic addon description (only if no manifest note already shown)
            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                panel.Children.Add(new TextBlock
                {
                    Text         = result.Content,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }
        }

        // Always show link to the releases page
        AddHyperlinkBlock(panel, "Все релизы на GitHub", releasesPageUrl, linkColour);
    }

    // ── Shared helpers for dialog content ────────────────────────────────────────

    /// <summary>
    /// Adds a text block with an optional hyperlink. If a URL is present, renders
    /// the text followed by a clickable link on the next line.
    /// </summary>
    private static void AddTextOrHyperlink(
        StackPanel panel,
        string text,
        string? url,
        string? urlLabel,
        SolidColorBrush textColour,
        SolidColorBrush linkColour)
    {
        if (!string.IsNullOrEmpty(url))
        {
            var para = new Microsoft.UI.Xaml.Documents.Paragraph();
            para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text       = text,
                Foreground = textColour,
                FontSize   = 13,
            });
            para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            var link = new Microsoft.UI.Xaml.Documents.Hyperlink
            {
                NavigateUri = new Uri(url),
                Foreground  = linkColour,
            };
            link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text     = urlLabel ?? url,
                FontSize = 13,
            });
            para.Inlines.Add(link);
            var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
            rtb.Blocks.Add(para);
            panel.Children.Add(rtb);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text         = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = textColour,
                FontSize     = 13,
                LineHeight   = 22,
            });
        }
    }

    /// <summary>
    /// Adds a standalone clickable hyperlink block to the panel.
    /// </summary>
    private static void AddHyperlinkBlock(
        StackPanel panel,
        string label,
        string url,
        SolidColorBrush linkColour)
    {
        var link = new Microsoft.UI.Xaml.Documents.Hyperlink
        {
            NavigateUri = new Uri(url),
            Foreground  = linkColour,
        };
        link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text     = label,
            FontSize = 13,
        });
        var para = new Microsoft.UI.Xaml.Documents.Paragraph();
        para.Inlines.Add(link);
        var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
        rtb.Blocks.Add(para);
        panel.Children.Add(rtb);
    }

    public async Task ShowUeExtendedWarningAsync(GameCardViewModel card)
    {
        try
        {
            // Check if user has dismissed this warning permanently
            if (_window.ViewModel.Settings.UeExtendedWarningDismissed) return;

            while (_window.Content.XamlRoot == null)
                await Task.Delay(100);

            var hasNotes = !string.IsNullOrWhiteSpace(card.Notes);
            var notesHint = hasNotes
                ? "\n\nПроверьте кнопку «Инфо» — там может быть дополнительная информация о совместимости с этой игрой."
                : "\n\nСпециальных заметок по этой игре нет — загляните в RenoDX Discord за отзывами сообщества.";

            var dlg = new ContentDialog
            {
                Title               = "⚠ Предупреждение о совместимости UE-Extended",
                Content             = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize     = 13,
                    Text         = "Не все игры на Unreal Engine совместимы с UE-Extended.\n\n" +
                                   "UE-Extended использует универсальный метод инъекции, работающий с большинством " +
                                   "играми на Unreal Engine, но может вызывать вылеты или визуальные проблемы в других. " +
                                   "Если у игры есть именной мод RenoDX, он специально адаптирован " +
                                   "для этой игры и может дать лучший результат." +
                                   notesHint,
                },
                PrimaryButtonText   = "Хорошо, понятно",
                SecondaryButtonText = "Больше не показывать",
                XamlRoot            = _window.Content.XamlRoot,
                Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
                RequestedTheme      = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(dlg);
            if (result == ContentDialogResult.Secondary)
            {
                _window.ViewModel.Settings.UeExtendedWarningDismissed = true;
                _window.ViewModel.SaveSettingsPublic();
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[DialogService.ShowUeExtendedWarningAsync] Failed to show UE warning for '{card.GameName}' — {ex.Message}"); }
    }

    // ── Vulkan Admin Required Dialog ────────────────────────────────────────────

    /// <summary>
    /// Shows a dialog when Vulkan ReShade install fails because the app isn't elevated.
    /// Offers to enable Admin Mode (persistent), restart once as admin, or cancel.
    /// </summary>
    public async Task ShowVulkanAdminRequiredDialogAsync()
    {
        try
        {
            while (_window.Content.XamlRoot == null)
                await Task.Delay(100);

            var dlg = new ContentDialog
            {
                Title = "Требуются права администратора",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Text = "Installing the Vulkan ReShade layer requires writing to C:\\ProgramData\\ReShade\\ " +
                           "и изменение системных ключей реестра, что требует прав администратора.\n\n" +
                           "Включить режим администратора — RHI всегда будет запускаться с повышенными правами (после настройки запрос UAC не появляется).\n\n" +
                           "Перезапуск от администратора — разовый запуск с повышенными правами для завершения установки.",
                },
                PrimaryButtonText = "Включить режим администратора",
                SecondaryButtonText = "Перезапуск от администратора",
                CloseButtonText = "Отмена",
                XamlRoot = _window.Content.XamlRoot,
                Background = Brush(ResourceKeys.SurfaceOverlayBrush),
                RequestedTheme = ElementTheme.Dark,
            };

            var result = await ShowSafeAsync(dlg);

            if (result == ContentDialogResult.Primary)
            {
                // Enable Admin Mode (creates scheduled task + restarts)
                try
                {
                    CreateAdminTaskAndRestart();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DialogService.ShowVulkanAdminRequiredDialog] Admin mode setup failed — {ex.Message}");
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                // One-time restart as admin
                RestartAsAdmin();
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowVulkanAdminRequiredDialog] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the Admin Mode scheduled task (UAC prompt) and restarts RHI through it.
    /// </summary>
    private static void CreateAdminTaskAndRestart()
    {
        var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        var taskName = "Режим администратора RHI";

        // Create the task (triggers UAC)
        var createArgs = $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONCE /ST 00:00 /SD 01/01/2000 /RL HIGHEST /F";
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", createArgs)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        using var createProc = System.Diagnostics.Process.Start(psi);
        createProc?.WaitForExit(15000);
        if (createProc?.ExitCode != 0)
            throw new InvalidOperationException($"schtasks /Create failed (exit {createProc?.ExitCode})");

        CrashReporter.Log("[DialogService.CreateAdminTaskAndRestart] Admin task created, restarting via task...");

        // Run the task to relaunch elevated (no UAC this time)
        var runPsi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", $"/Run /TN \"{taskName}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        System.Diagnostics.Process.Start(runPsi);

        // Exit current instance
        Environment.Exit(0);
    }

    /// <summary>
    /// Restarts RHI as admin via a one-time UAC prompt (no scheduled task).
    /// </summary>
    private static void RestartAsAdmin()
    {
        var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            System.Diagnostics.Process.Start(psi);
            CrashReporter.Log("[DialogService.RestartAsAdmin] Restarting as admin...");
            Environment.Exit(0);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt — do nothing
            CrashReporter.Log("[DialogService.RestartAsAdmin] User cancelled UAC prompt");
        }
    }
}
