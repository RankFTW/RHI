// DialogService.GitHubToken.cs: a pop-up when GitHub stops accepting the user's token (it expired or was revoked).
// RHI already drops a rejected token for the session (GitHubTokenHandler: a 401 resends the request without it) and
// shows a line in the status bar and in Settings › Accounts & Data; this tells the user properly, once per token
// (settings GitHubTokenNoticeFor = a short hash of the rejected token, never the token itself).
// Dev demo (unlock.txt): %LocalAppData%\RHI\demo_github_token_expired shows it at the next start and is then deleted.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class DialogService
{
    private const string TokenNoticeSetting = "GitHubTokenNoticeFor";
    private const string NewTokenUrl = "https://github.com/settings/tokens/new?description=RHI";
    private bool _tokenNoticeShowing;

    private static string DemoTokenNoticeFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "demo_github_token_expired");

    /// <summary>
    /// Shows the expired-token pop-up when GitHub rejected the token this session (or at startup) and the user hasn't
    /// seen it for this token yet; or the dev demo. Safe to call any number of times.
    /// </summary>
    internal async Task ShowGitHubTokenNoticeIfNeededAsync()
    {
        if (_tokenNoticeShowing || _window.IsShuttingDown) return;
        bool demo = DevUnlockService.IsUnlocked && File.Exists(DemoTokenNoticeFile);
        bool rejected = GitHubTokenHandler.TokenRejected || App._gitHubTokenExpiredOnStartup;
        if (!demo && !rejected) return;

        string? hash = null;
        if (!demo)
        {
            hash = TokenHash(DevUnlockService.GitHubApiToken ?? "oauth");
            var seen = SettingsViewModelFile.Get(TokenNoticeSetting);
            if (string.Equals(seen, hash, StringComparison.Ordinal)) return; // told about this token before
        }

        _tokenNoticeShowing = true;
        try
        {
            if (demo)
            {
                try { File.Delete(DemoTokenNoticeFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[DialogService.GitHubTokenNotice] demo file — {ex.Message}"); }
            }
            await WaitForXamlRootAsync();

            var content = new StackPanel { Spacing = 10, MaxWidth = 520 };
            void Para(string text) => content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 });
            if (demo) Para("(Demo: nothing has changed. This is what users see when GitHub stops accepting their token.)");
            Para("GitHub no longer accepts the token saved in RHI. It has expired, or it was revoked.");
            Para("Until you add a new one, RHI is back to GitHub's limit of 60 requests an hour, which isn't always enough for "
                + "installs and update checks.");
            Para("To fix it: make a new token on GitHub (choose Generate new token (classic), any name and expiry, leave every box "
                + "unticked), then paste it in Settings › Accounts & Data › GitHub token and click Save.");

            var dialog = new ContentDialog
            {
                Title = "Your GitHub token has expired",
                Content = content,
                PrimaryButtonText = "Make a new token",
                SecondaryButtonText = "Open Settings",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            var result = await ShowSafeAsync(dialog);
            if (result == ContentDialogResult.None && _window.IsShuttingDown) return;
            if (!demo && hash != null) SettingsViewModelFile.Set(TokenNoticeSetting, hash);

            if (result == ContentDialogResult.Primary)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(NewTokenUrl));
            if (result is ContentDialogResult.Primary or ContentDialogResult.Secondary)
                _window.OpenSettingsSection("Accounts");
            CrashReporter.Log($"[DialogService.GitHubTokenNotice] Shown{(demo ? " (demo)" : "")}: {result}");
        }
        catch (OperationCanceledException) { }
        finally { _tokenNoticeShowing = false; }
    }

    /// <summary>A short, one-way tag for a token (so the notice is shown once per token without storing it).</summary>
    private static string TokenHash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(bytes, 0, 8);
    }

    /// <summary>Reads / writes one plain value in settings.json (outside the view model's own keys).</summary>
    private static class SettingsViewModelFile
    {
        public static string? Get(string key) =>
            ViewModels.SettingsViewModel.LoadSettingsFile().TryGetValue(key, out var v) ? v : null;

        public static void Set(string key, string value) =>
            ViewModels.SettingsViewModel.UpdateSettingsFile(s => s[key] = value, "DialogService.GitHubTokenNotice");
    }
}
