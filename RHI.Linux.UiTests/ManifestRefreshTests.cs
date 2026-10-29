using System.Net;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RHI.Core;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.UiTests;

public sealed class ManifestRefreshTests
{
    [AvaloniaFact]
    public async Task LiveManifestRefreshUpdatesPresetsAndShowsInstallWarningsBeforeInstallation()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-manifest-ui-" + Guid.NewGuid().ToString("N"));
        var oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var oldPresets = (DlssProfile.SrPresets, DlssProfile.RrPresets, DlssProfile.FgPresets, DlssProfile.NrPresets);
        MainWindow? window = null;
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "cache"));
            Directory.CreateDirectory(root);
            var exe = Path.Combine(root, "Game.exe");
            File.WriteAllText(exe, "fixture");
            var game = new Game { Name = "Fresh Manifest Fixture", Root = root, Executable = exe, Executables = [exe] };
            using var http = new HttpClient(new ManifestHandler());
            window = new MainWindow([game], http);
            var refresh = typeof(MainWindow).GetMethod("RefreshCompatibility", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)refresh.Invoke(window, null)!;
            Assert.Contains(DlssProfile.SrPresets, preset => preset.Name == "Live UI Fixture" && preset.Value == 1234);
            window.Show();
            for (var i = 0; i < 100 && !window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Fresh install warning"); i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            var text = string.Join("\n", window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("Game compatibility notes and install warnings", text);
            Assert.Contains("Fresh install warning", text);
        }
        finally
        {
            window?.Close();
            (DlssProfile.SrPresets, DlssProfile.RrPresets, DlssProfile.FgPresets, DlssProfile.NrPresets) = oldPresets;
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", oldCache);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task ExplicitRefreshBypassesRecentNeuralRenderingReleaseCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-refresh-ui-" + Guid.NewGuid().ToString("N"));
        var oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var oldPresets = (DlssProfile.SrPresets, DlssProfile.RrPresets, DlssProfile.FgPresets, DlssProfile.NrPresets);
        MainWindow? window = null;
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "cache"));
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));
            var handler = new ReleaseHandler();
            using var http = new HttpClient(handler);
            window = new MainWindow([], http);
            var refresh = typeof(MainWindow).GetMethod("RefreshDlssCatalogs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)refresh.Invoke(window, [false])!;
            Assert.Equal(1, handler.Requests.Count(url => url == Sources.RhiAddonReleases));
            await (Task)refresh.Invoke(window, [false])!;
            Assert.Equal(1, handler.Requests.Count(url => url == Sources.RhiAddonReleases));
            await (Task)refresh.Invoke(window, [true])!;
            Assert.Equal(2, handler.Requests.Count(url => url == Sources.RhiAddonReleases));
            Assert.Equal(3, handler.Requests.Count(url => url == Sources.DlssManifest));
        }
        finally
        {
            window?.Close();
            (DlssProfile.SrPresets, DlssProfile.RrPresets, DlssProfile.FgPresets, DlssProfile.NrPresets) = oldPresets;
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", oldCache);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsoluteUri == Sources.DlssManifest ? "{}" : "[]")
            });
        }
    }

    private sealed class ManifestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(Sources.Manifest, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"dlssPresets":{"sr":[{"name":"Live UI Fixture","value":1234}]},
                     "installWarnings":{"Fresh Manifest Fixture":{"reshade":"Fresh install warning"}}}
                    """)
            });
        }
    }
}
