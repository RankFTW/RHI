// MainWindow.OneClick.cs — "Optimize" / "Undo Optimization" buttons in the game detail header.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _oneClickBusy;
    private int _oneClickRefreshTicket;

    /// <summary>Hooks the buttons to game selection. Called once from the constructor.</summary>
    private void InitOneClick()
    {
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.SelectedGame))
                DispatcherQueue.TryEnqueue(() => _ = RefreshOneClickAsync());
        };
    }

    /// <summary>Recomputes the button label / tooltip for the selected game (off the UI thread; stale results are dropped).</summary>
    internal async Task RefreshOneClickAsync()
    {
        var card = ViewModel.SelectedGame;
        int ticket = ++_oneClickRefreshTicket;
        if (card == null || string.IsNullOrEmpty(card.InstallPath))
        {
            DetailOptimizeBtn.Visibility = Visibility.Collapsed;
            DetailUndoOptimizeBtn.Visibility = Visibility.Collapsed;
            return;
        }

        DetailOptimizeBtn.Visibility = Visibility.Visible;
        DetailUndoOptimizeBtn.Visibility = _detailPanelBuilder.HasRestorePoint(card) ? Visibility.Visible : Visibility.Collapsed;
        if (_oneClickBusy) return;
        SetOptimizeLabel("⚡ Optimize", "Checking this game…", enabled: false);

        try
        {
            var (report, issues) = await _detailPanelBuilder.EvaluateOptimizationAsync(card);
            if (ticket != _oneClickRefreshTicket || ViewModel.SelectedGame != card) return;   // user moved on

            var lines = new List<string> { report.Summary };
            lines.AddRange(report.Items.Select(i => $"{(i.Satisfied ? "✔" : "•")} {i.Name}: {i.Detail}"));
            lines.AddRange(issues.Select(i => (i.Severity == PreflightSeverity.Blocking ? "⛔ " : "⚠ ") + i.Message));
            var tip = string.Join("\n", lines);

            switch (report.State)
            {
                case OptimizationState.Optimized:
                    SetOptimizeLabel("✔ Optimized", tip, enabled: false); break;
                case OptimizationState.PartiallyOptimized:
                    SetOptimizeLabel(report.DefaultsChanged ? "⚡ Optimize (defaults changed)" : "⚡ Optimize (partial)", tip, enabled: true); break;
                case OptimizationState.NeedsAttention:
                    SetOptimizeLabel("⚠ Needs attention", tip, enabled: false); break;
                default:
                    SetOptimizeLabel("⚡ Optimize", tip, enabled: true); break;
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.OneClick] status failed — {ex.Message}");
            if (ticket == _oneClickRefreshTicket) SetOptimizeLabel("⚡ Optimize", "Status could not be read.", enabled: true);
        }
    }

    private void SetOptimizeLabel(string text, string tooltip, bool enabled)
    {
        DetailOptimizeText.Text = text;
        ToolTipService.SetToolTip(DetailOptimizeBtn, tooltip);
        DetailOptimizeBtn.IsEnabled = enabled;
    }

    private async void OptimizeGame_Click(object sender, RoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null || _oneClickBusy) return;
        _oneClickBusy = true;
        DetailOptimizeBtn.IsEnabled = false;
        DetailUndoOptimizeBtn.IsEnabled = false;
        try
        {
            var outcome = await _detailPanelBuilder.RunOneClickOptimizeAsync(card, msg =>
                DispatcherQueue.TryEnqueue(() => DetailOptimizeText.Text = "⚡ " + msg));
            await ShowOneClickResultAsync("Optimize", outcome);
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.OptimizeGame_Click] {ex}"); }
        finally
        {
            _oneClickBusy = false;
            DetailUndoOptimizeBtn.IsEnabled = true;
            await RefreshOneClickAsync();
        }
    }

    private async void UndoOptimization_Click(object sender, RoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null || _oneClickBusy) return;

        var confirm = new ContentDialog
        {
            Title = "Undo Optimization",
            Content = $"Restore “{card.GameName}” to how it was immediately before the last Optimize?\n\n" +
                      "This puts back the DLSS/Streamline DLLs, the NVIDIA per-game settings and removes the Neural Rendering install it added.",
            PrimaryButtonText = "Undo",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot,
            Background = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme = ElementTheme.Dark,
        };
        if (await DialogService.ShowSafeAsync(confirm) != ContentDialogResult.Primary) return;

        _oneClickBusy = true;
        DetailOptimizeBtn.IsEnabled = false;
        DetailUndoOptimizeBtn.IsEnabled = false;
        try
        {
            var outcome = await _detailPanelBuilder.UndoOptimizationAsync(card, msg =>
                DispatcherQueue.TryEnqueue(() => DetailOptimizeText.Text = "↩ " + msg));
            await ShowOneClickResultAsync("Undo Optimization", outcome);
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.UndoOptimization_Click] {ex}"); }
        finally
        {
            _oneClickBusy = false;
            DetailUndoOptimizeBtn.IsEnabled = true;
            await RefreshOneClickAsync();
        }
    }

    private async Task ShowOneClickResultAsync(string title, OneClickOutcome outcome)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = outcome.Message, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var d in outcome.Details)
            panel.Children.Add(new TextBlock { Text = "• " + d, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush(ResourceKeys.TextSecondaryBrush) });
        foreach (var i in outcome.Issues)
            panel.Children.Add(new TextBlock
            {
                Text = (i.Severity == PreflightSeverity.Blocking ? "⛔ " : "⚠ ") + i.Message,
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = Brush(i.Severity == PreflightSeverity.Blocking ? ResourceKeys.AccentRedBrush : ResourceKeys.AccentAmberBrush),
            });

        var dialog = new ContentDialog
        {
            Title = outcome.Success ? title : $"{title} — not done",
            Content = new ScrollViewer { MaxHeight = 380, Content = panel },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            Background = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }
}
