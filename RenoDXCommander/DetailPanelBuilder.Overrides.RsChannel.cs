// DetailPanelBuilder.Overrides.RsChannel.cs — ReShade Channel Override + Update Inclusion + Middle Row Grid.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>Builds RS Channel Override, Update Inclusion, and Middle Row Grid.</summary>
    private void BuildRsChannelSection(OverridesPanelCtx ctx)
    {
        _window.ViewModel.SetLastUiAction($"BuildRsChannelSection({ctx.Card.GameName})");
        CrashReporter.Log($"[BuildRsChannelSection] Enter: '{ctx.Card.GameName}'");
        var card = ctx.Card;
        var gameName = ctx.GameName;
        var isLumaMode = ctx.IsLumaMode;
        var bitnessPanel = ctx.BitnessPanel;

        // ── ReShade Channel Override (logic shared with the ReShade ⚙: DetailPanelBuilder.Overrides.RsShared.cs) ──
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var channelLabel = new TextBlock
        {
            Text = "ReShade Channel",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(channelLabel,
            "Override the global ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        var (channelItemsList, defaultChannelSelection) = GetRsChannelChoices(card);

        var channelCombo = new ComboBox
        {
            ItemsSource = channelItemsList,
            SelectedItem = defaultChannelSelection,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(channelCombo,
            "Override the ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        bool channelComboInitializing = true;
        ctx.ChannelComboInitializing = true;

        // Allow re-opening the Custom picker by clicking Custom when already on Custom
        channelCombo.DropDownClosed += (s, ev) =>
        {
            if (channelComboInitializing) return;
            var current = channelCombo.SelectedItem as string;
            if (current == "Custom" && string.Equals(_window.ViewModel.GetReShadeChannelOverride(ctx.CapturedName, ctx.Card.Source), "Custom", StringComparison.OrdinalIgnoreCase))
            {
                channelComboInitializing = true;
                channelCombo.SelectedItem = "Stable";
                channelComboInitializing = false;
                channelCombo.SelectedItem = "Custom";
            }
        };

        channelCombo.SelectionChanged += async (s, ev) =>
        {
            try
            {
                var selected = channelCombo.SelectedItem as string;
                if (channelComboInitializing || ctx.ChannelComboInitializing) return;
                if (string.IsNullOrEmpty(selected)) return;

                if (await ApplyRsChannelAsync(ctx.Card, ctx.CapturedName, selected))
                {
                    if (selected != RsChannelLegacyItem) defaultChannelSelection = selected;
                }
                else
                {
                    channelComboInitializing = true;
                    channelCombo.SelectedItem = defaultChannelSelection;
                    channelComboInitializing = false;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] Channel change failed — {ex}");
            }
        };

        Grid.SetRow(channelLabel, 0); Grid.SetColumn(channelLabel, 2);
        Grid.SetRow(channelCombo, 1); Grid.SetColumn(channelCombo, 2);
        bitnessPanel.Children.Add(channelLabel);
        bitnessPanel.Children.Add(channelCombo);
        ctx.ChannelCombo = channelCombo;
        channelComboInitializing = false;
        ctx.ChannelComboInitializing = false;

        CrashReporter.Log($"[BuildRsChannelSection] ChannelCombo done, building UpdateInclusion: '{ctx.Card.GameName}'");

        // ── Global update inclusion (compact: button + summary) ──────────────────
        var capturedCard = card;

        var (updateInclusionBtn, updateSummaryText) = UpdateInclusionHelper.CreateUpdateInclusionControls(
            _window.ViewModel, ctx.CapturedName, card.IsREEngineGame, _window.Content.XamlRoot,
            onSaved: () =>
            {
                // Rebuild the detail panel so component rows reflect the new exclusion state
                // (e.g. ReShade row un-greys when REF is excluded)
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                {
                    _window.PopulateDetailPanel(refreshCard);
                    _window.DispatcherQueue?.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => BuildOverridesPanel(refreshCard));
                }
            },
            isDxvkEnabled: card.DxvkEnabled,
            store: card.Source ?? "");
        ctx.UpdateSummaryText = updateSummaryText; // assign for reset action to use

        var toggleRow = new StackPanel { Spacing = 0 };
        ToolTipService.SetToolTip(updateInclusionBtn, "Choose which components are included in Update All for this game.");
        toggleRow.Children.Add(updateInclusionBtn);
        toggleRow.Children.Add(updateSummaryText);

        // ── Global update inclusion section (Middle Row right column) ────────────
        var globalUpdateColumn = new StackPanel { Spacing = 0 };
        var globalUpdateHeading = new TextBlock
        {
            Text = "Update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };
        ToolTipService.SetToolTip(globalUpdateHeading,
            "Which components Update All updates for this game. This setting applies to this game only;\n"
            + "components switched off in Settings (Components to check) are skipped for every game.");
        globalUpdateColumn.Children.Add(globalUpdateHeading);
        globalUpdateColumn.Children.Add(toggleRow);

        // ── Middle Row vertical divider ──────────────────────────────────────────
        var middleRowDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };

        // ── Middle Row Grid (3 columns: Star | Auto | Star) — Bitness/API + Global update ──
        var middleRowGrid = new Grid { ColumnSpacing = 0 };
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(bitnessPanel, 0);
        Grid.SetColumn(middleRowDivider, 1);
        Grid.SetColumn(globalUpdateColumn, 2);

        middleRowGrid.Children.Add(bitnessPanel);
        middleRowGrid.Children.Add(middleRowDivider);
        middleRowGrid.Children.Add(globalUpdateColumn);

        _window.OverridesPanel.Children.Add(middleRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        CrashReporter.Log($"[BuildRsChannelSection] Exit: '{ctx.Card.GameName}'");
    }
}