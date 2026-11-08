// MainWindow.GameNotes.cs -- the game menu's "Game notes…": your own notes for a game, the same ones the
// RHI overlay shows (and edits) on its Info and links page. Stored by GameJournalStore.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private async Task EditGameNotesAsync(GameCardViewModel card)
    {
        try
        {
            if (Content?.XamlRoot is not { } xamlRoot) return;
            var key = GameKey.FromCard(card.GameName, card.Source).ToKey();
            var box = new TextBox
            {
                Text = GameJournalStore.GetNotes(key),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Width = 520,
                Height = 260,
                PlaceholderText = "Codes, routes, settings to remember…",
            };
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = "Only you see these. They're also on the RHI overlay's Info and links page while you play.",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(box);
            var dialog = new ContentDialog
            {
                Title = $"Notes for {card.GameName}",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.None, // Enter makes a new line
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 640.0;
            box.Loaded += (s, e) => box.Focus(FocusState.Programmatic);
            if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;
            GameJournalStore.SetNotes(key, box.Text ?? "");
            CrashReporter.Log($"[MainWindow.EditGameNotes] Saved notes for '{card.GameName}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.EditGameNotes] {ex.Message}");
        }
    }
}
