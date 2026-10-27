using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    // ── Nexus Mods (dev-only) ──────────────────────────────────────────────────

    /// <summary>
    /// Refreshes the Nexus status text and button states based on current settings.
    /// Called on settings page open and after connect/disconnect.
    /// </summary>
    public void RefreshNexusStatus()
    {
        var key      = ViewModel.Settings.NexusApiKey;
        var username = ViewModel.Settings.NexusUsername;
        var isPremium = ViewModel.Settings.NexusIsPremium;

        bool connected = !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(username);

        if (connected)
        {
            var tier = isPremium ? "Premium" : "Free";
            _window.NexusStatusText.Text = $"Connected as {username} ({tier})";
            _window.NexusStatusText.Foreground = UIFactory.Brush(
                isPremium ? ResourceKeys.AccentGreenBrush : ResourceKeys.TextSecondaryBrush);
            _window.NexusDisconnectRow.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            _window.NexusConnectBtn.Content = "Re-connect";
        }
        else
        {
            _window.NexusStatusText.Text = "Not connected";
            _window.NexusStatusText.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
            _window.NexusDisconnectRow.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            _window.NexusConnectBtn.Content = "Connect";
        }

        // NXM handler status
        bool nxmRegistered = NxmProtocolHandler.IsRegistered();
        _window.NxmStatusText.Text = nxmRegistered ? "RHI is the active nxm:// handler" : "Not registered";
        _window.NxmStatusText.Foreground = UIFactory.Brush(
            nxmRegistered ? ResourceKeys.AccentGreenBrush : ResourceKeys.TextSecondaryBrush);
        _window.NxmRegisterBtn.Content = nxmRegistered ? "Unregister" : "Register";
    }

    public async void NexusConnectBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var nexusDl  = App.Services.GetRequiredService<NexusDownloadService>();
            var nexusSso = App.Services.GetRequiredService<NexusSsoService>();

            _window.NexusConnectBtn.IsEnabled = false;
            _window.NexusStatusText.Text = "Opening browser for authorisation...";
            _window.NexusStatusText.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120));
            string? receivedKey = null;

            nexusSso.BrowserUrlReady += url =>
            {
                // Open the Nexus SSO page in the default browser
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.NexusConnectBtn_Click] Failed to open browser — {ex.Message}"); }

                _window.DispatcherQueue?.TryEnqueue(() =>
                    _window.NexusStatusText.Text = "Waiting for authorisation in browser...");
            };

            try
            {
                receivedKey = await nexusSso.AuthoriseAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) { CrashReporter.Log($"[SettingsHandler.NexusConnectBtn_Click] Nexus SSO cancelled or timed out — {ex.Message}"); }

            // Validate the key on a background thread, then marshal ALL UI updates to the dispatcher
            _ = Task.Run(async () =>
            {
                NexusUserInfo? info = null;
                if (!string.IsNullOrEmpty(receivedKey))
                {
                    try { info = await nexusDl.ValidateApiKeyAsync(receivedKey); }
                    catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.NexusConnectBtn_Click] Validate failed — {ex.Message}"); }
                }

                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    _window.NexusConnectBtn.IsEnabled = true;

                    if (string.IsNullOrEmpty(receivedKey))
                    {
                        _window.NexusStatusText.Text = "Authorisation timed out or was cancelled.";
                        _window.NexusStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                        return;
                    }

                    if (info == null)
                    {
                        _window.NexusStatusText.Text = "Received key was invalid. Please try again.";
                        _window.NexusStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                        return;
                    }

                    ViewModel.Settings.NexusApiKey    = receivedKey;
                    ViewModel.Settings.NexusIsPremium = info.IsPremium;
                    ViewModel.Settings.NexusUsername  = info.Name;
                    ViewModel.SaveSettingsPublic();

                    RefreshNexusStatus();
                    CrashReporter.Log($"[SettingsHandler.NexusConnectBtn_Click] Connected via SSO as {info.Name} (Premium={info.IsPremium})");
                });
            });
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.NexusConnectBtn_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    public void NexusDisconnectBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Settings.NexusApiKey    = "";
        ViewModel.Settings.NexusIsPremium = false;
        ViewModel.Settings.NexusUsername  = "";
        ViewModel.SaveSettingsPublic();
        RefreshNexusStatus();
        CrashReporter.Log("[SettingsHandler.NexusDisconnectBtn_Click] Disconnected from Nexus Mods");
    }

    public async void NxmRegisterBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            bool currentlyRegistered = NxmProtocolHandler.IsRegistered();

            if (currentlyRegistered)
            {
                NxmProtocolHandler.UnregisterProtocolHandler();
                CrashReporter.Log("[SettingsHandler.NxmRegisterBtn_Click] Unregistered nxm:// handler");
            }
            else
            {
                // Warn if another app already owns the handler
                if (NxmProtocolHandler.AnyHandlerRegistered())
                {
                    var result = await DialogService.ShowSafeAsync(new ContentDialog
                    {
                        Title = "NXM Protocol Handler",
                        Content = "Another application (e.g. Vortex or MO2) is already registered as the nxm:// handler. Registering RHI will replace it. Continue?",
                        PrimaryButtonText = "Register RHI",
                        CloseButtonText = "Cancel",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                    });
                    if (result != ContentDialogResult.Primary) return;
                }

                try
                {
                    NxmProtocolHandler.RegisterProtocolHandler();
                    CrashReporter.Log("[SettingsHandler.NxmRegisterBtn_Click] Registered nxm:// handler");
                }
                catch (Exception ex)
                {
                    await DialogService.ShowSafeAsync(new ContentDialog
                    {
                        Title = "NXM Registration Failed",
                        Content = $"Could not register the nxm:// handler: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                    });
                    return;
                }
            }

            RefreshNexusStatus();
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.NxmRegisterBtn_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    // ── GitHub OAuth ──────────────────────────────────────────────────────────

    public void RefreshGitHubStatus()
    {
        // If the token was cleared at startup due to 401, make sure the in-memory values are also cleared
        if (App._gitHubTokenExpiredOnStartup)
        {
            ViewModel.Settings.GitHubOAuthToken = "";
            ViewModel.Settings.GitHubUsername   = "";
        }

        var token    = ViewModel.Settings.GitHubOAuthToken;
        var username = ViewModel.Settings.GitHubUsername;
        bool connected = !string.IsNullOrEmpty(token);

        // Check if a PAT file is active
        var patPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "github_api.txt");
        bool hasPat = System.IO.File.Exists(patPath);

        // Update the PAT status text to show file state
        if (hasPat)
        {
            _window.GitHubPatStatusText.Text = "PAT file active — 5,000 req/hr (persists across restarts)";
            _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
            _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            // Only hide if we haven't just shown a save/clear message
            // (leave it visible if it was just set by a button click)
        }

        if (App._gitHubTokenExpiredOnStartup)
        {
            _window.GitHubStatusText.Text       = "GitHub session expired — please re-connect to restore the 5,000 req/hr limit";
            _window.GitHubStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentAmberDimBrush);
            _window.GitHubConnectBtn.Content    = "Re-connect";
            _window.GitHubDisconnectRow.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else if (connected)
        {
            var display = string.IsNullOrEmpty(username) ? "GitHub" : $"@{username}";
            _window.GitHubStatusText.Text       = $"Connected as {display} · 5,000 req/hr";
            _window.GitHubStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
            _window.GitHubConnectBtn.Content    = "Re-connect";
            _window.GitHubDisconnectRow.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            _window.GitHubStatusText.Text       = hasPat ? "Using PAT · 5,000 req/hr" : "Not connected · 60 req/hr";
            _window.GitHubStatusText.Foreground = hasPat ? UIFactory.Brush(ResourceKeys.AccentGreenBrush) : UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
            _window.GitHubConnectBtn.Content    = "Connect GitHub";
            _window.GitHubDisconnectRow.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        _window.GitHubDeviceCodePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        _window.GitHubConnectBtn.IsEnabled = true;
    }

    public async void GitHubConnectBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var authSvc = App.Services.GetRequiredService<GitHubAuthService>();
            var http    = App.Services.GetRequiredService<HttpClient>();

            _window.GitHubConnectBtn.IsEnabled = false;
            _window.GitHubConnectBtn.Content   = "Connecting...";
            _window.GitHubStatusText.Text      = "Requesting device code...";
            _window.GitHubStatusText.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
            _window.GitHubDeviceCodePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(300));

            // Step 1: get device code
            var deviceCode = await authSvc.RequestDeviceCodeAsync(cts.Token).ConfigureAwait(false);

            if (deviceCode == null)
            {
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    _window.GitHubStatusText.Text      = "Failed to start authorisation. Check your connection and try again.";
                    _window.GitHubStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                    _window.GitHubConnectBtn.IsEnabled  = true;
                    _window.GitHubConnectBtn.Content    = "Connect GitHub";
                });
                return;
            }

            // Step 2: show the code and open browser
            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                _window.GitHubUserCodeText.Text = deviceCode.UserCode;
                _window.GitHubVerificationLink.NavigateUri = new Uri(deviceCode.VerificationUri);
                _window.GitHubVerificationLink.Content     = deviceCode.VerificationUri;
                _window.GitHubDeviceCodePanel.Visibility   = Microsoft.UI.Xaml.Visibility.Visible;
                _window.GitHubStatusText.Text       = "Enter the code above at the link — waiting for authorisation...";
                _window.GitHubStatusText.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
            });

            // Open the verification URL in the default browser
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(deviceCode.VerificationUri) { UseShellExecute = true }); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.GitHubConnectBtn_Click] Failed to open browser — {ex.Message}"); }

            // Step 3: poll for the token
            var progress = new Progress<string>(msg =>
                _window.DispatcherQueue?.TryEnqueue(() => _window.GitHubStatusText.Text = msg));

            var token = await authSvc.PollForTokenAsync(deviceCode, progress, cts.Token).ConfigureAwait(false);

            if (string.IsNullOrEmpty(token))
            {
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    _window.GitHubStatusText.Text       = "Authorisation timed out or was cancelled.";
                    _window.GitHubStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                    _window.GitHubDeviceCodePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _window.GitHubConnectBtn.IsEnabled  = true;
                    _window.GitHubConnectBtn.Content    = "Connect GitHub";
                });
                return;
            }

            // Step 4: fetch username, apply token, persist
            var username = await authSvc.GetUsernameAsync(token, cts.Token).ConfigureAwait(false);

            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                ViewModel.Settings.GitHubOAuthToken = token;
                ViewModel.Settings.GitHubUsername   = username ?? "";
                ViewModel.SaveSettingsPublic();

                // Apply to DevUnlockService and the live HttpClient
                DevUnlockService.UpdateToken(token);
                GitHubAuthService.ApplyTokenToHttpClient(http, token);

                CrashReporter.Log($"[SettingsHandler.GitHubConnectBtn_Click] Connected as {username ?? "(unknown)"}");
                RefreshGitHubStatus();
            });
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.GitHubConnectBtn_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    public void GitHubDisconnectBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var http = App.Services.GetRequiredService<HttpClient>();

        ViewModel.Settings.GitHubOAuthToken = "";
        ViewModel.Settings.GitHubUsername   = "";
        ViewModel.SaveSettingsPublic();

        // Clear token from DevUnlockService and HttpClient
        // Reset the DevUnlockService cache so it re-reads from disk (may find a file-based token)
        DevUnlockService.ResetTokenCache();
        var fileToken = DevUnlockService.GitHubApiToken; // re-reads github_api.txt / unlock.txt
        GitHubAuthService.ApplyTokenToHttpClient(http, fileToken);

        CrashReporter.Log("[SettingsHandler.GitHubDisconnectBtn_Click] GitHub OAuth token removed");
        RefreshGitHubStatus();
    }

    public async void GitHubPatSaveBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var pat = _window.GitHubPatBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(pat))
            {
                _window.GitHubPatStatusText.Text = "Please enter a token first.";
                _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                return;
            }

            // Basic format check
            if (!pat.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase) &&
                !pat.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase))
            {
                _window.GitHubPatStatusText.Text = "Token should start with ghp_ or github_pat_";
                _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentAmberDimBrush);
                _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }

            _window.GitHubPatSaveBtn.IsEnabled = false;
            _window.GitHubPatSaveBtn.Content = "Validating...";

            // Validate the token
            bool valid = false;
            string? username = null;
            try
            {
                var http = App.Services.GetRequiredService<HttpClient>();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.github.com/user");
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {pat}");
                req.Headers.TryAddWithoutValidation("User-Agent", "RHI");
                using var resp = await http.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token);
                valid = resp.IsSuccessStatusCode;
                if (valid)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    doc.RootElement.TryGetProperty("login", out var loginEl);
                    username = loginEl.GetString();
                }
            }
            catch { valid = false; }

            _window.GitHubPatSaveBtn.IsEnabled = true;
            _window.GitHubPatSaveBtn.Content = "Save";

            if (!valid)
            {
                _window.GitHubPatStatusText.Text = "Token validation failed — check the token and try again.";
                _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                return;
            }

            // Write to github_api.txt
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RHI", "github_api.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, pat);
            }
            catch (Exception ex)
            {
                _window.GitHubPatStatusText.Text = $"Failed to save: {ex.Message}";
                _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                return;
            }

            // Apply immediately
            var http2 = App.Services.GetRequiredService<HttpClient>();
            DevUnlockService.ResetTokenCache();
            DevUnlockService.UpdateToken(pat);
            GitHubAuthService.ApplyTokenToHttpClient(http2, pat);

            _window.GitHubPatBox.Text = "";
            _window.GitHubPatStatusText.Text = username != null
                ? $"PAT saved — authenticated as @{username}"
                : "PAT saved and applied.";
            _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
            _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

            CrashReporter.Log($"[SettingsHandler.GitHubPatSave] PAT saved to github_api.txt (user={username ?? "unknown"})");
            RefreshGitHubStatus();
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.GitHubPatSaveBtn_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    public void GitHubPatClearBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "github_api.txt");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            _window.GitHubPatStatusText.Text = $"Failed to clear: {ex.Message}";
            _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
            _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            return;
        }

        // Revert HttpClient to OAuth token if present, else unauthenticated
        DevUnlockService.ResetTokenCache();
        var http = App.Services.GetRequiredService<HttpClient>();
        var oauthToken = ViewModel.Settings.GitHubOAuthToken;
        GitHubAuthService.ApplyTokenToHttpClient(http, string.IsNullOrEmpty(oauthToken) ? null : oauthToken);

        _window.GitHubPatBox.Text = "";
        _window.GitHubPatStatusText.Text = "PAT cleared.";
        _window.GitHubPatStatusText.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
        _window.GitHubPatStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        CrashReporter.Log("[SettingsHandler.GitHubPatClear] github_api.txt removed");
        RefreshGitHubStatus();
    }
}
