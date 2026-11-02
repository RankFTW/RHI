using Microsoft.UI.Xaml;
using RHI.ManifestEditor.UI;

namespace RHI.ManifestEditor;

public partial class App : Application
{
    private MainWindow? _window;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "ManifestEditor", "editor.log");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (IOException) { /* logging must never break the editor */ }
    }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log($"[App.UnhandledException] {e.Exception}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
