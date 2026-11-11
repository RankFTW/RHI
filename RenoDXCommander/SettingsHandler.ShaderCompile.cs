// SettingsHandler.ShaderCompile.cs -- Settings › Driver Settings › Driver defaults: "Compile shaders now" row under
// Shader pre-compile (built in C#, XAML constraint). Runs NVIDIA's shader compiler straight away, like the NVIDIA
// App's Shader Cache › Compile now, at the Shader pre-compile level. See NvShaderCompilerService.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private bool _shaderCompileRowAdded;
    private Button? _shaderCompileBtn;
    private TextBlock? _shaderCompileStatus;
    private bool _shaderCompileOurs; // RHI started the run being shown

    private const string ShaderCompileIdleText = "Compile now";

    private void EnsureShaderCompileRow()
    {
        if (_shaderCompileRowAdded) return;
        _shaderCompileRowAdded = true;
        try
        {
            var path = new List<UIElement>();
            bool found = FindPath(_window.SettingsCardsPanel, _window.ShaderPrecompileCombo, path);
            int ci = found ? path.FindLastIndex(e => e is StackPanel) : -1; // the column; the next element is the row
            if (ci < 0 || ci + 1 >= path.Count || path[ci] is not StackPanel column || path[ci + 1] is not FrameworkElement row)
            {
                CrashReporter.Log("[SettingsHandler.EnsureShaderCompileRow] Shader pre-compile row not found, not added");
                return;
            }

            _shaderCompileBtn = new Button { Content = ShaderCompileIdleText, Style = FindStyle("SettingsButtonStyle") };
            if (Application.Current.Resources.TryGetValue(ResourceKeys.SettingsLabelBrush, out var v) && v is Brush brush)
            {
                _shaderCompileBtn.Foreground = brush;
                _shaderCompileBtn.Resources["ButtonForegroundPointerOver"] = brush;
                _shaderCompileBtn.Resources["ButtonForegroundPressed"] = brush;
            }
            ToolTipService.SetToolTip(_shaderCompileBtn,
                "Runs NVIDIA's shader compiler now instead of waiting for the PC to be idle (the NVIDIA App's Compile now). " +
                "It rebuilds the DirectX 12 shaders your games have already saved in the shader cache for the installed driver, so there's less " +
                "stutter the first time you play after a driver update. It can't compile games you haven't played (or after Clear shader cache). Uses the Shader pre-compile level above (Medium when it's Off). " +
                "Takes a few minutes; click again to stop.");
            _shaderCompileBtn.Click += ShaderCompileBtn_Click;

            _shaderCompileStatus = new TextBlock
            {
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };

            int at = column.Children.IndexOf(row) + 1;
            column.Children.Insert(at, SettingRow("Compile shaders now", _shaderCompileBtn));
            column.Children.Insert(at + 1, _shaderCompileStatus);

            if (NvShaderCompilerService.FindCompiler() == null)
            {
                _shaderCompileBtn.IsEnabled = false;
                SetShaderCompileStatus("Needs an NVIDIA driver that includes the shader compiler (NvOSC.exe).");
            }
            else if (NvShaderCompilerService.IsRunning())
            {
                ShowShaderCompileRunning(ours: false);
                _ = WatchOtherShaderCompileAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureShaderCompileRow] Failed — {ex.Message}");
        }
    }

    private async void ShaderCompileBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_shaderCompileBtn == null) return;

            // Running (ours, the NVIDIA App's or the idle task's): the button stops it
            if (NvShaderCompilerService.IsRunning())
            {
                await Task.Run(NvShaderCompilerService.Stop);
                ShowShaderCompileIdle();
                SetShaderCompileStatus("Stopped. Shaders already compiled are kept.");
                return;
            }

            var exe = NvShaderCompilerService.FindCompiler();
            if (exe == null)
            {
                SetShaderCompileStatus("NVIDIA's shader compiler (NvOSC.exe) wasn't found in the installed driver.");
                return;
            }

            string resUtil = _window.ShaderPrecompileCombo.SelectedIndex switch
            {
                1 => "MIN",
                3 => "MAX",
                _ => "MED", // Medium, and Off (the NVIDIA App's own Compile now default)
            };

            ShowShaderCompileRunning(ours: true);
            var started = DateTime.Now;
            int? code = await NvShaderCompilerService.RunAsync(exe, resUtil, _window.LifetimeToken);
            if (_window.IsShuttingDown || !_shaderCompileOurs) return; // stopped with the button: already reported
            ShowShaderCompileIdle();

            var elapsed = DateTime.Now - started;
            string took = elapsed.TotalMinutes < 1 ? "under a minute" : $"{elapsed.TotalMinutes:F0} min";
            SetShaderCompileStatus(code switch
            {
                null => "NVIDIA's shader compiler couldn't be started. See the log for details.",
                // It only rebuilds shaders games have already saved in the cache: an empty or fresh cache ends at once
                0 when elapsed.TotalSeconds < 5 => "Nothing to compile. It rebuilds shaders your games have already saved in the shader cache, " +
                                                   "so it has work to do after you've played games, or after a driver update.",
                0 => $"Done at {DateTime.Now:HH:mm} ({took}).",
                _ => $"NVIDIA's shader compiler stopped with code {code}. If Shader pre-compile is Off, turn it on and try again.",
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.ShaderCompileBtn_Click] {ex.Message}");
            ShowShaderCompileIdle();
        }
    }

    /// <summary>A run RHI didn't start was going when Settings opened: reset the button when it ends.</summary>
    private async Task WatchOtherShaderCompileAsync()
    {
        try
        {
            while (!_window.IsShuttingDown && !_shaderCompileOurs && await Task.Run(NvShaderCompilerService.IsRunning))
                await Task.Delay(3000, _window.LifetimeToken);
            if (!_window.IsShuttingDown && !_shaderCompileOurs) { ShowShaderCompileIdle(); SetShaderCompileStatus(null); }
        }
        catch (OperationCanceledException) { /* RHI closing */ }
    }

    private void ShowShaderCompileRunning(bool ours)
    {
        _shaderCompileOurs = ours;
        if (_shaderCompileBtn != null) _shaderCompileBtn.Content = "Stop compiling";
        SetShaderCompileStatus(ours
            ? "Compiling shaders. This takes a few minutes and uses some CPU; you can keep using RHI."
            : "NVIDIA's shader compiler is already running (started by the NVIDIA App or its idle task).");
    }

    private void ShowShaderCompileIdle()
    {
        _shaderCompileOurs = false;
        if (_shaderCompileBtn != null) _shaderCompileBtn.Content = ShaderCompileIdleText;
    }

    private void SetShaderCompileStatus(string? text)
    {
        if (_shaderCompileStatus == null) return;
        _shaderCompileStatus.Text = text ?? "";
        _shaderCompileStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
